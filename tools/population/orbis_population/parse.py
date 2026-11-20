"""Lettura dei CSV di ISTAT e MEF nei dati compatti usati dal generatore di popolazione del gioco."""
import csv
import io
import re

MAX_AGE = 100  # nei file ISTAT l'età 100 vale "100 e più"


def _norm(s):
    return " ".join((s or "").replace("﻿", "").split())


def _rows(text, delimiter, first_header):
    """Righe dopo l'intestazione (la prima riga che inizia con first_header), come dizionari."""
    reader = list(csv.reader(io.StringIO(text), delimiter=delimiter))
    for i, row in enumerate(reader):
        if row and _norm(row[0]) == first_header:
            header = [_norm(h) for h in row]
            out = []
            for r in reader[i + 1:]:
                # righe vuote o di note in fondo al file (una sola colonna)
                if not r or not _norm(r[0]) or len(r) < len(header) - 1:
                    continue
                out.append({header[j]: r[j].strip() for j in range(min(len(header), len(r)))})
            return header, out
    raise ValueError(f"intestazione '{first_header}' non trovata")


def _int(v):
    v = (v or "").strip()
    return int(float(v)) if v not in ("", "-") else 0


def population(text, comune):
    """Abitanti per età (0–100) e sesso del comune, più il totale di ogni comune della provincia."""
    _, rows = _rows(text, ";", "Codice comune")
    male = [0] * (MAX_AGE + 1)
    female = [0] * (MAX_AGE + 1)
    total = None
    comuni = []
    for r in rows:
        age = _int(r["Età"])
        code = r["Codice comune"]
        if age == 999:
            comuni.append({"istat": code, "name": r["Comune"], "population": _int(r["Totale"])})
            if code == comune:
                total = _int(r["Totale"])
            continue
        if code != comune:
            continue
        if not 0 <= age <= MAX_AGE:
            raise ValueError(f"età fuori scala: {age}")
        male[age] = _int(r["Totale maschi"])
        female[age] = _int(r["Totale femmine"])
    if total is None:
        raise ValueError(f"comune {comune} non trovato")
    if sum(male) + sum(female) != total:
        raise ValueError(f"somma per età {sum(male) + sum(female)} diversa dal totale {total}")
    return {"total": total, "male": male, "female": female}, comuni


BALANCE = {
    "births": "Nati vivi - Totale",
    "deaths": "Morti - Totale",
    "moved_in": "Immigrati da altro comune - Totale",
    "moved_out": "Emigrati per altro comune - Totale",
    "immigrated": "Immigrati dall'estero - Totale",
    "emigrated": "Emigrati per l'estero - Totale",
    "population_start": "Popolazione censita al 1° gennaio - Totale",
    "population_end": "Popolazione al 31 dicembre - Totale",
}


def balance(text, comune):
    """Bilancio demografico dell'anno: nati, morti, trasferimenti (per tarare il ciclo di vita)."""
    _, rows = _rows(text, ";", "Codice comune")
    for r in rows:
        if r["Codice comune"] == comune:
            return {k: _int(r[col]) for k, col in BALANCE.items()}
    raise ValueError(f"comune {comune} non trovato nel bilancio")


# posizioni nella famiglia (ISTAT, previsioni delle famiglie), nell'ordine delle colonne
POSITIONS = [
    ("alone_male", "Persona sola maschio"),
    ("alone_female", "Persona sola femmina"),
    ("couple_no_children", "Persona in coppia senza figli"),
    ("couple_young_children", "Persona in coppia con almeno un figlio con meno di 20 anni"),
    ("couple_adult_children", "Persona in coppia con tutti i figli di 20 anni o più"),
    ("father_young_children", "Padre solo con almeno un figlio con meno di 20 anni"),
    ("father_adult_children", "Padre solo con tutti i figli di 20 anni o più"),
    ("mother_young_children", "Madre sola con almeno un figlio con meno di 20 anni"),
    ("mother_adult_children", "Madre sola con tutti i figli di 20 anni o più"),
    ("child", "Figlio/a"),
    ("other", "Altra posizione familiare"),
]

HOUSEHOLD_TYPES = [
    ("alone_male", "Persone sole maschi"),
    ("alone_female", "Persone sole femmine"),
    ("couple_no_children", "Coppie senza figli"),
    ("couple_young_children", "Coppie con almeno un figlio con meno di 20 anni"),
    ("couple_adult_children", "Coppie con con tutti i figli di 20 anni o più"),
    ("father_young_children", "Padri soli con almeno un figlio con meno di 20 anni"),
    ("father_adult_children", "Padri soli con tutti i figli di 20 anni o più"),
    ("mother_young_children", "Madri sole con almeno un figlio con meno di 20 anni"),
    ("mother_adult_children", "Madri sole con tutti i figli di 20 anni o più"),
    ("other", "Altro tipo di famiglia"),
]


def _age_class(label):
    label = _norm(label)
    m = re.fullmatch(r"(\d+)-(\d+)", label)
    if m:
        return int(m.group(1)), int(m.group(2))
    m = re.match(r"(\d+)", label)
    if m:
        return int(m.group(1)), MAX_AGE
    raise ValueError(f"classe di età non riconosciuta: {label}")


