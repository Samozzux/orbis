"""Normalizzazione dei tag OpenStreetMap nel modello di gioco (docs/02 §2 punto 3)."""
import re
import zlib

from Orbis.Core.World.Fb.AreaKind import AreaKind as AK
from Orbis.Core.World.Fb.BuildingFlag import BuildingFlag as BF
from Orbis.Core.World.Fb.BuildingUse import BuildingUse as BU
from Orbis.Core.World.Fb.LineKind import LineKind as LK
from Orbis.Core.World.Fb.RoadClass import RoadClass as RC
from Orbis.Core.World.Fb.RoadFlag import RoadFlag as RF
from Orbis.Core.World.Fb.RoofShape import RoofShape as RS
from Orbis.Core.World.Fb.TreeKind import TreeKind as TK
from Orbis.Core.World.Fb.WallMaterial import WallMaterial as WM

LEVEL_HEIGHT = 3.2  # m per piano (docs/02 §2)


def stable_random(osm_id, salt=0):
    """Numero in [0, 1) sempre uguale per lo stesso oggetto (seed = ID OSM, docs/02 §4)."""
    return (zlib.crc32(f"{osm_id}:{salt}".encode()) & 0xFFFFFF) / float(0x1000000)


_NUM = re.compile(r"^\s*(-?\d+(?:[.,]\d+)?)\s*(m|mt|metri|ft|')?\s*$", re.I)


def parse_length(value):
    """'12', '12 m', '12,5m', "40'" → metri (float) o None."""
    if not value:
        return None
    m = _NUM.match(value)
    if not m:
        return None
    v = float(m.group(1).replace(",", "."))
    if m.group(2) and m.group(2).lower() in ("ft", "'"):
        v *= 0.3048
    return v


def parse_int(value, lo=None, hi=None):
    if not value:
        return None
    m = re.match(r"^\s*(-?\d+)", value)
    if not m:
        return None
    v = int(m.group(1))
    if lo is not None and v < lo:
        return None
    if hi is not None and v > hi:
        return None
    return v


def parse_float(value):
    if not value:
        return None
    try:
        return float(value.replace(",", ".").split(";")[0].strip())
    except ValueError:
        return None


_COLORS = {
    "white": 0xF4F1EA, "black": 0x2B2B2B, "grey": 0x9A9A96, "gray": 0x9A9A96, "lightgrey": 0xCFCFCB,
    "lightgray": 0xCFCFCB, "darkgrey": 0x5E5E5B, "darkgray": 0x5E5E5B, "silver": 0xC0C0C0,
    "red": 0xB5483B, "darkred": 0x7F2A22, "brown": 0x8A5A3B, "maroon": 0x7A2E26, "orange": 0xE0893E,
    "yellow": 0xE8C95C, "beige": 0xE6D5B0, "cream": 0xF2E6C8, "ivory": 0xF6F0DC, "tan": 0xD2B48C,
    "pink": 0xE7A9A2, "salmon": 0xE69A85, "green": 0x6F9654, "darkgreen": 0x3F6233, "olive": 0x8A8A45,
    "blue": 0x4F79B5, "lightblue": 0x9CC1DF, "navy": 0x2F3F6E, "teal": 0x3E8A86, "terracotta": 0xC2643D,
    "sandybrown": 0xD9A066, "ochre": 0xCC9A48, "khaki": 0xD8CB8C, "gold": 0xD4AF37, "wheat": 0xF0DDB2,
    "peru": 0xC08046, "sienna": 0xA0522D, "chocolate": 0xA65E2E, "coral": 0xEE8064, "lavender": 0xD9D2E9,
}


def parse_color(value):
    """'#d4a373', '#fff', 'red' → 0x01RRGGBB (bit 24 = colore indicato), 0 se ignoto."""
    if not value:
        return 0
    v = value.strip().lower().replace(" ", "").replace("_", "")
    if v.startswith("#"):
        h = v[1:]
        if len(h) == 3:
            h = "".join(c * 2 for c in h)
        if len(h) == 6:
            try:
                return 0x1000000 | int(h, 16)
            except ValueError:
                return 0
        return 0
    c = _COLORS.get(v)
    return 0x1000000 | c if c is not None else 0


