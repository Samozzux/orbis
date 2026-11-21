# Stato del progetto

## Milestone attuale
**M4 — Cittadini e città viva**: iniziata il 19/11/2026, dopo la chiusura di M3 (branch `m4-piano`).

### Piano di M4 (a blocchi, ognuno con PR e test)
Criterio (docs/09): la città si muove da sola con migliaia di cittadini, ognuno ispezionabile (casa, lavoro, soldi, frigo), e le scelte del sindaco cambiano felicità ed economia.
1. **Dati reali.**
   - `tools/population/` (Python):
     - abitanti del comune per età e sesso (ISTAT);
     - redditi per fascia (MEF);
     - composizione delle famiglie (ISTAT);
     - nomi per anno di nascita e cognomi frequenti in Abruzzo;

     il risultato va in `data/population/chieti.json`.
   - Pack del mondo v4:
     - confini dei comuni, per sapere quali case stanno nel comune di Chieti;
     - linee bus reali da OSM (`route=bus`, con le fermate).
   - Calendario `data/events/`: festività italiane, Venerdì Santo, San Giustino.
   - CREDITS aggiornato.
   - **Fonti già verificate** (19/11):
     - ISTAT, popolazione per età e sesso al 1° gennaio 2026 (stima), un file per provincia: `demo.istat.it/data/posas/POSAS_2026_it_069_Chieti.zip`. Comune 069022: 48.368 abitanti. Licenza CC BY 4.0.
     - MEF, Dipartimento delle Finanze, redditi IRPEF per comune (anno d'imposta 2024): contribuenti per fascia di reddito e per tipo (dipendente, pensione, autonomo). Chieti: 36.435 contribuenti. Licenza CC BY 3.0 IT.
     - Ancora da trovare: famiglie per numero di componenti, nomi per anno di nascita, cognomi.

### Avanzamento M4
- **Dati reali, prima parte** (blocco 1, 20/11, branch `m4-dati`):
  - **Strumento.** `tools/population/`, in Python con la sola libreria standard. Scarica le fonti aperte e scrive `data/population/chieti.json` (20 KB). Contenuto:
    - abitanti di Chieti per età (0–100) e sesso: 48.368 al 1° gennaio 2026;
    - bilancio del 2025: 247 nati, 581 morti, 1.254 arrivati da altri comuni e 1.142 partiti, 324 arrivati dall'estero e 137 partiti;
    - famiglie dell'Abruzzo nel 2025: tipi, 2,19 persone in media e posizione in famiglia per età (sola, in coppia, figlio…), dalle previsioni ISTAT;
    - tavola di mortalità della provincia, per età e sesso;
    - redditi di Chieti nel 2024 (MEF): 36.435 contribuenti, divisi in 8 fasce di reddito e per tipo (dipendenti, pensioni, autonomi, imprese);
    - abitanti di tutti i 104 comuni della provincia (369.059 in tutto), per il livello L3.
  - **Controlli.** Lo strumento rifiuta i file che non tornano: somme per età diverse dal totale, fasce di reddito incomplete, età mancanti. 9 test (`python -m unittest`) e un workflow `Population data`.
  - **Famiglie per numero di componenti.** Non c'è un file ISTAT per comune: uso i dati regionali sulla posizione in famiglia per età, più che sufficienti per formare le famiglie.
  - **Nomi e cognomi.** `data/population/names-it.json` è una lista interna scritta a mano, perché l'ISTAT pubblica i nomi dei nati solo dal 1999 e senza file scaricabile. Contiene i 25 nomi più comuni per sesso e periodo di nascita (dal 1920 al 2029) e 95 cognomi frequenti in Abruzzo e in Italia.
  - **Calendario.** In `data/events/`:
    - `italia.json`: festività nazionali, con le date di Pasqua relative al giorno di Pasqua;
    - `chieti.json`: la processione del Venerdì Santo dalla cattedrale, alle 19, e San Giustino, l'11 maggio.
  - **CREDITS.** Aggiunti ISTAT (CC BY 4.0) e MEF (CC BY 3.0 IT). La schermata Crediti del gioco è ancora "in arrivo": quando si fa deve elencarli.
- **Pack del mondo v4** (blocco 1, 20/11, branch `m4-pack-v4`): `world-chieti-v4`, pubblicato e incluso nelle build.
  - **Tile.** Identici byte per byte a quelli del v3: stessi lotti, quindi le partite salvate restano valide. Il nuovo comando della pipeline `extras` (nel workflow: campo `base`) riscrive solo `index.bin`, con l'estratto OSM del 20/11. Ricostruire tutto con l'OSM di oggi avrebbe cambiato i numeri dei lotti.
  - **Comuni.** Tutti i 104 della provincia, col codice ISTAT e il confine semplificato a ~10 m. In C#, `RegionInfo.MunicipalityAt(lon, lat)` dice in quale comune cade un punto.
  - **Linee.** 40 linee (`route=bus/trolleybus/tram/train`) con 376 fermate e le strade del percorso in ordine. Ci sono i bus urbani di Chieti ("La Panoramica"), la filovia, i bus GTM di Pescara, i treni Trenitalia e i Flixbus. Molte linee in OSM hanno poche fermate segnate: se ne terrà conto nel blocco 4.
  - **Schema.** Campi nuovi in fondo a `RegionIndex`, che i lettori vecchi ignorano: il formato resta 1.
  - **Test.** 10 della pipeline (anche "aggiornamento con tile identici") e 169 del Core (indice v4 con comuni e linea bus).
  - **Indirizzo Geofabrik.** L'estratto del Sud Italia ora sta in `europe/italy/sud/`: il vecchio indirizzo faceva fallire il controllo md5.
- **Generatore di popolazione** (blocco 2, 20/11, branch `m4-popolazione`), in `Orbis.Core.Population`, logica pura:
  - **Luoghi.** `CityPlaces` prende dai tile del comune (`TilesFor` dice quali caricare) le case, i posti di lavoro e le scuole. Le stime sono in `data/balance/population.json`, valori iniziali da bilanciare:
    - **case:** edifici residenziali; gli "edificio generico" contano con peso 0,6. Superficie dei piani = impronta × piani;
    - **posti di lavoro:** edifici non residenziali (posti per m² secondo l'uso) e attività OSM nelle case (bar, negozi, farmacie…);
    - **scuole:** per livello (infanzia, scuola, università).
  - **Generazione.** `PopulationGenerator`, dal seed della partita e a fasi (`Steps()`), così il gioco può spezzarla su più fotogrammi:
    - **persone:** età e sesso esatti dall'ISTAT;
    - **famiglie:** dalle posizioni in famiglia per età (ISTAT, Abruzzo). Le coppie si formano per età e i figli vanno a genitori di età plausibile (17–50 anni di differenza). Le convivenze sono nella quota ISTAT, il resto sono parenti in casa;
    - **case:** ogni famiglia va in una casa in proporzione alla superficie, con appartamento, proprietà o affitto (6 €/m²);
    - **studio:** titolo per anno di nascita;
    - **lavoro e reddito:** pensionati, dipendenti, autonomi e imprenditori nei numeri del MEF. I redditi seguono le fasce MEF e vanno a chi ha più studio, età ed esperienza. Il 70% lavora nel comune, il resto fuori;
    - **scuola:** gli alunni vanno nella scuola più vicina con posti liberi;
    - **persona:** nomi per periodo di nascita; i figli prendono il cognome del padre. Poi tratti (senza contrari), gusti, aspetto e bisogni.
  - **Risultato** sui dati di Chieti, con case finte:
    - 48.368 cittadini e 22.560 famiglie, 2,14 persone in media (ISTAT 2,19);
    - persone sole: 36% (Abruzzo 37%);
    - 18.941 dipendenti con 26.700 € di reddito medio e 13.482 pensionati con 23.300 €.

    La generazione dura meno di un secondo sul Mac.
  - **Salvataggio.** `PopulationSerializer`, binario con versione, circa 3,6 MB prima della compressione. Salva tutto, non solo le differenze: i cittadini cambiano di continuo, e così la città non dipende da come verrebbe rigenerata su un'altra piattaforma o con un altro pack. Un file rovinato viene rifiutato.
  - **Test.** 9 nuovi (178 in tutto):
    - età e sesso uguali all'ISTAT;
    - famiglie coerenti: ognuno in una famiglia, coppie reciproche, nessun minore senza adulto;
    - case non troppo piene, posti di lavoro rispettati, ogni alunno ha la sua scuola;
    - redditi nelle fasce MEF;
    - stesso seed, stessa città;
    - salvataggio andata e ritorno;
    - case e lavori dai tile veri, col confine del comune.
  - **Nel gioco.** I dati sono copiati in Resources. Collegare il generatore alla nuova partita (caricamento dei tile del comune con la barra di avanzamento, salvataggio) si fa con la vita dei cittadini (blocco 3), quando la popolazione serve davvero alla simulazione.
- **Vita dei cittadini, parte economica e ciclo di vita** (blocco 3a, 20/11, branch `m4-vita`). Il blocco 3 è diviso in tre parti:
  - 3a: soldi e ciclo di vita giorno per giorno (livello L2);
  - 3b: popolazione generata nella nuova partita e salvata;
  - 3c: routine, bisogni, frigo e spesa vicino alla camera (L1).

  Cosa c'è nella 3a (`CityLife`):
  - **Ogni giorno, i soldi:**
    - stipendi e pensioni, con le tasse allo Stato e l'addizionale comunale al sindaco;
    - spese della famiglia: cibo, affitto e bollette, il 15% del netto risparmiato;
    - debiti fino a 3.000 € per adulto, poi l'aiuto sociale: lo paga per il 25% il comune e per il resto lo Stato.
  - **Attività:**
    - incassano dai clienti di fuori e dalla spesa dei cittadini (negozi, bar, ristoranti, in proporzione ai posti);
    - pagano stipendi e costi; pagano le tasse sul fatturato, commerciali o industriali secondo l'aliquota;
    - i posti non coperti dai residenti li coprono pendolari da fuori, con 1,45 posti per lavoratore residente come la stima di M2;
    - gli utili oltre tre mesi di costi vanno ai proprietari;
    - un'attività in rosso per 4 mesi chiude e licenzia.
  - **Lavoro.** Il 7% l'anno perde il lavoro; chi lo cerca lo trova in circa un anno nei posti liberi.
  - **Ciclo di vita** (se attivo):
    - morti con la tavola di mortalità ISTAT, con eredità al partner o in famiglia;
    - nascite nelle coppie, con l'età delle madri centrata sui 32 anni e il totale tarato sui nati ISTAT;
    - compleanni: asilo, scuola, maturità, università, laurea, pensione al 70%;
    - partenze e arrivi con le quote del bilancio ISTAT; chi arriva trova una casa libera, magari un lavoro, e porta i risparmi.
  - **Conti.** Il denaro si conserva: un test verifica al centesimo che cittadini, attività e comune cambiano solo per ciò che entra o esce dalla città. Lo stesso giorno non si simula due volte.
  - **Un anno sulla città di prova:**

    | | Simulati | ISTAT |
    |---|---|---|
    | Abitanti a fine anno | 48.172 (da 48.368) | −35 |
    | Nati | 245 | 247 |
    | Morti | 626 | 581 |
    | Arrivati | 1.492 | 1.578 |
    | Partiti | 1.304 | 1.279 |

    I disoccupati si assestano intorno al 12%, nessuna attività fallisce e l'anno si simula in pochi secondi sul Mac.
  - **Nel gioco (Core):**
    - a mezzanotte la simulazione fa la giornata dei cittadini, poi chiude i conti del comune. Tasse e aiuti sociali finiscono nel registro del giorno;
    - la previsione del pannello Finanze ne tiene conto;
    - se la partita ha i cittadini, la città esistente non si stima più (popolazione e posti veri).
  - **Saldo.** Quando i cittadini veri prendono il posto della città stimata, il costo dei servizi si aggiusta una volta (`ServicesAdjustPerDay`, salvato nella partita), così la partita mantiene il saldo che aveva. Le tasse delle attività dipendono da come è fatta la città, e il pareggio di M2 era tarato a mano sulla città stimata.
  - **Salvataggio della partita.**
    - Versione 2 con i cittadini, stato versione 3; i salvataggi vecchi si leggono ancora.
    - "Riavvolgi" riporta anche i cittadini se l'istantanea è degli ultimi 7 giorni: quelle dei cittadini stanno solo in memoria, perché pesano qualche MB. Più indietro, i cittadini restano come sono e riprendono a vivere dal giorno riavvolto.
  - **Test.** 6 nuovi (184 in tutto): denaro conservato, un anno senza valori assurdi, giorno non ripetuto, senza ciclo di vita nessuno nasce né muore, tasse nel registro di una partita vera, salvataggio e riavvolgimento.
  - **Dimensione del salvataggio.** Senza compressione è di circa 3,6 MB con 48.000 cittadini. Un compressore Deflate in C# puro (come il decompressore dei tile) lo ridurrebbe: si valuta con le prestazioni (blocco 8).
- **Cittadini nella partita** (blocco 3b, 21/11, branch `m4-cittadini-in-partita`):
  - **Quando partono.** Se una partita non ha ancora i cittadini (nuova, o salvata prima di M4), quando il mondo è pronto `PopulationLoader` carica uno alla volta i tile del comune di Chieti. Raccoglie case, lavori e scuole e genera i cittadini a fasi, una per fotogramma, mentre si gioca. Poi li collega alla partita mantenendo il saldo e salva.
  - **Avvisi.** "Arrivano gli abitanti di Chieti…" all'inizio, "48.368 abitanti vivono ora a Chieti…" alla fine.
  - **Con il pack v4 vero** (in locale): 64 tile, 6.139 case, 950 posti di lavoro, 57 scuole. 48.368 cittadini e 22.560 famiglie generati in 0,7 secondi sul Mac.
  - **Dati e salvataggio.** I dati della popolazione si leggono da Resources (`GameContent.Life`). Il salvataggio e il caricamento portano i cittadini con sé.
  - **Pack vecchio.** Se il pack non ha i confini dei comuni (pack prima del v4), la partita resta con la città stimata e lo dice nel log.
  - **Test PlayMode nuovo** `PopulationPlayTests`, con il pack vero (ignorato in CI, dove il pack manca):
    - nuova partita;
    - arrivo dei cittadini;
    - un giorno con le loro tasse nel registro;
    - salvataggio e ricaricamento.
- **Giornata dei cittadini** (blocco 3c, 21/11, branch `m4-routine`). `CityRoutines`, un passo ogni 15 minuti di gioco:
  - **Bisogni.** Fame, energia, igiene, svago, socialità e comfort, da 0 a 100: calano e si recuperano secondo l'attività. L'umore è la loro media.
  - **Attività.** Si dorme di notte a casa (i nottambuli un'ora dopo, i mattinieri un'ora prima). Poi:
    - lavoro dalle 8 alle 17 nei giorni feriali; negozi e bar dalle 9 alle 19, sabato compreso; chi lavora fuori città esce dal comune;
    - scuola dalle 8 alle 13 (asilo fino alle 16), università dalle 9 alle 17;
    - pasti a casa col cibo del frigo; col frigo vuoto si mangia fuori, se ci sono soldi;
    - quando il frigo scende sotto il 30%, un adulto va a fare la spesa nell'alimentari vero più adatto: vicino, con scorte, del prezzo giusto per il reddito (discount, normale, caro);
    - svago in bar, ristoranti, palestre e cinema veri quando servono divertimento o compagnia;
    - altrimenti a casa.
  - **Spostamenti.** A piedi fino a 1,2 km, oltre in auto, con il tempo di viaggio. `CurrentPosition` dà il punto lungo il tragitto, che servirà per le persone visibili (blocco 6).
  - **Spesa.** È un pagamento vero dal cittadino al negozio:
    - le vendite entrano negli incassi e nelle tasse del giorno;
    - le scorte si rifanno alle 6;
    - la vita del giorno toglie la spesa alimentare da quella generica, così il denaro continua a conservarsi al centesimo.
  - **Recupero ad app chiusa.** Non usa la giornata a passi: basta la simulazione riassuntiva (docs/01 §4).
  - **Per ora tutti a livello L1.** Con 48.000 cittadini un passo costa circa 5 ms sul Mac, dentro il budget L1 di 100.000. La divisione L1/L2 per quartiere serve quando si aggiungono altre città (M10).
  - **Tre giorni sulla città di prova:**
    - di notte dormono tutti; alle 10:30 chi lavora in città è al lavoro e gli alunni sono a scuola;
    - alle 8 circa 10.000 persone per strada; alle 19 il 15% degli adulti è fuori;
    - fame media 64, umore 71; i 67 alimentari incassano in media 7.900 € al giorno.
  - **Salvataggio.** Popolazione alla versione 3 (attività, luogo, viaggio, frigo, scorte); le versioni precedenti si leggono ancora.
  - **Test.** 5 nuovi: giornata feriale, frigo, spesa e scorte, denaro conservato con la spesa, posizioni e salvataggio, velocità.
2. **Core: popolazione.**
   - Generatore deterministico (dal seed della partita):
     - le famiglie vanno nelle case in proporzione alla superficie dei piani;
     - ogni cittadino ha identità, età, istruzione, reddito, risparmi, tratti e gusti;
     - lavoro e scuola sono posti veri: negozi, uffici e scuole da OSM, più gli edifici del catalogo.
   - Archivio compatto per decine di migliaia di cittadini; nel salvataggio solo le differenze.
   - Test: i numeri ISTAT sono rispettati e lo stesso seed dà la stessa città.
3. **Core: vita dei cittadini.**
   - Bisogni, routine e scelte a utilità.
   - Frigo e spesa al supermercato vero.
   - Stipendi, affitti, risparmi, debiti.
   - Attività commerciali con un conto economico: incassi dai clienti simulati, costi, utile, fallimenti.
   - Ciclo di vita: nascite, invecchiamento, morte, traslochi, emigrazione.
   - Livelli: L1 nei quartieri vicini alla camera, L2 statistico nel resto, L3 per gli altri comuni.
   - L'economia del sindaco passa ai cittadini veri: tasse sui redditi e sulle attività.
   - Test: un anno di gioco senza valori assurdi, denaro conservato, popolazione entro i limiti.
4. **Core: strade e traffico L1.**
   - Grafo stradale dai tile (sensi unici, corsie, velocità).
   - Viaggi sul grafo con la congestione per tratto.
   - Bus reali da OSM e linee disegnabili (fermate, frequenze, costi, incassi).
   - Strade nuove disegnabili (spostate qui da M2).
5. **Servizi, felicità, reazioni, eventi, meteo.**
   - Copertura di polizia, pompieri, ospedali, scuole e rifiuti, sia negli edifici reali sia in quelli del catalogo.
   - Felicità per ogni cittadino.
   - Reazioni ai tre livelli: commenti, petizioni, proteste, emigrazione.
   - Eventi dal calendario ed eventi generati.
   - Meteo reale da Open-Meteo, con un clima medio per mese come ripiego.
6. **Unity: città viva (L0).**
   - Persone e veicoli visibili vicino alla camera, con instancing.
   - Bus sulle linee, semafori e precedenze semplici, mezzi d'emergenza.
7. **Interfaccia.**
   - Tocco su un cittadino: casa, lavoro, soldi, frigo, famiglia, "Segui".
   - Tocco su un edificio: residenti, lavoratori, conti dell'attività.
   - Mappe di calore: felicità, servizi, traffico, valore.
   - Disegno di linee bus e strade.
   - Notizie ed eventi.
8. **Prestazioni, prove, build, STATO, tag `v0.4.0`.** Obiettivo 60 fps (minimo 30) sull'iPhone 15 Pro e 30 fps sul web.

**Scelte semplici per M4** (le cambio se Samuele preferisce altro):
- **Chi si simula.**
  - Comune di Chieti: tutti gli abitanti uno per uno (48.368 al 1° gennaio 2026, stima ISTAT), dentro il budget L1 di 100.000.
  - Altri comuni della provincia: solo L3, cioè indicatori di popolazione ed economia.
- **Simulazione sul web.** Il web non ha thread per il C#, quindi la simulazione gira a pezzi sul thread principale, con un tempo massimo per frame. Su iOS e Android gira su thread e Jobs. Così si rispetta la regola "niente lavoro pesante sul main thread".
- **Persone e veicoli.** In M4 li faccio da codice come gli edifici, simili a quelli veri. Il generatore completo dei personaggi (viso, capelli, vestiti; MakeHuman in docs/08) arriva con gli interni, la prima persona e la creazione del personaggio (M5–M6).

**M3 — Account e cloud**: chiusa il 19/11/2026 su scelta di Samuele **senza la prova sui dispositivi** ("Chiudi M3 senza prova"). Tag `v0.3.0` con IPA e APK; tutte le build di `main` verdi. Restano da provare sui dispositivi Face ID, impronta, passkey e il giro iPhone → Mac (vedi "Da verificare sui dispositivi" e "Cosa provare (M3)"). Iniziata il 17/11/2026 sera (branch `m3-account`). Gli amici (visita delle città, progetti condivisi, mi piace) sono spostati in M9 su scelta di Samuele (17/11/2026: "la parte degli amici la farei dopo").

### Ritocchi chiesti da Samuele dopo M2 (18/11/2026, branch `ritocchi-bottoni-edifici`)
- **Bottoni del gioco** ("il + e il − delle aliquote non sono centrati e tutte quelle cose lì"):
  - **Difetti.** Il contenuto era spostato a sinistra perché lo spazio interno era asimmetrico, pensato per un'icona; il − era un trattino minuscolo; l'anello bianco si interrompeva agli angoli in basso, dove inizia il bordino scuro.
  - **Correzione.** Ora ogni bottone "caramella" ha un anello bianco uniforme e dentro una faccia colorata, con il bordino scuro e il contenuto sempre centrato. Vale per i bottoni d'azione dei pannelli, i tondi della costruzione, i "chiudi", la velocità dell'orologio e i badge del menu e dei Dettagli.
  - **Aliquote.** + e − sono icone disegnate in bottoni quadrati.
  - **Verifica.** Controllato con le fotografie dell'interfaccia (telefono e desktop).
- **Stadi veri** ("lo stadio è un ammasso di elementi"):
  - **Causa.** Le tribune dello Stadio Guido Angelini in OSM sono edifici generici dentro l'area dello stadio e venivano disegnate come palazzi con le finestre.
  - **Tribune.** Gli edifici di tipo tribuna/stadio, quelli del landmark dello stadio e quelli generici lunghi almeno 20 m dentro un'area stadio diventano gradinate. Salgono dal campo con la pendenza di una tribuna vera (circa 35°), hanno le file di sedili colorati disegnate dallo shader (nuovo tipo di superficie 18) e muri in cemento senza finestre. Gli edifici messi "di punta" verso il campo restano edifici.
  - **Pista e campo.** La pista d'atletica è un vero anello con rettilinei e curve, in tartan con le corsie (tipo 19), con il prato all'interno. Il campo con le linee è quello principale (il prato più grande), non il campetto vicino.
  - **Torri faro.** La sera si accendono.
- **Stadio del catalogo** (rifatto):
  - campo regolamentare con le porte e quattro tribune a gradinata con i colori della squadra, scale e muri;
  - copertura a sbalzo sulla tribuna principale (distinti dal livello 2, curve al livello 3);
  - torri faro accese la sera, cartelloni a bordo campo, panchine e tabellone;
  - altezza a 34 m (le torri faro).
- **Centrale nucleare** (rifatta come quelle vere):
  - due torri di raffreddamento a iperboloide, vuote dentro, con i pilastri alla base;
  - edificio del reattore con la cupola, sala turbine e edificio ausiliario;
  - camino a bande bianche e rosse, stazione elettrica tra le torri, piazzale, strada e recinzione.
- **Tutti gli edifici del catalogo come quelli veri.** Richiesta di Samuele: "tutti gli edifici esistenti e da poter aggiungere devono essere reali"; la regola è ora in `docs/08` §2. Tutti i 150 elementi del catalogo hanno un modello del loro tipo; il generatore è diviso in `ObjectMesher.Town/Public/Industry/Leisure.cs`.
  - **Case.** Lotto con giardino, vialetto, muretto e cancello. Gronde, comignoli, fioriere e balconi sono gli stessi addobbi degli edifici di Chieti, con il fronte sul lato v = 0. Ci sono poi:
    - case a schiera e bifamiliari a più colori;
    - villa moderna con la piscina, casa con il portico, casale con la scala esterna e la loggia;
    - palazzo nobiliare a quattro ali con il cortile, il portone e il balcone;
    - villa liberty con la torretta;
    - loft in mattoni con la ciminiera;
    - condomini con i balconi continui e le tende da sole;
    - grattacielo di vetro con podio, rastremazione e antenna.
  - **Negozi.** Sono al piano terra di una palazzina a due piani con le case sopra (altezze del catalogo aggiornate). Hanno vetrine, tende, insegna e dehors con tavolini e ombrelloni; la farmacia ha la croce verde. Poi:
    - supermercato e centro commerciale con i parcheggi pieni di auto;
    - cinema col foyer e le locandine accese;
    - distributore con pensilina, pompe e totem;
    - albergo, uffici e torre di vetro.
  - **Servizi pubblici.**
    - Scuole con pensilina, bandiere e cortile (giochi all'infanzia, basket al liceo) e università a chiostro.
    - Ospedale con eliporto, pronto soccorso e ambulanze.
    - Caserma dei carabinieri con le gazzelle, vigili del fuoco con portoni rossi, torre e autopompe.
    - Poste con l'insegna gialla, biblioteca con il portico.
  - **Cultura e spettacolo.**
    - Museo con il pronao, teatro con la torre scenica e la loggia.
    - Chiesa con facciata a timpano, rosone, portale, abside e campanile; cappella con il campanile a vela.
    - Anfiteatro a gradoni, luna park con ruota panoramica, giostra, torre e autoscontri, discoteca.
  - **Industria.**
    - Capannoni con baie di carico e camion.
    - Stabilimenti col tetto a shed; allo stabilimento automobilistico le auto nuove in fila.
    - Cantina con le vasche inox, frantoio, cementificio con torre dei cicloni, forno rotante e silos.
  - **Campagna.**
    - Campi veri: grano con le carreggiate, mais, girasoli, lavanda, orto con capanno.
    - Pascolo con le pecore, stalla con le vacche, fienile aperto con le balle.
    - Serre a tunnel, silos, apiario.
  - **Energia.** Traliccio a traliccio con isolatori, sottostazione con trasformatori e portali, diga idroelettrica con bacino e condotte, centrali a gas, a biomasse e geotermica.
  - **Sport.**
    - Linee regolamentari di tennis, basket e calcetto disegnate dallo shader (parametro sport del tipo 8).
    - Reti trasparenti col telo basso, porte con le reti, torri faro.
    - Piscina con corsie, blocchi e spogliatoi; palazzetto ellittico con la cupola; pista d'atletica vera con il campo dentro.
  - **Trasporti.**
    - Parcheggi con le auto e multipiano a piani aperti con la "P".
    - Pensilina del bus, autostazione con gli autobus.
    - Piazza con rosone, lampioni e alberi; eliporto con la "H" e la manica a vento.
  - **Arredo e natura.**
    - Statue con figure umane, monumento ai caduti con la Vittoria alata, fontane a bacile.
    - Chiosco col bancone, parco giochi, rastrelliera con le bici.
    - Siepi e aiuole con fiori tondi, laghetto con i sassi.
  - **Corretto per strada.**
    - Alcuni parallelepipedi (file dei campi, siepi, aiuole) usavano per errore il muro con le finestre.
    - Il traliccio era fatto di pezzi staccati.
    - Il chiosco era un cubo verde.
  - **Prove e controlli.**
    - Il test "ogni elemento sta dentro la sua sagoma" ora elenca tutti gli elementi che sbagliano, non solo il primo.
    - Nuova fotografia `WorldSnapshotTests.Catalogo`: un foglio per categoria, con le piante, anche di sera con `ORBIS_MODELS_HOUR=21`.
- **Aeroporto e "molte altre cose"** (domanda di Samuele): come da specifica, l'aeroporto arriva con aeroporti e voli in M10 e il catalogo esteso in M9. Si può anticipare un aeroporto "statico" se Samuele lo vuole.
- **Prove.** 145 test .NET (8 nuovi: tribune dritte, ad anello, strette, di punta, pista). Nuova foto di controllo `WorldSnapshotTests.Modelli` (modelli del catalogo di giorno e di sera, stadio di Chieti). EditMode 10/10.

### Piano di M3 (a blocchi)
1. **Backend: account** — registrazione, accesso, token di accesso JWT da 1 ora, refresh token da 90 giorni ruotati a ogni rinnovo (riuso → sessione del dispositivo chiusa), uscita, elenco e chiusura delle sessioni, profilo, limite ai tentativi. ✅ fatto (vedi sotto).
2. **Backend: salvataggi nel cloud** — elenco, upload a pezzi con `baseRevision` e conflitto 409, download, eliminazione. ✅ fatto (vedi sotto).
3. **Backend: passkey per il web** (WebAuthn ES256/RS256 con openssl); ripiego: sessione lunga col refresh token nel browser. Si fa dopo i blocchi 4–6: per il criterio di M3 sul web basta il ripiego.
4. **Core (logica pura, test .NET)**: sessione dell'account (scadenze, un solo rinnovo alla volta), piano di sincronizzazione (carica, scarica, conflitto), metadati dei salvataggi (uuid, revisione, dispositivo).
5. **Unity: rete e piattaforme** — client delle API; refresh token protetto da Face ID nel Keychain (iOS, Swift), dall'impronta/volto con il Keystore (Android, Kotlin), passkey o IndexedDB sul web.
6. **Interfaccia** — crea account / entra, proposta di Face ID, dispositivi collegati, esci; stato cloud nell'elenco delle partite; scelta in caso di conflitto (Tieni questo dispositivo / Tieni il cloud / Tienili entrambi); sincronizzazione alla pausa e ogni 15 minuti.
7. **Prove, build, STATO, tag `v0.3.0`**. Criterio: Samuele crea l'account, entra con Face ID, gioca sull'iPhone e continua la stessa partita dal browser del Mac.

### Avanzamento M3
- **Backend: account** (blocco 1):
  - migrazione `002_auth.sql` (`orb_users`, `orb_refresh_tokens`, `orb_login_attempts`);
  - API `POST /auth/register`, `/auth/login`, `/auth/refresh`, `/auth/logout`, `GET /auth/sessions`, `DELETE /auth/sessions/{id}`, `GET`/`PATCH /me`;
  - password solo come hash bcrypt, refresh token e IP solo come hash, stesso errore per utente inesistente e password sbagliata (con lo stesso tempo di risposta);
  - limiti: 10 accessi falliti per IP e nome utente ogni 15 minuti (30 per IP), 5 account nuovi per IP all'ora;
  - test: 65 (44 nuovi), anche contro un MariaDB locale.
- **Backend: salvataggi nel cloud** (blocco 2):
  - migrazione `003_saves.sql` (`orb_saves`, `orb_save_uploads`, `orb_save_chunks`); i file stanno in `storage/saves/<utente>/` (cartella protetta), nel database solo i metadati;
  - API `GET /saves`, `GET`/`DELETE /saves/{uuid}`, `GET /saves/{uuid}/download` (anche a pezzi con `Range`), `POST .../upload/start` → `PUT .../chunk/{n}` (512 KB) → `POST .../finish`;
  - **conflitti**: l'upload dichiara la revisione da cui parte; se nel cloud ce n'è un'altra la risposta è 409 con i dati della versione cloud (dispositivo, data, livello…), sia all'inizio sia alla chiusura (due dispositivi che chiudono insieme: vince il primo);
  - pezzo rimandato dopo una caduta di rete: sostituisce il precedente; checksum SHA-256 controllato alla fine (422 se il file è arrivato rovinato, il cloud resta com'era);
  - limiti: 64 MB per partita, 30 partite per utente; resta solo l'ultima revisione;
  - verificato che Altervista lasci arrivare PUT, PATCH e DELETE al PHP;
  - test: 99 in tutto (34 nuovi).
- **In `main` e online (18/11, PR #12)**: test verdi anche su PHP 7.0 e MySQL 5.7; deploy su Altervista riuscito. Verificato dal vivo senza creare account: `apiVersion` 0.3.0 e migrazioni applicate fino a `003_saves.sql`; l'intestazione `Authorization` arriva al PHP (un token finto dà `invalid_token`, non `unauthorized`); il preflight CORS di un PUT dal web risponde 204 con gli header esposti.
- **Release `v0.2.0`**: IPA e APK allegati, build web pubblicata.
- **Core: account e sincronizzazione** (blocco 4, 18/11, branch `m3-client`), in `Orbis.Core.Online`, logica pura:
  - **Rete.** `IHttpTransport`: lo implementa la piattaforma (UnityWebRequest), nei test un backend finto con le regole di quello PHP. `ApiClient` copre tutte le API dell'account e dei salvataggi:
    - upload a pezzi con ogni pezzo riprovato tre volte se cade la rete;
    - download con il controllo del checksum;
    - errori come `ApiException`, che distingue rete assente, sessione chiusa e conflitto 409 con la versione cloud.
  - **Sessione dell'account.** `AccountSession`:
    - token di accesso in memoria, rinnovato un minuto prima della scadenza o dopo un 401;
    - un solo rinnovo alla volta: le chiamate concorrenti aspettano lo stesso;
    - refresh token in un `IRefreshTokenStore` (Keychain con Face ID, Keystore, IndexedDB nel blocco 5);
    - profilo in cache per entrare anche offline;
    - rinnovo rifiutato (scaduto, revocato, riusato) → sessione chiusa con il motivo;
    - identità del dispositivo creata al primo avvio (`device/id`).
  - **Sincronizzazione.** `SyncPlanner` (pura decisione) e `CloudSync` (esecuzione sull'archivio locale). Il legame col cloud sta in `saves/<id>/cloud` (uuid, revisione, checksum all'ultima sincronizzazione). Le regole:
    - giocata solo qui → carica;
    - solo altrove → scarica;
    - in entrambi → conflitto con le tre scelte (Tieni questo dispositivo, Tieni il cloud, Tienili entrambi: la copia locale diventa una partita nuova col nome del dispositivo);
    - solo nel cloud → scaricata come partita nuova;
    - la partita aperta non si sovrascrive mentre si gioca, aspetta il menu.
  - **Test.** 23 nuovi (168 in tutto), anche lo scenario di M3 "inizio sull'iPhone, continuo sul Mac".
- **Unity: rete, archivio protetto e schermate** (blocchi 5 e 6, 18/11, branch `m3-client`):
  - **Rete.** `Orbis.Net.UnityHttpTransport` (UnityWebRequest, senza thread: va anche sul web). `OnlineHub` crea client, sessione e sincronizzazione del dispositivo e sincronizza:
    - all'avvio;
    - al ritorno nel menu dopo una partita;
    - alla pausa dell'app (dopo il salvataggio);
    - ogni 15 minuti.
  - **Indirizzo delle API.** Quello predefinito è `https://giuliaa8.altervista.org/orbis`; "orbis.apiBase" nei PlayerPrefs lo sostituisce per le prove.
  - **Archivio protetto.**
    - **iOS:** Keychain col plugin Swift `Assets/Plugins/iOS/OrbisSecurity.swift`. Gli elementi normali sono accessibili dopo il primo sblocco; quelli protetti usano `.biometryCurrentSet` e leggerli mostra Face ID. Il post-process aggiunge LocalAuthentication e `SWIFT_VERSION`; `NSFaceIDUsageDescription` c'era già.
    - **Android:** Keystore col plugin Kotlin `Assets/Plugins/Android/OrbisSecurity.kt`, solo API di sistema (niente AndroidX). AES-GCM per i valori normali. Per il token protetto una coppia RSA: cifrare con la chiave pubblica non chiede nulla, così la rotazione ogni ora resta silenziosa; decifrare chiede l'impronta o il volto con BiometricPrompt. La chiave viene invalidata se si registrano nuove impronte.
    - **Web ed editor:** file nella cartella dati, che sul web sta nella memoria del sito (IndexedDB). È il ripiego della specifica finché non arrivano le passkey (blocco 3).
    - **Sblocco biometrico.** `PlatformTokenStore` mette il refresh token nell'elemento protetto se lo sblocco biometrico è attivo, altrimenti in quello normale.
  - **Schermate.**
    - **Barra del menu.** La voce "Account" (icona del profilo, col nome del giocatore quando è dentro) prende il posto di "Amici", spostati in M9.
    - **Primo avvio.** Senza account si apre la schermata di accesso: Entra / Crea account / Gioca offline, come in docs/04 §1. Chi sceglie offline non la rivede all'avvio.
    - **Avvii successivi.** Rientro automatico col token, con Face ID se attivo. Dopo il primo accesso viene proposto Face ID o l'impronta.
    - **Profilo.** Nome, stato del cloud con "Sincronizza adesso", interruttore Face ID / impronta, dispositivi collegati con "Scollega", Esci.
    - **Conflitti.** Una scheda per ogni conflitto, con le due versioni (livello, cassa, data) e le tre scelte. Compare solo nel menu: durante la partita aspetta.
    - **Elenco "Continua".** Mostra "Nel cloud" o "Solo su questo dispositivo".
    - **Testi.** 75 testi nuovi, in `data/localization/ui.json`.
  - **Eliminazioni.** Una partita eliminata nell'elenco viene tolta anche dal cloud.
  - **Test.** I test PlayMode ora cliccano "Crediti" invece di "Amici"; c'è un test nuovo, il clic su Account apre l'accesso. Nei test (batchmode) la schermata di accesso non si apre da sola sopra il menu.
- **Passkey per il web** (blocco 3, 18/11, branch `m3-passkey`):
  - **Backend (API 0.4.0).** Migrazione `004_passkeys.sql` (`orb_passkeys`, `orb_passkey_challenges`) e `PasskeyController`. Sei rotte:
    - opzioni e verifica per creare la passkey (serve l'accesso);
    - opzioni e verifica per entrare (senza nome utente);
    - elenco ed eliminazione.

    `Cbor` e `WebAuthn` in PHP 7.0 puro con openssl (presente su Altervista). Gli autenticatori che usano il contatore devono farlo crescere, altrimenti la passkey copiata viene rifiutata.
  - **Test del backend.** 27 nuovi (126 in tutto) con un autenticatore finto e firme vere ES256 e RS256. Coprono:
    - creazione e accesso;
    - origine di un altro sito, sfida riusata, verifica dell'utente mancante;
    - firma alterata, contatore copiato, passkey sconosciuta;
    - eliminazione e passkey degli altri.
  - **Web.** `OrbisWeb.jslib` fa partire `navigator.credentials.create/get` dentro il gesto del giocatore, come vuole Safari. La richiesta si prepara quando il dito o il mouse preme il pulsante e parte al rilascio; se il rilascio è già passato, parte subito. `WebPasskeys` è il lato C#.
  - **Schermate (solo web).**
    - "Entra con una passkey" sopra i campi dell'accesso;
    - proposta di creare una passkey dopo il primo accesso con la password;
    - sezione "Passkey" nel profilo con l'elenco, Elimina e "Crea una passkey".
  - **Testi.** 21 nuovi. Core: un test in più (169).
- **Revisione dei plugin Face ID e impronta** (19/11, branch `m3-riprova-biometria`), fatta prima della prova di Samuele:
  - **Swift e Kotlin.** Nessun difetto trovato:
    - il token (43 caratteri) sta largo nel limite di RSA-OAEP (190 byte);
    - le risposte tornano sul thread di Unity, perché nessun `await` usa `ConfigureAwait(false)`;
    - una chiave invalidata da nuove impronte viene cancellata e serve la password.
  - **Difetto corretto.** Se all'avvio si annullava Face ID o l'impronta, si finiva sulla password senza poter riprovare, anche se il token protetto c'era ancora. Ora il modulo di accesso mostra "Entra con Face ID" (o "con l'impronta o il volto") sopra i campi:
    - il pulsante compare solo se c'è un token protetto: lo dice un segno nell'archivio normale, letto senza mostrare la richiesta;
    - "Esci" e una sessione chiusa dal server tolgono il segno, quindi in quei casi il pulsante non compare;
    - se lo sblocco non riesce resta l'avviso e la password.

    Due test PlayMode nuovi.
- **Da verificare sui dispositivi** (non posso farlo da qui; ancora da fare, M3 è stata chiusa senza la prova):
  - Face ID e il Keychain sull'iPhone;
  - impronta e Keystore su Android;
  - il giro completo con l'account vero: crea l'account sull'iPhone, gioca, continua dal browser del Mac;
  - la passkey su Safari (Mac e iPhone) e su Chrome.

  Le build di GitHub compilano Swift e Kotlin.
- **Scelte semplici annotate**:
  - nessun account diventa amministratore da solo (il pannello admin arriva in M12): impostare `is_admin` alla registrazione in base a `ORBIS_ADMIN_USERNAME` permetterebbe a chiunque registri per primo quel nome di diventare amministratore;
  - una sola sessione per dispositivo: un nuovo accesso dallo stesso dispositivo chiude la precedente;
  - due rinnovi contemporanei con lo stesso token contano come riuso, quindi l'app ne fa uno alla volta.
  - una partita eliminata su un dispositivo viene tolta anche dal cloud, alla prossima sincronizzazione se si è offline. Gli altri dispositivi la tengono solo in locale, senza legame, invece di cancellarla.
  - il legame col cloud si scrive prima dell'upload: un upload interrotto riparte con lo stesso uuid e non crea doppioni.
  - **Rischio noto.** Se la risposta di un rinnovo va persa dopo che il server ha già ruotato il token, il rinnovo successivo conta come riuso e serve un nuovo accesso con la password. È raro e dipende dalla rotazione obbligatoria.

**M2 — Costruire ed economia**: completata il 17/11/2026, a conferma di Samuele dopo la prova sui dispositivi ("sì va bene", con una sola correzione: la scheda della missione toccava la barra del livello sul telefono). Tag `v0.2.0`. Iniziata il 16/11/2026 (scelta di Samuele: "M2 adesso").

### Piano di M2 (a blocchi, ognuno con PR e test)
1. **Core del gioco** (logica pura, `Orbis.Core.Game`):
   - lettore JSON;
   - catalogo con sagoma esatta, livelli e varianti;
   - stato della partita e comandi con annulla/ripeti illimitato;
   - piazzamento sulla griglia dei lotti (docs/04 §4), anche la demolizione degli edifici OSM che libera la loro sagoma;
   - orologio e scheduler;
   - economia del proprietario (affitti, tasse, suolo pubblico, manutenzione, cassa, registro dei movimenti);
   - XP, livelli e missioni;
   - salvataggio binario versionato, istantanee giornaliere, riavvolgimento, tempo ad app chiusa con riepilogo.
   - Test: un anno di gioco senza valori assurdi e conservazione del denaro (docs/03 §12).
2. **Dati**:
   - prime 150 voci del catalogo in `data/catalog/` (residenziale, commerciale, uffici e industria, energia, agricoltura, servizi, sport, cultura, trasporti, natura, decorazioni);
   - bilanciamento in `data/balance/` e 20 missioni in `data/scenarios/`.
3. **Unity**: modelli procedurali per famiglie di oggetti (case e palazzi, negozi, capannoni, pale eoliche, campi solari, centrali, campi e vigneti, alberi, arredo urbano), oggetti piazzati nel mondo, anteprima "fantasma" verde/rossa che si aggancia alla griglia.
4. **Interfaccia**:
   - nell'HUD: cassa, livello, annulla/ripeti, costruisci, finanze, missioni;
   - catalogo a schede con ricerca;
   - azioni sugli oggetti (sposta, ruota, demolisci, potenzia, varianti);
   - schermata finanze con le aliquote;
   - riavvolgi;
   - nuova partita con la configurazione del mondo e più salvataggi; "Continua".
5. **Salvataggi sul dispositivo** (app e web), salvataggio automatico, riepilogo al ritorno.
6. **Prove, build, STATO, tag `v0.2.0`**.

### Avanzamento M2 (17/11/2026)
Tutto il piano è scritto, sul branch `m2-core` (PR in arrivo); manca la prova di Samuele sui dispositivi.
- **Core** (blocco 1): fatto, 111 test `dotnet test` (un anno di gioco con conservazione del denaro, annulla/ripeti che non regala monete, salvataggio → caricamento identico, tempo ad app chiusa).
- **Dati** (blocco 2): 150 voci del catalogo con sagoma esatta e 5 livelli, bilanciamento, 20 missioni; test sui file veri.
- **Unity** (blocco 3): modelli procedurali per tutte le famiglie (test: ogni modello resta dentro la sua sagoma e la sua altezza); anteprima "fantasma" verde/rossa agganciata alla griglia del lotto, col motivo se non si può; cantiere che cresce dal suolo; demolizione degli edifici OSM che libera il lotto.
- **Interfaccia** (blocco 4):
  - HUD: cassa, livello con barra XP, missione corrente (tocco per i dettagli), annulla/ripeti, Costruisci, Finanze, Riavvolgi; avvisi per lavori finiti, missioni e livelli;
  - catalogo a schede per categoria con ricerca per nome; elementi bloccati con il livello di sblocco;
  - barra di costruzione: nome e costo, motivo in rosso, ruota, ✓ (touch), annulla; su desktop clic per piazzare, R ruota, Esc annulla;
  - Dettagli di un elemento piazzato: stato dei lavori, abitanti/posti, affitto/produzione, energia, manutenzione, sagoma; azioni Potenzia, Colori, Sposta, Ruota, Demolisci. Edifici reali: Demolisci (con costo al m²);
  - Finanze: abitanti, posti, felicità, energia, saldo previsto, aliquote con −/+ (annullabili), ultimo giorno chiuso e ultimi 30 giorni per voce;
  - Riavvolgi: punti di ripristino giornalieri (30) e settimanali, con conferma;
  - Nuova partita: nome, difficoltà, tempo ad app chiusa, modalità creativa; Continua: elenco delle partite (gioca, elimina); riepilogo "Mentre eri via".
- **Salvataggi** (blocco 5): file binari versionati (`ISaveStore`): ogni 60 s, a fine giornata di gioco, quando l'app va in pausa o si chiude e tornando al menu; sul web in IndexedDB (sincronizzato dopo ogni scrittura).
- **Prove** (blocco 6): EditMode 10 test (anche i testi composti dagli enum), PlayMode 9 test, tra cui una partita vera su Chieti: costruire su un lotto reale, annullare/ripetere, demolire un edificio OSM, un mese di gioco, riavvolgere, uscire e riaprire trovando tutto com'era.
- **Fotografie dell'interfaccia** (`UiSnapshotTests`, categoria Snapshot, non in CI): menu, nuova partita, HUD, missione, Dettagli, catalogo, finanze, riavvolgi e costruzione, su iPhone 15 Pro in verticale (con Dynamic Island) e desktop. Controllate a occhio: corretti la barra in basso troppo larga sul telefono, la scritta © OpenStreetMap sotto i bottoni, le barre di scorrimento di sistema, i nomi lunghi nella barra di costruzione, le aliquote disallineate, le colonne del catalogo su desktop.
- **Scelte semplici annotate**: "Continua" apre l'elenco delle partite (anche con una sola); la nuova partita parte alla data reale di oggi alle 10; un'ora di gioco dura un minuto reale (×velocità dell'orologio); i comandi che completano una missione la annullano insieme a loro.

### Edifici reali in stile gioco (17/11/2026, richiesta di Samuele durante la prova di M2)
Branch `m2-stile-gioco`.
- **Addobbi per tipo di edificio** (`BuildingDecorator`, logica pura con test). Tipo dall'uso OSM e dal POI principale dell'edificio:
  - bar, caffè e ristoranti: tende a righe sulle vetrine, insegna che la sera si accende, dehors con tavolini (con ombrelloni dal livello 2) se il marciapiede è largo almeno 2,3 m;
  - negozi: tende e insegne; farmacie: croce verde luminosa; banche: insegna;
  - alberghi: pensilina, insegna verticale, bandiere; scuole, municipi, ospedali: bandiere italiana ed europea, insegne, croce rossa;
  - benzinai: pensilina con fascia colorata e pompe; tettoie: pilastri e copertura invece di un blocco pieno;
  - industrie: portoni di carico, fascia con insegna, pannelli solari sui tetti grandi; fattorie: balle di fieno;
  - case: fioriere sotto le finestre, balconi con ringhiera, comignoli sui tetti a falde, parapetto e impianti sulle terrazze.
- **Livelli 1→5**: ogni livello aggiunge addobbi (più fioriere e balconi fioriti, piante e pergolati in terrazza, fioriere ai lati dell'ingresso, lucine, festoni di bandierine, lampioncini, più tavolini).
- **Potenzia anche gli edifici reali**: Dettagli → "Potenzia" (fino al livello 5). Costo = superficie dei piani × 25 × livello attuale; affitto in più 1,25 al mese per m² e per livello (rientro in circa 20 mesi di gioco); un po' di felicità. Si annulla come le altre azioni; il salvataggio passa alla versione 2 e legge ancora i salvataggi vecchi.
- **Più colori**: intonaci più vivi (ocra, salmone, rosa antico, giallo paglia, terracotta, verde salvia, azzurro polvere) e terrazze in cotto con le piastrelle disegnate invece del grigio.
- **In collina** tende e insegne seguono la quota del marciapiede davanti alla facciata; se l'edificio è incassato nel pendio, il locale ha un'insegna a totem sul marciapiede.
- **Prestazioni**: addobbi solo nei tile vicini, in mesh separate che si spengono oltre 300–800 m secondo il profilo (telefono via web 300 m e densità 0,4; desktop 800 m). Nel centro di Chieti pesano quanto gli edifici (112 mila triangoli contro 111 mila nei 5 tile vicini).
- **Scelte semplici annotate**: il potenziamento degli edifici reali è immediato (niente cantiere); gli addobbi sono geometria procedurale semplice (stile Classico); i modelli "HD" più dettagliati (cornicioni, persiane in rilievo, insegne con il nome vero del locale) sono il passo successivo.

### Grafica HD di edifici e strade (17/11/2026, mentre Samuele prova M2)
Branch `m2-grafica-hd`, scelta di Samuele: "Grafica HD edifici".
- **Insegne con il nome vero**: bar, negozi, farmacie, banche, alberghi, scuole e uffici pubblici hanno sull'insegna il nome preso da OSM. Il testo è scritto in 3D col font del gioco (Lilita One), solo sulle 48 insegne più vicine (entro 140 m); da lontano resta il pannello colorato. Testo chiaro su fondo scuro e viceversa; la sera l'insegna è accesa.
- **Rilievi**: sui tetti a falde una gronda in coppi che sporge di mezzo metro, col sottogronda, ma solo lungo i lati di gronda (non sui timpani); sui tetti piani un cornicione sotto il parapetto.
- **Strade**:
  - strisce pedonali vicino agli incroci veri (almeno tre strade), disegnate dallo shader con un po' di usura;
  - lampioni ogni 32 m a lati alterni sui marciapiedi delle vie urbane e ai bordi delle vie pedonali con nome, accesi la sera.
- **Prestazioni**: nei 5 tile vicini al centro gli addobbi sono 193 mila triangoli, contro 111 mila degli edifici; si spengono oltre 550–800 m. Sui profili leggeri (telefono via web, telefoni con poca memoria) niente gronde, cornicioni e lampioni.
- **Strade non più sgranate** (segnalazione di Samuele):
  - **Cause.** Su iPhone, Android e web il gioco renderizzava all'80% e ingrandiva l'immagine, quindi tutto era morbido. Inoltre la grana dell'asfalto era troppo fitta e da lontano sfarfallava, e le vie residenziali non avevano segnaletica: sembravano terra.
  - **Risoluzione.** Ora è piena sui dispositivi capaci (85% solo su telefono via web e sui telefoni con poca memoria).
  - **Asfalto.** Più scuro, con grana fine solo da vicino, rattoppi, tracce delle ruote, cunette, mezzeria tratteggiata anche sulle vie residenziali larghe e cordolo chiaro del marciapiede.
  - **Terreno urbano.** Cortili lastricati e giardini a chiazze invece di una distesa beige.
- **Anteprime nel catalogo** (richiesta di Samuele): sotto il nome di ogni elemento c'è il suo modello 3D in tre quarti su un prato, fotografato fuori scena sempre con la luce del giorno.
  - Le anteprime arrivano poche alla volta (al massimo ~3 ms per frame) e restano in memoria per la partita.
- **Prossimi passi possibili**: persiane e balconi più dettagliati da vicino, casali e fienili in campagna, alberi lungo i viali.

### Correzioni dalla prova di Samuele (17/11/2026)
Branch `m2-costruzione`.
- **Edifici che entravano nel terreno**:
  - **Due cause.** Sui pendii i modelli del catalogo si appoggiavano alla quota media o a quella più bassa della sagoma, e il lato a monte finiva sotto terra. Inoltre molti pezzi (silos, ciminiere, colonne dei portici, tende dei negozi, fioriere, cestini, siepi, aiuole, eliporto, pannelli sui tetti) avevano quote come se il terreno fosse a 0 m, mentre Chieti è a circa 300 m.
  - **Correzione.** Ogni volume sta in piano alla quota più alta del suo pezzo di sagoma, con un basamento in pietra verso valle. Campi, filari, alveari, pannelli a terra e recinzioni seguono il terreno.
  - **Test nuovi.** Nessun vertice sotto terra; su un pendio del 20% il piano terra non è mai sotto il terreno. Verificato anche con le foto.
- **Terreno "sgranato"** (segnalazione di Samuele):
  - **Cause.** La mappa delle colture era di 512 pixel per tile (3,5 m per pixel, bordi a gradini), con un rumore di colore diverso per ogni pixel e senza dettagli veri.
  - **Mappa più fine.** Ora è di 1024 pixel nei tile vicini, circa 1,8 m per pixel (512 su telefono via web e sui telefoni con poca memoria), con un tono stabile per area.
  - **Mappa dei materiali.** Una seconda mappa, letta senza filtro, dice per ogni area il tipo di suolo (prato, campo, vigneto, uliveto/frutteto, bosco, città, sabbia/roccia), la coltura del campo (grano, verde, arato, file larghe) e direzione e fase delle file.
  - **Nuovo shader del terreno.** Disegna in metri:
    - solchi dell'arato, file del grano con i passaggi del trattore, file di girasole e mais;
    - nei vigneti terra sotto i filari ed erba in mezzo, allineate esattamente alle viti (test per ogni orientamento);
    - prato a ciuffi e chiazze secche, sottobosco, città a macchie.
  - **Bordi e lontananza.** I bordi tra le aree ondeggiano di un paio di metri: niente più gradini. Da lontano i dettagli sfumano, senza moiré.
  - **Colori.** I campi hanno il colore della loro coltura: grano dorato, verde, arato bruno.
  - **Memoria.** Circa 9 MB per tile vicino (prima 1,4 MB).
- **Telecamera in modalità costruzione**:
  - entrando (costruisci o sposta) vola verso il punto, con una distanza adatta alle misure dell'elemento (50–280 m) e un'inclinazione da tre quarti (45–58°); non si allontana mai;
  - spostando un elemento si centra su di esso;
  - uscendo torna alla distanza e all'inclinazione di prima.
- **Scheda della missione attaccata alla barra del livello sul telefono** (ultima segnalazione prima del tag):
  - **Causa.** La scheda aveva una posizione fissa (150 punti dall'alto), esattamente dove finisce la barra del livello.
  - **Correzione.** Ora sta nella stessa colonna di cassa e livello, con 10 punti di distacco su telefono, tablet e desktop.
  - **Verifica.** Controllata con le fotografie dell'interfaccia.

**M1 — Il mondo: Chieti**: completata il 16/11/2026, a conferma di Samuele ("quasi perfetto" + correzioni unite con la PR #5). Tag `v0.1.0` dopo le build verdi.

### Piano di M1 (a blocchi, ognuno con PR e test)
1. **Formato dei tile** — schema FlatBuffers `tools/world-pipeline/schema/orbis_tile.fbs`, codice generato per Python e C# (in `Orbis.Core`, testabile con `dotnet test`), compressione Deflate.
2. **Pipeline** `tools/world-pipeline` (Python): estratto Geofabrik `italy/sud` → confine della provincia di Chieti (OSM) → tile z14 con edifici (sagoma reale, altezza, tetto, colori, nome, indirizzo, ID OSM), strade, aree (verde, acqua, **campi, vigneti, uliveti, frutteti**, boschi, urbano), POI con nomi, alberi, **lotti** (isolati tra le strade) e terreno (Terrain Tiles Terrarium) → file per tile + indice. Test con dati finti.
3. **Workflow `world-data.yml`** (manuale): esegue la pipeline in CI, pubblica il pack come Release `world-chieti-v<n>`; le build lo scaricano in `StreamingAssets/world` (sul web i tile arrivano uno alla volta da GitHub Pages, stessa origine, niente problemi CORS).
4. **Unity — mondo**: caricamento dei tile attorno alla camera, terreno con le colture disegnate, strade, edifici procedurali stile *Abruzzo* (facciate con finestre, tetti in coppi), alberi, acqua.
5. **Camera** completa (3D/2D, gesti touch, mouse, tastiera), verticale e orizzontale.
6. **Selezione** di un edificio → pannello Dettagli in vetro con i dati OSM.
7. **Lotti e griglia di occupazione** (`docs/04` §4) dalle sagome reali, con vista di controllo.
8. **Giorno/notte, cielo, meteo simulato**, **3 landmark** (Cattedrale di San Giustino, Teatro Marrucino, stadio Guido Angelini), **globo** base e zoom continuo.
9. **Prestazioni** (≥ 30 fps), prova su iPhone e web, tag `v0.1.0`.

### Avanzamento M1 (16/11/2026)
- **In `main` (PR #3)**: formato dei tile (FlatBuffers + Deflate, decompressore C# puro), lettore C#, pipeline Python con test, workflow *World data*.
- **Pack di Chieti** (Release `world-chieti-v*`, costruito in CI in ~6 minuti). Numeri del v1:
  - 1.863 tile (70 MB): 955 completi più 908 di contorno;
  - dentro la provincia: 77 mila edifici, 94 mila strade, 227 mila aree, 16 mila POI, 12 mila lotti, 372 mila alberi.
  - Il v3 aggiunge l'orizzonte (terreno a 50 km), il globo, il Teatro Marrucino trovato dal POI e il fondale sotto il mare.
- **Sul ramo `m1-mondo`** (PR in preparazione):
  - mondo 3D a tile con 3 livelli di dettaglio, su un thread di lavoro sulle app e a passi da pochi ms sul web;
  - edifici sulla sagoma reale: i tetti sono calcolati come "minimo di piani", quindi mai fuori dall'impronta; facciate con finestre, persiane, portoni, vetrine e chiese con monofore; coppi; luci di notte;
  - strade con marciapiedi, strisce, ponti, ferrovie e sampietrini;
  - colture disegnate sul terreno (mosaico dei campi), uliveti e frutteti a sesto, vigneti a filari, boschi, parchi;
  - mare e orizzonte fino a 50 km (Majella, Gran Sasso, costa);
  - cielo a gradiente con sole reale per Chieti (posizione per data e ora), stelle, nuvole, pioggia, nebbia, meteo simulato;
  - telecamera completa (touch, mouse, tastiera), 3D e 2D, inerzia, doppio tocco per centrare;
  - globo con passaggio continuo: oltre i 45 km si sale sul globo, sotto i 55 km si torna a terra;
  - HUD con orologio, meteo, velocità (pausa, 1×, 2×, 4×, 10×), 2D/3D, nord e lotti;
  - nomi delle strade sulla mappa; pannello Dettagli con i dati OSM (nome, indirizzo, tipo, altezza, piani, tetto, superficie, negozi dentro, posizione, ID OSM);
  - vista "Lotti": griglia da 1 m dei lotti vicini, celle libere in verde e occupate in rosso (mappa di occupazione con test in Core);
  - landmark: Cattedrale di San Giustino (dalle parti OSM, con campanile), Teatro Marrucino (portico), stadio Guido Angelini (campo con linee e torri faro; tribune dai dati OSM);
  - profili di qualità automatici: desktop, mobile, web, web su telefono.
- **Test**: 92 .NET, 9 pytest, Unity EditMode 9/9 e PlayMode 7/7. Le foto di controllo (`WorldSnapshotTests`) si fanno con GPU e pack: `ORBIS_SNAPSHOT_DIR=… Unity -runTests -testFilter WorldSnapshotTests`.
- **Verifiche dopo l'unione (16/11)**: iOS verde; web verde (16 MB iniziali più 66 MB di mondo in streaming, WebGPU, avvio di Chieti senza errori in console, provato nel browser integrato). Android: la prima build è fallita per un errore di rete di Gradle (plugin Android non scaricato da Google Maven), ed è stata rilanciata. Il test PlayMode `ClicMouse_SuUnEdificio_ApreIDettagli` verifica in locale, con il pack, che un clic vero su un edificio apra i Dettagli.
- **Build**: iOS, Android e web scaricano il pack indicato in `data/world/packs.json` (StreamingAssets). Il limite di 60 MB del web esclude i tile, che arrivano uno alla volta.
- **Ambiguità risolte**:
  - Quote del terreno come `base + valore × passo`: `int16` in cm non arriva ai 2.793 m della Majella.
  - Il globo base per ora sta dentro il pack di Chieti (`globe.jpg`, generato dai Terrain Tiles); i confini e le città di Natural Earth arrivano più avanti.
  - "Nuova partita" entra direttamente a Chieti: la scelta del luogo e del ruolo è in M2.
  - L'orologio di gioco parte alle 10 del giorno attuale (prima impressione con la luce del giorno); dal M2 l'ora sarà salvata con la partita.
- **Correzioni dopo la prova di Samuele (16/11 sera)**:
  - HUD non più coperto dalla Dynamic Island: la safe area ora delimita davvero le schermate (prima era un padding che le schermate in posizione assoluta ignoravano).
  - Rotazione a due dita come su Mappe: la città gira insieme alle dita.
  - Bottone "livelli": pannello con gli interruttori "Nomi delle vie" (scelta salvata sul dispositivo) e "Lotti e griglia".
  - Grafica più realistica:
    - post-processing nel mondo: tonemapping ACES, bloom leggero, contrasto, vignettatura (Volume globale nella scena, così gli shader restano nelle build; spento sul web dei telefoni);
    - luce del cielo più debole del sole, così le ombre si leggono;
    - ombre del profilo Mobile a 2 cascate da 2048;
    - intonaco con macchie, colature e sporco alla base; cornici e davanzali delle finestre;
    - coppi più vari, chiome più scure alla base, terreno con variazioni ampie.
- **Da fare per chiudere M1**: unione di `m1-mondo`, build verdi, prova su iPhone e web (fluidità ≥ 30 fps), tag `v0.1.0`.

**M0 — Fondamenta: completata il 16/11/2026 (tag `v0.0.0`).** Samuele ha provato l'app iOS (tutto ok) e la versione web (dopo la correzione dei clic); `/api/health` risponde `ok`. **L'APK Android non è stato provato su un telefono** (Samuele non ha un dispositivo Android): la build è verde e firmata, la prova resta da fare quando ci sarà un Android.

## Già pronto (fatto da Samuele il 15/11/2026)
- Repository pubblico `Samozzux/orbis` con `.gitignore` Unity.
- Account Unity Personal attivo; Unity Hub con Unity **6000.6.9f1** sul Mac (modulo iOS Build Support da aggiungere).
- Secrets GitHub: `UNITY_EMAIL`, `UNITY_PASSWORD`, `UNITY_LICENSE` (contenuto di `UnityEntitlementLicense.xml`), `FTP_HOST`, `FTP_USER`, `FTP_PASSWORD`.
- Altervista: sito `giuliaa8.altervista.org`, cartella `/orbis/` creata, database attivo.
- iPhone 15 Pro con iOS 26; Sideloadly già usato per altre app.
- Android: build APK richiesta il 15/11/2026 (keystore da creare in M0).
- Specifica completa in `docs/`.

## Fatto in M0 (sessione del 15/11/2026)
- **Progetto Unity 6000.6.9f1 + URP** creato dal template ufficiale dell'editor (impostazioni generate da Unity, non scritte a mano). Pacchetti: URP 17.6, Input System 1.20.1, Addressables 2.11.2, Entities + Entities Graphics 6.6, Burst 2.0, Collections 6.6, Mathematics 1.4, Cinemachine 6.6, UI Toolkit (modulo), Localization 1.5.13, AI Navigation 2.0.16, Test Framework 1.8.
- **Assembly** di `docs/01` §2 in `Assets/Orbis/`: Core (`noEngineReferences`), Sim, World, Gameplay, UI, Sports.View, Audio, Net, Platform, Rendering, EditorTools, test EditMode/PlayMode.
- **Orbis.Core** (logica pura): `AppVersion` (confronto versioni/minAppVersion), `BootProgress` (avanzamento pesato del caricamento), `ScreenClassifier` (telefono/tablet/desktop dalla dimensione in punti), `ISaveStore` + `MemorySaveStore`, `DeviceInfo`.
- **tests/Orbis.Core.Tests** (.NET 8, xUnit, C# 9 come Unity): 42 test.
- **Orbis.Platform**: tutte le interfacce di `docs/01` §10 (`ILocalLanguageModel`, `IBiometricAuth`, `ISecureStore`, `ISpeech`, `IUserMusic`, `ISaveStore`, `IHaptics`, `INotifications`, `IDeviceProfile`) con implementazioni Editor/iOS/Android/Web che usano i ripieghi. Unico plugin nativo: `Assets/Plugins/WebGL/OrbisWeb.jslib` (densità dello schermo, touch, WebGPU).
- **Interfaccia di avvio** (UI Toolkit, stile *liquid glass* iOS 26 su richiesta di Samuele): logo vettoriale animato (globo che ruota con orbita e satellite) → caricamento con barra in vetro → menu principale con bottoni a capsula in vetro (Nuova partita, Continua) e **navbar fluttuante** (Amici, Impostazioni, Crediti). Il vetro mostra lo sfondo sfocato che sta dietro, con riflesso sul bordo, tinta e ombra; stati premuto/hover/fuoco da tastiera. Layout adattivo (compact/medium/wide + verticale), safe area (Dynamic Island), 1 unità UI = 1 punto. Le voci del menu mostrano "In arrivo nelle prossime versioni".
- **Testi**: tutti dalla tabella Localization "UI" generata da `data/localization/ui.json` (anche Info.plist iOS e pagina web).
- **Generatori da codice** (`Orbis > Genera progetto`, eseguiti anche da ogni build): impostazioni player (bundle id `com.samuelecozza.orbis`, iOS 26 iPhone+iPad, Android API 29 ARM64 IL2CPP Vulkan+GLES3 ASTC, Web WebGPU+WebGL 2 Brotli con decompression fallback), icona dell'app disegnata da codice, tabelle Localization, PanelSettings, scena `Boot`.
- **BuildScript** (`BuildIOS`, `BuildAndroid`, `BuildWeb`) con versione da tag, numero build, Info.plist, firma Android dal keystore (o chiave di debug se manca).
- **Pagina web** personalizzata: schermo intero, caricamento nello stesso stile, avviso per browser non supportati, "Aggiungi a schermata Home" (manifest + icone).
- **Backend PHP** (compatibile PHP 7.0+): router unico, CORS, migrazioni automatiche con lock (`orb_migrations`, `orb_app_config`), `GET /api/health`, `GET /api/config`; test `backend/tests/run.php` (21 test).
- **Workflow**: `core-tests`, `backend` (test su PHP 7.0 e 8.3 con MySQL 5.7 → deploy FTPS → verifica health), `web-build` (→ GitHub Pages), `android-build` (→ `Orbis-apk`), `ios-build` (→ `Orbis-ipa`), `unity-tests` (PR e manuale).
- **Script** `tools/setup/configura-github.sh` per creare i segreti mancanti senza che le password passino da altre parti.

## Fatto il 16/11/2026: menu in stile "da gioco" (richiesta di Samuele)
- **Città 3D viva dietro il menu**: mini-città low-poly su un'isola-collina (omaggio a Chieti) generata da codice, sempre uguale: case con tetti in coppi, cattedrale con campanile, stadio, uliveti a filari, alberi, strada ad anello con auto che girano, nuvole che passano; telecamera che ruota lentamente e si adatta a orizzontale/verticale; cielo procedurale, sole con ombre morbide, nebbia. Si costruisce durante il passo "Preparo il mondo…" del caricamento (`Assets/Orbis/UI/Runtime/Menu3D`).
- **Liquid glass vero**: una seconda telecamera disegna la scena a bassa risoluzione, lo shader `Hidden/Orbis/GlassBlur` la sfoca e la rende più viva; bottoni, navbar, barra e consigli mostrano la città sfocata che sta davvero dietro. Senza GPU (test in batchmode) resta lo sfondo fisso.
- **Stile da gioco**: font **Lilita One** (OFL) per titoli ed etichette, titolo con contorno e ombra netta, bottoni a capsula spessi (arancio "Nuova partita", blu "Continua") che si schiacciano alla pressione e rimbalzano all'entrata, navbar con icone in cerchi colorati, barra di caricamento con percentuale e riquadro "Lo sapevi?" con consigli, avviso a banner arancio.
- **Verticale e orizzontale**: `DisplaySettings` in Core (con test), servizio `IScreenOrientation` in Platform (app iOS/Android: rotazione automatica o blocco; web/desktop: segue il dispositivo), primo pannello **Impostazioni** (vetro, interruttore "Rotazione automatica", scelta Verticale/Orizzontale con icone), scelta salvata sul dispositivo e applicata a ogni avvio; player settings con verticale e orizzontale consentiti.
- **CI più veloce** (come prevede `docs/07` §2): IPA, APK e web si compilano solo dopo l'unione in `main`, sui tag `v*` e a mano; sulle PR girano core, backend e test Unity. Regola su `main`: richiesti `core` e `unity-tests`, vietati force-push e cancellazione; unione automatica consentita.

## Verifiche M0
- **Test locali**: `dotnet test` 42/42; Unity EditMode 9/9, PlayMode 1/1 (avvio fino al menu senza avvisi né errori in console, anche con GPU Metal e città 3D); backend 17/17 senza DB.
- **CI su `m0-fondamenta`**: tutte verdi. Core tests; Backend 21/21 su PHP 7.0.33 e 8.3 con MySQL 5.7; **Android** → `Orbis-apk` 42,7 MB (19 min); **iOS** su `macos-26` con Xcode 26 → `Orbis-ipa` 54,5 MB (20 min); **Web** → 15,2 MB compressi (27 min, limite 60 MB). Le tre build girano in parallelo senza problemi di licenza.
- **Prova della build web in locale** (browser integrato, Chrome 156): WebGPU attivo, testi caricati da Localization (Addressables), logo → caricamento → menu, avviso "in arrivo" su clic/tap e con Invio; layout verificato a 1024×768 (desktop/tablet), 852×393 (telefono orizzontale) e 393×852 (telefono verticale, anche ridimensionando dal vivo). Console senza errori né avvisi; restano solo messaggi informativi di URP sugli shader di post-processing non inclusi (si sistemano in M1 con i profili di stile).
- **Bug trovato dalla CI**: le impostazioni di Localization create da Unity contenevano un selettore di lingua fisso su "en". Sul Mac (sistema in italiano) funzionava, ma su dispositivi o browser in inglese il gioco avrebbe mostrato le chiavi al posto dei testi. Ora il generatore imposta sempre l'italiano, `Loc` ha un ripiego e un test EditMode lo controlla.
- **Bug dei clic sul web** (segnalato da Samuele il 16/11): sul sito nessun bottone rispondeva al mouse/tocco, mentre l'app iOS andava. Causa: la schermata di caricamento della pagina HTML, finito il caricamento, restava sopra il gioco trasparente (il suo `display: flex` annullava l'attributo `hidden`) e prendeva tutti i clic; la tastiera invece funzionava. Corretto nel modello della pagina (`[hidden]` nasconde sempre, `pointer-events: none` a caricamento finito, fuoco al gioco) e verificato sul sito pubblicato. Aggiunto un test PlayMode con clic veri del mouse via Input System (`PointerClickTests`, da eseguire senza GPU come in CI).
- **Nota tecnica**: un clic sintetico con pressione e rilascio nello stesso istante (come quello degli strumenti di automazione) non viene registrato da UI Toolkit; i clic e i tap reali sì (verificato con una pressione di 150 ms).
- **Una build Unity alla volta** (docs/07 §2): tutti i job Unity (iOS, Android, Web, test) sono nel gruppo di concorrenza `unity-license` con `queue: max` (funzione di GitHub del maggio 2026): aspettano in coda senza essere annullati. Prima erano in parallelo e una build Android è stata terminata dal sistema (codice 137) mentre partivano tre build insieme con lo stesso account Personal. Una push completa ora richiede circa 70 minuti di build in sequenza.
- **Licenza Unity in CI**: `buildalon/unity-setup@v3` (installa 6000.6.9f1 dal `ProjectVersion.txt`, moduli per piattaforma) + `buildalon/activate-unity-license@v3` con `license: personal` e `UNITY_EMAIL`/`UNITY_PASSWORD`. Il secret `UNITY_LICENSE` per ora non serve (resta come riserva). Sui runner GitHub macOS/Linux l'azione **non restituisce** la licenza Personal a fine job, per scelta: le macchine condividono la stessa identità e restituirla revocherebbe la licenza agli altri job.
- **HTTPS Altervista**: attivo (certificato valido su `https://giuliaa8.altervista.org`).
- **FTP Altervista**: accetta TLS esplicito; il certificato è per `*.altervista.org` e non copre `ftp.giuliaa8.altervista.org`, quindi il deploy usa FTPS in modalità "loose" (cifrato, senza controllo del nome).
- **Su `main` dopo l'unione (16/11/2026)**: build **Android** verde con APK firmato dal keystore di Orbis (42,7 MB), **iOS** verde (IPA 54,5 MB), **Web** verde e **pubblicata** su https://samozzux.github.io/orbis/ (verificato: la pagina carica e parte).
- **Altervista e FTP**: il primo deploy è stato rifiutato ("aggiungi la nazione da cui ti stai connettendo tra quelle consentite"): i server di GitHub si collegano dagli **Stati Uniti**. Samuele ha aggiunto gli USA tra le nazioni FTP consentite e il deploy del 16/11 è riuscito. Il workflow stampa sempre il Paese di provenienza.
- **`/api/health`** (16/11/2026, dal vivo): `status: ok`, HTTPS attivo; **PHP 8.0.30**; **MySQL 8.0.45** con utf8mb4, connesso con password vuota; migrazioni 1 applicata / 0 in attesa; estensioni openssl, zlib, mbstring, pdo_mysql, curl presenti, **sodium assente**; `upload_max_filesize` 128M, `post_max_size` 256M, `memory_limit` 512M, `max_execution_time` 60 s → pezzi di upload da 512 KB; segreto JWT e username admin configurati. CORS: ammessa solo `https://samozzux.github.io` (+ localhost), preflight 204. `config.php`, `src/`, `migrations/`, `storage/` e il file di stato del deploy rispondono 403.
- **CORS dei download GitHub Releases dal web** (verificato il 15/11/2026): **non utilizzabili**. `github.com/.../releases/download/...` risponde 302 senza `Access-Control-Allow-Origin`; anche passando da `api.github.com` (che ha CORS) il redirect finale su `release-assets.githubusercontent.com` risponde senza header CORS. GitHub Pages invece risponde con `Access-Control-Allow-Origin: *`. → vedi "Decisioni da confermare".

## Decisioni prese
- **Amici dopo** (Samuele, 17/11/2026): amici, visita delle città, progetti condivisi e mi piace escono da M3, che diventa "Account e cloud". Li ho messi in M9 ("Progressione e contenuti"), dove c'è anche l'editor dei progetti di edifici da condividere; si possono spostare altrove se Samuele preferisce. Fino ad allora la voce "Amici" del menu resta "In arrivo".
- **Edifici reali in stile gioco** (Samuele, 17/11/2026): gli edifici restano quelli veri (sagoma, altezza, piani, tetto da OSM) ma sono ricostruiti "da gioco", colorati e con i dettagli tipici del loro uso: bar e ristoranti con tende, insegne e dehors; negozi con vetrine, tende e insegne; benzinai con pensilina e pompe; stadi, aeroporti, campagne, strade con i loro elementi. Ogni edificio ha **addobbi** (fioriere, balconi fioriti, ombrelloni, lucine, bandiere…) e **potenziandolo gli addobbi cambiano a ogni livello** (1→5), anche per gli edifici reali. Lo stile resta il **Classico** (3D colorato tipo Megapolis) con più dettaglio, in direzione SimCity; dettagli in `docs/08` §3.
- Motore Unity (non Unreal) per compilare IPA, APK e web gratis su GitHub Actions.
- Piattaforme: iOS (iPhone + iPad), Android (APK) e Web (GitHub Pages).
- Dati sportivi da fonti aperte, niente scraping vietato.
- Tempo predefinito: 1 ora di gioco = 1 minuto reale.
- UI: **liquid glass** stile iOS 26 per bottoni, navbar e pannelli (Samuele, 15/11/2026), resa **più da gioco** (16/11/2026): città 3D animata dietro il menu, font Lilita One, bottoni spessi e colorati, animazioni a rimbalzo. Componenti riusabili in `Assets/Orbis/UI/Runtime/Glass` (`GlassSurface`, `GlassBackdrop`) e `Menu3D`.
- CI: build IPA/APK/web su `main`, tag e a mano; sulle PR solo test (Samuele, 16/11/2026). Una build Unity alla volta con la coda `unity-license`.
- **Orientamento** (Samuele, 16/11/2026, "importantissimo"): su telefono e tablet il gioco è giocabile **in verticale e in orizzontale**. Di default **rotazione automatica attiva e avvio in verticale**; in Impostazioni → Schermo si spegne la rotazione e si sceglie Verticale o Orizzontale (spegnendola si blocca l'orientamento in uso). Nel browser l'orientamento segue il dispositivo (Safari e gli altri browser non permettono il blocco) e le Impostazioni lo spiegano. Desktop: orizzontale. Aggiornati `CLAUDE.md`, `docs/00`, `docs/01`, `docs/04`, `docs/09`. Ogni schermata futura deve avere layout verticale e orizzontale.
- Testi nei file UXML come chiavi `#chiave` sostituite all'avvio dalla tabella Localization "UI" (scelta più semplice e verificabile da test: un test controlla che ogni chiave usata esista).
- Classi di layout: lato corto < 500 pt → telefono; lato lungo ≥ 1280 pt e lato corto ≥ 700 pt → desktop; altrimenti tablet (scelta semplice, da rivedere se serve).
- Prima del Keychain/Keystore (M3) `ISecureStore` è solo in memoria; salvataggi su file (iOS/Android/Editor) e in memoria sul web fino a M2 (SQLite / IndexedDB).
- **Sagome precise e posizionamento a griglia** (Samuele, 16/11/2026): ogni edificio/oggetto ha un'impronta esatta che coincide con il modello e si piazza solo dentro lotti delimitati, su griglia (1 m edifici, 0,5 m decorazioni), con mappa di occupazione e rotazioni a 90°/45°; mai posizioni casuali. Dettagli in `docs/04` §4; recepito in `docs/00`, `docs/08`, `docs/09` (M1 prepara i lotti, M2 li usa).
- **Energia e campagne** (Samuele, 16/11/2026): nel catalogo pale eoliche, fotovoltaico, idroelettrico, termoelettrico, biomasse, geotermico, **nucleare**, linee elettriche; campi coltivati, **vigneti**, uliveti, frutteti, serre, fattorie. Simulazione di rete elettrica e produzione agricola in `docs/03` §8; aree agricole reali da OSM già in M1.
- **Grafica**: la città del menu è un diorama generato da codice che anticipa lo stile **Classico** (3D colorato); in gioco la città sarà la Chieti vera (strade e sagome OSM, facciate, tetti, alberi, giorno/notte), molto più dettagliata; gli stili Realistico e Pixel arrivano in M11.
- Database di produzione: `my_<FTP_USER>` / utente `<FTP_USER>` / host `localhost` (convenzione Altervista), password vuota salvo secret `DB_PASSWORD`.

## Decisioni da confermare
- **Strade nuove in M2** (docs/09: "strade" in modalità costruzione): in M2 si piazzano come elementi sulla griglia dei lotti (vialetti, parcheggi, piazze), senza cambiare la rete stradale. Il disegno libero di nuove strade, con incroci e lotti ricalcolati, si sposta a M4 insieme al traffico, che usa il grafo stradale. Motivo: ridisegnare i lotti richiede di ricalcolare le sagome a runtime su tutti i dispositivi.
- **Salvataggi locali**: file binari versionati in `persistentDataPath` (su web Unity li tiene in IndexedDB) invece di SQLite. SQLite richiederebbe librerie native diverse per iOS, Android e web; l'interfaccia `ISaveStore` resta quella di docs/01 §7, quindi si potrà cambiare in seguito.
0. **Compressione dei tile: Deflate invece di zstd** (`docs/02` §2 punto 6). zstd su iOS, Android e soprattutto web richiederebbe una libreria nativa o codice non sicuro in C#; Deflate è già incluso in .NET/Unity su tutte le piattaforme e comprime poco meno. Proposta: Deflate ora, zstd solo se servirà davvero.
1. **Hosting dei dati pesanti per il web** (CORS GitHub Releases negativo). Proposta: (a) per M1 il pack di Chieti e il globo base, inclusi nelle app, sul web si servono da **GitHub Pages** insieme al gioco (stessa origine, nessun problema CORS; limiti: sito ≤ 1 GB, file ≤ 100 MB); (b) per Italia/mondo (M10) **Cloudflare R2** (piano gratuito 10 GB, traffico in uscita gratuito, richiede un account Cloudflare di Samuele), alimentato dallo stesso workflow. Le app iOS/Android continuano a scaricare dalle GitHub Releases. Nessun account creato.
2. **.NET dei test**: .NET 8 è fuori supporto dal 10/11/2026. Funziona, ma propongo di passare il progetto di test a **.NET 10 (LTS)**.
3. **Versione PHP**: Altervista usa **PHP 8.0.30**, non 7.x. Le regole di `CLAUDE.md` (niente `fn()`, `match`, `?->`, proprietà tipizzate) restano rispettate e i test girano anche su PHP 7.0. Proposta: alzare il minimo a PHP 8.0 (si potrebbero usare `match`, `?->`, proprietà tipizzate), oppure lasciare tutto com'è per sicurezza. Non urgente.
4. **Riferimenti tra assembly**: `Orbis.UI` usa anche `Orbis.Platform` (per profilo del dispositivo e servizi), non solo Core e Gameplay come nella tabella di `docs/01` §2.

## Problemi aperti
- **Test PlayMode dei clic sul menu (solo in locale)** (visto il 18/11, risolto il 18/11): non era un problema del gioco. I test dei clic col mouse vanno lanciati con `-nographics`, come in CI e come dice il loro commento: con la GPU del Mac il mouse vero si mescola a quello del test e, dopo altri test, i clic si perdono. Con `-nographics` l'intera suite PlayMode passa anche in locale, compresa la partita vera di `GameplayTests` col pack. Ora con la GPU quei test si saltano da soli, con la spiegazione, invece di fallire.
- **Build iOS in coda** (17/11, risolto su richiesta di Samuele): la build iOS di `main` aspettava da ore un runner `macos-26` di GitHub tenendo occupata la licenza Unity condivisa (`concurrency: unity-license`), e test e altre build restavano bloccati. Ora `ios-build.yml` ha due job: l'esportazione Unity → progetto Xcode gira su Linux (con la licenza, runner subito disponibili), la compilazione Xcode → IPA su macOS senza licenza. Se il Mac tarda, aspetta solo l'IPA.
- **Lotti con griglia anomala** (visto il 17/11): almeno un lotto di Chieti (id 2476, 158×161 m) ha origine o angolo della griglia che rendono enorme la sua griglia intera. Per costruire non è un problema (si controlla solo sotto l'elemento), ma va guardato nella pipeline quando si rigenera il pack.
- **Unity terminato dal sistema in CI (codice 137)**: due volte (build Android del 15/11 e build web del 16/11) Unity è stato ucciso un secondo dopo l'avvio, sempre con la cartella `Library` ripristinata dalla cache; un'altra build con cache invece è andata bene, e la build web era sola in coda (non è un conflitto di licenza). Causa non ancora certa. Contromisura: `.github/scripts/run-unity.sh` (usato da tutti i job Unity) in quel caso registra memoria e messaggi del kernel, cancella `Library` e riprova una volta da zero. Se la diagnostica indicherà la causa, si correggerà alla radice. Prima prova dopo la correzione (16/11, commit `c16787b`): iOS, web e Android verdi senza bisogno del nuovo tentativo.
- Il modulo **iOS Build Support** non è installato sul Mac: non serve (l'IPA si compila in CI), ma senza non si può provare la build iOS in locale.
- Gli APK compilati prima del 15/11 alle 23:38 sono firmati con una chiave di debug: se ne hai installato uno, va disinstallato prima di installare quello firmato con la chiave di Orbis.

## Cosa deve fare Samuele
- ✅ Fatto il 15/11: script `tools/setup/configura-github.sh` (JWT, admin, keystore Android, GitHub Pages); tolta la password del database.
- ✅ Fatto il 16/11: Stati Uniti aggiunti tra le nazioni FTP consentite su Altervista.
- ✅ Fatto il 16/11: prova di M0 sui dispositivi (tag `v0.0.0`).
- ✅ Fatto il 16/11: prova di M1 ("quasi perfetto", correzioni nella PR #5), tag `v0.1.0`.
- ✅ Fatto il 17/11: prova di M2 ("sì va bene"; corretta la scheda della missione sul telefono), tag `v0.2.0`.
- ⏳ Prova di M3 sui dispositivi: rimandata da Samuele il 19/11 (M3 chiusa senza la prova, tag `v0.3.0`). Quando la fai, segui "Cosa provare (M3)" e dimmi cosa non va: le correzioni hanno la precedenza su M4.

## Cosa provare (M3 — Account e cloud)
Release `v0.3.0` (IPA e APK allegati) e build web su GitHub Pages.
- **iPhone, primo avvio**: dopo il caricamento si apre "Account".
  - "Crea account" → nome utente (3–20 caratteri), nome da mostrare, password (almeno 8) → "Crea l'account".
  - Alla domanda "Entrare con Face ID?" → "Sì, attiva".
- **iPhone, riavvio**: chiudere del tutto l'app e riaprirla. Compare Face ID e poi "Bentornato"; in basso nella barra c'è il tuo nome.
- **Face ID annullato**: riaprire l'app e, quando compare Face ID, toccare "Annulla". Nel modulo di accesso c'è "Entra con Face ID": toccandolo Face ID ricompare e si entra senza password.
- **Giocare**: "Nuova partita", costruire qualcosa, tornare al menu con la casetta. In Account → "Partite nel cloud" deve dire "Tutto sincronizzato alle …", e in "Continua" la partita deve risultare "Nel cloud".
- **Mac, browser** (Safari o Chrome sulla pagina del gioco):
  1. Account → "Entra" con nome utente e password.
  2. Alla domanda "Creare una passkey?" → "Crea la passkey", con Touch ID o il telefono.
  3. In "Continua" c'è la partita dell'iPhone, con lo stesso livello e la stessa cassa: giocarla un po' e tornare al menu.
- **Ritorno sull'iPhone**: riaprire l'app; dopo il menu la partita si aggiorna con quello che hai fatto sul Mac.
- **Passkey**: nel browser Account → Esci, poi "Entra con una passkey", senza password.
- **Conflitto** (facoltativo):
  1. Giocare la stessa partita offline sull'iPhone (modalità aereo) e intanto sul Mac.
  2. Riattivare la rete e tornare al menu dell'iPhone.
  3. Compare "Partita giocata in due posti" con le tre scelte: provare "Tienili entrambi".
- **Android**: come sull'iPhone, con l'impronta o il volto al posto di Face ID.
- **Dispositivi collegati**: Account → l'elenco mostra iPhone, Mac e Android; "Scollega" su uno dei tre lo fa uscire.

## Cosa provare (M2 — Costruire ed economia)
Build dopo l'unione della PR di M2: web su GitHub Pages, `Orbis-ipa` e `Orbis-apk` dalle ultime esecuzioni verdi.
- **Nuova partita**: menu → "Nuova partita" → nome, difficoltà, opzioni → "Inizia". In alto a sinistra cassa e livello, sotto la prima missione (toccarla per leggerla).
- **Costruire**: bottone arancio "Costruisci" → scegliere una scheda (Case, Energia, Agricoltura…) o cercare per nome → toccare un elemento.
  - iPhone: il fantasma sta al centro dello schermo, si sposta trascinando la mappa; verde = si può (✓ per costruire), rosso = c'è il motivo; il bottone blu ruota.
  - Mac/PC: il fantasma segue il mouse, clic per costruire, R ruota, Esc esce.
  - Provare una casa in un cortile, una pala eolica o un campo solare in campagna, un vigneto.
- **Lavori**: l'edificio cresce dal suolo; quando è finito arriva l'avviso. Velocità dell'orologio (bottone arancio dell'ora) per accelerare.
- **Annulla/ripeti**: le due frecce in basso; annullare restituisce tutto, anche i premi.
- **Toccare un elemento costruito**: Dettagli con Potenzia, Colori, Sposta, Ruota, Demolisci. **Toccare un edificio reale**: c'è "Demolisci" e il lotto si libera.
- **Finanze** (grafico): entrate e uscite per voce, aliquote −/+; **Riavvolgi** (freccia indietro): tornare a un giorno passato.
- **Un mese di gioco**: lasciar correre il tempo (a 10× un giorno dura circa 2 minuti e mezzo) e guardare la cassa salire o scendere.
- **Chiudere e riaprire**: casa → menu → "Continua" → la partita è com'era. Chiudere del tutto l'app e riaprirla dopo qualche minuto: compare "Mentre eri via" con quanto è successo.
- **Verticale e orizzontale**, e dire se la fluidità resta buona con qualche decina di edifici costruiti.

## Cosa provare (M1 — Chieti in 3D)
Build dopo l'unione della PR #4 (16/11): web su GitHub Pages, `Orbis-ipa` e `Orbis-apk` dalle ultime esecuzioni verdi.
- **Entrare**: menu → "Nuova partita" → compare "Carico Chieti…" e poi la città vera, con il centro storico in vista.
- **Muoversi**:
  - sull'iPhone: un dito sposta, il pizzico fa lo zoom, due dita ruotano, due dita su/giù inclinano, doppio tocco centra;
  - su Mac/PC: tasto sinistro sposta, destro ruota e inclina, rotella zoom verso il puntatore, WASD/frecce, Q-E, Z-X, M per il 2D.
  - Provare sia in verticale sia in orizzontale.
- **Riconoscere**: Corso Marrucino e le vie col nome sulla mappa, la Cattedrale di San Giustino col campanile, il Teatro Marrucino, lo stadio Angelini con la pista. Più fuori: vigneti a filari, uliveti, campi colorati, la costa e la Majella all'orizzonte.
- **Toccare un edificio** → si illumina e si apre il pannello Dettagli (nome, indirizzo, tipo, altezza, piani, tetto, superficie, negozi dentro, posizione, ID OSM). Si chiude con la X o toccando il terreno.
- **HUD**:
  - orologio (1 ora di gioco = 1 minuto) e meteo: si vede passare il giorno, poi la notte con le finestre accese e le stelle;
  - bottone arancio: pausa, 1×, 2×, 4×, 10×;
  - mappa/cubo: 2D/3D; bussola: nord in alto;
  - livelli: vista "Lotti" (griglia verde/rossa da vicino);
  - casa: torna al menu.
- **Globo**: allontanarsi al massimo e continuare a zoomare → si sale sul globo; trascinare per girarlo; zoomare verso l'Italia per tornare a terra.
- **Fluidità**: dire se è fluido (obiettivo almeno 30 fps) sull'iPhone 15 Pro e nel browser del Mac; sul web il primo caricamento della città richiede qualche secondo in più.

## Cosa provare (fine M0)
Build con la grafica nuova **pronte** (16/11, commit `c16787b`): web online su GitHub Pages (16 MB, verificata), APK 43,5 MB firmato con la chiave di Orbis, IPA 54,8 MB.
- **Web**: aprire `https://samozzux.github.io/orbis/` da Mac, iPhone (in verticale e in orizzontale) e iPad → logo, caricamento con consigli, città 3D che gira con mare e Majella, bottoni di vetro e navbar; "Impostazioni" apre il pannello (nel browser con la nota sull'orientamento); le altre voci mostrano "In arrivo"; su Mac anche Tab/frecce + Invio, Esc chiude le Impostazioni.
- **iPhone / iPad**: scaricare `Orbis-ipa` dall'ultima esecuzione verde di "iOS build" e installarlo con Sideloadly (istruzioni nel README) → parte in verticale e ruota col telefono; in Impostazioni → Schermo spegnere "Rotazione automatica" e provare Verticale/Orizzontale; chiudere e riaprire l'app: la scelta resta. Controllare la Dynamic Island in entrambi gli orientamenti.
- **Android**: scaricare `Orbis-apk` dall'ultima esecuzione verde di "Android build" e installarlo (istruzioni nel README) → stesse prove dell'iPhone; anche il tasto "indietro" chiude le Impostazioni.
- **Backend**: `https://giuliaa8.altervista.org/orbis/api/health` → JSON con `"status": "ok"` (verificato il 16/11).

## Registro sessioni
### 15/11/2026 — M0 Fondamenta
- **Fatto**: tutto il codice di M0 (vedi sopra) sul branch `m0-fondamenta`, tutte le pipeline verdi (core, backend, IPA, APK, web), build web provata in locale. Su richiesta di Samuele l'interfaccia è in stile liquid glass iOS 26.
- **Corretto durante la prova**: l'avviso "in arrivo" non compariva (elemento cercato nel posto sbagliato; sul web i riferimenti nulli non danno errore) → ora gli elementi mancanti fanno fallire subito l'avvio con un messaggio chiaro e il test PlayMode preme davvero "Nuova partita". Il vetro era troppo "lattiginoso" (spazio colore lineare) → sfocatura resa più viva e velature più leggere.
- **Resta**: azioni di Samuele (sopra); unione in `main` con primo deploy di web e backend; lettura dei valori di `/api/health`; prova sui dispositivi; tag `v0.0.0`.

### 16/11/2026 — M0 su main e menu "da gioco"
- **Fatto**: PR #1 unita in `main`; build Android (APK firmato), iOS e Web verdi; versione web pubblicata su GitHub Pages. Menu rifatto in stile gioco con città 3D, mare, Majella, vetro che sfoca la scena vera, font Lilita One (PR #2). CI resa più veloce: sulle PR solo test.
- **Poi**: gioco in verticale e orizzontale con Impostazioni → Schermo (decisione di Samuele, specifica aggiornata); PR #2 unita; backend online dopo lo sblocco FTP su Altervista, `/api/health` verificato.
- **Resta**: prova di Samuele sui dispositivi; tag `v0.0.0`; decisioni da confermare (hosting web dei dati pesanti, .NET 10, versione PHP minima, riferimenti tra assembly).

### 17/11/2026 — M2 Costruire ed economia
- **Fatto**: core del gioco, dati, modelli procedurali, salvataggi web (commit precedenti su `m2-core`); in questa sessione interfaccia della partita (HUD economico, catalogo con ricerca, barra di costruzione, Dettagli con azioni, Finanze, Riavvolgi), Nuova partita e Continua con più salvataggi, riepilogo del tempo ad app chiusa, 143 nuovi testi italiani, test di gioco sul mondo vero.
- **Corretto durante le prove**: le missioni e i livelli completati da un'azione del giocatore non davano l'avviso; il controllo dello spazio costruiva la griglia dell'intero lotto e sui lotti agricoli enormi andava in errore ("lotto troppo grande") → ora controlla solo la finestra sotto l'elemento, veloce ovunque (test con un lotto di 3×3 km); WASD muoveva la camera mentre si scriveva nella ricerca; ritocchi grafici dalle fotografie dell'interfaccia.
- **Resta**: PR, build verdi, prova di Samuele, tag `v0.2.0`.
- **Poi, sempre il 17/11**: PR #6 unita in `main` (build per la prova di M2 avviate). Su richiesta di Samuele: edifici reali in stile gioco con addobbi per tipo e livello, potenziamento degli edifici reali, città più colorata (branch `m2-stile-gioco`).
- **Sera del 17/11**: PR #7 unita. Dalla prova di Samuele: edifici costruiti che entravano nel terreno (corretto), telecamera più vicina in modalità costruzione, terreno non più sgranato (branch `m2-costruzione`).
- **Poi**: su scelta di Samuele, grafica HD di edifici e strade: insegne con i nomi veri, gronde e cornicioni, strisce pedonali e lampioni; strade nitide e anteprime 3D nel catalogo (branch `m2-grafica-hd`).
- **Chiusura di M2 (sera del 17/11)**: PR #8, #9 e #10 unite (la #10 divide la build iOS in due job). Samuele ha provato M2 sui dispositivi: va bene. Corretta la scheda della missione che sul telefono toccava la barra del livello; tag `v0.2.0`. Su scelta di Samuele gli amici passano da M3 a M9.
- **Prossimo passo**: M3 — Account e cloud.

### 18/11/2026 — Ritocchi dopo la prova di M2
- **Fatto**: su richiesta di Samuele bottoni del gioco rifatti (centrati, anello uniforme), stadi veri con gradinate, pista e fari, stadio del catalogo e centrale nucleare come quelli veri (branch `ritocchi-bottoni-edifici`). Il lato gioco di M3 (account e sincronizzazione) è in pausa e riprende subito dopo.
- **Poi**: Samuele chiede che tutti gli edifici, quelli che ci sono e quelli da aggiungere, siano come quelli veri. Rifatti tutti i 150 modelli del catalogo (stessa PR #13); regola scritta in `docs/08` §2.
- **Lato gioco di M3**: account, Face ID, impronta e sincronizzazione (PR #14); passkey per il web con API 0.4.0 (PR #15); test dei clic e passi della prova di M3 (PR #16). Tutte unite in `main`, con il backend online.

### 19/11/2026 — In attesa della prova di M3
- **Fatto**: rivisti i plugin Face ID (Swift) e impronta (Kotlin) prima della prova sui dispositivi. Aggiunto "Entra con Face ID / con l'impronta" nel modulo di accesso, per riprovare dopo aver annullato la richiesta all'avvio (branch `m3-riprova-biometria`).
- **Chiusura di M3**: PR #17 unita, build di `main` verdi (web, IPA, APK). Samuele sceglie di chiudere M3 senza la prova sui dispositivi; tag `v0.3.0`.
- **Prossimo passo**: M4 — Cittadini e città viva, cominciando dal piano a blocchi.
