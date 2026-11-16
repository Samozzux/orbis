"""Lotti: isolati delimitati dalle strade reali, con la griglia di piazzamento (docs/04 §4).

Un lotto è l'area chiusa tra le strade (meno la sede stradale e i marciapiedi). Ogni lotto ha
una griglia allineata al suo lato principale sulla strada: lì il gioco piazzerà gli edifici.
"""
import math
from dataclasses import dataclass

import numpy as np
import shapely
from shapely.geometry import LineString, Polygon, box
from shapely.geometry.polygon import orient

from Orbis.Core.World.Fb.AreaKind import AreaKind as AK
from Orbis.Core.World.Fb.LineKind import LineKind as LK
from Orbis.Core.World.Fb.LotKind import LotKind
from Orbis.Core.World.Fb.RoadClass import RoadClass as RC

# strade che delimitano i lotti (le strade di servizio e i sentieri restano dentro i lotti)
BOUNDARY_ROADS = {RC.Motorway, RC.Trunk, RC.Primary, RC.Secondary, RC.Tertiary, RC.Unclassified,
                  RC.Residential, RC.LivingStreet, RC.Pedestrian, RC.Track, RC.Rail, RC.LightRail,
                  RC.NarrowGauge, RC.Raceway, RC.Runway}
URBAN_ROADS = {RC.Primary, RC.Secondary, RC.Tertiary, RC.Unclassified, RC.Residential, RC.LivingStreet}
BOUNDARY_LINES = {LK.River, LK.Canal}
# peso della classe nella scelta del lato principale
_ROAD_WEIGHT = {RC.Motorway: 0.3, RC.Trunk: 0.6, RC.Primary: 1.6, RC.Secondary: 1.5, RC.Tertiary: 1.4,
                RC.Unclassified: 1.1, RC.Residential: 1.3, RC.LivingStreet: 1.2, RC.Pedestrian: 1.25,
                RC.Track: 0.8, RC.Rail: 0.2, RC.LightRail: 0.2, RC.NarrowGauge: 0.2, RC.Raceway: 0.5,
                RC.Runway: 0.1}
_URBAN_AREAS = {AK.Residential, AK.Commercial, AK.Retail, AK.Industrial, AK.Construction, AK.School,
                AK.University, AK.Hospital, AK.Garages}
_PARK_AREAS = {AK.Park, AK.Garden, AK.Playground, AK.Pitch, AK.SportsCentre, AK.Stadium}
_WATER_AREAS = {AK.Water, AK.River, AK.Lake, AK.Reservoir, AK.Basin, AK.Pond, AK.Sea}

MIN_LOT_M2 = 20.0
SIDEWALK = 1.5  # m per lato nelle strade urbane


@dataclass
class Lot:
    id: int
    kind: int
    geom: Polygon          # metri Mercatore
    grid_x: float
    grid_y: float
    grid_angle: float      # gradi da est, antiorario
    front_road: int
    area_m2: float


