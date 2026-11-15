# 02 — Mondo reale

Obiettivo: ricreare in 3D il mondo reale **il più fedelmente possibile**, con pianta identica (strade, isolati, posizione e forma degli edifici, parchi, fiumi, ferrovie) e **luoghi famosi riprodotti fedelmente**. Si parte da **Chieti**, poi **Italia**, poi **mondo**.

## 1. Fonti dati (tutte gratuite e con licenza che ne permette l'uso)

| Dato | Fonte | Licenza / nota |
|---|---|---|
| Strade, edifici, aree, POI, ferrovie, acqua, aeroporti | **OpenStreetMap** (estratti Geofabrik per paese/regione) | ODbL: citare "© OpenStreetMap contributors" |
| Altitudine del terreno | **Terrain Tiles** (AWS Open Data, formato Terrarium) oppure Copernicus DEM GLO-30 | dati aperti, citare la fonte |
| Globo a bassa risoluzione (confini, coste, città, laghi) | **Natural Earth** | pubblico dominio |
| Popolazione per comune, età, sesso | **ISTAT** (demografia per comune) | CC-BY |
| Redditi per comune | **MEF** – dichiarazioni dei redditi per comune (open data) | open data |
| Popolazione mondiale per città | GeoNames (cities) | CC-BY |
| Meteo reale | **Open-Meteo** API (gratuita, senza chiave) | CC-BY 4.0 |

## 2. Pipeline (`tools/world-pipeline/`, Python 3.12)

Eseguita da GitHub Actions (`world-data.yml`, avvio manuale con parametro regione), mai sul telefono.

1. **Scarica** l'estratto Geofabrik che contiene la regione (es. per Chieti: l'estratto dell'Italia centrale che include l'Abruzzo) e ritaglia con `osmium extract` sul confine richiesto.
2. **Filtra** con `osmium tags-filter` i tag utili (building, highway, railway, landuse, natural, leisure, amenity, shop, tourism, aeroway, waterway, man_made, barrier, power, public_transport…).
3. **Normalizza** in un modello di gioco:
   - edifici → sagoma, altezza (`height` o `building:levels`×3,2 m o stima per tipo/zona), forma del tetto (`roof:shape` o stima), materiali/colori (`building:material`, `building:colour`, `roof:colour`), tipo d'uso (casa, condominio, negozio, chiesa, scuola…), nome, indirizzo, ID OSM;
   - strade → polilinee con classe, corsie, larghezza, senso unico, ponte/galleria, livello, superficie, marciapiedi, limiti;
   - aree → parchi, prati, boschi, campi, acqua, spiagge, parcheggi, piazze;
   - POI → convertiti nelle categorie del catalogo (supermercato, bar, farmacia…), con nome reale;
   - alberi singoli (`natural=tree`) + densità alberi per aree verdi;
   - impianti sportivi (`leisure=stadium/pitch/sports_centre`, `sport=*`) → collegati agli impianti del database sport;
   - aeroporti (`aeroway=*`): piste, taxiway, terminal, gate, eliporti.