# ---------------------------------------------------------------- edifici

_BUILDING_USE = {
    "house": BU.House, "detached": BU.Detached, "semidetached_house": BU.House, "bungalow": BU.House,
    "villa": BU.Detached, "apartments": BU.Apartments, "residential": BU.Residential,
    "terrace": BU.Terrace, "dormitory": BU.Apartments, "commercial": BU.Commercial, "retail": BU.Retail,
    "supermarket": BU.Retail, "office": BU.Office, "industrial": BU.Industrial, "factory": BU.Industrial,
    "manufacture": BU.Industrial, "warehouse": BU.Warehouse, "storage_tank": BU.Industrial,
    "religious": BU.Religious, "church": BU.Church, "cathedral": BU.Cathedral, "basilica": BU.Cathedral,
    "chapel": BU.Chapel, "monastery": BU.Religious, "convent": BU.Religious, "mosque": BU.Religious,
    "synagogue": BU.Religious, "temple": BU.Religious, "shrine": BU.Chapel, "school": BU.School,
    "university": BU.University, "college": BU.University, "kindergarten": BU.Kindergarten,
    "hospital": BU.Hospital, "clinic": BU.Hospital, "civic": BU.Civic, "public": BU.Civic,
    "government": BU.Government, "townhall": BU.Government, "fire_station": BU.Civic,
    "hotel": BU.Hotel, "garage": BU.Garage, "garages": BU.Garages, "carport": BU.Roof,
    "shed": BU.Shed, "hut": BU.Hut, "cabin": BU.Hut, "farm": BU.Farm, "farm_auxiliary": BU.FarmAuxiliary,
    "barn": BU.Barn, "cowshed": BU.Stable, "stable": BU.Stable, "sty": BU.Stable, "greenhouse": BU.Greenhouse,
    "sports_hall": BU.Sports, "sports_centre": BU.Sports, "stadium": BU.Stadium, "grandstand": BU.Stadium,
    "train_station": BU.TrainStation, "transportation": BU.Transportation, "parking": BU.Parking,
    "ruins": BU.Ruins, "roof": BU.Roof, "construction": BU.Construction, "tower": BU.Tower,
    "castle": BU.Castle, "bunker": BU.Bunker, "service": BU.Service, "toilets": BU.Toilets,
    "kiosk": BU.Kiosk, "bridge": BU.Bridge, "monument": BU.Monument, "theatre": BU.Theatre,
    "museum": BU.Museum, "pavilion": BU.Roof,
}

_AMENITY_USE = {
    "place_of_worship": BU.Church, "school": BU.School, "university": BU.University, "college": BU.University,
    "kindergarten": BU.Kindergarten, "hospital": BU.Hospital, "clinic": BU.Hospital, "townhall": BU.Government,
    "police": BU.Civic, "fire_station": BU.Civic, "post_office": BU.Civic, "library": BU.Civic,
    "theatre": BU.Theatre, "cinema": BU.Commercial, "bank": BU.Office, "pharmacy": BU.Retail,
    "parking": BU.Parking, "toilets": BU.Toilets, "courthouse": BU.Government, "marketplace": BU.Retail,
}

# altezza tipica (m) e forma del tetto stimata per uso, quando OSM non le indica
_DEFAULT_HEIGHT = {
    BU.House: 7.0, BU.Detached: 7.5, BU.Apartments: 16.0, BU.Residential: 12.0, BU.Terrace: 9.0,
    BU.Commercial: 9.0, BU.Retail: 6.0, BU.Office: 15.0, BU.Industrial: 9.0, BU.Warehouse: 8.0,
    BU.Religious: 14.0, BU.Church: 16.0, BU.Cathedral: 26.0, BU.Chapel: 7.0, BU.School: 11.0,
    BU.University: 14.0, BU.Kindergarten: 5.0, BU.Hospital: 20.0, BU.Civic: 11.0, BU.Government: 14.0,
    BU.Hotel: 16.0, BU.Garage: 3.0, BU.Garages: 3.0, BU.Shed: 3.0, BU.Hut: 3.0, BU.Farm: 7.0,
    BU.FarmAuxiliary: 5.0, BU.Barn: 7.0, BU.Greenhouse: 3.5, BU.Stable: 5.0, BU.Sports: 10.0,
    BU.Stadium: 14.0, BU.TrainStation: 9.0, BU.Transportation: 7.0, BU.Parking: 9.0, BU.Ruins: 4.0,
    BU.Roof: 4.0, BU.Construction: 6.0, BU.Tower: 22.0, BU.Castle: 16.0, BU.Bunker: 3.0,
    BU.Service: 3.5, BU.Toilets: 3.0, BU.Kiosk: 3.0, BU.Bridge: 6.0, BU.Monument: 8.0,
    BU.Theatre: 18.0, BU.Museum: 14.0,
}

