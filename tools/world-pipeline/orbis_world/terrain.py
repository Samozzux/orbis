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


def far_grid(source, bounds, cell):
    """Griglia di quote (righe da sud) che copre `bounds` (Mercatore) con celle di `cell` metri Mercatore."""
    minx, miny, maxx, maxy = bounds
    cols = int(math.ceil((maxx - minx) / cell)) + 1
    rows = int(math.ceil((maxy - miny) / cell)) + 1
    xs = minx + np.arange(cols) * cell
    ys = miny + np.arange(rows) * cell
    gx, gy = np.meshgrid(xs, ys)
    h = source.sample(gx.ravel(), gy.ravel()).reshape(rows, cols)
    base, step, values = quantize(h)
    return {"x0": float(minx), "y0": float(miny), "cell": float(cell), "cols": cols, "rows": rows,
            "base": base, "step": step, "values": values}


def globe_image(z=3, width=2048, cache_dir="cache", log=print, fetch=None):
    """Immagine equirettangolare della Terra (pack globo base): rilievi e fondali dai Terrain Tiles,
    colori ipsometrici e ombreggiatura. Restituisce un'immagine PIL RGB."""
    n = 1 << z
    src = TerrariumSource((-geo.HALF + 1, -geo.HALF + 1, geo.HALF - 1, geo.HALF - 1), cache_dir, z=z, log=log, fetch=fetch)
    height = width // 2
    lon = (np.arange(width) + 0.5) / width * 360.0 - 180.0
    lat = 90.0 - (np.arange(height) + 0.5) / height * 180.0
    glon, glat = np.meshgrid(lon, lat)
    x, y = geo.to_merc(glon, np.clip(glat, -geo.MAX_LAT, geo.MAX_LAT))
    h = src.sample(x.ravel(), y.ravel()).reshape(height, width)
    # oltre il limite del Mercatore (poli): ghiaccio sopra la terraferma, mare scuro
    polar = np.abs(glat) > 84.5
    h = np.where(polar & (glat > 0), -1000.0, h)
    h = np.where(polar & (glat < 0), 2500.0, h)
    land = h > 0
    # colori: mare per profondità, terra per quota
    def lerp(a, b, t):
        t = np.clip(t, 0, 1)[..., None]
        return np.asarray(a, dtype=np.float64) * (1 - t) + np.asarray(b, dtype=np.float64) * t
    sea = lerp((79, 143, 192), (18, 52, 98), -h / 5000.0)
    low = lerp((112, 152, 82), (160, 160, 104), h / 900.0)
    mid = lerp((160, 160, 104), (140, 118, 92), (h - 900) / 1600.0)
    high = lerp((140, 118, 92), (236, 236, 240), (h - 2500) / 1800.0)
    col = np.where((h < 900)[..., None], low, np.where((h < 2500)[..., None], mid, high))
    # deserti (fascia subtropicale bassa e secca): tono sabbia
    desert = (np.abs(glat) > 15) & (np.abs(glat) < 33) & (h > 0) & (h < 1500)
    col = np.where(desert[..., None], lerp(col, (214, 190, 140), np.full(h.shape, 0.55)), col)
    # ghiacci polari
    ice = (land & (np.abs(glat) > 66)) | (glat < -62)
    col = np.where(ice[..., None], lerp(col, (240, 244, 248), np.full(h.shape, 0.85)), col)
    col = np.where(land[..., None], col, sea)
    # ombreggiatura dei rilievi (luce da nord-ovest)
    gy, gx = np.gradient(np.where(land, h, 0.0))
    shade = np.clip(1.0 + (gx - gy) * 0.0016, 0.7, 1.25)
    col = col * np.where(land, shade, 1.0)[..., None]
    from PIL import Image
    return Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB")
