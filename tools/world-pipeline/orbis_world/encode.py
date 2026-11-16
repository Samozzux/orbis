"""Serializzazione FlatBuffers + compressione Deflate dei tile e dell'indice (docs/02 §2 punti 6-7)."""
import struct
import zlib

import flatbuffers
import numpy as np

from Orbis.Core.World.Fb import Area as FA
from Orbis.Core.World.Fb import Building as FB
from Orbis.Core.World.Fb import Line as FL
from Orbis.Core.World.Fb import Lot as FLot
from Orbis.Core.World.Fb import Poi as FP
from Orbis.Core.World.Fb import RegionIndex as FR
from Orbis.Core.World.Fb import Ring as FRing
from Orbis.Core.World.Fb import Road as FRoad
from Orbis.Core.World.Fb import Terrain as FTer
from Orbis.Core.World.Fb import Tile as FT
from Orbis.Core.World.Fb import TileEntry as FTE

FORMAT_VERSION = 1  # deve coincidere con TileReader.FormatVersion (C#)
MAGIC = b"OTZ1"
FILE_IDENTIFIER = b"ORBT"


def pack(buf):
    """Contenitore: "OTZ1" + dimensione non compressa (uint32 LE) + Deflate raw."""
    c = zlib.compressobj(9, zlib.DEFLATED, -15, 9)
    data = c.compress(bytes(buf)) + c.flush()
    return MAGIC + struct.pack("<I", len(buf)) + data


def unpack(data):
    assert data[:4] == MAGIC, "file del mondo non riconosciuto"
    size = struct.unpack("<I", data[4:8])[0]
    raw = zlib.decompress(data[8:], -15)
    assert len(raw) == size
    return raw


def _ivec(b, arr):
    return b.CreateNumpyVector(np.ascontiguousarray(arr, dtype=np.int32).ravel())


def _str(b, s):
    return b.CreateString(s) if s else None


def _holes(b, start_vector, holes):
    if not holes:
        return None
    rings = []
    for h in holes:
        pts = _ivec(b, h)
        FRing.RingStart(b)
        FRing.RingAddPts(b, pts)
        rings.append(FRing.RingEnd(b))
    start_vector(b, len(rings))
    for r in reversed(rings):
        b.PrependUOffsetTRelative(r)
    return b.EndVector()


def _table_vector(b, start_vector, offsets):
    if not offsets:
        return None
    start_vector(b, len(offsets))
    for o in reversed(offsets):
        b.PrependUOffsetTRelative(o)
    return b.EndVector()