def compute_lots(roads, lines, buildings, areas, region, scale, log=print):
    """Calcola i lotti dentro `region` (Polygon/MultiPolygon Mercatore). `scale` = metri veri / Mercatore."""
    minx, miny, maxx, maxy = region.bounds
    margin = 300.0
    clip = box(minx - margin, miny - margin, maxx + margin, maxy + margin)

    b_roads = [r for r in roads if r.attrs["cls"] in BOUNDARY_ROADS and r.geom.intersects(clip)
               and not (r.attrs["flags"] & 2)]  # le gallerie non dividono il terreno
    b_lines = [l for l in lines if l.attrs["kind"] in BOUNDARY_LINES and l.geom.intersects(clip)]
    log(f"  lotti: {len(b_roads):,} strade e {len(b_lines):,} corsi d'acqua di confine")
    if not b_roads:
        return []

    geoms = [r.geom for r in b_roads] + [l.geom for l in b_lines] + [clip.exterior]
    noded = shapely.union_all(shapely.intersection(np.array(geoms, dtype=object), clip))
    blocks = list(shapely.get_parts(shapely.polygonize(shapely.get_parts(noded))))
    log(f"  lotti: {len(blocks):,} isolati grezzi")

    # sede stradale: metà larghezza (+ marciapiede in città), convertita in metri Mercatore
    half = []
    for r in b_roads:
        w = r.attrs["width"] / 2.0 + (SIDEWALK if r.attrs["cls"] in URBAN_ROADS else 0.3)
        half.append(w / scale)
    for l in b_lines:
        half.append(max(l.attrs["width"] / 2.0, 1.0) / scale)
    corridor_geoms = np.array([r.geom for r in b_roads] + [l.geom for l in b_lines], dtype=object)
    corridors = shapely.buffer(corridor_geoms, np.array(half), cap_style="flat", join_style="round", quad_segs=2)
    corridor_tree = shapely.STRtree(corridors)
    road_tree = shapely.STRtree(np.array([r.geom for r in b_roads], dtype=object))

    building_geoms = np.array([b.geom for b in buildings], dtype=object)
    building_tree = shapely.STRtree(building_geoms) if len(building_geoms) else None
    urban = np.array([a.geom for a in areas if a.kind in _URBAN_AREAS], dtype=object)
    parks = np.array([a.geom for a in areas if a.kind in _PARK_AREAS], dtype=object)
    water = np.array([a.geom for a in areas if a.kind in _WATER_AREAS], dtype=object)
    urban_tree = shapely.STRtree(urban) if len(urban) else None
    park_tree = shapely.STRtree(parks) if len(parks) else None
    water_tree = shapely.STRtree(water) if len(water) else None

    min_area = MIN_LOT_M2 / (scale * scale)
    lots = []
    next_id = 1
    for block in blocks:
        if not block.intersects(region) or block.area < min_area:
            continue
        near = corridor_tree.query(block)
        lot_geom = block
        if len(near):
            lot_geom = shapely.difference(block, shapely.union_all(corridors[near]))
        lot_geom = shapely.make_valid(lot_geom) if not lot_geom.is_valid else lot_geom
        for part in shapely.get_parts(lot_geom):
            if part.geom_type != "Polygon" or part.area < min_area:
                continue
            part = orient(shapely.simplify(part, 0.2 / scale), 1.0)
            if part.is_empty or part.geom_type != "Polygon":
                continue
            kind = _classify(part, building_tree, building_geoms, urban_tree, urban, park_tree, parks,
                             water_tree, water)
            gx, gy, angle, front = _grid(part, road_tree, b_roads, scale)
            lots.append(Lot(next_id, kind, part, gx, gy, angle, front, float(part.area * scale * scale)))
            next_id += 1
    log(f"  lotti: {len(lots):,} lotti finali")
    return lots


def _coverage(part, tree, geoms):
    if tree is None:
        return 0.0
    idx = tree.query(part, predicate="intersects")
    if not len(idx):
        return 0.0
    inter = shapely.intersection(geoms[idx], part)
    return float(np.sum(shapely.area(inter)) / part.area)


def _classify(part, building_tree, building_geoms, urban_tree, urban, park_tree, parks, water_tree, water):
    if _coverage(part, water_tree, water) > 0.6:
        return LotKind.Water
    if _coverage(part, park_tree, parks) > 0.5:
        return LotKind.Park
    built = _coverage(part, building_tree, building_geoms)
    if built > 0.12 or _coverage(part, urban_tree, urban) > 0.4:
        return LotKind.Urban
    if built > 0.02:
        return LotKind.Suburban
    return LotKind.Rural


def _grid(part, road_tree, roads, scale):
    """Origine, angolo e strada del lato principale: il lato più lungo che si affaccia su una strada."""
    coords = np.asarray(part.exterior.coords)[:-1]
    n = len(coords)
    best = None
    for i in range(n):
        a = coords[i]
        b = coords[(i + 1) % n]
        dx, dy = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dy) * scale
        if length < 3.0:
            continue
        mid = shapely.points((a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0)
        j = road_tree.query_nearest(mid, max_distance=40.0 / scale)
        if len(j) == 0:
            continue
        road = roads[int(j[0])]
        dist = shapely.distance(mid, road.geom) * scale
        if dist > road.attrs["width"] / 2.0 + SIDEWALK + 6.0:
            continue
        score = length * _ROAD_WEIGHT.get(road.attrs["cls"], 1.0)
        if best is None or score > best[0]:
            best = (score, a, math.degrees(math.atan2(dy, dx)), road.osm_id)
    if best is not None:
        return float(best[1][0]), float(best[1][1]), best[2], best[3]
    # nessuna strada vicina: griglia sul lato lungo del rettangolo minimo
    with np.errstate(all="ignore"):
        rect_geom = shapely.minimum_rotated_rectangle(part)
    if rect_geom.geom_type != "Polygon":
        x, y = coords[0]
        return float(x), float(y), 0.0, 0
    rect = np.asarray(rect_geom.exterior.coords)[:-1]
    if len(rect) < 4:
        x, y = coords[0]
        return float(x), float(y), 0.0, 0
    e0 = rect[1] - rect[0]
    e1 = rect[2] - rect[1]
    if math.hypot(*e1) > math.hypot(*e0):
        start, e = rect[1], e1
    else:
        start, e = rect[0], e0
    ring = LineString(rect.tolist() + [rect[0].tolist()])
    if not shapely.is_ccw(ring):  # l'interno deve stare a sinistra dell'asse x
        start, e = start + e, -e
    return float(start[0]), float(start[1]), math.degrees(math.atan2(e[1], e[0])), 0
