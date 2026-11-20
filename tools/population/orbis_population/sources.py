"""Scaricamento delle fonti aperte (ISTAT, MEF) con cache locale.

Solo file pubblicati per il riuso: ISTAT (CC BY 4.0) e MEF - Dipartimento delle Finanze (CC BY 3.0 IT).
"""
import io
import os
import urllib.request
import zipfile

ISTAT_DEMO = "https://demo.istat.it/data"
MEF_STAT = "https://www1.finanze.gov.it/finanze/analisi_stat/public/v_4_0_0/contenuti"
USER_AGENT = "Orbis-population/1.0 (open data import)"


def urls(region):
    """Indirizzi dei file per la regione di gioco (vedi regions/*.json)."""
    y = region["years"]
    prov = region["province"]
    # i file provinciali dell'ISTAT usano il nome con i trattini bassi al posto degli spazi
    pname = prov["name"].replace(" ", "_")
    fam = ISTAT_DEMO + "/previsionifamiliari/"
    return {
        "population": f"{ISTAT_DEMO}/posas/POSAS_{y['population']}_it_{prov['code']}_{pname}.zip",
        "balance": f"{ISTAT_DEMO}/p2/P2_{y['balance']}_it_{prov['code']}_{pname}.zip",
        "household_positions": fam + "Popolazione_prevista_per_posizione_familiare.csv.zip",
        "household_types": fam + "Famiglie_per_tipologia_familiare.csv.zip",
        "household_size": fam + "Numero_medio_di_componenti_familiari.csv.zip",
        "mortality": f"{ISTAT_DEMO}/tvm/datiprovincialicompleti{y['mortality']}.zip",
        "income": f"{MEF_STAT}/Redditi_e_principali_variabili_IRPEF_su_base_comunale_CSV_{y['income']}.zip?d=1615465800",
    }


def fetch(url, cache_dir):
    """Scarica una volta e tiene il file nella cache (le fonti cambiano una volta l'anno)."""
    os.makedirs(cache_dir, exist_ok=True)
    name = url.split("/")[-1].split("?")[0]
    path = os.path.join(cache_dir, name)
    if not os.path.exists(path):
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
        tmp = path + ".part"
        with open(tmp, "wb") as f:
            f.write(data)
        os.replace(tmp, path)
    with open(path, "rb") as f:
        return f.read()


def zip_text(data):
    """Testo del primo CSV dentro lo zip (UTF-8 con o senza BOM, altrimenti Latin-1)."""
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        names = [n for n in z.namelist() if n.lower().endswith(".csv")]
        if not names:
            raise ValueError("nessun CSV nello zip")
        raw = z.read(names[0])
    try:
        return raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return raw.decode("latin-1")