def encode_tile(z, x, y, tile):
    """`tile`: dict con liste già in coordinate locali intere (vedi tiler.py). Restituisce bytes compressi."""
    b = flatbuffers.Builder(1 << 16)

    buildings = []
    for e in tile.get("buildings", []):
        outer = _ivec(b, e["outer"])
        holes = _holes(b, FB.BuildingStartHolesVector, e.get("holes"))
        name, addr, lm = _str(b, e.get("name")), _str(b, e.get("address")), _str(b, e.get("landmark"))
        a = e["attrs"]
        FB.BuildingStart(b)
        FB.BuildingAddOsmId(b, e["osm_id"])
        FB.BuildingAddOsmType(b, e["osm_type"])
        FB.BuildingAddOuter(b, outer)
        if holes:
            FB.BuildingAddHoles(b, holes)
        FB.BuildingAddHeight(b, int(round(a["height"] * 100)))
        if a["min_height"]:
            FB.BuildingAddMinHeight(b, int(round(a["min_height"] * 100)))
        FB.BuildingAddLevels(b, a["levels"])
        FB.BuildingAddRoofShape(b, a["roof"])
        if a["roof_height"]:
            FB.BuildingAddRoofHeight(b, int(round(a["roof_height"] * 100)))
        FB.BuildingAddUse(b, a["use"])
        FB.BuildingAddMaterial(b, a["material"])
        FB.BuildingAddWallColor(b, a["wall_color"])
        FB.BuildingAddRoofColor(b, a["roof_color"])
        FB.BuildingAddFlags(b, a["flags"])
        FB.BuildingAddYear(b, a["year"])
        if name:
            FB.BuildingAddName(b, name)
        if addr:
            FB.BuildingAddAddress(b, addr)
        if lm:
            FB.BuildingAddLandmark(b, lm)
        buildings.append(FB.BuildingEnd(b))

    roads = []
    for e in tile.get("roads", []):
        pts = _ivec(b, e["pts"])
        a = e["attrs"]
        name, ref = _str(b, a.get("name")), _str(b, a.get("ref"))
        FRoad.RoadStart(b)
        FRoad.RoadAddOsmId(b, e["osm_id"])
        FRoad.RoadAddPts(b, pts)
        FRoad.RoadAddCls(b, a["cls"])
        FRoad.RoadAddWidth(b, min(65535, int(round(a["width"] * 100))))
        FRoad.RoadAddLanes(b, a["lanes"])
        FRoad.RoadAddOneway(b, a["oneway"])
        FRoad.RoadAddLayer(b, a["layer"])
        FRoad.RoadAddFlags(b, a["flags"])
        FRoad.RoadAddMaxSpeed(b, min(255, a["max_speed"]))
        if name:
            FRoad.RoadAddName(b, name)
        if ref:
            FRoad.RoadAddRef(b, ref)
        roads.append(FRoad.RoadEnd(b))

    lines = []
    for e in tile.get("lines", []):
        pts = _ivec(b, e["pts"])
        a = e["attrs"]
        name = _str(b, a.get("name"))
        FL.LineStart(b)
        FL.LineAddOsmId(b, e["osm_id"])
        FL.LineAddKind(b, a["kind"])
        FL.LineAddPts(b, pts)
        FL.LineAddWidth(b, min(65535, int(round(a["width"] * 100))))
        if name:
            FL.LineAddName(b, name)
        lines.append(FL.LineEnd(b))

    areas = []
    for e in tile.get("areas", []):
        outer = _ivec(b, e["outer"])
        holes = _holes(b, FA.AreaStartHolesVector, e.get("holes"))
        name, lm = _str(b, e.get("name")), _str(b, e.get("landmark"))
        FA.AreaStart(b)
        FA.AreaAddOsmId(b, e["osm_id"])
        FA.AreaAddOsmType(b, e["osm_type"])
        FA.AreaAddKind(b, e["kind"])
        FA.AreaAddOuter(b, outer)
        if holes:
            FA.AreaAddHoles(b, holes)
        if name:
            FA.AreaAddName(b, name)
        if lm:
            FA.AreaAddLandmark(b, lm)
        areas.append(FA.AreaEnd(b))

    pois = []
    for e in tile.get("pois", []):
        cat, name, addr = _str(b, e["category"]), _str(b, e.get("name")), _str(b, e.get("address"))
        FP.PoiStart(b)
        FP.PoiAddOsmId(b, e["osm_id"])
        FP.PoiAddOsmType(b, e["osm_type"])
        FP.PoiAddX(b, e["x"])
        FP.PoiAddY(b, e["y"])
        if cat:
            FP.PoiAddCategory(b, cat)
        if name:
            FP.PoiAddName(b, name)
        if addr:
            FP.PoiAddAddress(b, addr)
        FP.PoiAddBuilding(b, e.get("building", -1))
        pois.append(FP.PoiEnd(b))

    lots = []
    for e in tile.get("lots", []):
        outer = _ivec(b, e["outer"])
        holes = _holes(b, FLot.LotStartHolesVector, e.get("holes"))
        FLot.LotStart(b)
        FLot.LotAddId(b, e["id"])
        FLot.LotAddKind(b, e["kind"])
        FLot.LotAddOuter(b, outer)
        if holes:
            FLot.LotAddHoles(b, holes)
        FLot.LotAddGridX(b, e["grid_x"])
        FLot.LotAddGridY(b, e["grid_y"])
        FLot.LotAddGridAngle(b, e["grid_angle"])
        FLot.LotAddFrontRoad(b, e["front_road"])
        FLot.LotAddArea(b, e["area"])
        lots.append(FLot.LotEnd(b))

    trees = tile.get("trees")
    tree_pts = tree_kinds = tree_heights = None
    if trees is not None and len(trees["pts"]):
        tree_pts = _ivec(b, trees["pts"])
        tree_kinds = b.CreateNumpyVector(np.asarray(trees["kinds"], dtype=np.uint8))
        tree_heights = b.CreateNumpyVector(np.asarray(trees["heights"], dtype=np.uint8))

    terrain = None
    if tile.get("terrain") is not None:
        size, base, step, values = tile["terrain"]
        hv = b.CreateNumpyVector(np.ascontiguousarray(values, dtype=np.uint16).ravel())
        FTer.TerrainStart(b)
        FTer.TerrainAddSize(b, size)
        FTer.TerrainAddBase(b, base)
        FTer.TerrainAddStep(b, step)
        FTer.TerrainAddHeights(b, hv)
        terrain = FTer.TerrainEnd(b)

    v_buildings = _table_vector(b, FT.TileStartBuildingsVector, buildings)
    v_roads = _table_vector(b, FT.TileStartRoadsVector, roads)
    v_lines = _table_vector(b, FT.TileStartLinesVector, lines)
    v_areas = _table_vector(b, FT.TileStartAreasVector, areas)
    v_pois = _table_vector(b, FT.TileStartPoisVector, pois)
    v_lots = _table_vector(b, FT.TileStartLotsVector, lots)

    FT.TileStart(b)
    FT.TileAddFormat(b, FORMAT_VERSION)
    FT.TileAddZ(b, z)
    FT.TileAddX(b, x)
    FT.TileAddY(b, y)
    if v_buildings:
        FT.TileAddBuildings(b, v_buildings)
    if v_roads:
        FT.TileAddRoads(b, v_roads)
    if v_lines:
        FT.TileAddLines(b, v_lines)
    if v_areas:
        FT.TileAddAreas(b, v_areas)
    if v_pois:
        FT.TileAddPois(b, v_pois)
    if tree_pts:
        FT.TileAddTreePts(b, tree_pts)
        FT.TileAddTreeKinds(b, tree_kinds)
        FT.TileAddTreeHeights(b, tree_heights)
    if v_lots:
        FT.TileAddLots(b, v_lots)
    if terrain:
        FT.TileAddTerrain(b, terrain)
    root = FT.TileEnd(b)
    b.Finish(root, file_identifier=FILE_IDENTIFIER)
    return pack(b.Output())


