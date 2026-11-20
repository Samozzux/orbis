"""Costruzione del region pack: elementi OSM → tile z14 in coordinate locali → file (docs/02 §2 punti 5-7)."""
import datetime
import json
import math
import os
import re
import zlib
from collections import defaultdict

import numpy as np
import shapely
from shapely.geometry import LineString, Polygon, box
from shapely.geometry.polygon import orient

from Orbis.Core.World.Fb.AreaKind import AreaKind as AK
from Orbis.Core.World.Fb.BuildingFlag import BuildingFlag as BF
from Orbis.Core.World.Fb.LineKind import LineKind as LK
from Orbis.Core.World.Fb.RoadClass import RoadClass as RC

from . import encode, geo, lots as L, osm, tags as T, terrain as TR

ATTRIBUTION = "© OpenStreetMap contributors (ODbL) · Terrain Tiles: Mapzen/AWS Open Data"

# negli anelli esterni (fuori dalla provincia) solo terreno, aree grandi, strade principali e fiumi
OUTER_ROADS = {RC.Motorway, RC.Trunk, RC.Primary, RC.Secondary, RC.Tertiary, RC.Rail}
OUTER_LINES = {LK.River, LK.Canal}
OUTER_MIN_AREA_M2 = 20000.0


def _merc_geom(g):
    return shapely.transform(g, lambda c: np.column_stack(geo.to_merc(c[:, 0], c[:, 1])))


def tiles_covering(z, geom):
    """Tile z che intersecano la geometria (Mercatore)."""
    out = set()
    minx, miny, maxx, maxy = geom.bounds
    shapely.prepare(geom)
    for tx, ty in geo.tile_range(z, minx, miny, maxx, maxy):
        if geom.intersects(box(*geo.tile_bounds(z, tx, ty))):
            out.add((tx, ty))
    return out


def resolve_parts(buildings, log=print):
    """Se un edificio è descritto da parti (building:part), si usano le parti e si toglie il contorno.
    Le parti ereditano nome, indirizzo e uso dal contorno."""
    parts = [b for b in buildings if b.attrs["flags"] & BF.Part]
    if not parts:
        return buildings
    tree = shapely.STRtree(np.array([p.geom for p in parts], dtype=object))
    drop = set()
    for i, b in enumerate(buildings):
        if b.attrs["flags"] & BF.Part:
            continue
        idx = tree.query(b.geom, predicate="intersects")
        if not len(idx):
            continue
        covered = sum(parts[j].geom.intersection(b.geom).area for j in idx)
        if covered >= 0.5 * b.geom.area:
            drop.add(i)
            for j in idx:
                p = parts[j]
                if p.geom.representative_point().within(b.geom):
                    p.name = p.name or b.name
                    p.address = p.address or b.address
                    if p.attrs["use"] == 0:
                        p.attrs["use"] = b.attrs["use"]
                    for k, v in b.tags.items():
                        p.tags.setdefault(k, v)
    log(f"  parti di edificio: {len(parts):,}, contorni sostituiti: {len(drop):,}")
    return [b for i, b in enumerate(buildings) if i not in drop]


def match_landmarks(buildings, areas, landmarks, pois=(), log=print):
    """Collega gli elementi OSM ai landmark di data/landmarks: per ID OSM, per nome dell'edificio o
    dell'area, oppure per nome di un POI (il landmark va all'edificio che lo contiene)."""
    report = []
    tree = shapely.STRtree(np.array([b.geom for b in buildings], dtype=object)) if buildings else None
    for lm in landmarks:
        m = lm.get("match", {})
        ids = {(t, int(i)) for t, i in (x.split("/") for x in m.get("osm", []))}
        rx = re.compile(m["name_regex"], re.I) if m.get("name_regex") else None
        req = m.get("tags", {})
        types = m.get("types", ["building", "area"])
        hits = []
        targets = []
        if "building" in types:
            targets += [("building", b) for b in buildings]
        if "area" in types:
            targets += [("area", a) for a in areas]
        for kind, e in targets:
            t = "way" if e.osm_type == 2 else ("relation" if e.osm_type == 3 else "node")
            ok = (t, e.osm_id) in ids
            if not ok and rx is not None and e.name and rx.search(e.name):
                ok = all(e.tags.get(k) == v for k, v in req.items())
            if ok:
                e.landmark = lm["key"]
                hits.append(f"{kind} {t}/{e.osm_id} «{e.name}»")
        if not hits and "poi" in types and rx is not None and tree is not None:
            for p in pois:
                if not p.name or not rx.search(p.name):
                    continue
                idx = tree.query(shapely.points(p.x, p.y), predicate="within")
                for j in idx:
                    b = buildings[int(j)]
                    b.landmark = lm["key"]
                    b.name = b.name or p.name
                    hits.append(f"building way/{b.osm_id} (dal POI «{p.name}»)")
        report.append({"key": lm["key"], "matches": hits})
        log(f"  landmark {lm['key']}: {len(hits)} elementi" + (f" ({hits[0]})" if hits else " — NON TROVATO"))
    return report


