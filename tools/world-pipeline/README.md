# Pipeline del mondo

OpenStreetMap + Terrain Tiles → **region pack** di tile z14 per il gioco (`docs/02` §2).

## Come si usa
- **In CI** (normale): GitHub → Actions → *World data* → *Run workflow* con regione (`chieti`) e versione.
  Il workflow scarica l'estratto Geofabrik, ritaglia la regione, costruisce il pack e lo pubblica
  come Release `world-<regione>-v<versione>` con anteprime PNG e `report.json`.
  Poi si aggiorna `data/world/packs.json` con tag, nome del file e sha256: le build lo includono.
- **Test** (nessun download): `python -m pytest -q tests` (una città finta in OSM XML).

## Contenuto di un pack
- `index.bin`: regione, origine, scala, elenco dei tile con dimensione e CRC-32, confine semplificato.
- `14/<x>/<y>.bin`: un file per tile, FlatBuffers (`schema/orbis_tile.fbs`) compresso Deflate.
  - **completi** (provincia + 1 km): edifici con sagoma reale, strade, linee, aree (anche campi, vigneti,
    uliveti, frutteti), POI, alberi, **lotti con griglia**, terreno 65×65;
  - **di contorno** (fino a 12 km): terreno, aree grandi, strade principali e fiumi, per avere l'orizzonte.

## Schema
Dopo una modifica a `schema/orbis_tile.fbs`: `./genera-codice.sh` (serve `flatc` 25.12.19) e aumentare
`FORMAT_VERSION` (`orbis_world/encode.py`) e `TileReader.FormatVersion` (C#).
