"""Anteprime PNG del pack (controllo visivo in CI): centro città con lotti e panoramica della regione."""
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw

from Orbis.Core.World.Fb.AreaKind import AreaKind as AK
from Orbis.Core.World.Fb.RegionIndex import RegionIndex
from Orbis.Core.World.Fb.Tile import Tile

from . import encode, geo

AREA_COLORS = {
    AK.Residential: (233, 223, 210), AK.Commercial: (238, 214, 204), AK.Retail: (240, 210, 200),
    AK.Industrial: (220, 210, 225), AK.Park: (170, 214, 150), AK.Garden: (180, 220, 160), AK.Grass: (190, 225, 165),
    AK.Meadow: (200, 228, 170), AK.Pitch: (140, 200, 130), AK.Stadium: (150, 205, 140), AK.Forest: (110, 165, 100),
    AK.Scrub: (160, 190, 120), AK.Grassland: (205, 225, 165), AK.Farmland: (238, 228, 175), AK.Vineyard: (190, 175, 110),
    AK.Orchard: (180, 210, 130), AK.OliveGrove: (165, 180, 120), AK.Beach: (245, 230, 180), AK.Sand: (240, 225, 175),
    AK.BareRock: (200, 195, 185), AK.Water: (150, 195, 230), AK.River: (150, 195, 230), AK.Lake: (150, 195, 230),
    AK.Reservoir: (150, 195, 230), AK.Sea: (140, 185, 225), AK.Cemetery: (190, 210, 180), AK.Parking: (215, 215, 215),
    AK.Pedestrian: (230, 225, 215), AK.Meadowland: (205, 228, 170),
}


def _read(path):
    with open(path, "rb") as f:
        raw = encode.unpack(f.read())
    return Tile.GetRootAs(raw, 0)


def _poly(draw, pts, ox, oy, k, h, fill=None, outline=None, width=1):
    p = pts.reshape(-1, 2).astype(np.float64)
    xy = [(ox + x * k, h - (oy + y * k)) for x, y in p]
    if len(xy) >= 3:
        draw.polygon(xy, fill=fill, outline=outline, width=width)


def render(pack_dir, tiles, px_per_tile, detail=True):
    z = 14
    xs = [t[0] for t in tiles]
    ys = [t[1] for t in tiles]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    w = (x1 - x0 + 1) * px_per_tile
    h = (y1 - y0 + 1) * px_per_tile
    img = Image.new("RGB", (w, h), (225, 232, 210))
    draw = ImageDraw.Draw(img)
    k = px_per_tile / (geo.tile_size(z) * 100.0)  # pixel per cm Mercatore
    for tx, ty in tiles:
        path = os.path.join(pack_dir, str(z), str(tx), f"{ty}.bin")
        if not os.path.exists(path):
            continue
        t = _read(path)
        ox = (tx - x0) * px_per_tile
        oy = (y1 - ty) * px_per_tile
        ter = t.Terrain()
        if ter is not None:
            hv = ter.Base() / 100.0 + ter.HeightsAsNumpy().astype(np.float64).reshape(65, 65) * ter.Step() / 1000.0
            shade = np.clip(200 + (hv - 300) * 0.08, 150, 245).astype(np.uint8)
            tim = Image.fromarray(np.flipud(shade)).resize((px_per_tile, px_per_tile)).convert("RGB")
            img.paste(tim, (ox, h - oy - px_per_tile))
        areas = sorted((t.Areas(i) for i in range(t.AreasLength())), key=lambda a: a.Kind() >= 128)
        for a in areas:
            _poly(draw, a.OuterAsNumpy(), ox, oy, k, h, fill=AREA_COLORS.get(a.Kind(), (215, 220, 200)))
        for i in range(t.RoadsLength()):
            r = t.Roads(i)
            p = r.PtsAsNumpy().reshape(-1, 2).astype(np.float64)
            if not detail and r.Cls() > 4:
                continue
            width = max(1, int(r.Width() / 100.0 * k * 100 / math.cos(math.radians(42.35))))
            draw.line([(ox + x * k, h - (oy + y * k)) for x, y in p], fill=(255, 255, 255) if r.Cls() < 32 else (90, 90, 90),
                      width=width)
        if detail:
            for i in range(t.LotsLength()):
                _poly(draw, t.Lots(i).OuterAsNumpy(), ox, oy, k, h, outline=(230, 120, 40))
            for i in range(t.BuildingsLength()):
                b = t.Buildings(i)
                col = (200, 80, 60) if b.Landmark() else (150, 140, 130)
                _poly(draw, b.OuterAsNumpy(), ox, oy, k, h, fill=col)
            tp = t.TreePtsAsNumpy()
            if isinstance(tp, np.ndarray):
                for x, y in tp.reshape(-1, 2):
                    cx, cy = ox + x * k, h - (oy + y * k)
                    draw.ellipse((cx - 2, cy - 2, cx + 2, cy + 2), fill=(60, 130, 60))
    return img


def make_previews(pack_dir, out_dir, region):
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(pack_dir, "index.bin"), "rb") as f:
        idx = RegionIndex.GetRootAs(encode.unpack(f.read()), 0)
    tiles = [(idx.Tiles(i).X(), idx.Tiles(i).Y()) for i in range(idx.TilesLength())]
    mx, my = geo.to_merc(region["origin"]["lon"], region["origin"]["lat"])
    cx, cy = geo.tile_of(14, float(mx), float(my))
    centre = [(cx + dx, cy + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (cx + dx, cy + dy) in set(tiles)]
    render(pack_dir, centre, 1400).save(os.path.join(out_dir, "centro.png"))
    render(pack_dir, tiles, 48, detail=False).save(os.path.join(out_dir, "regione.png"))
