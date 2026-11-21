# Crediti

Elenco di tutte le risorse esterne usate in Orbis, con autore, fonte e licenza. Va aggiornato ogni volta che si aggiunge un asset o una fonte dati (vedi `CLAUDE.md`).

## Dati
| Risorsa | Fonte | Licenza |
|---|---|---|
| Mappe (strade, edifici, aree, campagne, luoghi) | © OpenStreetMap contributors, estratti [Geofabrik](https://download.geofabrik.de/) | ODbL 1.0 |
| Altitudine del terreno | [Terrain Tiles](https://registry.opendata.aws/terrain-tiles/) (Mapzen, AWS Open Data; fonti: SRTM, GMTED, ETOPO1, EU-DEM e altre) | dati aperti, attribuzione richiesta |
| Popolazione per età e sesso, bilancio demografico, previsioni delle famiglie, tavole di mortalità (`data/population/`) | ISTAT, [demo.istat.it](https://demo.istat.it/) — elaborazione di Orbis con `tools/population/` | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/deed.it) |
| Redditi IRPEF su base comunale (`data/population/`) | MEF - Dipartimento delle Finanze, [statistiche fiscali](https://www1.finanze.gov.it/finanze/analisi_stat/public/index.php?opendata=yes) — elaborazione di Orbis | [CC BY 3.0 IT](https://creativecommons.org/licenses/by/3.0/it/) |
| Meteo reale nel gioco e clima medio per mese (`data/climate/`) | [Open-Meteo.com](https://open-meteo.com/) (previsioni e archivio storico; dati ERA5 di Copernicus/ECMWF) — clima elaborato da Orbis con `tools/climate/` | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/deed.it) |

## Software
| Componente | Autore | Fonte | Licenza |
|---|---|---|---|
| FlatBuffers (runtime C# per leggere i tile) | Google | [google/flatbuffers](https://github.com/google/flatbuffers) v25.12.19 | Apache 2.0 (`Assets/Orbis/Core/ThirdParty/FlatBuffers/LICENSE.txt`) |

## Font
| Risorsa | Autore | Fonte | Licenza |
|---|---|---|---|
| Lilita One (titoli e bottoni) | Juan Montoreano | [Google Fonts](https://github.com/google/fonts/tree/main/ofl/lilitaone) | SIL Open Font License 1.1 (`Assets/Orbis/UI/Fonts/LilitaOne/OFL.txt`); copia per le insegne del mondo in `Assets/Orbis/World/Resources/OrbisFonts/` |

## Modelli 3D, texture, suoni, musica
_(da compilare)_ — la mini-città del menu, il logo e l'icona sono generati da codice.