_ROOF = {
    "flat": RS.Flat, "gabled": RS.Gabled, "hipped": RS.Hipped, "pyramidal": RS.Pyramidal,
    "skillion": RS.Skillion, "half-hipped": RS.HalfHipped, "half_hipped": RS.HalfHipped,
    "mansard": RS.Mansard, "dome": RS.Dome, "onion": RS.Onion, "round": RS.Round, "gambrel": RS.Gambrel,
    "saltbox": RS.Gabled, "side_hipped": RS.Hipped, "cross_gabled": RS.Gabled, "tented": RS.Pyramidal,
    "cone": RS.Pyramidal, "lean_to": RS.Skillion, "barrel": RS.Round, "crosspitched": RS.Gabled,
}

_MATERIAL = {
    "plaster": WM.Plaster, "stucco": WM.Plaster, "brick": WM.Brick, "stone": WM.Stone, "sandstone": WM.Stone,
    "limestone": WM.Stone, "concrete": WM.Concrete, "cement_block": WM.Concrete, "glass": WM.Glass,
    "metal": WM.Metal, "steel": WM.Metal, "wood": WM.Wood, "timber_framing": WM.Wood, "marble": WM.Marble,
}

_FLAT_USES = {BU.Industrial, BU.Warehouse, BU.Commercial, BU.Retail, BU.Office, BU.Hospital, BU.Sports,
              BU.Parking, BU.Garages, BU.Garage, BU.Kiosk, BU.Toilets, BU.Bunker, BU.Ruins, BU.Construction,
              BU.Stadium, BU.Bridge, BU.Roof, BU.Service, BU.University}


def building_use(tags):
    b = tags.get("building:part") if tags.get("building", "no") in ("no", "") else tags.get("building")
    use = _BUILDING_USE.get(b or "", BU.Unknown)
    am = _AMENITY_USE.get(tags.get("amenity", ""))
    if use in (BU.Unknown, BU.Commercial, BU.Civic, BU.Residential) and am is not None:
        use = am
    if use == BU.Unknown:
        if tags.get("shop"):
            use = BU.Retail
        elif tags.get("tourism") in ("hotel", "guest_house", "hostel"):
            use = BU.Hotel
        elif tags.get("tourism") == "museum":
            use = BU.Museum
        elif tags.get("office"):
            use = BU.Office
        elif tags.get("historic") in ("castle", "fort"):
            use = BU.Castle
        elif tags.get("man_made") in ("tower", "water_tower"):
            use = BU.Tower
    if tags.get("building") == "church" and "cattedrale" in (tags.get("name", "").lower()):
        use = BU.Cathedral
    return use


