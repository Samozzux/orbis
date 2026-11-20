"""Test del lettore delle fonti con piccoli estratti nello stesso formato dei file ISTAT e MEF."""
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from orbis_population import build, parse  # noqa: E402

POP = '﻿"Popolazione residente per età e sesso al 1° gennaio 2026 (stima)"\n' \
      '"Codice comune";"Comune";"Età";"Totale maschi";"Totale femmine";"Totale"\n' \
      + "".join(f'"069022";"Chieti";{a};{2 if a < 3 else 0};{1 if a < 3 else 0};{3 if a < 3 else 0}\n' for a in range(101)) \
      + '"069022";"Chieti";999;6;3;9\n"069001";"Altino";0;1;1;2\n"069001";"Altino";999;1;1;2\n' \
      '"Dati provvisori"\n'

BAL = '"Bilancio demografico"\n"Codice comune";"Comune";"Popolazione censita al 1° gennaio - Totale";"Nati vivi - Totale";' \
      '"Morti - Totale";"Immigrati da altro comune - Totale";"Emigrati per altro comune - Totale";' \
      '"Immigrati dall\'estero - Totale";"Emigrati per l\'estero - Totale";"Popolazione al 31 dicembre - Totale";"Informazioni"\n' \
      '"069022";"Chieti";100;2;3;10;9;4;1;103;"p"\n'

POS_HEADER = "Regione/Ripartizione;Anno;Classe di età;" + ";".join(c for _, c in parse.POSITIONS) + ";Totale\n"
POSITIONS = POS_HEADER + "Abruzzo;2025;00-04;0;0;0;0;0;0;0;0;0;9;1;10\n" \
                         "Abruzzo;2025;85 e oltre;3;5;2;0;0;0;0;0;0;0;1;11\n" \
                         "Italia;2025;00-04;0;0;0;0;0;0;0;0;0;99;1;100\n"
TYPES = "Regione/Ripartizione;Anno;" + ";".join(c for _, c in parse.HOUSEHOLD_TYPES) + ";Totale\n" \
        "Abruzzo;2025;1;2;3;4;5;6;7;8;9;10;55\n"
SIZE = "Regione/Ripartizione;Anno;Numero medio componenti;Numero medio componenti delle famiglie con almeno un nucleo\n" \
       "Abruzzo;2025;2.19;2.88\n"

MORT = "Cod. provincia,Provincia,Sesso,Età,Sopravviventi,Decessi,Probabilità di morte (per mille),Anni vissuti," \
       "Probabilità prospettive di sopravvivenza,Speranza di vita,Informazioni\n" \
       "69,Chieti,Maschi,0,100000,200,2.0,0,0,80,s\n69,Chieti,Maschi,1,99800,10,0.1,0,0,79,s\n" \
       "69,Chieti,Femmine,0,100000,150,1.5,0,0,85,s\n69,Chieti,Femmine,1,99850,8,0.08,0,0,84,s\n" \
       "69,Chieti,Maschi e femmine,0,100000,175,1.75,0,0,82,s\n1,Torino,Maschi,0,100000,1,9.0,0,0,80,s\n"


def _mef():
    cols = ["Anno di imposta", "Codice catastale", "Codice Istat Comune", "Denominazione Comune", "Numero contribuenti"]
    vals = ["2024", "C632", "069022", "CHIETI", "12"]
    for kind in ["Reddito da fabbricati", "Reddito da lavoro dipendente e assimilati", "Reddito da pensione",
                 "Reddito da lavoro autonomo (comprensivo dei valori nulli)",
                 "Reddito di spettanza dell'imprenditore in contabilita' ordinaria  (comprensivo dei valori nulli)",
                 "Reddito di spettanza dell'imprenditore in contabilita' semplificata (comprensivo dei valori nulli)",
                 "Reddito da partecipazione  (comprensivo dei valori nulli)"]:
        cols += [kind + " - Frequenza", kind + " - Ammontare in euro"]
        vals += ["2", "1000"]
    cols += ["Reddito complessivo - Frequenza", "Reddito complessivo - Ammontare in euro"]
    vals += ["10", "300000"]
    classes = [("minore o uguale a zero euro", 0, 0), ("da 0 a 10000 euro", 4, 20000), ("da 10000 a 15000 euro", 3, 40000),
               ("da 15000 a 26000 euro", 2, 40000), ("da 26000 a 55000 euro", 0, 0), ("oltre 120000 euro", 1, 200000)]
    for label, n, amount in classes:
        cols += [f"Reddito complessivo {label} - Frequenza", f"Reddito complessivo {label} - Ammontare in euro"]
        vals += [str(n), str(amount)]
    return ";".join(cols) + ";\r\n" + ";".join(vals) + "\r\n"


