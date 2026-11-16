"""Riga di comando della pipeline del mondo.

  python -m orbis_world boundary --region regions/chieti.json --pbf admin.pbf --out boundary.geojson
  python -m orbis_world build --region regions/chieti.json --pbf region.pbf --boundary boundary.geojson \
      --out out/chieti --cache cache --version 1
"""
import argparse
import json
import os
import sys
import time

import numpy as np
import shapely
from shapely.geometry import mapping, shape

from . import geo, osm, terrain, tiler

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


def _log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def _region(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def cmd_boundary(a):
    region = _region(a.region)
    b = region["boundary"]
    geom, tags = osm.find_boundary(a.pbf, b["admin_level"], b["match"], log=_log)
    if geom is None:
        _log("confine non trovato")
        return 1
    buffer_m = region.get("outer_buffer_m", 12000) + 3000
    merc = shapely.transform(geom, lambda c: np.column_stack(geo.to_merc(c[:, 0], c[:, 1])))
    minx, miny, maxx, maxy = merc.buffer(buffer_m / geo.scale_at(region["origin"]["lat"])).bounds
    lon0, lat0 = geo.to_lonlat(minx, miny)
    lon1, lat1 = geo.to_lonlat(maxx, maxy)
    bbox = [round(float(lon0), 5), round(float(lat0), 5), round(float(lon1), 5), round(float(lat1), 5)]
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"type": "Feature", "properties": {"name": tags.get("name"), "bbox_extract": bbox},
                   "geometry": mapping(geom)}, f)
    print(",".join(str(v) for v in bbox))
    return 0


def cmd_build(a):
    region = _region(a.region)
    with open(a.boundary, encoding="utf-8") as f:
        boundary = shape(json.load(f)["geometry"])
    landmarks = []
    if region.get("landmarks"):
        with open(os.path.join(ROOT, region["landmarks"]), encoding="utf-8") as f:
            landmarks = json.load(f)["landmarks"]
    merc = shapely.transform(boundary, lambda c: np.column_stack(geo.to_merc(c[:, 0], c[:, 1])))
    pad = (region.get("outer_buffer_m", 12000) + 3000) / geo.scale_at(region["origin"]["lat"])
    source = terrain.TerrariumSource(merc.buffer(pad).bounds, a.cache, log=_log)
    tiler.build(region, a.pbf, boundary, source, a.out, landmarks=landmarks, version=a.version,
                source_name=a.source or os.path.basename(a.pbf), log=_log)
    return 0


def cmd_preview(a):
    from . import preview
    preview.make_previews(a.pack, a.out, _region(a.region))
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(prog="orbis_world")
    sub = p.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("boundary", help="trova il confine della regione e stampa il bbox da estrarre")
    b.add_argument("--region", required=True)
    b.add_argument("--pbf", required=True)
    b.add_argument("--out", required=True)
    c = sub.add_parser("build", help="costruisce il region pack")
    c.add_argument("--region", required=True)
    c.add_argument("--pbf", required=True)
    c.add_argument("--boundary", required=True)
    c.add_argument("--out", required=True)
    c.add_argument("--cache", default="cache")
    c.add_argument("--version", type=int, default=1)
    c.add_argument("--source", default="")
    v = sub.add_parser("preview", help="immagini PNG di controllo del pack")
    v.add_argument("--pack", required=True)
    v.add_argument("--out", required=True)
    v.add_argument("--region", required=True)
    a = p.parse_args(argv)
    return {"boundary": cmd_boundary, "build": cmd_build, "preview": cmd_preview}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