def building_attrs(osm_id, tags, footprint_area):
    """Restituisce dict con altezza (m), base (m), piani, tetto, materiale, colori, flag, anno."""
    use = building_use(tags)
    flags = 0
    if tags.get("building:part") and tags.get("building", "no") in ("no", ""):
        flags |= BF.Part
    if tags.get("historic") or tags.get("heritage"):
        flags |= BF.Historic
    if tags.get("shop") or tags.get("amenity") in ("bar", "cafe", "restaurant", "pharmacy", "bank"):
        flags |= BF.HasShops

    levels = parse_int(tags.get("building:levels"), 0, 200)
    roof_levels = parse_int(tags.get("roof:levels"), 0, 20) or 0
    min_level = parse_int(tags.get("building:min_level"), 0, 200)
    height = parse_length(tags.get("height"))
    min_height = parse_length(tags.get("min_height"))
    if min_height is None and min_level:
        min_height = min_level * LEVEL_HEIGHT
    roof_height = parse_length(tags.get("roof:height"))

    if height is None or height <= 0 or height > 600:
        flags |= BF.HeightEstimated
        if levels:
            height = (levels + roof_levels) * LEVEL_HEIGHT + (1.0 if roof_levels == 0 else 0.0)
        else:
            base = _DEFAULT_HEIGHT.get(use)
            if base is None:  # building=yes: stima dalla superficie
                if footprint_area < 25:
                    base = 3.0
                elif footprint_area < 90:
                    base = 6.5
                elif footprint_area < 250:
                    base = 9.5
                elif footprint_area < 900:
                    base = 13.0
                else:
                    base = 11.0
            # variazione stabile ±12% per non avere file di edifici tutti uguali
            height = base * (0.88 + 0.24 * stable_random(osm_id, 1))
        if levels is None:
            levels = max(1, int(round(height / LEVEL_HEIGHT - 0.3)))
    if levels is None:
        levels = max(1, int(round(height / LEVEL_HEIGHT - 0.3)))

    roof = _ROOF.get(tags.get("roof:shape", ""), RS.Unknown)
    if roof == RS.Unknown:
        flags |= BF.RoofEstimated
        if use in (BU.Church, BU.Chapel, BU.Cathedral, BU.Barn, BU.Stable, BU.FarmAuxiliary, BU.Greenhouse):
            roof = RS.Gabled
        elif use in _FLAT_USES or footprint_area > 1500 or height > 30:
            roof = RS.Flat
        elif footprint_area < 12:
            roof = RS.Flat
        else:
            # Abruzzo: tetti a padiglione e a capanna in coppi, qualche tetto piano
            r = stable_random(osm_id, 2)
            roof = RS.Hipped if r < 0.55 else (RS.Gabled if r < 0.85 else RS.Flat)

    if tags.get("location") == "underground" or (parse_int(tags.get("layer"), -10, 10) or 0) < 0 and tags.get("location") != "overground":
        flags |= BF.Underground

    year = 0
    m = re.search(r"(1[0-9]{3}|20[0-9]{2})", tags.get("start_date", "") or tags.get("construction_date", ""))
    if m:
        year = int(m.group(1))

    return {
        "use": use,
        "height": float(height),
        "min_height": float(min_height or 0.0),
        "levels": int(min(levels, 255)),
        "roof": roof,
        "roof_height": float(roof_height or 0.0),
        "material": _MATERIAL.get(tags.get("building:material", ""), WM.Unknown),
        "wall_color": parse_color(tags.get("building:colour")),
        "roof_color": parse_color(tags.get("roof:colour")),
        "flags": flags,
        "year": year,
    }


def address(tags):
    street = tags.get("addr:street") or tags.get("addr:place") or ""
    number = tags.get("addr:housenumber") or ""
    if street and number:
        return f"{street} {number}"
    return street or ""


# ---------------------------------------------------------------- strade e ferrovie

_HIGHWAY = {
    "motorway": RC.Motorway, "trunk": RC.Trunk, "primary": RC.Primary, "secondary": RC.Secondary,
    "tertiary": RC.Tertiary, "unclassified": RC.Unclassified, "residential": RC.Residential,
    "living_street": RC.LivingStreet, "service": RC.Service, "pedestrian": RC.Pedestrian, "track": RC.Track,
    "footway": RC.Footway, "cycleway": RC.Cycleway, "path": RC.Path, "steps": RC.Steps,
    "bridleway": RC.Bridleway, "raceway": RC.Raceway, "busway": RC.BusWay, "road": RC.Unclassified,
    "corridor": None, "platform": None, "construction": None, "proposed": None, "elevator": None,
}

_RAILWAY = {
    "rail": RC.Rail, "light_rail": RC.LightRail, "tram": RC.Tram, "subway": RC.Subway,
    "narrow_gauge": RC.NarrowGauge, "funicular": RC.Funicular, "monorail": RC.Monorail,
}

_AEROWAY = {"runway": RC.Runway, "taxiway": RC.Taxiway}

