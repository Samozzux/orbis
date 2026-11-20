# Popolazione reale (docs/03 §2)

Scarica le fonti aperte e scrive `data/population/<regione>.json`, che il generatore di popolazione del gioco (Core) legge per creare i cittadini.

| Dato | Fonte | Licenza |
|---|---|---|
| Abitanti per età (0–100) e sesso del comune, totali di tutti i comuni della provincia | ISTAT, popolazione residente al 1° gennaio (`demo.istat.it/data/posas/`) | CC BY 4.0 |
| Nati, morti, trasferimenti dell'anno | ISTAT, bilancio demografico (`demo.istat.it/data/p2/`) | CC BY 4.0 |
| Famiglie per tipo, dimensione media, posizione in famiglia per età (regione) | ISTAT, previsioni delle famiglie, base 2025 (`demo.istat.it/data/previsionifamiliari/`) | CC BY 4.0 |
| Probabilità di morte per età e sesso (provincia) | ISTAT, tavole di mortalità (`demo.istat.it/data/tvm/`) | CC BY 4.0 |
| Contribuenti per fascia di reddito e per tipo di reddito | MEF - Dipartimento delle Finanze, IRPEF su base comunale | CC BY 3.0 IT |

Nomi e cognomi (`data/population/names-it.json`) sono una lista interna di nomi comuni per periodo di nascita, scritta a mano: nessuna fonte esterna.

Solo libreria standard di Python (3.12 o successivo).

```bash
cd tools/population
python -m orbis_population --region regions/chieti.json --cache cache --out ../../data/population/chieti.json
python -m unittest discover -s tests
```

I file scaricati restano in `cache/` (non versionata). Per un anno nuovo basta cambiare `years` in `regions/<regione>.json`.
