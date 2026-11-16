"""Lettura di un estratto OSM (.osm.pbf o .osm) con pyosmium → oggetti in metri Mercatore."""
from dataclasses import dataclass, field

import numpy as np
import osmium
import shapely
from shapely import wkb
from shapely.geometry import LineString, Point

from Orbis.Core.World.Fb.OsmType import OsmType

from . import geo, tags as T


def _merc(geom):
    return shapely.transform(geom, lambda c: np.column_stack(geo.to_merc(c[:, 0], c[:, 1])))


@dataclass
class Building:
    osm_id: int
    osm_type: int
    geom: object  # Polygon o MultiPolygon in metri Mercatore
    attrs: dict
    name: str = ""
    address: str = ""
    landmark: str = ""
    tags: dict = field(default_factory=dict)


@dataclass
class Road:
    osm_id: int
    geom: LineString
    attrs: dict


@dataclass
class Line:
    osm_id: int
    geom: LineString
    attrs: dict


@dataclass
class Area:
    osm_id: int
    osm_type: int
    kind: int
    geom: object
    name: str = ""
    landmark: str = ""
    tags: dict = field(default_factory=dict)


@dataclass
class Poi:
    osm_id: int
    osm_type: int
    x: float
    y: float
    category: str
    name: str
    address: str


@dataclass
class Tree:
    x: float
    y: float
    kind: int
    height: float


@dataclass
class OsmData:
    buildings: list = field(default_factory=list)
    roads: list = field(default_factory=list)
    lines: list = field(default_factory=list)
    areas: list = field(default_factory=list)
    pois: list = field(default_factory=list)
    trees: list = field(default_factory=list)
    coastlines: list = field(default_factory=list)


def _tags(obj):
    return {t.k: t.v for t in obj.tags}