MEF = _mef()

REGION = {
    "key": "chieti",
    "municipality": {"istat": "069022", "name": "Chieti"},
    "province": {"code": "069", "name": "Chieti"},
    "region": "Abruzzo",
    "years": {"population": 2026, "balance": 2025, "households": 2025, "mortality": 2025, "income": 2024},
}

TEXTS = {"population": POP, "balance": BAL, "household_positions": POSITIONS, "household_types": TYPES,
         "household_size": SIZE, "mortality": MORT, "income": MEF}


class ParseTests(unittest.TestCase):
    def test_population_per_eta_e_comuni(self):
        pop, comuni = parse.population(POP, "069022")
        self.assertEqual(pop["total"], 9)
        self.assertEqual(len(pop["male"]), 101)
        self.assertEqual(pop["male"][:4], [2, 2, 2, 0])
        self.assertEqual(comuni, [{"istat": "069022", "name": "Chieti", "population": 9},
                                  {"istat": "069001", "name": "Altino", "population": 2}])

    def test_population_somma_sbagliata(self):
        with self.assertRaises(ValueError):
            parse.population(POP.replace('"069022";"Chieti";999;6;3;9', '"069022";"Chieti";999;6;3;10'), "069022")

    def test_comune_mancante(self):
        with self.assertRaises(ValueError):
            parse.population(POP, "069099")

    def test_bilancio(self):
        b = parse.balance(BAL, "069022")
        self.assertEqual(b["births"], 2)
        self.assertEqual(b["deaths"], 3)
        self.assertEqual(b["immigrated"], 4)
        self.assertEqual(b["population_end"], 103)

    def test_famiglie(self):
        h = parse.households(POSITIONS, TYPES, SIZE, "Abruzzo", 2025)
        self.assertEqual(h["size"]["average"], 2.19)
        self.assertEqual(h["types"]["other"], 10)
        self.assertEqual([(a["from"], a["to"]) for a in h["positions_by_age"]], [(0, 4), (85, 100)])
        self.assertEqual(h["positions_by_age"][1]["counts"][1], 5)
        self.assertEqual(len(h["position_keys"]), len(h["positions_by_age"][0]["counts"]))

    def test_mortalita(self):
        m = parse.mortality(MORT, "069")
        self.assertEqual(m["male"], [0.002, 0.0001])
        self.assertEqual(m["female"], [0.0015, 0.00008])

    def test_redditi(self):
        i = parse.income(MEF, "069022")
        self.assertEqual(i["taxpayers"], 12)
        self.assertEqual(i["total"]["count"], 10)
        self.assertEqual(i["classes"][0], {"from": None, "to": 0, "count": 0, "amount": 0})
        self.assertEqual(i["classes"][-1], {"from": 120000, "to": None, "count": 1, "amount": 200000})
        self.assertEqual(i["kinds"]["business"], {"count": 4, "amount": 2000})

    def test_file_completo(self):
        data = build.build(REGION, cache_dir=None, texts=TEXTS)
        text = build.dumps(data)
        back = json.loads(text)
        self.assertEqual(back["population"]["total"], 9)
        self.assertEqual(back["format"], build.FORMAT)
        self.assertIn('"male": [2, 2, 2, 0', text)
        self.assertEqual(back["province"]["municipalities"][0]["istat"], "069001")


class DataFileTests(unittest.TestCase):
    """Il file versionato nel repository è coerente (stesse regole del generatore nel Core)."""

    def test_chieti(self):
        path = os.path.join(os.path.dirname(__file__), "..", "..", "..", "data", "population", "chieti.json")
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        pop = d["population"]
        self.assertEqual(sum(pop["male"]) + sum(pop["female"]), pop["total"])
        self.assertGreater(pop["total"], 40000)
        self.assertEqual(sum(c["count"] for c in d["income"]["classes"]), d["income"]["total"]["count"])
        self.assertEqual(len(d["mortality"]["male"]), len(d["mortality"]["female"]))
        self.assertTrue(all(0 <= q <= 1 for q in d["mortality"]["male"] + d["mortality"]["female"]))


if __name__ == "__main__":
    unittest.main()