# larghezza della carreggiata (m) per classe, a doppio senso
_ROAD_WIDTH = {
    RC.Motorway: 7.5, RC.Trunk: 9.0, RC.Primary: 8.0, RC.Secondary: 7.0, RC.Tertiary: 6.5,
    RC.Unclassified: 5.5, RC.Residential: 6.0, RC.LivingStreet: 5.0, RC.Service: 4.0, RC.Pedestrian: 6.0,
    RC.Track: 3.0, RC.Footway: 2.0, RC.Cycleway: 2.0, RC.Path: 1.5, RC.Steps: 2.0, RC.Bridleway: 2.0,
    RC.Raceway: 10.0, RC.BusWay: 6.0, RC.Rail: 3.0, RC.LightRail: 3.0, RC.Tram: 3.0, RC.Subway: 3.0,
    RC.NarrowGauge: 2.5, RC.Funicular: 2.5, RC.Monorail: 2.0, RC.Runway: 45.0, RC.Taxiway: 23.0,
}

_UNPAVED = {"unpaved", "gravel", "fine_gravel", "dirt", "ground", "earth", "grass", "sand", "mud",
            "compacted", "pebblestone", "woodchips", "rock"}


def road_attrs(tags):
    """dict con gli attributi della strada, o None se il way non è una strada utile."""
    cls = None
    link = False
    hw = tags.get("highway")
    if hw:
        if hw.endswith("_link"):
            link = True
            hw = hw[:-5]
        cls = _HIGHWAY.get(hw)
    if cls is None and tags.get("railway") in _RAILWAY:
        cls = _RAILWAY[tags["railway"]]
    if cls is None and tags.get("aeroway") in _AEROWAY:
        cls = _AEROWAY[tags["aeroway"]]
    if cls is None:
        return None
    if tags.get("area") == "yes" and cls in (RC.Pedestrian, RC.Footway, RC.Service):
        return None  # piazze: trattate come aree

    lanes = parse_int(tags.get("lanes"), 1, 12) or 0
    oneway = 0
    ow = tags.get("oneway", "")
    if ow in ("yes", "1", "true"):
        oneway = 1
    elif ow == "-1":
        oneway = -1
    junction = tags.get("junction", "")
    if junction in ("roundabout", "circular") or cls == RC.Motorway and ow != "no":
        oneway = oneway or 1

    width = parse_length(tags.get("width"))
    if width is None or width <= 0 or width > 80:
        if lanes and cls < RC.Rail:
            width = lanes * (3.5 if cls <= RC.Primary else 3.0)
        else:
            width = _ROAD_WIDTH.get(cls, 4.0)
            if oneway and cls <= RC.LivingStreet:
                width *= 0.62
            if tags.get("service") == "driveway":
                width = 3.0
            elif tags.get("service") == "parking_aisle":
                width = 5.0
            if link:
                width = min(width, 5.0)

    flags = 0
    if tags.get("bridge", "no") not in ("no", ""):
        flags |= RF.Bridge
    if tags.get("tunnel", "no") not in ("no", "", "building_passage"):
        flags |= RF.Tunnel
    if link:
        flags |= RF.Link
    if junction in ("roundabout", "circular"):
        flags |= RF.Roundabout
    sw = tags.get("sidewalk", "")
    if sw in ("both", "left", "yes"):
        flags |= RF.SidewalkLeft
    if sw in ("both", "right", "yes"):
        flags |= RF.SidewalkRight
    if tags.get("surface", "") in _UNPAVED or tags.get("tracktype", "grade1") != "grade1" and cls == RC.Track:
        flags |= RF.Unpaved
    if tags.get("lit") == "yes":
        flags |= RF.Lit
    if tags.get("embankment") == "yes":
        flags |= RF.Embankment
    if tags.get("cutting") == "yes":
        flags |= RF.Cutting

    speed = 0
    ms = tags.get("maxspeed", "")
    v = parse_int(ms, 1, 250)
    if v:
        speed = v
    return {
        "cls": cls,
        "width": float(width),
        "lanes": lanes,
        "oneway": oneway,
        "layer": max(-5, min(5, parse_int(tags.get("layer"), -10, 10) or 0)),
        "flags": flags,
        "max_speed": speed,
        "name": tags.get("name", ""),
        "ref": tags.get("ref", ""),
    }


