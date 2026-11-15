# 05 — Sport

Tutti gli sport sono presenti **da subito** e hanno **vita propria** nel gioco: dopo l'import iniziale dei dati reali, la storia (risultati, carriere, trasferimenti, ritiri, nuovi talenti) la scrive la simulazione. Non c'è sincronizzazione con la realtà.

## 1. Elenco sport

| Gruppo | Sport | Motore | Visualizzazione 3D |
|---|---|---|---|
| Squadra (principali) | Calcio, Basket, Pallavolo | dedicato | completa |
| Squadra | Rugby, Pallanuoto, Pallamano, Hockey su ghiaccio, Hockey su prato, Football americano, Baseball, Calcio a 5, Beach volley, Cricket | dedicato semplificato | completa semplificata |
| Racchetta | Tennis | dedicato | completa |
| Racchetta | Padel, Ping pong, Badminton, Squash | derivato dal tennis | completa semplificata |
| Motori | Formula 1, MotoGP, Rally, Superbike | a tempi sul giro | gara 3D sui circuiti (reali o cittadini disegnati dal giocatore) |
| Ciclismo | Strada (grandi giri, classiche), Pista, MTB | a tempi/tappe | gruppo 3D sulle strade reali |
| Atletica | Corsa (100 m → maratona), salti, lanci | a prestazione | completa |
| Acquatici | Nuoto, Tuffi, Canottaggio, Canoa, Vela | a prestazione/punteggio | completa semplificata |
| Invernali | Sci alpino, Sci di fondo, Biathlon, Pattinaggio, Snowboard, Bob | a prestazione/punteggio | completa semplificata |
| Combattimento | Boxe, Judo, Scherma, Lotta, Taekwondo, MMA | a scambi/punteggio | completa semplificata |
| Individuali | Golf, Ginnastica, Equitazione, Tiro a segno/volo, Tiro con l'arco, Arrampicata, Triathlon | a prestazione/punteggio | completa semplificata |
| Altri | Freccette, Biliardo, Bocce, Scacchi, eSport (simulati) | generico | tabellone/vista semplice |

Se uno sport non ha ancora una visualizzazione 3D completa, mostra comunque la partita con una vista semplificata (campo schematico + cronaca + statistiche).

## 2. Struttura dei dati (Core)
- **Sport**: regole di punteggio, durata, formati, ruoli, attributi rilevanti.
- **Atleta**: anagrafica (nome reale o generato), nazionalità, età, ruolo/specialità, **attributi** (0–100, specifici per sport: es. calcio → velocità, tecnica, tiro, passaggio, difesa, fisico, portiere…; tennis → servizio, risposta, dritto, rovescio, volée, resistenza, mentalità, superfici), potenziale, forma, morale, infortuni, contratto, valore di mercato, carriera.
- **Squadra/club**: nome, città, colori, stemma (generato, mai loghi reali), impianto, rosa, staff, bilancio, tifosi, proprietario (NPC o giocatore).
- **Impianto**: collegato al luogo reale del region pack (stadio, palazzetto, circuito, campo), capienza, livello.
- **Competizione**: campionato a gironi, coppa a eliminazione, torneo a tabellone, circuito/tour con classifica a punti, gara a tappe, evento singolo; calendario, regolamento (punti, spareggi, promozioni/retrocessioni).
- **Stagione**: istanza di una competizione con risultati e statistiche.

## 3. Motori di simulazione (Core, deterministici con seed)
- **Calcio**: simulazione a eventi minuto per minuto basata su possesso, zone di campo e attributi; produce cronologia (azioni, tiri, gol, falli, cartellini, cambi, infortuni), statistiche e posizioni chiave per la visualizzazione. Tattiche e formazioni influenzano il modello.
- **Tennis**: modello punto per punto (probabilità di vincere il punto al servizio/in risposta da attributi, superficie, forma, stanchezza) → game, set, tie-break, partita; scambi generati per la visualizzazione.
- **Basket**: simulazione a possessi (tiro da 2/3, rimbalzi, palle perse, falli, liberi).
- **Pallavolo**: rally point system con fondamentali.
- **Motori/ciclismo/atletica/nuoto/sci**: tempi da modello prestazione = attributi × condizioni × casualità × strategia (pit stop, fughe, scie).
- **Generico**: per gli sport minori, modello a punteggio basato su forza relativa.
- Partite non guardate: simulate in modo **rapido** (stesso modello senza dettagli di visualizzazione); partite guardate: simulate in modo **dettagliato** e mostrate in tempo reale.

## 4. Vita propria
- Invecchiamento e crescita degli atleti (curve per sport), ritiri, **nuovi giovani** generati ogni anno (settori giovanili, scuole sportive), nomi generati per i nuovi.
- **Mercato trasferimenti** con finestre, valutazioni, ingaggi, prestiti; squadre NPC con strategia (ambizione, budget).
- Allenatori NPC con stile; esoneri.
- Bilanci dei club: biglietti, diritti TV, sponsor, stipendi; fallimenti e rifondazioni.
- Calendari annuali che seguono l'anno di gioco.

