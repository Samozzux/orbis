# 09 — Milestone

La specifica descrive il gioco completo (versione 1.0). Si costruisce **a tappe**: ogni milestone aggiunge sistemi allo stesso gioco, che resta sempre installabile e giocabile su **iOS, Android e web**.

Regole valide per tutte le milestone:
- Criteri di completamento comuni: `core-tests` verde, build **IPA**, **APK** e **web** verdi, nessun errore in console all'avvio, `docs/STATO.md` aggiornato, `CREDITS.md` aggiornato se sono stati aggiunti asset/dati esterni.
- A fine milestone: tag di versione (`v0.<n>.0`) e breve nota per Samuele in `docs/STATO.md` con **cosa provare** sull'iPhone, su Android e sul web.
- Quando serve un'azione di Samuele (segreti, account, impostazioni GitHub/Altervista), fermarsi e dargli istruzioni passo passo semplici.

---

## M0 — Fondamenta
**Obiettivo**: progetto e pipeline funzionanti da cima a fondo.
- Progetto Unity 6000.6.9f1 + URP + pacchetti di `CLAUDE.md`, struttura cartelle e assembly di `docs/01-architettura.md` §2.
- Generatore di scene da codice; scena di avvio con logo "Orbis", schermata di caricamento, menu principale vuoto (UI Toolkit, layout adattivo).
- Progetto `tests/Orbis.Core.Tests` con un primo test.
- Interfacce `Orbis.Platform` con implementazioni Editor/Ios/Web vuote o minime.
- Workflow: `core-tests`, `ios-build` (IPA scaricabile), `android-build` (APK firmato scaricabile), `web-build` (online su GitHub Pages), `backend` (deploy + `/api/health`).
- Verifiche e annotazioni in `STATO.md`: azioni per licenza Unity in CI, valori di `/api/health`, HTTPS Altervista, **CORS dei download GitHub Releases dal web**.
- Chiedere a Samuele: `ORBIS_JWT_SECRET`, `ORBIS_ADMIN_USERNAME`, eventuale `DB_PASSWORD`, keystore Android (creazione guidata + 4 secret), attivazione GitHub Pages.

**Completata quando**: Samuele installa l'IPA e l'APK e vede il menu; apre `https://samozzux.github.io/orbis/` e vede lo stesso menu; `/orbis/api/health` risponde.

## M1 — Il mondo: Chieti
- Pipeline `tools/world-pipeline` per la **provincia di Chieti** + pack globo base.
- Caricamento tile, terreno, strade, edifici procedurali (profilo *Abruzzo*), aree verdi, acqua, alberi, POI con nomi reali.
- Camera completa (3D/2D, tutti i gesti e comandi mouse/tastiera di `docs/04` §2), zoom continuo dal globo alla strada; camera e HUD funzionano sia in verticale sia in orizzontale.
- Selezione con pannello Dettagli (dati reali OSM).
- Ciclo giorno/notte, cielo, meteo simulato base.
- Primi landmark di Chieti (almeno 3, es. Cattedrale di San Giustino, Teatro Marrucino, stadio Angelini).
- Stile **Classico**.

**Completata quando**: si naviga Chieti fluidamente (≥ 30 fps su iPhone 15 Pro, Android di fascia medio-alta e desktop), in verticale e in orizzontale, si riconoscono le strade reali e i landmark, si tocca un edificio e se ne vedono i dati.

## M2 — Costruire ed economia
- Catalogo dati (prime 150 voci tra tutte le categorie edifici/natura/decorazioni), varianti e livelli.
- Modalità costruzione: posiziona, sposta, ruota, demolisci, potenzia, varianti, strade, piani di progetto.
- Comandi + **annulla/ripeti illimitato**; **snapshot giornalieri** e **riavvolgi**.
- Tempo di gioco con velocità, pausa, tempo ad app chiusa con riepilogo.
- Economia del proprietario: affitti, suolo pubblico, tasse, manutenzione, bilancio, cassa; schermata finanze.
- Salvataggi locali (SQLite / IndexedDB), più salvataggi, configurazione del mondo.
- XP, livelli, prime 20 missioni.

**Completata quando**: si gioca una partita stile Megapolis su Chieti per un mese di gioco, guadagnando e spendendo, con annulla e riavvolgi funzionanti; chiudendo e riaprendo tutto è com'era.

## M3 — Account, cloud e amici
- Backend: migrazioni, auth completa (token, rotazione, limiti), Face ID (iOS), sblocco biometrico (Android), passkey o ripiego (web), CORS.
- Backup e sincronizzazione salvataggi con conflitti; stesso salvataggio su iPhone e web.
- Amici, visita della città di un amico, progetti condivisi, mi piace.

**Completata quando**: Samuele crea l'account, entra con Face ID, gioca sull'iPhone e continua la stessa partita dal browser del Mac.

