# Pipeline del mondo

OpenStreetMap + Terrain Tiles → **region pack** di tile z14 per il gioco (`docs/02` §2).

## Come si usa
- **In CI** (normale): GitHub → Actions → *World data* → *Run workflow* con regione (`chieti`) e versione.
  Il workflow scarica l'estratto Geofabrik, ritaglia la regione, costruisce il pack e lo pubblica
  come Release `world-<regione>-v<versione>` con anteprime PNG e `report.json`.
  Poi si aggiorna `data/world/packs.json` con tag, nome del file e sha256: le build lo includono.
- **Aggiornare un pack senza toccare i tile**: stesso workflow con `base` = un pack pubblicato (es.
  `world-chieti-v3`). I tile restano identici byte per byte, quindi i lotti e le partite salvate restano validi.
  Si riscrive solo `index.bin`, con i comuni e le linee di trasporto pubblico presi dall'estratto di oggi
  (comando `python -m orbis_world extras`).
- **Test** (nessun download): `python -m pytest -q tests` (una città finta in OSM XML).

## Contenuto di un pack
- `index.bin`: regione, origine, scala, elenco dei tile con dimensione e CRC-32, confine semplificato. Dal pack v4:
  - **comuni** (confini di livello 8 con codice ISTAT), per sapere in quale comune sta un edificio;
  - **linee di trasporto pubblico** (`route=bus/trolleybus/tram/train`): le strade del percorso in ordine (ID OSM delle
    strade dei tile) e le fermate.
- `14/<x>/<y>.bin`: un file per tile, FlatBuffers (`schema/orbis_tile.fbs`) compresso Deflate.
  - **completi** (provincia + 1 km): edifici con sagoma reale, strade, linee, aree (anche campi, vigneti,
    uliveti, frutteti), POI, alberi, **lotti con griglia**, terreno 65×65;
  - **di contorno** (fino a 12 km): terreno, aree grandi, strade principali e fiumi, per avere l'orizzonte.

## Schema
Dopo una modifica a `schema/orbis_tile.fbs`: `./genera-codice.sh` (serve `flatc` 25.12.19). Se la modifica non è
compatibile, aumentare `FORMAT_VERSION` (`orbis_world/encode.py`) e `TileReader.FormatVersion` (C#). Non serve
quando si aggiungono campi in fondo a una tabella: è il caso del pack v4, che i lettori vecchi leggono ignorando
i campi nuovi.
