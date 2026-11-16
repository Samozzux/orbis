"""Test della pipeline con una piccola città finta (nessun download): pytest tools/world-pipeline/tests"""
import math
import os
import shutil
import sys

import numpy as np
import pytest
import shapely

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import orbis_world  # noqa: E402,F401  (aggiunge il codice generato al path)
from orbis_world import encode, geo, osm, terrain, tiler  # noqa: E402
from orbis_world import tags as T  # noqa: E402
from Orbis.Core.World.Fb.AreaKind import AreaKind as AK  # noqa: E402
from Orbis.Core.World.Fb.BuildingFlag import BuildingFlag as BF  # noqa: E402
from Orbis.Core.World.Fb.LotKind import LotKind  # noqa: E402
from Orbis.Core.World.Fb.RegionIndex import RegionIndex  # noqa: E402
from Orbis.Core.World.Fb.RoadClass import RoadClass as RC  # noqa: E402
from Orbis.Core.World.Fb.RoofShape import RoofShape as RS  # noqa: E402
from Orbis.Core.World.Fb.Tile import Tile  # noqa: E402
from Orbis.Core.World.Fb.TreeKind import TreeKind as TK  # noqa: E402

LAT0, LON0 = 42.3524445, 14.166459
HERE = os.path.dirname(os.path.abspath(__file__))
FIXTURES_CS = os.path.join(HERE, "..", "..", "..", "tests", "Orbis.Core.Tests", "Fixtures")


class OsmWriter:
    """Scrive un file .osm XML: coordinate in metri veri attorno all'origine (x est, y nord)."""

    def __init__(self):
        self.nodes = []
        self.ways = []
        self.relations = []
        self.next_node = 1
        self.next_way = 1000

    def node(self, x, y, **tags):
        lat = LAT0 + y / 111132.0
        lon = LON0 + x / (111320.0 * math.cos(math.radians(LAT0)))
        nid = self.next_node
        self.next_node += 1
        self.nodes.append((nid, lat, lon, tags))
        return nid

    def way(self, pts, closed=False, **tags):
        ids = [self.node(x, y) for x, y in pts]
        if closed:
            ids.append(ids[0])
        wid = self.next_way
        self.next_way += 1
        self.ways.append((wid, ids, tags))
        return wid

    def rect(self, x0, y0, x1, y1, **tags):
        return self.way([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], closed=True, **tags)

    def relation(self, members, **tags):
        rid = 5000 + len(self.relations)
        self.relations.append((rid, members, tags))
        return rid

    def write(self, path):
        def t(tags):
            return "".join(f'<tag k="{k.replace("_", ":") if k.startswith("addr_") or k.startswith("building_") or k.startswith("roof_") else k}" v="{v}"/>'
                           for k, v in tags.items())
        with open(path, "w", encoding="utf-8") as f:
            f.write('<?xml version="1.0" encoding="UTF-8"?>\n<osm version="0.6">\n')
            for nid, lat, lon, tags in self.nodes:
                f.write(f'<node id="{nid}" version="1" lat="{lat:.8f}" lon="{lon:.8f}">{t(tags)}</node>\n')
            for wid, ids, tags in self.ways:
                f.write(f'<way id="{wid}" version="1">' + "".join(f'<nd ref="{i}"/>' for i in ids) + t(tags) + "</way>\n")
            for rid, members, tags in self.relations:
                f.write(f'<relation id="{rid}" version="1">' +
                        "".join(f'<member type="way" ref="{m}" role="{r}"/>' for m, r in members) + t(tags) +
                        "</relation>\n")
            f.write("</osm>\n")


