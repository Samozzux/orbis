"""Mette insieme le fonti nel file data/population/<regione>.json letto dal generatore di popolazione (Core)."""
import json

from . import parse, sources

FORMAT = 1

SOURCES = [
    {"name": "ISTAT — Popolazione residente per età e sesso al 1° gennaio (demo.istat.it)", "license": "CC BY 4.0"},
    {"name": "ISTAT — Bilancio demografico (demo.istat.it)", "license": "CC BY 4.0"},
    {"name": "ISTAT — Previsioni delle famiglie, base 1/1/2025 (demo.istat.it)", "license": "CC BY 4.0"},
    {"name": "ISTAT — Tavole di mortalità provinciali (demo.istat.it)", "license": "CC BY 4.0"},
    {"name": "MEF - Dipartimento delle Finanze — Redditi e principali variabili IRPEF su base comunale", "license": "CC BY 3.0 IT"},
]


def build(region, cache_dir, texts=None):
    """texts: i CSV già letti (nei test); altrimenti si scaricano dalle fonti."""
    if texts is None:
        texts = {k: sources.zip_text(sources.fetch(u, cache_dir)) for k, u in sources.urls(region).items()}
    mun = region["municipality"]
    y = region["years"]
    pop, comuni = parse.population(texts["population"], mun["istat"])
    return {
        "format": FORMAT,
        "region": region["key"],
        "municipality": {"istat": mun["istat"], "name": mun["name"]},
        "population_date": f"{y['population']}-01-01",
        "sources": SOURCES,
        "population": pop,
        "balance": dict(year=y["balance"], **parse.balance(texts["balance"], mun["istat"])),
        "households": parse.households(texts["household_positions"], texts["household_types"], texts["household_size"],
                                       region["region"], y["households"]),
        "mortality": dict(year=y["mortality"], **parse.mortality(texts["mortality"], region["province"]["code"])),
        "income": parse.income(texts["income"], mun["istat"]),
        "province": {"code": region["province"]["code"], "name": region["province"]["name"],
                     "municipalities": sorted(comuni, key=lambda c: c["istat"])},
    }


def dumps(data):
    """JSON leggibile ma compatto: le liste di numeri restano su una riga."""
    def enc(v, ind):
        pad = "  " * ind
        if isinstance(v, dict):
            if not v:
                return "{}"
            items = [f'{pad}  {json.dumps(k, ensure_ascii=False)}: {enc(x, ind + 1)}' for k, x in v.items()]
            return "{\n" + ",\n".join(items) + "\n" + pad + "}"
        if isinstance(v, list):
            if all(not isinstance(x, (dict, list)) for x in v):
                return "[" + ", ".join(json.dumps(x, ensure_ascii=False) for x in v) + "]"
            items = [pad + "  " + enc(x, ind + 1) for x in v]
            return "[\n" + ",\n".join(items) + "\n" + pad + "]"
        return json.dumps(v, ensure_ascii=False)
    return enc(data, 0) + "\n"
