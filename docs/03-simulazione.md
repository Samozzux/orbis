# 03 — Simulazione

Obiettivo: una città **viva, realistica e sempre diversa**, che gira bene su telefono e browser. Tutti i parametri numerici qui sotto sono **valori iniziali** da bilanciare: vanno in `data/balance/*.json`, mai fissi nel codice.

## 1. Tempo
- 1 ora di gioco = 1 minuto reale (predefinito). Opzioni in `docs/00-visione.md` §14.
- Calendario reale (giorni della settimana, festività italiane, stagioni, orari di alba/tramonto calcolati dalla latitudine).
- Il gioco inizia alla data reale del giorno della creazione della partita, poi va avanti con il tempo di gioco.

## 2. Popolazione sintetica (`tools/population/` + generatore in Core)
- Per ogni comune: numero di abitanti, distribuzione per età e sesso (ISTAT), distribuzione dei redditi (MEF), composizione delle famiglie (stime ISTAT).
- Si generano **famiglie** e le si assegnano alle abitazioni del region pack in base al volume degli edifici residenziali (m² stimati × occupazione media).
- Ogni cittadino ha:
  - identità: nome e cognome generati (liste di nomi italiani per fascia d'età e cognomi frequenti per regione, diverse per paese nel mondo), età, sesso, aspetto (parametri per il generatore di personaggi);
  - famiglia e relazioni (partner, figli, genitori, amici, colleghi, vicini) con punteggio di relazione;
  - casa, lavoro o scuola, orari;
  - **economia personale**: reddito, risparmi, debiti, affitto o mutuo, spese fisse;
  - **inventario**: frigo, dispensa, oggetti, auto/moto/bici, animali domestici;
  - **bisogni** (0–100): fame, energia, igiene, divertimento, socialità, comfort, salute;
  - **tratti** di personalità (5–8 tratti tipo: socievole, pigro, ambizioso, tifoso di…, sportivo, ecc.), gusti (cibo, musica, sport, squadra del cuore), opinioni politiche semplificate;
  - **memoria** degli eventi importanti (usata da dialoghi e IA);
  - **umore** e felicità con la città.
- Per le città fuori dall'Italia si usano dati GeoNames + parametri medi del paese.

## 3. Vita dei cittadini
- **Routine giornaliera** generata dai tratti e dagli impegni: sveglia, colazione (consuma dal frigo), lavoro/scuola, pranzo, spesa (va davvero al supermercato più conveniente/vicino, compra in base al reddito e ai gusti, riempie il frigo), svago (bar, parco, palestra, cinema, stadio nei giorni di partita), cena, sonno.
- **Scelte guidate da utilità**: ogni attività possibile ha un punteggio = bisogni soddisfatti × preferenze × distanza × costo × casualità. Questo produce comportamenti sempre diversi.
- **Disuguaglianze**: il reddito determina cibo (qualità e quantità), casa, arredo, auto, vacanze, svaghi; chi ha poco può saltare pasti, indebitarsi, cercare un secondo lavoro, chiedere aiuto ai servizi sociali.
- **Ciclo di vita** (attivabile): nascite (probabilità per età/coppia), crescita, scuola (primaria → università), primo lavoro, pensione, morte (tavole di mortalità ISTAT semplificate); matrimoni, separazioni, traslochi, immigrazione/emigrazione legata a felicità e lavoro.
- **Lavoro**: ogni attività economica ha posti di lavoro per tipo; assunzioni in base a competenze/istruzione; promozioni, licenziamenti, disoccupazione, sussidi.
- **Salute** semplificata: malattie stagionali, infortuni, visite in ospedale/ambulanza (senza dettagli medici realistici).

## 4. Economia della città (ruolo proprietario)

### Entrate
| Voce | Calcolo iniziale |
|---|---|
| Affitti degli immobili di proprietà comunale | canone mensile = valore immobile × rendimento (3–6% annuo) ÷ 12 |
| Canone di suolo pubblico | per m² di dehors, bancarelle, cantieri, insegne, eventi; tariffa per zona (centro più caro) |
| Tasse residenziali | % sul reddito delle famiglie (cursore 0–5%) |
| Tasse commerciali e industriali | % sul fatturato/utile delle attività (cursore) |
| Tributi sui rifiuti e servizi | per famiglia/attività |
| Biglietti trasporti pubblici, parcheggi | per passeggero / ora |
| Eventi, stadi comunali, musei, turismo | biglietti, tassa di soggiorno |
| Missioni, livelli, sfide | premi fissi |

### Uscite
Manutenzione edifici e strade, servizi pubblici (polizia, pompieri, sanità, scuole, rifiuti, illuminazione, verde), stipendi comunali, trasporti, interessi sui debiti, eventi organizzati.

### Regole
- **Tasse troppo alte** → calo felicità, chiusura attività, emigrazione (solo se le reazioni sono attive).
- **Bilancio negativo prolungato** → debito; oltre una soglia, commissariamento (scenario di sconfitta morbido: si può riavvolgere).
- **Valore immobiliare** di ogni edificio = base per tipo × superficie × moltiplicatori di zona (servizi vicini, trasporti, parchi, monumenti, vista, inquinamento, rumore, criminalità), aggiornato giornalmente. Influenza affitti e prezzi di vendita.
- **Attività commerciali** con conto economico reale: incassi dai clienti simulati, costi (merce, stipendi, affitto, tasse), utile; falliscono o crescono.
- **Borsa**: le aziende della città più grandi sono quotate; prezzo che segue utili e notizie; dividendi.
- **Banca**: prestiti al comune e ai cittadini, mutui, tassi influenzati da un indicatore macroeconomico simulato.
- **Eventi macro casuali**: boom turistico, recessione, inflazione, scioperi, crisi energetica (frequenza da configurazione).

## 5. Economia del cittadino (ruolo cittadino)
- Portafoglio personale; stipendio, spese, risparmi.
- Possedimenti: case (affittabili ad altri cittadini), negozi, aziende, squadre, impianti, quote in borsa.
- Le attività possedute generano utili/perdite secondo le regole delle attività commerciali.
- Il cittadino paga tasse e canoni alla città come tutti gli altri.

## 6. Reazioni dei cittadini e politica
- **Felicità** per cittadino = media pesata di: bisogni, casa, lavoro, servizi vicini, sicurezza, traffico, inquinamento, tasse, eventi, risultati della squadra del cuore.
- Impostazione reazioni:
  - *Disattivate*: la felicità è solo un indicatore.
  - *Leggere*: commenti sul social, lievi effetti su produttività.
  - *Realistiche*: petizioni, proteste in piazza, scioperi, emigrazione, voto contrario.
- **Sindaco IA** (quando il giocatore è cittadino): ha un programma (priorità: tasse, servizi, verde, sport, cultura, traffico), un budget, e ogni settimana di gioco decide interventi. Usa le stesse regole e comandi del giocatore. Con l'IA locale disponibile, le motivazioni e i discorsi sono generati dal modello; altrimenti decisioni a regole + testi a modelli.
- Il cittadino può: proporre idee, firmare/avviare petizioni, votare nei referendum, partecipare a proteste, **candidarsi alle elezioni** (ogni 5 anni di gioco, o anticipate). Campagna elettorale con comizi, programma, sondaggi. Se vince diventa proprietario della città.

## 7. Traffico e trasporti
- Grafo stradale dal region pack (corsie, sensi unici, incroci, semafori stimati).
- L1: viaggi calcolati come percorsi sul grafo con tempi dipendenti dalla congestione (modello a capacità per arco).
- L0: veicoli e pedoni visibili su corsie, semafori, precedenze, parcheggi; incidenti rari.
- Mezzi pubblici: linee disegnate dal giocatore (bus, tram, metro, treni, funivie) con fermate, frequenze, costi e incassi; le linee reali da OSM (`route=bus` ecc.) si importano come linee iniziali dove disponibili.
- Aerei ed elicotteri: voli tra aeroporti scaricati (traffico simulato a livello macro, visibile vicino agli aeroporti).

## 8. Servizi ed emergenze
Polizia, vigili del fuoco, ambulanze, rifiuti, manutenzione: ogni servizio ha copertura (raggio/tempo di arrivo), capacità e costo. Gli interventi sono eventi visibili (mezzi con sirene, incendi spenti).

### Energia
- Ogni edificio consuma elettricità (per tipo, livello e ora del giorno); le centrali la producono: **eolico** (dipende dal vento del meteo), **solare** (dall'ora e dalle nuvole), idroelettrico, termoelettrico, biomasse, geotermico, **nucleare** (molta energia costante, costoso da costruire, consenso dei cittadini da gestire).
- Bilancio rete: se la produzione non basta → acquisto di energia (costo) o **blackout** nelle zone servite peggio. Linee elettriche e sottostazioni definiscono la copertura.
- Effetti: costi, inquinamento (termoelettrico), rumore e paesaggio (eolico), felicità e valore immobiliare nelle vicinanze.

### Agricoltura
- Campi, vigneti, uliveti, frutteti, serre e fattorie producono (per stagione) grano, uva, olive, frutta, ortaggi; la produzione alimenta cantine, frantoi, mercati e supermercati e quindi l'economia della città (vino e olio d'Abruzzo).
- Le aree agricole reali arrivano da OpenStreetMap (`landuse=farmland/vineyard/orchard`).

## 9. Ambiente
Inquinamento (aria, rumore) da traffico e industrie, verde urbano, rifiuti; effetti su salute, felicità e valore immobiliare. Meteo che influenza comportamenti (con la pioggia meno gente al parco, più traffico).

## 10. Eventi
- Calendario di eventi in `data/events/*.json`: festività nazionali, eventi locali (es. Chieti: processione del Venerdì Santo, festa del patrono San Giustino — date da verificare), sagre, concerti, fiere, mercati settimanali.
- Eventi generati: inaugurazioni, scandali, record sportivi, incidenti, matrimoni famosi, ecc.
- Ogni evento ha effetti (afflusso di persone, incassi, felicità, traffico) e produce notizie.

## 11. Disastri (opzionali)
Terremoto, alluvione, incendio, blackout, nevicata eccezionale: danni agli edifici, servizi d'emergenza, ricostruzione con costi. Mai attivi senza consenso nelle impostazioni.

## 12. Determinismo e test
- Il Core usa un generatore casuale con seed per salvataggio (+ seed per sistema), così le simulazioni sono riproducibili nei test.
- Test obbligatori: bilancio economico di una città campione per 1 anno di gioco senza crash né valori assurdi (NaN, negativi impossibili), crescita/decrescita della popolazione entro limiti, conservazione del denaro (nessuna moneta creata dal nulla salvo le fonti previste).