def make_city(path):
    w = OsmWriter()
    # griglia di strade: due vie est-ovest e due nord-sud (isolati di ~100 m)
    w.way([(-300, 0), (300, 0)], highway="residential", name="Corso Marrucino")
    w.way([(-300, 120), (300, 120)], highway="tertiary", name="Via Arniense")
    w.way([(0, -200), (0, 300)], highway="residential", name="Via Pollione", oneway="yes")
    w.way([(130, -200), (130, 300)], highway="primary", name="Viale Amendola", ref="SS 5")
    w.way([(-300, -200), (300, -200), (300, 300), (-300, 300)], closed=True, highway="unclassified")
    # edifici dentro l'isolato tra le vie (0..130, 0..120)
    w.rect(10, 10, 40, 30, building="apartments", building_levels="5", building_colour="#e8c48a",
           addr_street="Corso Marrucino", addr_housenumber="12")
    w.rect(60, 10, 75, 22, building="yes")
    w.rect(80, 60, 120, 100, building="church", name="Cattedrale di San Giustino", amenity="place_of_worship")
    # edificio con parti: il contorno va sostituito dalle parti
    w.rect(20, 60, 60, 100, building="yes", name="Palazzo de' Mayo")
    w.rect(20, 60, 40, 100, building_part="yes", height="14")
    w.rect(40, 60, 60, 100, building_part="yes", height="20", roof_shape="hipped")
    # campagna
    w.rect(-280, -180, -150, -60, landuse="vineyard")
    w.rect(-140, -180, -20, -60, landuse="orchard", trees="olive_trees")
    w.rect(150, 140, 280, 280, landuse="farmland", crop="wheat")
    # alberi, POI, linea elettrica
    w.node(-50, 50, natural="tree", genus="Olea")
    w.node(-60, 50, natural="tree", leaf_type="needleleaved")
    w.node(30, 20, amenity="bar", name="Bar Centrale")
    w.node(-100, 200, shop="bakery", name="Forno Abruzzese")
    w.way([(-300, 250), (300, 260)], power="line")
    w.way([(-250, 210), (-200, 230), (-150, 210)], waterway="stream", name="Fosso")
    # confine "provincia"
    b = w.way([(-320, -220), (320, -220), (320, 320), (-320, 320)], closed=True)
    w.relation([(b, "outer")], type="boundary", boundary="administrative", admin_level="6", ref="CH",
               name="Chieti")
    w.write(path)


@pytest.fixture(scope="module")
def city(tmp_path_factory):
    d = tmp_path_factory.mktemp("city")
    path = str(d / "city.osm")
    make_city(path)
    return path, d


def test_tags_heights_and_roofs():
    a = T.building_attrs(1, {"building": "apartments", "building:levels": "5"}, 600)
    assert a["levels"] == 5
    assert abs(a["height"] - (5 * 3.2 + 1)) < 1e-6
    assert a["flags"] & BF.HeightEstimated
    b = T.building_attrs(2, {"building": "house", "height": "8,5 m", "roof:shape": "gabled"}, 100)
    assert b["height"] == 8.5 and b["roof"] == RS.Gabled
    assert not b["flags"] & BF.HeightEstimated and not b["flags"] & BF.RoofEstimated
    # stima stabile: stesso ID → stessa altezza
    assert T.building_attrs(7, {"building": "yes"}, 200)["height"] == T.building_attrs(7, {"building": "yes"}, 200)["height"]
    assert T.parse_color("#FFF") == 0x1FFFFFF
    assert T.parse_color("red") & 0x1000000
    assert T.parse_color("boh") == 0


def test_tags_roads_areas_trees():
    r = T.road_attrs({"highway": "primary_link", "bridge": "yes", "maxspeed": "50"})
    assert r["cls"] == RC.Primary and r["max_speed"] == 50
    assert T.road_attrs({"highway": "proposed"}) is None
    assert T.road_attrs({"railway": "rail"})["cls"] == RC.Rail
    assert T.road_attrs({"highway": "motorway"})["oneway"] == 1
    assert T.area_kind({"landuse": "vineyard"}) == AK.Vineyard
    assert T.area_kind({"landuse": "orchard", "trees": "olive_trees"}) == AK.OliveGrove
    assert T.area_kind({"landuse": "orchard"}) == AK.Orchard
    assert T.area_kind({"natural": "water", "water": "lake"}) == AK.Lake
    assert T.area_kind({"leisure": "stadium", "landuse": "recreation_ground"}) == AK.Stadium
    assert T.tree_kind({"genus": "Olea"}) == TK.Olive
    assert T.tree_kind({"species": "Pinus pinea"}) == TK.StonePine
    assert T.poi_category({"amenity": "bench"}) is None
    assert T.poi_category({"amenity": "pharmacy"}) == "amenity/pharmacy"