def encode_index(key, name, version, z, entries, origin_lat, origin_lon, boundary_lonlat, attribution,
                 created, source):
    """`entries`: lista di (x, y, size, crc, buildings)."""
    b = flatbuffers.Builder(1 << 14)
    s_key, s_name = b.CreateString(key), b.CreateString(name)
    s_attr, s_created, s_source = b.CreateString(attribution), b.CreateString(created), b.CreateString(source)
    v_boundary = b.CreateNumpyVector(np.asarray(boundary_lonlat, dtype=np.float64).ravel())
    entries = sorted(entries, key=lambda e: (e[1], e[0]))
    FR.RegionIndexStartTilesVector(b, len(entries))
    for e in reversed(entries):
        FTE.CreateTileEntry(b, *e)
    v_tiles = b.EndVector()
    FR.RegionIndexStart(b)
    FR.RegionIndexAddFormat(b, FORMAT_VERSION)
    FR.RegionIndexAddKey(b, s_key)
    FR.RegionIndexAddName(b, s_name)
    FR.RegionIndexAddVersion(b, version)
    FR.RegionIndexAddZ(b, z)
    FR.RegionIndexAddTiles(b, v_tiles)
    FR.RegionIndexAddOriginLat(b, origin_lat)
    FR.RegionIndexAddOriginLon(b, origin_lon)
    FR.RegionIndexAddBoundary(b, v_boundary)
    FR.RegionIndexAddAttribution(b, s_attr)
    FR.RegionIndexAddCreated(b, s_created)
    FR.RegionIndexAddSource(b, s_source)
    root = FR.RegionIndexEnd(b)
    b.Finish(root)
    return pack(b.Output())