def load(path, log=print):
    """Legge l'estratto e restituisce OsmData con tutte le geometrie in metri Mercatore."""
    fab = osmium.geom.WKBFactory()
    data = OsmData()
    n = 0
    fp = osmium.FileProcessor(str(path)).with_locations().with_areas()
    for o in fp:
        n += 1
        if n % 1_000_000 == 0:
            log(f"  {n:,} oggetti letti")
        if o.is_node():
            if not o.tags:
                continue
            tg = _tags(o)
            if tg.get("natural") == "tree":
                x, y = geo.to_merc(o.location.lon, o.location.lat)
                h = T.parse_length(tg.get("height")) or 0.0
                data.trees.append(Tree(float(x), float(y), T.tree_kind(tg), h if 0 < h < 60 else 0.0))
                continue
            cat = T.poi_category(tg)
            if cat:
                x, y = geo.to_merc(o.location.lon, o.location.lat)
                data.pois.append(Poi(o.id, OsmType.Node, float(x), float(y), cat, tg.get("name", ""), T.address(tg)))
        elif o.is_way():
            if not o.tags or len(o.nodes) < 2:
                continue
            tg = _tags(o)
            try:
                if tg.get("natural") == "coastline":
                    data.coastlines.append(_merc(wkb.loads(fab.create_linestring(o), hex=True)))
                    continue
                ra = T.road_attrs(tg)
                la = None if ra else T.line_attrs(tg)
                is_row = tg.get("natural") == "tree_row"
                if not ra and not la and not is_row:
                    continue
                line = _merc(wkb.loads(fab.create_linestring(o), hex=True))
            except (RuntimeError, ValueError, osmium.InvalidLocationError):
                continue  # nodi mancanti ai bordi dell'estratto
            if ra:
                data.roads.append(Road(o.id, line, ra))
            elif la:
                data.lines.append(Line(o.id, line, la))
            elif is_row:
                kind = T.tree_kind(tg)
                # un albero ogni ~9 m reali (≈ 12 m Mercatore a 42°N)
                length = line.length
                count = max(2, int(length / 12.0) + 1)
                for i in range(count):
                    p = line.interpolate(i / (count - 1), normalized=True)
                    data.trees.append(Tree(p.x, p.y, kind, 0.0))
        elif o.is_area():
            if not o.tags:
                continue
            tg = _tags(o)
            is_building = tg.get("building", "no") not in ("no", "") or tg.get("building:part", "no") not in ("no", "")
            kind = None if is_building else T.area_kind(tg)
            cat = T.poi_category(tg) if tg.get("name") else None
            if not is_building and kind is None and not cat:
                continue
            try:
                geom = _merc(wkb.loads(fab.create_multipolygon(o), hex=True))
            except (RuntimeError, ValueError, osmium.InvalidLocationError):
                continue
            if not geom.is_valid:
                geom = shapely.make_valid(geom)
                geom = shapely.union_all([g for g in getattr(geom, "geoms", [geom])
                                          if g.geom_type in ("Polygon", "MultiPolygon")]) if geom.geom_type == "GeometryCollection" else geom
            if geom.is_empty or geom.geom_type not in ("Polygon", "MultiPolygon"):
                continue
            osm_type = OsmType.Way if o.from_way() else OsmType.Relation
            oid = o.orig_id()
            if is_building:
                # superficie vera approssimata: area Mercatore × cos²(lat)
                lat = geo.to_lonlat(0, geom.centroid.y)[1]
                area_m2 = geom.area * geo.scale_at(float(lat)) ** 2
                data.buildings.append(Building(oid, osm_type, geom, T.building_attrs(oid, tg, area_m2),
                                               tg.get("name", ""), T.address(tg), tags=_keep_tags(tg)))
            elif kind is not None:
                data.areas.append(Area(oid, osm_type, kind, geom, tg.get("name", ""), tags=_keep_tags(tg)))
            if cat or (is_building and T.poi_category(tg)):
                p = geom.representative_point()
                data.pois.append(Poi(oid, osm_type, p.x, p.y, cat or T.poi_category(tg), tg.get("name", ""),
                                     T.address(tg)))
    log(f"  letti {n:,} oggetti: {len(data.buildings):,} edifici, {len(data.roads):,} strade, "
        f"{len(data.areas):,} aree, {len(data.pois):,} POI, {len(data.trees):,} alberi, {len(data.lines):,} linee")
    return data


_KEEP = ("name", "building", "amenity", "leisure", "historic", "tourism", "wikidata", "religion", "denomination",
         "sport", "building:part")


def _keep_tags(tg):
    """Pochi tag utili per riconoscere i landmark."""
    return {k: tg[k] for k in _KEEP if k in tg}


def find_boundary(path, admin_level, match, log=print):
    """Cerca il confine amministrativo (relazione) che soddisfa `match`: un dict di tag richiesti
    oppure una lista di alternative (basta che una corrisponda). Restituisce (MultiPolygon lon/lat, tag)
    oppure (None, None) dopo aver elencato i confini trovati a quel livello."""
    alternatives = match if isinstance(match, list) else [match]
    fab = osmium.geom.WKBFactory()
    seen = []
    fp = osmium.FileProcessor(str(path)).with_locations().with_areas()
    for o in fp:
        if not o.is_area() or o.from_way():
            continue
        tg = _tags(o)
        if tg.get("boundary") != "administrative" or tg.get("admin_level") != str(admin_level):
            continue
        seen.append(f"{o.orig_id()} {tg.get('name')} ref={tg.get('ref')} iso={tg.get('ISO3166-2')}")
        if any(all(tg.get(k) == v for k, v in alt.items()) for alt in alternatives):
            geom = wkb.loads(fab.create_multipolygon(o), hex=True)
            log(f"  confine trovato: relazione {o.orig_id()} «{tg.get('name')}»")
            return geom, tg
    log(f"  confine non trovato tra {len(seen)} confini di livello {admin_level}:")
    for line in seen[:80]:
        log("    " + line)
    return None, None


def point(x, y):
    return Point(x, y)