# ---------------------------------------------------------------- linee

_WATERWAY = {"river": (LK.River, 12.0), "stream": (LK.Stream, 2.5), "canal": (LK.Canal, 6.0),
             "drain": (LK.Drain, 2.0), "ditch": (LK.Ditch, 1.0)}
_BARRIER = {"wall": (LK.Wall, 0.4), "city_wall": (LK.CityWall, 2.0), "retaining_wall": (LK.RetainingWall, 0.5),
            "fence": (LK.Fence, 0.1), "hedge": (LK.Hedge, 0.8), "guard_rail": (LK.Guardrail, 0.2)}


def line_attrs(tags):
    kind = None
    if tags.get("waterway") in _WATERWAY and tags.get("tunnel", "no") in ("no", ""):
        kind, width = _WATERWAY[tags["waterway"]]
    elif tags.get("power") == "line":
        kind, width = LK.PowerLine, 0.1
    elif tags.get("power") == "minor_line":
        kind, width = LK.MinorPowerLine, 0.05
    elif tags.get("barrier") in _BARRIER:
        kind, width = _BARRIER[tags["barrier"]]
    elif tags.get("historic") == "citywalls":
        kind, width = LK.CityWall, 2.0
    if kind is None:
        return None
    w = parse_length(tags.get("width"))
    if w and 0 < w < 500:
        width = w
    return {"kind": kind, "width": float(width), "name": tags.get("name", "")}


# ---------------------------------------------------------------- aree

_LANDUSE = {
    "residential": AK.Residential, "commercial": AK.Commercial, "retail": AK.Retail,
    "industrial": AK.Industrial, "construction": AK.Construction, "railway": AK.Railway,
    "military": AK.Military, "cemetery": AK.Cemetery, "farmland": AK.Farmland, "vineyard": AK.Vineyard,
    "orchard": AK.Orchard, "allotments": AK.Allotments, "greenhouse_horticulture": AK.Greenhouse,
    "farmyard": AK.Farmyard, "plant_nursery": AK.PlantNursery, "quarry": AK.Quarry, "landfill": AK.Landfill,
    "meadow": AK.Meadowland, "grass": AK.Grass, "forest": AK.Forest, "recreation_ground": AK.Park,
    "village_green": AK.Grass, "brownfield": AK.Brownfield, "greenfield": AK.Greenfield,
    "garages": AK.Garages, "basin": AK.Basin, "reservoir": AK.Reservoir, "flowerbed": AK.Garden,
    "education": AK.School, "religious": AK.Grass, "salt_pond": AK.Pond, "aquaculture": AK.Pond,
}
_NATURAL = {
    "wood": AK.Forest, "scrub": AK.Scrub, "heath": AK.Heath, "grassland": AK.Grassland, "fell": AK.Grassland,
    "wetland": AK.Wetland, "beach": AK.Beach, "sand": AK.Sand, "shingle": AK.Beach, "bare_rock": AK.BareRock,
    "scree": AK.Scree, "glacier": AK.Glacier, "shrubbery": AK.Scrub, "rock": AK.BareRock,
}
_WATER = {"river": AK.River, "lake": AK.Lake, "reservoir": AK.Reservoir, "pond": AK.Pond, "basin": AK.Basin,
          "canal": AK.River, "stream": AK.River, "oxbow": AK.Lake, "lagoon": AK.Lake, "wastewater": AK.Basin}
_LEISURE = {
    "park": AK.Park, "garden": AK.Garden, "playground": AK.Playground, "pitch": AK.Pitch,
    "sports_centre": AK.SportsCentre, "stadium": AK.Stadium, "golf_course": AK.GolfCourse, "track": AK.Track,
    "swimming_pool": AK.SwimmingPool, "dog_park": AK.Park, "common": AK.Grass, "recreation_ground": AK.Park,
}
_AMENITY_AREA = {
    "parking": AK.Parking, "school": AK.School, "university": AK.University, "college": AK.University,
    "hospital": AK.Hospital, "grave_yard": AK.Cemetery, "kindergarten": AK.School, "marketplace": AK.Pedestrian,
}
_TOURISM = {"camp_site": AK.Camping, "caravan_site": AK.Camping, "zoo": AK.Zoo, "theme_park": AK.ThemePark}

