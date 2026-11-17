#!/usr/bin/env python3
"""Genera data/catalog/*.json (prime 150 voci di M2, docs/08 §2) da tabelle compatte.

Costi, rendite e capienze vengono da formule per categoria: si ribilancia cambiando le costanti qui sotto
e rilanciando lo script (python3 tools/catalog/genera_catalogo.py). Ogni voce ha una sagoma esatta
multipla della griglia (1 m; 0,5 m per natura e decorazioni) e da 1 a 5 livelli.
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "data", "catalog")

WALLS_HISTORIC = [("ocra", "Intonaco ocra", "#e3c08a"), ("rosa", "Rosa antico", "#e8bca4"),
                  ("crema", "Crema", "#f1e6cf"), ("pietra", "Pietra della Majella", "#cdbb9a", 1.15)]
WALLS_MODERN = [("bianco", "Bianco", "#f2eee6"), ("sabbia", "Sabbia", "#e6cfae"), ("grigio", "Grigio perla", "#dcd6cc"),
                ("mattoni", "Mattoni a vista", "#b4704f", 1.1)]
ROOF = "#c0623f"


def variants(walls, roof=ROOF):
    out = []
    for w in walls:
        v = {"key": w[0], "name": w[1], "wall": w[2], "roof": roof}
        if len(w) > 3:
            v["costMultiplier"] = w[3]
        out.append(v)
    return out


def levels(n, base, scale_capacity=1.3, **fields):
    """n livelli: il primo costa `base`, i successivi meno (potenziamento); le rendite crescono del 30% a livello."""
    cost_steps = [1.0, 0.7, 0.9, 1.2, 1.6]
    out = []
    for i in range(n):
        lv = {"level": i + 1, "cost": int(round(base * cost_steps[i], -1)),
              "buildHours": round(fields.get("hours", 4) * (1 + 0.5 * i), 1)}
        k = scale_capacity ** i
        for f in ("capacity", "jobs"):
            if fields.get(f):
                lv[f] = int(round(fields[f] * k))
        for f in ("rentPerMonth", "maintenancePerMonth", "productionPerMonth"):
            if fields.get(f):
                lv[f] = int(round(fields[f] * k, -1))
        if fields.get("powerKw"):
            lv["powerKw"] = round(fields["powerKw"] * k, 1)
        out.append(lv)
    return out


ITEMS = []


def add(key, category, name, w, d, h, family, unlock=1, lv=1, var=None, effects=None, tags=(), **model):
    item = {"key": key, "category": category, "name": name, "footprint": {"w": w, "d": d}, "height": h,
            "model": dict(family=family, **model), "unlock": {"playerLevel": unlock}, "levels": lv,
            "variants": var or [], "effects": effects or {}, "tags": list(tags)}
    ITEMS.append(item)


# ------------------------------------------------------------------ residenziale (20)
def house(key, name, w, d, floors, unlock, roof="hipped", style="historic", n=5, tags=("casa",)):
    area = w * d * floors
    cost = area * 420
    cap = max(2, round(area / 38))
    add(key, "residential", name, w, d, round(floors * 3.2 + (1.6 if roof != "flat" else 0.6), 1), "house", unlock,
        levels(n, cost, capacity=cap, rentPerMonth=cost * 0.05, maintenancePerMonth=cost * 0.004, hours=4 + floors,
               powerKw=-1.5 * cap),
        variants(WALLS_HISTORIC if style == "historic" else WALLS_MODERN),
        {"happiness": 0.5, "happinessRadius": 20, "landValue": 1}, tags, roof=roof, floors=floors, style=style)


house("casa_piccola", "Casetta", 8, 7, 1, 1)
house("casa_due_piani", "Casa su due piani", 9, 8, 2, 1)
house("villetta_abruzzese", "Villetta abruzzese", 12, 10, 2, 2, roof="hipped")
house("bifamiliare", "Bifamiliare", 16, 10, 2, 3, roof="gabled")
house("casa_a_schiera", "Case a schiera", 24, 9, 2, 4, roof="gabled")
house("villa", "Villa con giardino", 18, 14, 2, 8, roof="hipped", style="modern")
house("casale_ristrutturato", "Casale ristrutturato", 16, 12, 2, 6, roof="gabled")
house("palazzina_storica", "Palazzina del centro", 14, 12, 3, 3)
house("palazzo_nobiliare", "Palazzo nobiliare", 22, 18, 4, 12)
house("condominio_4", "Condominio 4 piani", 20, 14, 4, 5, roof="flat", style="modern")
house("condominio_6", "Condominio 6 piani", 22, 16, 6, 8, roof="flat", style="modern")
house("condominio_8", "Condominio 8 piani", 24, 16, 8, 12, roof="flat", style="modern")
house("torre_residenziale", "Torre residenziale", 18, 18, 14, 20, roof="flat", style="modern")
house("casa_moderna", "Casa moderna", 12, 10, 2, 5, roof="flat", style="modern")
house("casa_con_portico", "Casa con portico", 13, 10, 2, 4, roof="hipped")
house("loft", "Loft in ex fabbrica", 20, 14, 3, 10, roof="flat", style="modern")
house("residence", "Residence", 30, 16, 4, 14, roof="flat", style="modern")
house("casetta_borgo", "Casetta del borgo", 7, 7, 2, 1, roof="gabled")
house("villa_liberty", "Villa liberty", 16, 14, 3, 15, roof="hipped")
house("grattacielo", "Grattacielo", 28, 28, 25, 28, roof="flat", style="modern")

# ------------------------------------------------------------------ commerciale (22)
def shop(key, name, w, d, floors, unlock, sign, awning="#c8473b", n=4, tags=("negozio",), hotel=False):
    area = w * d * floors
    cost = area * 560
    jobs = max(2, round(area / 28))
    add(key, "commercial", name, w, d, round(floors * 3.4 + 0.8, 1), "shop", unlock,
        levels(n, cost, jobs=jobs, capacity=round(area / 30) if hotel else 0, rentPerMonth=cost * 0.06,
               maintenancePerMonth=cost * 0.005, hours=5 + floors, powerKw=-4 * jobs / 3),
        variants(WALLS_HISTORIC[:3] + WALLS_MODERN[:1]), {"happiness": 1, "happinessRadius": 40, "landValue": 2},
        tags, sign=sign, awning=awning, floors=floors)


shop("bar", "Bar", 8, 8, 1, 1, "BAR", "#c8473b", tags=("negozio", "bar"))
shop("pizzeria", "Pizzeria", 10, 9, 1, 2, "PIZZERIA", "#2f7d4f")
shop("ristorante", "Ristorante", 14, 10, 1, 4, "RISTORANTE", "#7a2e26")
shop("gelateria", "Gelateria", 7, 7, 1, 2, "GELATERIA", "#e8a6b5")
shop("panetteria", "Forno", 8, 8, 1, 1, "FORNO", "#c58d3c")
shop("farmacia", "Farmacia", 9, 8, 1, 3, "FARMACIA", "#2f9e5a")
shop("tabaccheria", "Tabaccheria", 6, 6, 1, 1, "TABACCHI", "#3a3a3a")
shop("edicola_negozio", "Libreria", 8, 8, 1, 3, "LIBRI", "#3f5f8a")
shop("abbigliamento", "Negozio di abbigliamento", 10, 9, 2, 5, "MODA", "#222222")
shop("ferramenta", "Ferramenta", 10, 10, 1, 4, "FERRAMENTA", "#b5651d")
shop("alimentari", "Alimentari", 9, 8, 1, 2, "ALIMENTARI", "#4f7d2f")
shop("supermercato", "Supermercato", 40, 30, 1, 7, "SUPERMERCATO", "#d23c2e")
shop("centro_commerciale", "Centro commerciale", 80, 60, 2, 18, "CENTRO", "#3c6fd2")
shop("albergo", "Albergo", 20, 16, 5, 9, "HOTEL", "#2b4a6f", hotel=True)
shop("bed_and_breakfast", "Bed & breakfast", 12, 10, 2, 4, "B&B", "#8a6f4f", hotel=True)
shop("enoteca", "Enoteca", 9, 8, 1, 6, "ENOTECA", "#6e1f2f")
shop("pasticceria", "Pasticceria", 8, 8, 1, 3, "PASTICCERIA", "#d98fae")
shop("banca", "Banca", 14, 12, 3, 8, "BANCA", "#1f3f6e")
shop("uffici_piccoli", "Uffici", 16, 12, 4, 6, "UFFICI", "#4f5f6f", tags=("uffici",))
shop("torre_uffici", "Torre di uffici", 24, 24, 12, 20, "BUSINESS", "#2f3f4f", tags=("uffici",))
shop("cinema_multisala", "Cinema multisala", 40, 30, 2, 12, "CINEMA", "#7a1f1f")
shop("distributore", "Distributore di carburante", 20, 14, 1, 5, "CARBURANTE", "#e0b020", tags=("negozio", "auto"))

# ------------------------------------------------------------------ uffici e industria (12)
def industry(key, name, w, d, h, unlock, kind, n=4, tags=("industria",)):
    area = w * d
    cost = area * 260
    jobs = max(4, round(area / 55))
    add(key, "industrial", name, w, d, h, "industrial", unlock,
        levels(n, cost, jobs=jobs, productionPerMonth=cost * 0.07, maintenancePerMonth=cost * 0.006, hours=8,
               powerKw=-0.08 * area),
        [{"key": "grigio", "name": "Grigio", "wall": "#c8ccce", "roof": "#9a9a96"},
         {"key": "azzurro", "name": "Azzurro", "wall": "#b9c3c9", "roof": "#8a96a0"},
         {"key": "bianco", "name": "Bianco", "wall": "#e3e1db", "roof": "#a9a399"}],
        {"happiness": -1, "happinessRadius": 80, "landValue": -2, "pollution": 2}, tags, kind=kind)


industry("capannone", "Capannone", 30, 20, 8, 3, "shed")
industry("magazzino", "Magazzino logistico", 50, 30, 10, 6, "shed")
industry("officina", "Officina meccanica", 20, 14, 6, 2, "workshop")
industry("pastificio", "Pastificio", 40, 25, 12, 9, "factory", tags=("industria", "alimentare"))
industry("cantina_sociale", "Cantina sociale", 36, 24, 10, 8, "winery", tags=("industria", "vino"))
industry("frantoio", "Frantoio", 24, 18, 8, 7, "oilmill", tags=("industria", "olio"))
industry("fabbrica", "Fabbrica", 60, 40, 14, 12, "factory")
industry("fabbrica_auto", "Stabilimento automobilistico", 120, 80, 18, 25, "factory")
industry("cementificio", "Cementificio", 60, 40, 30, 16, "plant")
industry("falegnameria", "Falegnameria", 22, 14, 7, 4, "workshop")
industry("tipografia", "Tipografia", 20, 16, 7, 5, "workshop")
industry("centro_logistico", "Centro logistico", 80, 50, 12, 18, "shed")

# ------------------------------------------------------------------ energia (12)
def energy(key, name, w, d, h, unlock, family, kw, cost, n=3, effects=None, tags=("energia",), **model):
    add(key, "energy", name, w, d, h, family, unlock,
        levels(n, cost, scale_capacity=1.25, powerKw=kw, maintenancePerMonth=cost * 0.004, jobs=max(1, round(kw / 2000)),
               hours=12),
        [], effects or {}, tags, **model)


energy("pala_eolica", "Pala eolica", 40, 40, 70, 6, "wind", 600, 180000, tags=("energia", "wind", "pulita"),
       effects={"happiness": -0.5, "happinessRadius": 300})
energy("parco_eolico", "Parco eolico (4 pale)", 90, 90, 70, 14, "windfarm", 2400, 680000, tags=("energia", "wind", "pulita"),
       effects={"happiness": -1, "happinessRadius": 500})
energy("pannelli_solari_piccoli", "Pannelli solari", 12, 8, 2, 2, "solar", 12, 9000, tags=("energia", "solar", "pulita"))
energy("campo_fotovoltaico", "Campo fotovoltaico", 60, 40, 3, 8, "solar", 260, 95000, tags=("energia", "solar", "pulita"))
energy("centrale_idroelettrica", "Centrale idroelettrica", 40, 30, 20, 16, "hydro", 1600, 520000, tags=("energia", "pulita"))
energy("centrale_biomasse", "Centrale a biomasse", 40, 30, 25, 12, "thermal", 1800, 600000,
       effects={"happiness": -1, "happinessRadius": 300, "pollution": 2}, chimneys=1)
energy("centrale_gas", "Centrale termoelettrica a gas", 70, 50, 45, 18, "thermal", 18000, 2600000,
       effects={"happiness": -3, "happinessRadius": 800, "pollution": 5}, chimneys=2)
energy("centrale_geotermica", "Centrale geotermica", 40, 40, 20, 20, "thermal", 3000, 950000,
       effects={"happiness": -1, "happinessRadius": 300}, chimneys=0, steam="true")
energy("centrale_nucleare", "Centrale nucleare", 160, 120, 150, 30, "nuclear", 110000, 25000000,
       effects={"happiness": -6, "happinessRadius": 2000, "landValue": -4}, tags=("energia", "nucleare"))
energy("sottostazione", "Sottostazione elettrica", 20, 16, 6, 5, "substation", 0.01, 40000, n=1)
energy("traliccio", "Traliccio dell'alta tensione", 6, 6, 40, 4, "pylon", 0.01, 6000, n=1)
energy("tetto_solare", "Tetto solare (condominio)", 20, 14, 14, 9, "solarbuilding", 45, 160000, tags=("energia", "solar", "pulita"))

# ------------------------------------------------------------------ agricoltura e campagna (18)
def farm(key, name, w, d, h, unlock, family, rate, cost_m2, n=3, tags=("agricoltura",), **model):
    area = w * d
    cost = area * cost_m2
    add(key, "agriculture", name, w, d, h, family, unlock,
        levels(n, cost, scale_capacity=1.25, productionPerMonth=area * rate, maintenancePerMonth=cost * 0.003,
               jobs=max(1, round(area / 2500)), hours=6),
        [], {"happiness": 0.5, "happinessRadius": 30}, tags, **model)


farm("campo_grano", "Campo di grano", 50, 40, 1, 1, "field", 0.08, 3, crop="wheat")
farm("campo_mais", "Campo di mais", 50, 40, 2, 2, "field", 0.09, 3, crop="corn")
farm("campo_girasoli", "Campo di girasoli", 50, 40, 2, 3, "field", 0.09, 3, crop="sunflower")
farm("orto", "Orto", 20, 14, 1, 1, "field", 0.18, 5, crop="vegetables")
farm("vigneto", "Vigneto (Montepulciano)", 40, 30, 2, 2, "vineyard", 0.32, 9, tags=("agricoltura", "vineyard", "vino"))
farm("vigneto_trebbiano", "Vigneto (Trebbiano)", 40, 30, 2, 4, "vineyard", 0.3, 9, tags=("agricoltura", "vineyard", "vino"), grape="white")
farm("uliveto", "Uliveto", 42, 28, 5, 2, "olivegrove", 0.26, 8, tags=("agricoltura", "olive", "olio"))
farm("frutteto", "Frutteto", 30, 25, 4, 3, "orchard", 0.24, 8, tags=("agricoltura", "frutta"))
farm("serra", "Serra", 30, 12, 4, 5, "greenhouse", 0.75, 60, tags=("agricoltura", "serra"))
farm("lavanda", "Campo di lavanda", 40, 30, 1, 6, "field", 0.15, 5, crop="lavender")
farm("zafferano", "Zafferano dell'Aquila", 20, 15, 1, 9, "field", 0.6, 12, crop="saffron")
farm("pascolo", "Pascolo con pecore", 60, 40, 1, 4, "pasture", 0.06, 2, tags=("agricoltura", "animali"))
farm("casale", "Casale di campagna", 16, 12, 8, 2, "farmhouse", 0.0, 220, tags=("agricoltura", "casale"))
farm("fienile", "Fienile", 14, 10, 8, 2, "barn", 0.05, 150)
farm("stalla", "Stalla", 24, 12, 7, 4, "barn", 0.25, 160, tags=("agricoltura", "animali"))
farm("silos", "Silos", 10, 10, 18, 5, "silo", 0.12, 260)
farm("apiario", "Apiario", 8, 6, 1.5, 3, "beehives", 0.9, 25, tags=("agricoltura", "miele"))
farm("agriturismo", "Agriturismo", 20, 14, 8, 8, "farmhouse", 0.35, 300, tags=("agricoltura", "turismo"))

# ------------------------------------------------------------------ servizi pubblici (10)
def service(key, name, w, d, floors, unlock, kind, cost, capacity, happiness, radius, tags=("servizi",)):
    add(key, "services", name, w, d, round(floors * 3.6 + 1, 1), "civic", unlock,
        levels(3, cost, scale_capacity=1.3, capacity=capacity, jobs=max(3, round(capacity / 12)),
               maintenancePerMonth=cost * 0.008, hours=24, powerKw=-w * d * floors * 0.02),
        variants(WALLS_HISTORIC[:3]), {"happiness": happiness, "happinessRadius": radius, "landValue": 2}, tags, kind=kind,
        floors=floors)


service("scuola_infanzia", "Scuola dell'infanzia", 24, 16, 1, 3, "school", 300000, 90, 3, 400)
service("scuola_primaria", "Scuola primaria", 36, 20, 2, 4, "school", 650000, 250, 4, 600)
service("scuola_superiore", "Liceo", 50, 24, 3, 9, "school", 1400000, 600, 4, 1200)
service("universita", "Università", 90, 60, 4, 22, "university", 8000000, 3000, 6, 3000)
service("ambulatorio", "Ambulatorio", 16, 12, 2, 4, "clinic", 220000, 40, 3, 600)
service("ospedale", "Ospedale", 100, 60, 6, 20, "hospital", 12000000, 400, 8, 5000)
service("caserma_carabinieri", "Caserma dei carabinieri", 30, 20, 2, 6, "police", 700000, 40, 4, 1500)
service("vigili_del_fuoco", "Caserma dei vigili del fuoco", 36, 24, 2, 7, "fire", 900000, 30, 4, 2000)
service("ufficio_postale", "Ufficio postale", 16, 12, 2, 3, "post", 180000, 12, 2, 800)
service("biblioteca", "Biblioteca comunale", 24, 18, 2, 6, "library", 450000, 120, 3, 800)

# ------------------------------------------------------------------ sport (10)
def sport(key, name, w, d, h, unlock, family, cost, happiness, rent=0, tags=("sport",), **model):
    add(key, "sports", name, w, d, h, family, unlock,
        levels(3, cost, scale_capacity=1.3, rentPerMonth=rent, maintenancePerMonth=cost * 0.006, jobs=max(1, round(cost / 200000)),
               hours=12),
        [], {"happiness": happiness, "happinessRadius": 600, "landValue": 1}, tags, **model)


sport("campo_calcetto", "Campo da calcetto", 42, 25, 1, 2, "pitch", 60000, 2, 900, sport="football5")
sport("campo_calcio", "Campo da calcio", 110, 75, 1, 6, "pitch", 260000, 3, 2500, sport="football")
sport("campo_tennis", "Campo da tennis", 36, 18, 1, 3, "pitch", 70000, 2, 1000, sport="tennis")
sport("campo_basket", "Campo da basket", 32, 19, 1, 2, "pitch", 45000, 2, 500, sport="basket")
sport("piscina_scoperta", "Piscina scoperta", 50, 30, 2, 7, "pool", 420000, 4, 4000)
sport("palestra", "Palestra", 24, 18, 8, 4, "hall", 260000, 2, 3000, kind="gym")
sport("palazzetto", "Palazzetto dello sport", 70, 50, 16, 14, "hall", 2800000, 5, 12000, kind="arena")
sport("pista_atletica", "Pista di atletica", 180, 100, 2, 12, "track", 900000, 4, 3000)
sport("skate_park", "Skate park", 30, 20, 2, 5, "pitch", 80000, 2, 0, sport="skate")
sport("stadio_piccolo", "Stadio (5.000 posti)", 160, 120, 18, 20, "stadium", 9000000, 7, 45000, tags=("sport", "stadio"))

# ------------------------------------------------------------------ cultura e svago (8)
def culture(key, name, w, d, h, unlock, family, cost, happiness, rent, tags=("cultura",), **model):
    add(key, "culture", name, w, d, h, family, unlock,
        levels(3, cost, scale_capacity=1.3, rentPerMonth=rent, maintenancePerMonth=cost * 0.005, jobs=max(2, round(cost / 150000)),
               hours=16),
        variants(WALLS_HISTORIC[:3]), {"happiness": happiness, "happinessRadius": 800, "landValue": 3}, tags, **model)


culture("museo", "Museo", 40, 30, 14, 10, "civic", 2200000, 5, 8000, kind="museum")
culture("teatro", "Teatro", 36, 28, 18, 12, "civic", 3000000, 6, 9000, kind="theatre")
culture("chiesa", "Chiesa", 30, 18, 30, 6, "church", 900000, 3, 0)
culture("cappella", "Cappella", 10, 8, 12, 2, "church", 90000, 1, 0)
culture("anfiteatro", "Anfiteatro all'aperto", 50, 40, 8, 14, "amphitheatre", 1200000, 5, 6000)
culture("luna_park", "Luna park", 80, 60, 25, 16, "funfair", 1800000, 7, 22000, tags=("cultura", "svago"))
culture("discoteca", "Discoteca", 30, 24, 8, 10, "hall", 600000, 2, 9000, tags=("cultura", "svago"))
culture("galleria_arte", "Galleria d'arte", 16, 12, 7, 6, "shop", 260000, 2, 2500, sign="ARTE")

# ------------------------------------------------------------------ trasporti (8)
def transport(key, name, w, d, h, unlock, family, cost, rent, happiness=1, tags=("trasporti",), **model):
    add(key, "transport", name, w, d, h, family, unlock,
        levels(2, cost, scale_capacity=1.4, rentPerMonth=rent, maintenancePerMonth=cost * 0.005, jobs=max(0, round(cost / 300000)),
               hours=6),
        [], {"happiness": happiness, "happinessRadius": 300, "landValue": 1}, tags, **model)


transport("parcheggio_piccolo", "Parcheggio (20 posti)", 25, 20, 0.2, 1, "parking", 30000, 1200)
transport("parcheggio_grande", "Parcheggio (80 posti)", 50, 40, 0.2, 4, "parking", 110000, 4800)
transport("parcheggio_multipiano", "Parcheggio multipiano", 40, 30, 14, 12, "parkinggarage", 1500000, 18000)
transport("fermata_bus", "Fermata dell'autobus", 6, 2, 3, 1, "busstop", 6000, 0, 1)
transport("stazione_bus", "Autostazione", 40, 26, 7, 8, "station", 900000, 9000, 2)
transport("vialetto", "Vialetto pedonale", 12, 3, 0.1, 1, "path", 1500, 0, 0)
transport("piazza", "Piazza pavimentata", 30, 30, 0.1, 3, "paving", 45000, 0, 3, tags=("trasporti", "piazza"))
transport("eliporto", "Eliporto", 30, 30, 1, 18, "helipad", 700000, 6000, 1)

# ------------------------------------------------------------------ natura (15)
def nature(key, name, w, d, h, unlock, family, cost, happiness, tags=("natura",), **model):
    add(key, "nature", name, w, d, h, family, unlock,
        levels(1, cost, maintenancePerMonth=max(5, cost * 0.01), hours=1),
        [], {"happiness": happiness, "happinessRadius": 40, "landValue": 0.5}, tags, **model)


nature("olivo", "Olivo", 3, 3, 5, 1, "tree", 250, 0.3, tags=("natura", "tree"), kind="olive")
nature("pino_domestico", "Pino domestico", 5, 5, 13, 1, "tree", 400, 0.4, tags=("natura", "tree"), kind="stonepine")
nature("cipresso", "Cipresso", 2, 2, 13, 1, "tree", 300, 0.3, tags=("natura", "tree"), kind="cypress")
nature("quercia", "Quercia", 6, 6, 12, 2, "tree", 450, 0.5, tags=("natura", "tree"), kind="broadleaf")
nature("tiglio", "Tiglio", 5, 5, 11, 2, "tree", 380, 0.4, tags=("natura", "tree"), kind="broadleaf")
nature("palma", "Palma", 3, 3, 9, 3, "tree", 600, 0.4, tags=("natura", "tree"), kind="palm")
nature("abete", "Abete", 4, 4, 14, 3, "tree", 350, 0.3, tags=("natura", "tree"), kind="conifer")
nature("ciliegio", "Ciliegio", 4, 4, 5, 2, "tree", 320, 0.5, tags=("natura", "tree"), kind="fruit")
nature("siepe", "Siepe", 6, 1, 1.5, 1, "hedge", 120, 0.1)
nature("aiuola", "Aiuola fiorita", 4, 4, 0.5, 1, "flowerbed", 180, 0.3)
nature("prato", "Prato", 10, 10, 0.1, 1, "lawn", 150, 0.2)
nature("cespugli", "Cespugli", 3, 2, 1.5, 1, "shrubs", 90, 0.1)
nature("laghetto", "Laghetto", 12, 8, 0.5, 5, "pond", 3500, 1.5)
nature("bosco_piccolo", "Boschetto", 20, 20, 12, 4, "grove", 6000, 2, tags=("natura", "tree"))
nature("giardino_rose", "Roseto", 8, 6, 1, 4, "flowerbed", 900, 1, color="rose")

# ------------------------------------------------------------------ decorazioni e arredo urbano (15)
def decor(key, name, w, d, h, unlock, family, cost, happiness, tags=("decorazione",), **model):
    add(key, "decoration", name, w, d, h, family, unlock,
        levels(1, cost, maintenancePerMonth=max(2, cost * 0.006), hours=0.5),
        [], {"happiness": happiness, "happinessRadius": 30}, tags, **model)


decor("panchina", "Panchina", 1.5, 0.5, 0.9, 1, "bench", 300, 0.1)
decor("lampione", "Lampione", 0.5, 0.5, 4.5, 1, "lamp", 600, 0.1)
decor("lampione_storico", "Lampione storico", 0.5, 0.5, 4, 3, "lamp", 900, 0.2, style="historic")
decor("fontana", "Fontana", 4, 4, 2.5, 3, "fountain", 6000, 1.5)
decor("fontana_monumentale", "Fontana monumentale", 8, 8, 5, 10, "fountain", 30000, 3, size="large")
decor("statua", "Statua", 2, 2, 4, 5, "statue", 9000, 1)
decor("fioriera", "Fioriera", 1.5, 0.5, 0.8, 1, "planter", 250, 0.1)
decor("cestino", "Cestino", 0.5, 0.5, 1, 1, "bin", 120, 0.05)
decor("gazebo", "Gazebo", 4, 4, 3.5, 4, "gazebo", 4500, 0.8)
decor("chiosco", "Chiosco delle bibite", 3, 2.5, 3, 2, "kiosk", 3500, 0.5)
decor("parco_giochi", "Parco giochi", 12, 10, 3, 3, "playground", 12000, 2)
decor("dehors", "Dehors con tavolini", 6, 4, 2.5, 2, "terrace", 2500, 0.6)
decor("monumento_caduti", "Monumento ai caduti", 4, 4, 6, 6, "statue", 15000, 1.5, size="large")
decor("bandiera", "Pennone con bandiera", 2, 1, 8, 2, "flag", 800, 0.2)
decor("pensilina_bici", "Rastrelliera per bici", 3, 1, 1, 2, "bikerack", 700, 0.2)


def main():
    by_cat = {}
    for it in ITEMS:
        by_cat.setdefault(it["category"], []).append(it)
    os.makedirs(OUT, exist_ok=True)
    keys = set()
    for it in ITEMS:
        assert it["key"] not in keys, it["key"]
        keys.add(it["key"])
    for cat, items in by_cat.items():
        with open(os.path.join(OUT, f"{cat}.json"), "w", encoding="utf-8") as f:
            json.dump({"_info": "Generato da tools/catalog/genera_catalogo.py: modificare lo script, non questo file.",
                       "items": items}, f, ensure_ascii=False, indent=1)
            f.write("\n")
    print(f"{len(ITEMS)} voci in {len(by_cat)} categorie: " + ", ".join(f"{k} {len(v)}" for k, v in by_cat.items()))


if __name__ == "__main__":
    main()