4. **Terreno**: heightmap 65×65 per tile (int16, cm) dalle Terrain Tiles; strade ed edifici appoggiati e "scavati" correttamente (Chieti è su una collina: il terreno è fondamentale).
5. **Taglia in tile**: griglia Web Mercator **z14** (≈1,75 km di lato alla latitudine di Chieti). Coordinate locali al tile quantizzate (cm, int32).
6. **Serializza** in formato binario compatto (**FlatBuffers**, schema in `tools/world-pipeline/schema/orbis_tile.fbs`, condiviso con C#) + compressione **zstd**.
7. **Impacchetta** in **region pack** (archivio con indice: tile, bounding box, versione, hash). Una regione italiana per pack; nel mondo un paese per pack (paesi grandi divisi nelle sotto-regioni Geofabrik). Ogni file ≤ 2 GB.
8. **Pubblica** come asset di una GitHub Release (tag `world-<regione>-v<n>`) e aggiorna il `manifest.json` dei dati del mondo.

### Globo base (incluso nell'app)
Un pack "globo" sempre presente, poche centinaia di MB al massimo: terreno mondiale a bassa risoluzione, coste, laghi, fiumi principali, confini, città con popolazione (punti → generano "macchie" urbane stilizzate da lontano), autostrade e ferrovie principali semplificate. Permette di girare il mondo intero dal primo avvio.

### Pack incluso nella build
Il region pack della **provincia di Chieti** (dettaglio completo) è incluso nelle app iOS/Android e scaricato per primo sul web: si gioca subito senza download.

## 3. Livelli di dettaglio del mondo

| Distanza / zoom | Cosa si vede | Da dove |
|---|---|---|
| Globo / paese | terreno, coste, confini, città come macchie | pack globo |
| Regione | strade principali, macchie urbane, terreno | pack globo + region pack (se scaricato) |
| Città | tutte le strade, edifici come blocchi (LOD2), alberi impostor | region pack |
| Quartiere | edifici completi con tetti, facciate, finestre, dettagli stradali | region pack + asset pack |
| Strada / prima persona | dettaglio massimo, arredo urbano, insegne, persone, veicoli | asset pack + generazione procedurale |

Il passaggio è continuo: i tile vengono caricati/scaricati in base alla camera con priorità per distanza e direzione di movimento.

## 4. Generazione procedurale degli edifici
Gli edifici normali **non** sono modelli salvati: si generano al volo dal tile (frazione di secondo per tile, su thread di lavoro) e si mettono in cache in memoria.

- **Profili di stile architettonico per area geografica** (dati in `data/architecture/*.json`): es. *Abruzzo centro storico* (intonaci caldi, coppi, persiane, balconi in ferro), *Italia anni '60-'80* (condomini in laterizio/intonaco, balconi continui, tapparelle), *Nord Europa*, *USA suburbano*, *Medio Oriente*… Il profilo si sceglie da posizione + epoca stimata + tipo edificio.
- **Facciata modulare**: la parete si divide in piani e campate; ogni campata riceve un modulo (finestra, portafinestra con balcone, porta, vetrina, insegna) dal kit del profilo.
- **Tetti**: piani, a falde (algoritmo *straight skeleton*), a padiglione, mansarda, cupole per edifici religiosi; materiali dal profilo o dai tag OSM.
- **Piano terra commerciale** quando nel tile ci sono POI dentro la sagoma (vetrine e insegne con il nome reale).
- **Variabilità controllata**: seed = ID OSM, così lo stesso edificio è sempre uguale.
- Gli edifici modificati dal giocatore (potenziamenti, varianti, nuovi edifici) usano i modelli del catalogo e sostituiscono quelli procedurali.

## 5. Luoghi famosi (landmark) riprodotti fedelmente
- Elenco curato in `data/landmarks/*.json`: ID OSM (o area), nome, modello 3D, fonte e licenza, scala, rotazione, offset, LOD, categoria (monumento, stadio, aeroporto, edificio importante, chiesa, ponte…), effetti di gioco (turismo, felicità).
- Al caricamento del tile, se un edificio/area OSM è un landmark, il procedurale viene sostituito dal modello dedicato.
- **Fonti dei modelli**: modelli gratuiti con licenza CC0/CC-BY (es. Sketchfab con licenza indicata, Poly Haven), oppure modellati da script Blender (`tools/blender/landmarks/`) partendo da sagome OSM, altezze e riferimenti pubblici. Ogni modello con licenza CC-BY va in `CREDITS.md`.
- **Aeroporti**: generatore dedicato da dati OSM (piste, segnaletica, terminal, gate, torri) + landmark per i terminal principali.
- **Stadi e impianti**: generatore parametrico (campo, tribune, coperture, capienza) + landmark per gli stadi famosi.

### Priorità iniziale dei landmark
1. **Chieti** (lista iniziale da verificare e completare con l'admin): Cattedrale di San Giustino, Teatro Marrucino, Tempietti romani, Corso Marrucino, Palazzo de' Mayo, Villa Comunale e Museo archeologico (Villa Frigerj), Chiesa di San Francesco al Corso, stadio Guido Angelini, area di Chieti Scalo (stazione, università), ospedale.
2. **Provincia di Chieti e Abruzzo** (es. Lanciano, Vasto, Ortona, Pescara con aeroporto d'Abruzzo).
3. **Italia – i 100 luoghi più famosi** (Colosseo, San Pietro, Pantheon, Fontana di Trevi, Duomo di Milano, Torre di Pisa, Santa Maria del Fiore, Piazza San Marco, Ponte di Rialto, Mole Antonelliana, Arena di Verona, Reggia di Caserta, principali stadi e aeroporti…).
4. **Mondo – i 300 luoghi più famosi**.

## 6. Download e spazio
- All'avvio: globo + Chieti già pronti.
- Quando la camera si avvicina a una zona non scaricata, il region pack si scarica con priorità alta; mentre arriva si mostra il livello del globo.
- **Italia in background**: tutte le regioni italiane si scaricano automaticamente (solo Wi-Fi sulle app iOS e Android, configurabile), poi il resto del mondo **su richiesta** o in background se attivato.
- Una zona scaricata è **completa e permanente**, funziona offline.
- **Gestione spazio** nelle impostazioni: spazio usato per regione, eliminazione, limite massimo, "scarica tutta Italia ora".
- Il 3D si rigenera dai dati: lo spazio occupato è soprattutto dati compatti. Stima indicativa da verificare in M10: una regione italiana media 50–300 MB.

## 7. Partita "da zero"
Si usa il terreno reale (altitudini, coste, fiumi) di una zona scelta, senza edifici e strade; oppure un terreno generato. Tutto il resto funziona uguale.

## 8. Partita "storica"
Contenuto progressivo: per le città con dati disponibili (all'inizio solo Chieti, in un secondo momento altre) si costruisce una versione semplificata della città in un anno passato partendo dai dati attuali, eliminando gli edifici più recenti (stimati da tag `start_date` OSM e da euristiche) e cambiando veicoli/oggetti con quelli d'epoca. Da qui la città cresce nel tempo.