def test_geo_roundtrip():
    x, y = geo.to_merc(LON0, LAT0)
    lon, lat = geo.to_lonlat(x, y)
    assert abs(lon - LON0) < 1e-9 and abs(lat - LAT0) < 1e-9
    tx, ty = geo.tile_of(14, float(x), float(y))
    x0, y0, x1, y1 = geo.tile_bounds(14, tx, ty)
    assert x0 <= x < x1 and y0 <= y < y1
    assert abs(geo.tile_size(14) - 2445.98490512564) < 1e-6


def test_terrain_quantize_roundtrip():
    h = np.linspace(-12.3, 2793.4, 65 * 65).reshape(65, 65)
    base, step, v = terrain.quantize(h)
    back = base / 100.0 + v.astype(np.float64) * step / 1000.0
    assert np.max(np.abs(back - h)) <= step / 1000.0
    flat = np.full((65, 65), 330.0)
    base, step, v = terrain.quantize(flat)
    assert step == 10 and np.all(base / 100.0 + v * step / 1000.0 == 330.0)


def test_terrarium_decode():
    rgb = np.array([[[128, 0, 0], [129, 74, 128]]], dtype=np.uint8)
    h = terrain.decode_terrarium(rgb)
    assert h[0, 0] == 0.0
    assert abs(h[0, 1] - (256 + 74 + 0.5)) < 1e-9


def test_pack_roundtrip():
    data = os.urandom(1000) + b"abc" * 5000
    assert encode.unpack(encode.pack(data)) == data


