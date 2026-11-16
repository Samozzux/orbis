"""Terreno dai Terrain Tiles (AWS Open Data, formato Terrarium) — docs/02 §2 punto 4."""
import io
import math
import os
import time
import urllib.request

import numpy as np
from PIL import Image

from . import geo

TERRARIUM_URL = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png"
SAMPLES = 65  # campioni per lato del tile z14 (docs/02 §2)


def decode_terrarium(rgb):
    """PNG Terrarium (array H×W×3 uint8) → quote in metri."""
    rgb = rgb.astype(np.float64)
    return rgb[..., 0] * 256.0 + rgb[..., 1] + rgb[..., 2] / 256.0 - 32768.0


class TerrariumSource:
    """Mosaico di tile Terrarium a zoom `z` che copre un rettangolo Mercatore, con campionamento bilineare."""

    def __init__(self, bounds, cache_dir, z=12, log=print, fetch=None):
        self.z = z
        self.cache_dir = cache_dir
        self.log = log
        self._fetch = fetch or self._download
        minx, miny, maxx, maxy = bounds
        tiles = list(geo.tile_range(z, minx, miny, maxx, maxy))
        self.tx0 = min(t[0] for t in tiles)
        self.ty0 = min(t[1] for t in tiles)
        tx1 = max(t[0] for t in tiles)
        ty1 = max(t[1] for t in tiles)
        w = (tx1 - self.tx0 + 1) * 256
        h = (ty1 - self.ty0 + 1) * 256
        self.mosaic = np.zeros((h, w), dtype=np.float32)
        log(f"  terreno: {len(tiles)} tile Terrarium z{z}")
        for tx, ty in tiles:
            img = self._fetch(z, tx, ty)
            ox = (tx - self.tx0) * 256
            oy = (ty - self.ty0) * 256
            self.mosaic[oy:oy + 256, ox:ox + 256] = img

    def _download(self, z, x, y):
        path = os.path.join(self.cache_dir, f"terrarium-{z}-{x}-{y}.png")
        if not os.path.exists(path):
            os.makedirs(self.cache_dir, exist_ok=True)
            url = TERRARIUM_URL.format(z=z, x=x, y=y)
            for attempt in range(5):
                try:
                    req = urllib.request.Request(url, headers={"User-Agent": "orbis-world-pipeline"})
                    with urllib.request.urlopen(req, timeout=60) as r:
                        data = r.read()
                    with open(path + ".tmp", "wb") as f:
                        f.write(data)
                    os.replace(path + ".tmp", path)
                    break
                except OSError as e:
                    if attempt == 4:
                        raise
                    self.log(f"  nuovo tentativo per {url}: {e}")
                    time.sleep(2 + attempt * 3)
        with Image.open(path) as im:
            return decode_terrarium(np.asarray(im.convert("RGB")))

    def sample(self, xs, ys):
        """Quote (m) nei punti Mercatore (array)."""
        size = geo.tile_size(self.z)
        gx = (np.asarray(xs) + geo.HALF) / size * 256.0 - self.tx0 * 256 - 0.5
        gy = (geo.HALF - np.asarray(ys)) / size * 256.0 - self.ty0 * 256 - 0.5
        h, w = self.mosaic.shape
        gx = np.clip(gx, 0, w - 1.001)
        gy = np.clip(gy, 0, h - 1.001)
        x0 = np.floor(gx).astype(np.int64)
        y0 = np.floor(gy).astype(np.int64)
        fx = gx - x0
        fy = gy - y0
        m = self.mosaic
        a = m[y0, x0] * (1 - fx) + m[y0, x0 + 1] * fx
        b = m[y0 + 1, x0] * (1 - fx) + m[y0 + 1, x0 + 1] * fx
        return a * (1 - fy) + b * fy


class FunctionSource:
    """Terreno da funzione (test): f(x, y) con array Mercatore → quote."""

    def __init__(self, f):
        self.f = f

    def sample(self, xs, ys):
        return self.f(np.asarray(xs, dtype=np.float64), np.asarray(ys, dtype=np.float64))


def tile_heights(source, z, tx, ty, n=SAMPLES):
    """Griglia n×n di quote del tile: righe da sud a nord, colonne da ovest a est, bordi inclusi."""
    x0, y0, x1, y1 = geo.tile_bounds(z, tx, ty)
    xs = np.linspace(x0, x1, n)
    ys = np.linspace(y0, y1, n)
    gx, gy = np.meshgrid(xs, ys)  # gy[0] = sud
    return source.sample(gx.ravel(), gy.ravel()).reshape(n, n)


def quantize(heights):
    """Quote in metri → (base cm, passo mm, valori uint16)."""
    h = np.asarray(heights, dtype=np.float64)
    lo = float(np.min(h))
    hi = float(np.max(h))
    base_cm = int(math.floor(lo * 100.0))
    range_mm = (hi * 1000.0) - base_cm * 10.0
    step = max(10, int(math.ceil(range_mm / 65535.0)))
    values = np.round((h * 1000.0 - base_cm * 10.0) / step)
    values = np.clip(values, 0, 65535).astype(np.uint16)
    return base_cm, step, values