## M4 — Cittadini e città viva
- Popolazione sintetica di Chieti da dati ISTAT/MEF.
- Simulazione a livelli L0–L3, routine, bisogni, inventario (frigo), reddito, lavoro, ciclo di vita.
- Traffico (L1 + L0 visibile), pedoni, mezzi pubblici base (bus da OSM + linee disegnabili).
- Servizi ed emergenze, felicità con mappe di calore, reazioni dei cittadini (3 livelli), eventi e feste dal calendario, meteo reale (Open-Meteo).
- Attività commerciali con conti reali.

**Completata quando**: la città si muove da sola con migliaia di cittadini, ognuno ispezionabile (casa, lavoro, soldi, frigo), e le scelte del sindaco cambiano felicità ed economia.

## M5 — Interni e prima persona
- Generatori di interni (case per fascia di reddito, supermercato, bar/ristorante, negozi, uffici, scuola, ospedale, stazione).
- Vista "casa delle bambole" e prima persona; camminare in città ed entrare negli edifici.
- Contenuti degli interni legati alla simulazione (frigo, scaffali, clienti, casse).
- Guida di veicoli (auto, moto, bus) e uso dei mezzi pubblici.
- Modalità foto.

**Completata quando**: si entra in qualsiasi casa di Chieti trovando interni diversi e coerenti con la famiglia, e nel supermercato si vedono clienti e incassi reali.

## M6 — Modalità cittadino (simulatore di vita)
- Creazione personaggio, famiglia, animali; bisogni e azioni stile Sims; dialoghi **standard**.
- Carriere, lavoro, attività propria, immobiliare, acquisto e modifica di case e arredi.
- Telefono nel gioco con le app; diario di vita.
- Possesso a scelta; **cambio ruolo** libero; sindaco a regole quando si è cittadini; petizioni, voto, elezioni.

**Completata quando**: si può vivere a Chieti da cittadino, lavorare, comprare casa e arredarla, aprire un bar, candidarsi sindaco e, vincendo, passare alla gestione della città.

## M7 — IA, media e musica
- Plugin Swift **Foundation Models** + interfaccia; IA di sistema Android sui dispositivi compatibili; modello locale del browser se disponibile; ripieghi.
- Dialoghi **avanzati IA**, sindaco IA, giornale, social "Piazza", radio con DJ e voce sintetica.
- Sistema musica: libreria libera, playlist per contesto, "La mia musica" (Apple Music su iOS, file utente su Android e web), impostazioni.

**Completata quando**: sull'iPhone 15 Pro si parla liberamente con un NPC che ricorda i fatti della simulazione, e il giornale racconta gli eventi reali della partita.

## M8 — Sport
- Import una tantum dai dati aperti (`tools/sports-import`), generazione attributi, pack nomi reali/generati.
- Pannello admin: gestione contenuti sport + **import massivo** (necessario per i campionati dilettantistici).
- Motori: calcio, tennis, basket, pallavolo (dedicati) → poi tutti gli altri sport dell'elenco di `docs/05-sport.md` (dedicati semplificati o generico).
- Competizioni con calendari, classifiche, tabelloni; vita propria (mercato, crescita, ritiri, giovani); Olimpiadi.
- Visualizzazione 3D: calcio, tennis, basket, pallavolo complete; gli altri con vista semplificata in un primo momento.
- Ruoli: tifoso, atleta, allenatore, proprietario; giorni di partita in città; fanta; figurine.

**Completata quando**: tutti gli sport hanno stagioni che avanzano da sole, si guarda una partita di Serie A in 3D allo stadio, si allena un tennista e si possiede una squadra.

## M9 — Progressione e contenuti
Scenari e sfide, obiettivi, achievement, livelli completi, eventi casuali e macroeconomici, disastri, modalità creativa, editor modulare di edifici, terraforming completo, partita "da zero", time-lapse, catalogo esteso verso gli obiettivi di `docs/08` §2.

## M10 — Italia e mondo
Region pack di tutte le regioni italiane, download manager completo (background, priorità, gestione spazio), profili architettonici per aree del mondo, pack dei paesi su richiesta, landmark Italia (100) e mondo (300), aeroporti e voli, viaggi tra città, partita "storica" per Chieti.

## M11 — Stili grafici completi
Stile **Realistico** e stile **Pixel** completi, cambio a runtime, profili di qualità automatici per iOS, Android e web, ottimizzazioni finali.

## M12 — Amministrazione completa e rifinitura
Pannello admin completo per tutti i pack (catalogo, landmark, eventi, scenari, musica, testi), menu sviluppatore nel gioco, accessibilità, bilanciamento economico, test di lunga durata (1 anno di gioco), pulizia e **v1.0.0**.