def households(positions_text, types_text, size_text, region, year):
    """Famiglie della regione: tipi, dimensione media e posizione in famiglia per classe d'età."""
    year = str(year)
    _, rows = _rows(positions_text, ";", "Regione/Ripartizione")
    by_age = []
    for r in rows:
        if r["Regione/Ripartizione"] != region or r["Anno"] != year:
            continue
        lo, hi = _age_class(r["Classe di età"])
        counts = [_int(r[col]) for _, col in POSITIONS]
        if sum(counts) != _int(r["Totale"]):
            raise ValueError(f"posizioni {lo}-{hi}: somma diversa dal totale")
        by_age.append({"from": lo, "to": hi, "counts": counts})
    if not by_age:
        raise ValueError(f"nessuna riga per {region} {year}")
    by_age.sort(key=lambda a: a["from"])

    _, rows = _rows(types_text, ";", "Regione/Ripartizione")
    types = None
    for r in rows:
        if r["Regione/Ripartizione"] == region and r["Anno"] == year:
            types = {k: _int(r[col]) for k, col in HOUSEHOLD_TYPES}
            if sum(types.values()) != _int(r["Totale"]):
                raise ValueError("tipi di famiglia: somma diversa dal totale")
    if types is None:
        raise ValueError(f"tipi di famiglia: nessuna riga per {region} {year}")

    _, rows = _rows(size_text, ";", "Regione/Ripartizione")
    size = None
    for r in rows:
        if r["Regione/Ripartizione"] == region and r["Anno"] == year:
            size = {
                "average": float(r["Numero medio componenti"].replace(",", ".")),
                "average_with_nucleus": float(r["Numero medio componenti delle famiglie con almeno un nucleo"].replace(",", ".")),
            }
    if size is None:
        raise ValueError(f"dimensione delle famiglie: nessuna riga per {region} {year}")

    return {
        "region": region,
        "year": int(year),
        "size": size,
        "types": types,
        "position_keys": [k for k, _ in POSITIONS],
        "positions_by_age": by_age,
    }


def mortality(text, province_code):
    """Probabilità di morire entro l'anno (qx) per età e sesso, dalla tavola di mortalità della provincia."""
    _, rows = _rows(text, ",", "Cod. provincia")
    code = int(province_code)
    out = {"male": {}, "female": {}}
    for r in rows:
        if _int(r["Cod. provincia"]) != code:
            continue
        sex = {"Maschi": "male", "Femmine": "female"}.get(r["Sesso"])
        if sex is None:
            continue
        out[sex][_int(r["Età"])] = float(r["Probabilità di morte (per mille)"]) / 1000.0
    if not out["male"] or not out["female"]:
        raise ValueError(f"tavola di mortalità della provincia {province_code} non trovata")
    res = {}
    for sex, table in out.items():
        top = max(table)
        if sorted(table) != list(range(top + 1)):
            raise ValueError("tavola di mortalità con età mancanti")
        res[sex] = [round(min(1.0, table[a]), 7) for a in range(top + 1)]
    return res


INCOME_KINDS = {
    "employee": ["Reddito da lavoro dipendente e assimilati"],
    "pension": ["Reddito da pensione"],
    "self_employed": ["Reddito da lavoro autonomo (comprensivo dei valori nulli)"],
    "business": [
        "Reddito di spettanza dell'imprenditore in contabilita' ordinaria (comprensivo dei valori nulli)",
        "Reddito di spettanza dell'imprenditore in contabilita' semplificata (comprensivo dei valori nulli)",
    ],
    "partnership": ["Reddito da partecipazione (comprensivo dei valori nulli)"],
    "buildings": ["Reddito da fabbricati"],
}


def income(text, comune):
    """Redditi IRPEF del comune: contribuenti per fascia di reddito complessivo e per tipo di reddito."""
    header, rows = _rows(text, ";", "Anno di imposta")
    row = next((r for r in rows if r.get("Codice Istat Comune", "").zfill(6) == comune), None)
    if row is None:
        raise ValueError(f"comune {comune} non trovato nei redditi MEF")
    classes = []
    for col in header:
        m = re.fullmatch(r"Reddito complessivo (.+) - Frequenza", col)
        if not m:
            continue
        label = m.group(1)
        amount = _int(row[col.replace("Frequenza", "Ammontare in euro")])
        count = _int(row[col])
        if label.startswith("minore o uguale a zero"):
            lo, hi = None, 0
        elif label.startswith("oltre"):
            lo, hi = int(re.search(r"\d+", label).group()), None
        else:
            n = [int(x) for x in re.findall(r"\d+", label)]
            if len(n) != 2:
                continue
            lo, hi = n
        classes.append({"from": lo, "to": hi, "count": count, "amount": amount})
    if len(classes) < 5:
        raise ValueError("fasce di reddito non trovate")
    kinds = {}
    for key, cols in INCOME_KINDS.items():
        kinds[key] = {
            "count": sum(_int(row[c + " - Frequenza"]) for c in cols),
            "amount": sum(_int(row[c + " - Ammontare in euro"]) for c in cols),
        }
    total = {"count": _int(row["Reddito complessivo - Frequenza"]), "amount": _int(row["Reddito complessivo - Ammontare in euro"])}
    if sum(c["count"] for c in classes) != total["count"]:
        raise ValueError("fasce di reddito: somma dei contribuenti diversa dal totale")
    return {
        "year": _int(row["Anno di imposta"]),
        "taxpayers": _int(row["Numero contribuenti"]),
        "total": total,
        "classes": classes,
        "kinds": kinds,
    }