def sea_polygons(coastlines, bounds, log=print):
    """Mare dentro `bounds` (Mercatore) dalle linee di costa OSM (terra a sinistra, acqua a destra)."""
    if not coastlines:
        return []
    clip = box(*bounds)
    lines = [c.intersection(clip) for c in coastlines]
    lines = [g for l in lines for g in shapely.get_parts(l) if g.geom_type == "LineString" and not g.is_empty]
    if not lines:
        return []
    merged = shapely.line_merge(shapely.union_all(lines))
    noded = shapely.union_all([merged, clip.exterior])
    polys = list(shapely.get_parts(shapely.polygonize(shapely.get_parts(noded))))
    votes = np.zeros(len(polys))
    tree = shapely.STRtree(np.array(polys, dtype=object))
    for line in lines:
        c = np.asarray(line.coords)
        for i in range(0, len(c) - 1, max(1, (len(c) - 1) // 8)):
            a, b = c[i], c[i + 1]
            d = b - a
            n = math.hypot(*d)
            if n < 1e-6:
                continue
            mid = (a + b) / 2.0
            right = mid + np.array([d[1], -d[0]]) / n * 2.0   # acqua
            left = mid - np.array([d[1], -d[0]]) / n * 2.0    # terra
            for pt, v in ((right, 1), (left, -1)):
                p = shapely.points(*pt)
                for j in tree.query(p, predicate="intersects"):
                    votes[j] += v
    sea = [p for p, v in zip(polys, votes) if v > 0]
    log(f"  mare: {len(sea)} poligoni da {len(lines)} tratti di costa")
    return [osm.Area(0, 0, AK.Sea, p, "Mare Adriatico") for p in sea]


# ---------------------------------------------------------------- conversione in coordinate locali

def _local_ring(coords, x0, y0):
    a = np.round((np.asarray(coords)[:, :2] - (x0, y0)) * 100.0).astype(np.int64)
    if len(a) > 1 and (a[0] == a[-1]).all():
        a = a[:-1]
    if len(a) > 1:
        keep = np.ones(len(a), dtype=bool)
        keep[1:] = np.any(a[1:] != a[:-1], axis=1)
        a = a[keep]
    return a.astype(np.int32)


def _local_line(line, x0, y0):
    a = np.round((np.asarray(line.coords)[:, :2] - (x0, y0)) * 100.0).astype(np.int64)
    keep = np.ones(len(a), dtype=bool)
    keep[1:] = np.any(a[1:] != a[:-1], axis=1)
    return a[keep].astype(np.int32)


def _polygons(geom):
    return [g for g in shapely.get_parts(geom) if g.geom_type == "Polygon" and not g.is_empty]


def _poly_record(poly, x0, y0):
    poly = orient(poly, 1.0)
    outer = _local_ring(poly.exterior.coords, x0, y0)
    if len(outer) < 3:
        return None
    holes = [h for h in (_local_ring(r.coords, x0, y0) for r in poly.interiors) if len(h) >= 3]
    return outer, holes


def _split(geom, z, keep_tiles):
    """(tile, parte della geometria) per ogni tile toccato, tagliando ai bordi del tile."""
    minx, miny, maxx, maxy = geom.bounds
    tiles = [t for t in geo.tile_range(z, minx, miny, maxx, maxy) if t in keep_tiles]
    if not tiles:
        return []
    if len(tiles) == 1:
        tb = box(*geo.tile_bounds(z, *tiles[0]))
        if tb.contains(geom):
            return [(tiles[0], geom)]
    out = []
    for t in tiles:
        part = geom.intersection(box(*geo.tile_bounds(z, *t)))
        if not part.is_empty:
            out.append((t, part))
    return out


# ---------------------------------------------------------------- costruzione

def municipality_rings(geom, tolerance=0.0001):
    """Anelli (esterni e buchi) semplificati di ~10 m, in lon/lat, senza ripetere il primo punto."""
    rings = []
    for poly in _polygons(geom):
        p = poly.simplify(tolerance, preserve_topology=True)
        if p.is_empty:
            continue
        for ring in [p.exterior, *p.interiors]:
            c = np.asarray(ring.coords)[:-1]
            if len(c) >= 3:
                rings.append(c)
    return rings


def transit(data, area, log=print, road_ids=None):
    """Linee di trasporto pubblico con almeno due fermate nell'area e il percorso su strade del pack
    (`road_ids`: ID delle strade presenti nei tile; di default quelle lette dall'estratto).
    Restituisce (fermate [(id, lon, lat, nome)], linee [dict per encode.encode_index])."""
    if road_ids is None:
        road_ids = {r.osm_id for r in data.roads}
    shapely.prepare(area)
    stops, index, routes = [], {}, []
    for r in sorted(data.routes, key=lambda r: r.osm_id):
        sids = [s for s in r.stops if s in data.stops]
        inside = 0
        for s in sids:
            x, y = geo.to_merc(data.stops[s].lon, data.stops[s].lat)
            inside += bool(shapely.contains_xy(area, float(x), float(y)))
        ways = [w for w in r.ways if w in road_ids]
        if inside < 2 or not ways:
            continue
        idx = []
        for s in sids:
            if s not in index:
                index[s] = len(stops)
                st = data.stops[s]
                stops.append((st.osm_id, float(st.lon), float(st.lat), st.name))
            idx.append(index[s])
        tg = r.tags
        routes.append({"osm_id": r.osm_id, "mode": r.mode, "ref": tg.get("ref", ""), "name": tg.get("name", ""),
                       "from": tg.get("from", ""), "to": tg.get("to", ""), "operator": tg.get("operator", ""),
                       "network": tg.get("network", ""), "colour": T.parse_color(tg.get("colour")),
                       "ways": ways, "stops": idx})
    log(f"trasporto pubblico: {len(routes)} linee su {len(data.routes)}, {len(stops)} fermate")
    return stops, routes


def build(region, osm_path, boundary_lonlat, terrain_source, out_dir, landmarks=(), version=1,
          source_name="", log=print, far_source=None, municipalities=()):
    """Costruisce il region pack in `out_dir` (index.bin + z/x/y.bin + report.json).
    `municipalities`: [(codice ISTAT, nome, geometria lon/lat)] da osm.municipalities (pack v4)."""
    z = region.get("zoom", 14)
    origin_lat, origin_lon = region["origin"]["lat"], region["origin"]["lon"]
    scale = geo.scale_at(origin_lat)

    boundary = _merc_geom(boundary_lonlat)
    full_area = boundary.buffer(region.get("full_buffer_m", 1000) / scale)
    outer_area = boundary.buffer(region.get("outer_buffer_m", 12000) / scale)
    full_tiles = tiles_covering(z, full_area)
    outer_tiles = tiles_covering(z, outer_area) - full_tiles
    all_tiles = full_tiles | outer_tiles
    log(f"tile z{z}: {len(full_tiles)} completi + {len(outer_tiles)} di contorno")

    log("lettura OSM…")
    data = osm.load(osm_path, log=log)
    buildings = resolve_parts(data.buildings, log=log)
    buildings = [b for b in buildings if not (b.attrs["flags"] & BF.Underground)]
    report = {"landmarks": match_landmarks(buildings, data.areas, landmarks, pois=data.pois, log=log)}

    tb = [geo.tile_bounds(z, *t) for t in all_tiles]
    bounds = (min(b[0] for b in tb), min(b[1] for b in tb), max(b[2] for b in tb), max(b[3] for b in tb))
    sea = sea_polygons(data.coastlines, bounds, log=log)
    areas = data.areas + sea
    sea_union = shapely.union_all([a.geom for a in sea]) if sea else None
    if sea_union is not None:
        shapely.prepare(sea_union)

    log("lotti…")
    full_union = shapely.union_all([box(*geo.tile_bounds(z, *t)) for t in full_tiles])
    region_lots = L.compute_lots(data.roads, data.lines, buildings, areas, full_union, scale, log=log)

    tiles = defaultdict(lambda: defaultdict(list))

    for b in buildings:
        p = b.geom.representative_point()
        t = geo.tile_of(z, p.x, p.y)
        if t not in full_tiles:
            continue
        x0, y0 = geo.tile_origin(z, *t)
        for poly in _polygons(b.geom):
            rec = _poly_record(poly, x0, y0)
            if rec is None:
                continue
            tiles[t]["buildings"].append({
                "osm_id": b.osm_id, "osm_type": b.osm_type, "outer": rec[0], "holes": rec[1],
                "attrs": b.attrs, "name": b.name, "address": b.address, "landmark": b.landmark,
                "_geom": poly})

    for r in data.roads:
        for t, part in _split(r.geom, z, all_tiles):
            if t in outer_tiles and r.attrs["cls"] not in OUTER_ROADS:
                continue
            x0, y0 = geo.tile_origin(z, *t)
            for seg in shapely.get_parts(part):
                if seg.geom_type != "LineString":
                    continue
                pts = _local_line(seg, x0, y0)
                if len(pts) >= 2:
                    tiles[t]["roads"].append({"osm_id": r.osm_id, "pts": pts, "attrs": r.attrs})

    for l in data.lines:
        for t, part in _split(l.geom, z, all_tiles):
            if t in outer_tiles and l.attrs["kind"] not in OUTER_LINES:
                continue
            x0, y0 = geo.tile_origin(z, *t)
            for seg in shapely.get_parts(part):
                if seg.geom_type != "LineString":
                    continue
                pts = _local_line(seg, x0, y0)
                if len(pts) >= 2:
                    tiles[t]["lines"].append({"osm_id": l.osm_id, "pts": pts, "attrs": l.attrs})

    simplify = 0.4 / scale
    for a in areas:
        geom = a.geom
        if len(shapely.get_coordinates(geom)) > 200:
            geom = shapely.simplify(geom, simplify)
        big = geom.area * scale * scale >= OUTER_MIN_AREA_M2
        for t, part in _split(geom, z, all_tiles if big else full_tiles):
            x0, y0 = geo.tile_origin(z, *t)
            for poly in _polygons(part):
                rec = _poly_record(poly, x0, y0)
                if rec is None:
                    continue
                tiles[t]["areas"].append({"osm_id": a.osm_id, "osm_type": a.osm_type, "kind": a.kind,
                                          "outer": rec[0], "holes": rec[1], "name": a.name,
                                          "landmark": a.landmark})

    for p in data.pois:
        t = geo.tile_of(z, p.x, p.y)
        if t not in full_tiles:
            continue
        x0, y0 = geo.tile_origin(z, *t)
        tiles[t]["pois"].append({"osm_id": p.osm_id, "osm_type": p.osm_type,
                                 "x": int(round((p.x - x0) * 100)), "y": int(round((p.y - y0) * 100)),
                                 "category": p.category, "name": p.name, "address": p.address, "_pt": (p.x, p.y)})

    for tr in data.trees:
        t = geo.tile_of(z, tr.x, tr.y)
        if t in full_tiles:
            tiles[t]["trees"].append(tr)

    max_lot = 600.0 / scale
    for lot in region_lots:
        minx, miny, maxx, maxy = lot.geom.bounds
        whole = (maxx - minx) <= max_lot and (maxy - miny) <= max_lot
        if whole:
            p = lot.geom.representative_point()
            pieces = [(geo.tile_of(z, p.x, p.y), lot.geom)]
        else:
            pieces = _split(lot.geom, z, full_tiles)
        for t, part in pieces:
            if t not in full_tiles:
                continue
            x0, y0 = geo.tile_origin(z, *t)
            for poly in _polygons(part):
                rec = _poly_record(poly, x0, y0)
                if rec is None:
                    continue
                tiles[t]["lots"].append({
                    "id": lot.id, "kind": lot.kind, "outer": rec[0], "holes": rec[1],
                    "grid_x": int(round((lot.grid_x - x0) * 100)), "grid_y": int(round((lot.grid_y - y0) * 100)),
                    "grid_angle": int(round(lot.grid_angle * 1000)), "front_road": lot.front_road,
                    "area": float(poly.area * scale * scale)})

    log("scrittura dei tile…")
    os.makedirs(out_dir, exist_ok=True)
    entries = []
    totals = defaultdict(int)
    total_bytes = 0
    for i, t in enumerate(sorted(all_tiles)):
        tile = tiles.get(t, {})
        _link_pois(tile)
        trees = tile.get("trees", [])
        x0, y0 = geo.tile_origin(z, *t)
        rec = {k: tile.get(k, []) for k in ("buildings", "roads", "lines", "areas", "pois", "lots")}
        if trees:
            rec["trees"] = {
                "pts": np.round((np.array([(tr.x, tr.y) for tr in trees]) - (x0, y0)) * 100).astype(np.int32),
                "kinds": [tr.kind for tr in trees],
                "heights": [min(255, int(round(tr.height * 10))) for tr in trees]}
        heights = TR.tile_heights(terrain_source, z, *t)
        if sea_union is not None and sea_union.intersects(box(*geo.tile_bounds(z, *t))):
            # in mare il fondale resta sotto l'acqua (la batimetria a 30 m sfiora lo zero vicino a riva)
            tx0, ty0, tx1, ty1 = geo.tile_bounds(z, *t)
            gx, gy = np.meshgrid(np.linspace(tx0, tx1, TR.SAMPLES), np.linspace(ty0, ty1, TR.SAMPLES))
            inside = shapely.contains_xy(sea_union, gx, gy)
            heights = np.where(inside, np.minimum(heights, -2.0), heights)
        base, step, values = TR.quantize(heights)
        rec["terrain"] = (TR.SAMPLES, base, step, values)
        data_bytes = encode.encode_tile(z, t[0], t[1], rec)
        path = os.path.join(out_dir, str(z), str(t[0]), f"{t[1]}.bin")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(data_bytes)
        crc = zlib.crc32(data_bytes) & 0xFFFFFFFF
        entries.append((t[0], t[1], len(data_bytes), crc, len(rec["buildings"])))
        total_bytes += len(data_bytes)
        for k in ("buildings", "roads", "lines", "areas", "pois", "lots"):
            totals[k] += len(rec[k])
        totals["trees"] += len(trees)
        if (i + 1) % 200 == 0:
            log(f"  {i + 1}/{len(all_tiles)} tile")

    far = None
    if far_source is not None:
        # orizzonte: ~50 km attorno alla regione, celle da ~450 m veri (max 65535 vertici in Unity)
        radius = region.get("far_radius_m", 50000) / scale
        bx0, by0, bx1, by1 = boundary.bounds
        cell = region.get("far_cell_m", 450) / scale
        fb = (bx0 - radius, by0 - radius, bx1 + radius, by1 + radius)
        while ((fb[2] - fb[0]) / cell + 1) * ((fb[3] - fb[1]) / cell + 1) > 65000:
            cell *= 1.1
        far = TR.far_grid(far_source, fb, cell)
        log(f"terreno lontano: {far['cols']}×{far['rows']} celle da {cell * scale:.0f} m")
    outline = max(_polygons(boundary_lonlat), key=lambda p: p.area).simplify(0.0005)
    created = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    stops, routes = transit(data, outer_area, log=log)
    muns = [(code, name, municipality_rings(geom)) for code, name, geom in municipalities]
    index = encode.encode_index(region["key"], region["name"], version, z, entries, origin_lat, origin_lon,
                                np.asarray(outline.exterior.coords), ATTRIBUTION, created, source_name, far=far,
                                municipalities=muns, stops=stops, routes=routes)
    with open(os.path.join(out_dir, "index.bin"), "wb") as f:
        f.write(index)
    report.update({"version": version, "created": created, "source": source_name, "zoom": z,
                   "tiles": {"full": len(full_tiles), "outer": len(outer_tiles)},
                   "bytes": total_bytes + len(index), "counts": dict(totals),
                   "municipalities": len(muns), "transit": {"routes": len(routes), "stops": len(stops)}})
    with open(os.path.join(out_dir, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    log(f"fatto: {len(entries)} tile, {total_bytes / 1e6:.1f} MB, " +
        ", ".join(f"{k} {v:,}" for k, v in totals.items()))
    return report


def _link_pois(tile):
    """Indice dell'edificio che contiene ogni POI (per le vetrine al piano terra)."""
    pois = tile.get("pois")
    bl = tile.get("buildings")
    if not pois:
        return
    if not bl:
        for p in pois:
            p["building"] = -1
        return
    tree = shapely.STRtree(np.array([b["_geom"] for b in bl], dtype=object))
    for p in pois:
        idx = tree.query(shapely.points(*p["_pt"]), predicate="within")
        p["building"] = int(idx[0]) if len(idx) else -1


# ---------------------------------------------------------------- aggiornamento di un pack esistente

def add_extras(region, pack_dir, osm_path, boundary_lonlat, municipalities=(), version=None, source_name="",
               log=print):
    """Pack v4 a partire da un pack già pubblicato: i tile restano identici (stessi lotti, quindi le partite
    salvate restano valide) e si riscrive solo l'indice con comuni e linee di trasporto pubblico."""
    from Orbis.Core.World.Fb.RegionIndex import RegionIndex
    from Orbis.Core.World.Fb.Tile import Tile

    index_path = os.path.join(pack_dir, "index.bin")
    with open(index_path, "rb") as f:
        idx = RegionIndex.GetRootAs(encode.unpack(f.read()), 0)
    if idx.Format() != encode.FORMAT_VERSION:
        raise ValueError(f"formato dell'indice {idx.Format()} non supportato")
    z = idx.Z()
    entries = []
    road_ids = set()
    for i in range(idx.TilesLength()):
        e = idx.Tiles(i)
        entries.append((e.X(), e.Y(), e.Size(), e.Crc(), e.Buildings()))
        with open(os.path.join(pack_dir, str(z), str(e.X()), f"{e.Y()}.bin"), "rb") as f:
            data_bytes = f.read()
        if len(data_bytes) != e.Size() or (zlib.crc32(data_bytes) & 0xFFFFFFFF) != e.Crc():
            raise ValueError(f"tile {z}/{e.X()}/{e.Y()} diverso dall'indice")
        t = Tile.GetRootAs(encode.unpack(data_bytes), 0)
        for j in range(t.RoadsLength()):
            road_ids.add(t.Roads(j).OsmId())
    far = None
    fo = idx.Far()
    if fo is not None:
        far = {"x0": fo.X0(), "y0": fo.Y0(), "cell": fo.Cell(), "cols": fo.Cols(), "rows": fo.Rows(),
               "base": fo.Base(), "step": fo.Step(), "values": fo.HeightsAsNumpy()}
    old_version = idx.Version()
    version = version if version is not None else old_version + 1
    old_source = (idx.Source() or b"").decode("utf-8")
    source = old_source + (f" · trasporti e comuni: {source_name}" if source_name else "")
    log(f"pack v{old_version}: {len(entries)} tile, {len(road_ids):,} strade")

    scale = geo.scale_at(idx.OriginLat())
    outer_area = _merc_geom(boundary_lonlat).buffer(region.get("outer_buffer_m", 12000) / scale)
    log("lettura OSM…")
    data = osm.load(osm_path, log=log)
    stops, routes = transit(data, outer_area, log=log, road_ids=road_ids)
    muns = [(code, name, municipality_rings(geom)) for code, name, geom in municipalities]
    created = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    index = encode.encode_index(
        (idx.Key() or b"").decode("utf-8"), (idx.Name() or b"").decode("utf-8"), version, z, entries,
        idx.OriginLat(), idx.OriginLon(), idx.BoundaryAsNumpy().reshape(-1, 2),
        (idx.Attribution() or b"").decode("utf-8"), created, source, far=far,
        municipalities=muns, stops=stops, routes=routes)
    with open(index_path, "wb") as f:
        f.write(index)
    report_path = os.path.join(pack_dir, "report.json")
    report = {}
    if os.path.exists(report_path):
        with open(report_path, encoding="utf-8") as f:
            report = json.load(f)
    report.update({"version": version, "created": created, "source": source, "base_version": old_version,
                   "municipalities": len(muns), "transit": {"routes": len(routes), "stops": len(stops)}})
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    log(f"indice v{version}: {len(muns)} comuni, {len(routes)} linee, {len(stops)} fermate")
    return report

