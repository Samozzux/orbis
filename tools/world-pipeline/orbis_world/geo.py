"""Web Mercator e tile XYZ: stesse formule di Assets/Orbis/Core/World/Geo/WebMercator.cs."""
import math

import numpy as np

R = 6378137.0
HALF = math.pi * R
MAX_LAT = 85.05112877980659


def to_merc(lon, lat):
    """lon/lat in gradi (scalari o array numpy) → metri Mercatore."""
    lat = np.clip(lat, -MAX_LAT, MAX_LAT)
    x = np.asarray(lon, dtype=np.float64) * HALF / 180.0
    y = np.log(np.tan((90.0 + np.asarray(lat, dtype=np.float64)) * math.pi / 360.0)) * R
    return x, y


def to_lonlat(x, y):
    lon = np.asarray(x, dtype=np.float64) / HALF * 180.0
    lat = np.arctan(np.exp(np.asarray(y, dtype=np.float64) / R)) * 360.0 / math.pi - 90.0
    return lon, lat


def tile_size(z):
    return 2.0 * HALF / (1 << z)


def tile_origin(z, tx, ty):
    """Angolo sud-ovest del tile in metri Mercatore."""
    s = tile_size(z)
    return -HALF + tx * s, HALF - (ty + 1) * s


def tile_bounds(z, tx, ty):
    x0, y0 = tile_origin(z, tx, ty)
    s = tile_size(z)
    return x0, y0, x0 + s, y0 + s


def tile_of(z, x, y):
    s = tile_size(z)
    n = 1 << z
    tx = int(math.floor((x + HALF) / s))
    ty = int(math.floor((HALF - y) / s))
    return min(max(tx, 0), n - 1), min(max(ty, 0), n - 1)


def tile_range(z, minx, miny, maxx, maxy):
    """Tile (tx, ty) che toccano il rettangolo in metri Mercatore."""
    x0, y1 = tile_of(z, minx, miny)
    x1, y0 = tile_of(z, maxx, maxy)
    for ty in range(y0, y1 + 1):
        for tx in range(x0, x1 + 1):
            yield tx, ty


def scale_at(lat):
    """Metri veri per metro Mercatore alla latitudine data."""
    return math.cos(math.radians(lat))
