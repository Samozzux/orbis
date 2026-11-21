"""Clima medio di una città per mese (ripiego del meteo reale, docs/03 §9).

Scarica dall'archivio storico di Open-Meteo (dati ERA5, licenza CC BY 4.0, https://open-meteo.com) i valori
giornalieri di un periodo e calcola per ogni mese: temperatura media, minima e massima, pioggia, ore di pioggia,
giorni di pioggia, nuvolosità, vento e radiazione solare. Il risultato va in data/climate/<regione>.json.

Uso: python3 orbis_climate.py --lat 42.35 --lon 14.17 --name Chieti --region chieti --start 1995 --end 2024
"""
import argparse
import json
import os
import urllib.parse
import urllib.request

DAILY = ["temperature_2m_mean", "temperature_2m_max", "temperature_2m_min", "precipitation_sum", "precipitation_hours",
         "cloud_cover_mean", "wind_speed_10m_mean", "shortwave_radiation_sum"]


def fetch(lat, lon, start, end):
    q = urllib.parse.urlencode({
        "latitude": lat, "longitude": lon, "start_date": f"{start}-01-01", "end_date": f"{end}-12-31",
        "daily": ",".join(DAILY), "timezone": "Europe/Rome",
    })
    req = urllib.request.Request("https://archive-api.open-meteo.com/v1/archive?" + q, headers={"User-Agent": "orbis-climate"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.load(r)


def normals(daily):
    months = {m: {k: [] for k in DAILY} for m in range(1, 13)}
    for i, day in enumerate(daily["time"]):
        m = int(day[5:7])
        for k in DAILY:
            v = daily[k][i]
            if v is not None:
                months[m][k].append(v)
    out = []
    for m in range(1, 13):
        d = months[m]
        mean = lambda k: sum(d[k]) / len(d[k]) if d[k] else 0.0
        days = len(d["precipitation_sum"]) or 1
        out.append({
            "month": m,
            "tempMean": round(mean("temperature_2m_mean"), 1),
            "tempMax": round(mean("temperature_2m_max"), 1),
            "tempMin": round(mean("temperature_2m_min"), 1),
            "rainMmPerDay": round(mean("precipitation_sum"), 2),
            "rainHoursPerDay": round(mean("precipitation_hours"), 2),
            "rainyDayShare": round(sum(1 for v in d["precipitation_sum"] if v >= 1.0) / days, 3),
            "cloudCover": round(mean("cloud_cover_mean"), 1),
            "windKmh": round(mean("wind_speed_10m_mean"), 1),
            "radiationMjPerDay": round(mean("shortwave_radiation_sum"), 2),
        })
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lat", type=float, required=True)
    ap.add_argument("--lon", type=float, required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--region", required=True)
    ap.add_argument("--start", type=int, default=1995)
    ap.add_argument("--end", type=int, default=2024)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()
    data = fetch(a.lat, a.lon, a.start, a.end)
    result = {
        "format": 1,
        "_info": f"Clima medio di {a.name} per mese ({a.start}–{a.end}), dall'archivio storico di Open-Meteo (ERA5, CC BY 4.0), "
                 "elaborato da tools/climate. Serve quando il meteo reale non c'è (senza rete o con il meteo simulato).",
        "region": a.region,
        "name": a.name,
        "latitude": a.lat,
        "longitude": a.lon,
        "period": [a.start, a.end],
        "months": normals(data["daily"]),
    }
    out = a.out or os.path.join(os.path.dirname(__file__), "..", "..", "data", "climate", a.region + ".json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("scritto", os.path.normpath(out))


if __name__ == "__main__":
    main()