_OLIVE = re.compile(r"olive|olea|ulivo|olivo", re.I)


def area_kind(tags):
    """AreaKind dell'area o None. L'ordine dà la precedenza ai tag più specifici."""
    if tags.get("waterway") == "riverbank":
        return AK.River
    if tags.get("natural") == "water" or tags.get("landuse") in ("reservoir", "basin") and tags.get("water"):
        return _WATER.get(tags.get("water", ""), AK.Water)
    if tags.get("leisure") in _LEISURE:
        return _LEISURE[tags["leisure"]]
    if tags.get("amenity") in _AMENITY_AREA:
        if tags["amenity"] == "parking" and tags.get("parking", "surface") not in ("surface", "", "lane"):
            return None  # parcheggi multipiano o sotterranei: niente asfalto a terra
        return _AMENITY_AREA[tags["amenity"]]
    if tags.get("tourism") in _TOURISM:
        return _TOURISM[tags["tourism"]]
    if tags.get("highway") == "pedestrian" or tags.get("area:highway") or tags.get("place") == "square":
        return AK.Pedestrian
    lu = tags.get("landuse")
    if lu == "orchard" and (_OLIVE.search(tags.get("trees", "")) or _OLIVE.search(tags.get("crop", ""))
                            or _OLIVE.search(tags.get("produce", ""))):
        return AK.OliveGrove
    if lu in _LANDUSE:
        return _LANDUSE[lu]
    if tags.get("natural") in _NATURAL:
        return _NATURAL[tags["natural"]]
    if tags.get("man_made") in ("bridge", "pier"):
        return AK.Pedestrian
    return None


# ---------------------------------------------------------------- alberi e POI

def tree_kind(tags):
    text = " ".join(tags.get(k, "") for k in ("species", "genus", "taxon", "species:it", "name")).lower()
    if _OLIVE.search(text):
        return TK.Olive
    if "cupress" in text or "cipress" in text:
        return TK.Cypress
    if "pinea" in text or "pino domestico" in text:
        return TK.StonePine
    if "phoenix" in text or "washingtonia" in text or "palm" in text:
        return TK.Palm
    if any(w in text for w in ("malus", "prunus", "citrus", "pyrus", "ficus carica")):
        return TK.Fruit
    if tags.get("leaf_type") == "needleleaved" or any(w in text for w in ("pinus", "abies", "picea", "cedrus")):
        return TK.Conifer
    return TK.Broadleaf


POI_KEYS = ("amenity", "shop", "tourism", "leisure", "office", "craft", "healthcare", "historic",
            "public_transport", "railway", "aeroway", "man_made", "sport", "emergency", "power")

_POI_SKIP = {
    "amenity": {"bench", "waste_basket", "parking_space", "vending_machine", "recycling", "bicycle_parking",
                "waste_disposal", "hunting_stand", "drinking_water", "telephone", "post_box", "clock",
                "grit_bin", "parking_entrance", "motorcycle_parking", "fountain"},
    "man_made": {"pole", "manhole", "utility_pole", "survey_point", "street_cabinet", "pipeline", "embankment",
                 "cutline", "dyke", "breakwater", "groyne", "pier", "bridge", "clearcut", "mast", "antenna"},
    "power": {"pole", "tower", "portal", "catenary_mast", "insulator", "terminal", "line", "minor_line", "cable"},
    "historic": {"boundary_stone", "milestone"},
    "railway": {"level_crossing", "crossing", "switch", "signal", "buffer_stop", "railway_crossing",
                "milestone", "rail", "abandoned", "disused", "tram", "light_rail", "narrow_gauge"},
    "leisure": {"picnic_table", "slipway", "firepit", "bleachers", "outdoor_seating"},
    "public_transport": {"stop_position"},
}


def poi_category(tags):
    for key in POI_KEYS:
        v = tags.get(key)
        if not v or v in ("no", "yes") and key not in ("emergency",):
            continue
        if v in _POI_SKIP.get(key, ()):
            continue
        if key == "power" and v not in ("plant", "generator", "substation"):
            continue
        if key == "emergency" and v not in ("ambulance_station",):
            continue
        return f"{key}/{v}"
    return None