def test_build_region(city):
    path, d = city
    geom, tg = osm.find_boundary(path, 6, {"ref": "CH"}, log=lambda *_: None)
    assert geom is not None and tg["name"] == "Chieti"

    x0, y0 = geo.to_merc(LON0, LAT0)

    def hills(x, y):
        return 330.0 + (x - float(x0)) * 0.05 + (y - float(y0)) * 0.02

    region = {"key": "test", "name": "Città di prova", "origin": {"lat": LAT0, "lon": LON0}, "zoom": 14,
              "full_buffer_m": 100, "outer_buffer_m": 600}
    landmarks = [{"key": "cattedrale_san_giustino", "match": {"name_regex": "san giustino", "types": ["building"]}}]
    out = str(d / "pack")
    logs = []
    report = tiler.build(region, path, geom, terrain.FunctionSource(hills), out, landmarks=landmarks,
                         version=3, source_name="city.osm", log=logs.append)
    assert report["landmarks"][0]["matches"], logs

    with open(os.path.join(out, "index.bin"), "rb") as f:
        index_file = f.read()
    idx = RegionIndex.GetRootAs(encode.unpack(index_file), 0)
    assert idx.Format() == encode.FORMAT_VERSION and idx.Version() == 3 and idx.Key() == b"test"
    entries = [idx.Tiles(i) for i in range(idx.TilesLength())]
    assert len(entries) >= 1
    assert sum(e.Buildings() for e in entries) == 5  # 3 edifici + 2 parti (contorno sostituito)

    tx, ty = geo.tile_of(14, float(x0), float(y0))
    tile_path = os.path.join(out, "14", str(tx), f"{ty}.bin")
    with open(tile_path, "rb") as f:
        tile_file = f.read()
    t = Tile.GetRootAs(encode.unpack(tile_file), 0)
    assert Tile.TileBufferHasIdentifier(encode.unpack(tile_file), 0)
    assert (t.X(), t.Y(), t.Z()) == (tx, ty, 14)

    buildings = [t.Buildings(i) for i in range(t.BuildingsLength())]
    names = {b.Name() for b in buildings}
    assert b"Palazzo de' Mayo" in names  # nome ereditato dalle parti
    cath = next(b for b in buildings if b.Name() == b"Cattedrale di San Giustino")
    assert cath.Landmark() == b"cattedrale_san_giustino"
    apt = next(b for b in buildings if b.Address() == b"Corso Marrucino 12")
    assert apt.Levels() == 5 and apt.WallColor() == 0x1E8C48A
    outer = apt.OuterAsNumpy().reshape(-1, 2)
    assert len(outer) == 4
    # anello antiorario
    xs, ys = outer[:, 0].astype(float), outer[:, 1].astype(float)
    assert 0.5 * np.sum(xs * np.roll(ys, -1) - np.roll(xs, -1) * ys) > 0
    # lato reale 30 m: in cm Mercatore 30 / cos(lat) * 100
    width_cm = (xs.max() - xs.min())
    assert abs(width_cm * math.cos(math.radians(LAT0)) / 100.0 - 30.0) < 0.3

    kinds = {t.Areas(i).Kind() for i in range(t.AreasLength())}
    assert AK.Vineyard in kinds and AK.OliveGrove in kinds and AK.Farmland in kinds

    roads = [t.Roads(i) for i in range(t.RoadsLength())]
    amendola = next(r for r in roads if r.Name() == b"Viale Amendola")
    assert amendola.Cls() == RC.Primary and amendola.Ref() == b"SS 5"
    assert next(r for r in roads if r.Name() == b"Via Pollione").Oneway() == 1

    pois = [t.Pois(i) for i in range(t.PoisLength())]
    bar = next(p for p in pois if p.Name() == b"Bar Centrale")
    assert bar.Building() >= 0 and buildings[bar.Building()].Address() == b"Corso Marrucino 12"
    assert t.TreeKindsLength() == 2 and TK.Olive in [t.TreeKinds(i) for i in range(2)]

    lots = [t.Lots(i) for i in range(t.LotsLength())]
    assert len(lots) >= 4
    for lot in lots:
        angle = lot.GridAngle() / 1000.0
        # strade allineate agli assi: la griglia deve esserlo anche lei
        assert min(abs(angle - a) for a in (-180, -90, 0, 90, 180)) < 1.0, angle
    central = [l for l in lots if l.Kind() == LotKind.Urban]
    assert central, [l.Kind() for l in lots]
    # nessun lotto invade la sede stradale di Viale Amendola (x = 130 m, larga 8 m + marciapiedi)
    k = math.cos(math.radians(LAT0)) / 100.0
    tile_x0, _ = geo.tile_origin(14, tx, ty)
    road_x = (float(x0) - tile_x0) * math.cos(math.radians(LAT0)) + 130.0
    for lot in lots:
        pts = lot.OuterAsNumpy().reshape(-1, 2) * k
        assert not np.any((pts[:, 0] > road_x - 5.4) & (pts[:, 0] < road_x + 5.4))

    ter = t.Terrain()
    assert ter.Size() == 65
    h = ter.Base() / 100.0 + ter.HeightsAsNumpy().astype(float) * ter.Step() / 1000.0
    expected = terrain.tile_heights(terrain.FunctionSource(hills), 14, tx, ty).ravel()
    assert np.max(np.abs(h - expected)) <= ter.Step() / 1000.0

    if os.environ.get("ORBIS_WRITE_FIXTURES") == "1":
        os.makedirs(FIXTURES_CS, exist_ok=True)
        shutil.copyfile(tile_path, os.path.join(FIXTURES_CS, "tile_sample.bin"))
        shutil.copyfile(os.path.join(out, "index.bin"), os.path.join(FIXTURES_CS, "index_sample.bin"))


def test_cli_boundary_and_preview(city, tmp_path):
    from orbis_world import __main__ as cli
    from orbis_world import preview
    import json as _json
    path, d = city
    region_file = tmp_path / "r.json"
    region = {"key": "test", "name": "Prova", "origin": {"lat": LAT0, "lon": LON0},
              "boundary": {"admin_level": 6, "match": {"ref": "CH"}}, "outer_buffer_m": 600, "full_buffer_m": 100}
    region_file.write_text(_json.dumps(region))
    out = tmp_path / "b.geojson"
    assert cli.main(["boundary", "--region", str(region_file), "--pbf", path, "--out", str(out)]) == 0
    bbox = _json.loads(out.read_text())["properties"]["bbox_extract"]
    assert bbox[0] < LON0 < bbox[2] and bbox[1] < LAT0 < bbox[3]
    pack = d / "pack"
    if (pack / "index.bin").exists():
        preview.make_previews(str(pack), str(tmp_path / "prev"), region)
        assert (tmp_path / "prev" / "centro.png").exists() and (tmp_path / "prev" / "regione.png").exists()