## 5. Il giocatore e lo sport
- **Proprietario della città**: costruisce e potenzia impianti, ospita eventi, incassa canoni e biglietti, può possedere club.
- **Cittadino**:
  - tifoso (va allo stadio, compra biglietti e maglie, esulta/protesta sul social);
  - **atleta** (carriera personale in uno sport, allenamenti, gare);
  - **allenatore** (squadre o singoli atleti: tennisti, cestisti, piloti…): formazione, tattica, allenamenti, mercato se anche manager;
  - **proprietario/presidente** di club: budget, mercato, stadio, ingaggi, sponsor.
- **Guardare le partite**: dallo stadio (entri e ti siedi) o dal menu/telefono. Telecamere TV multiple, replay dei momenti salienti, statistiche dal vivo, telecronaca (modelli testuali; voce sintetica opzionale), velocità x1/x2/x4, "solo highlights", salta alla fine.
- **Fantacalcio / fantatennis** sui campionati simulati, con leghe tra amici.
- **Album di figurine**: pacchetti guadagnati con missioni/eventi; collezioni per campionato.
- Giorni di partita: afflusso di tifosi, bar pieni, traffico intorno allo stadio, incassi per la città.

## 6. Competizioni iniziali (dati reali, poi vita propria)
- **Calcio**: Serie A, B, C (gironi), Serie D (gironi), Coppa Italia; campionati dilettantistici **abruzzesi** fino alla Terza Categoria (girone di Chieti) inseriti tramite admin/import manuale; principali campionati esteri (Inghilterra, Spagna, Germania, Francia, Portogallo, Olanda + altri disponibili); Champions/Europa/Conference League; nazionali con Mondiali ed Europei.
- **Tennis**: circuiti ATP e WTA (Slam, Masters/1000, 500, 250, Challenger), Coppa Davis/BJK Cup, classifiche a punti con regole simili alle reali.
- **Basket**: Serie A, Serie A2, NBA, EuroLeague, nazionali.
- **Pallavolo**: SuperLega e Serie A1 femminile, competizioni europee, nazionali.
- **Motori**: F1, MotoGP con calendari su circuiti reali (landmark), campionati piloti/costruttori.
- **Ciclismo**: Giro d'Italia, Tour, Vuelta, classiche, Mondiali.
- **Altri sport**: campionati/eventi principali italiani e internazionali, **Olimpiadi** ogni 4 anni di gioco (estive e invernali) con tutti gli sport olimpici.

## 7. Dati iniziali: import una tantum (`tools/sports-import/`)
Fonti permesse (verificare i termini d'uso di ogni fonte prima dell'uso e annotarli in `tools/sports-import/FONTI.md`):

| Fonte | Uso | Note |
|---|---|---|
| **Wikidata** (SPARQL) | squadre, atleti, nazionalità, date di nascita, ruoli, impianti, competizioni di tutti gli sport | CC0 — fonte principale |
| **openfootball** | campionati, squadre, calendari di calcio | dominio pubblico |
| **football-data.org** | rose e competizioni di calcio (piano gratuito con chiave) | rispettare limiti di richieste |
| **TheSportsDB** | squadre, atleti, eventi di molti sport (chiave gratuita) | rispettare termini e limiti |
| **Wikipedia** | integrazioni puntuali (rose, albi d'oro) | CC BY-SA: preferire Wikidata |

**Non si fa scraping** di siti che lo vietano (es. 365Scores, LiveTennis e simili).

Procedura:
1. Script Python per sport che interrogano le fonti, normalizzano, deduplicano (per ID Wikidata) e producono JSON in `data/sports/raw/`.
2. **Generazione degli attributi**: gli attributi di gioco (0–100) non esistono nelle fonti: si stimano da dati disponibili (livello del campionato, ruolo, età, presenze, classifica/ranking, premi) con formule documentate + casualità controllata. Tutto modificabile dall'admin.
3. Validazione e report (duplicati, campi mancanti) in `data/sports/report.md`.
4. Output finale in `data/sports/pack/` → importato nel DB tramite il pannello admin (import massivo) → distribuito come **content pack "sport"**.
5. L'import si esegue **una volta**; da quel momento i dati sono nostri e restano per sempre. Si può ripetere in futuro per aggiornare la base delle **nuove** partite.

## 8. Nomi reali e data pack
- Nomi reali di atleti, squadre e competizioni stanno in un **data pack** separato (`sport-real-names`), sovrapposto a un pack con **nomi generati**.
- Opzione "Nomi reali" on/off nella configurazione del mondo.
- **Mai loghi, stemmi o maglie ufficiali**: stemmi e divise sono generati (colori sociali sì).
- In caso di pubblicazione futura basta rimuovere il pack dei nomi reali.

## 9. Test
Per ogni motore: test statistici su 10.000 partite simulate (distribuzione gol/punti/set realistica, vantaggio per la squadra più forte, nessun risultato impossibile) e test di regolamento (classifiche, spareggi, tabelloni, punti ranking).
