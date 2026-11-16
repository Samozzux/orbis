# Stato del progetto

## Milestone attuale
**M1 — Il mondo: Chieti**: in corso (iniziata il 16/11/2026).

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

### Avanzamento M1 (sessione del 16/11/2026)
- **Fatto e unito in `main` (PR #3)**: formato dei tile (FlatBuffers + Deflate, decompressore C# puro), lettore C#, pipeline Python con test, workflow *World data*.
- **Sul ramo `m1-mondo` (compila in Unity, 92 test .NET verdi, non ancora in PR)**:
  - mondo in Unity a tile con 3 livelli di dettaglio (thread su app, a passi sul web);
  - edifici sulla sagoma reale: tetti calcolati come "minimo di piani", quindi mai fuori dall'impronta; facciate con finestre, persiane, portoni e vetrine disegnate dallo shader; coppi; luci di notte;
  - strade con marciapiedi e segnaletica, ponti; colture disegnate sul terreno; uliveti e frutteti a sesto, vigneti a filari, boschi;
  - mare e terreno lontano (orizzonte a 50 km);
  - cielo con sole reale per Chieti, nuvole, pioggia, nebbia, meteo simulato;
  - telecamera touch/mouse/tastiera e vista 2D;
  - HUD con orologio, meteo e velocità; pannello Dettagli con i dati OSM;
  - vista dei lotti con la griglia da 1 m e le celle occupate;
  - generatori per Cattedrale, Teatro Marrucino e stadio; test di foto (`WorldSnapshotTests`).
- **Pack di Chieti**: il primo tentativo è fallito (confine non trovato con `ref=CH`). È stato corretto sul ramo `m1-pipeline-fix` ed è ripartito (run 43924869628, in corso alla chiusura). Questo pack non ha ancora il terreno lontano: serve una v2 dopo l'unione di `m1-mondo`.
- **Da fare**:
  - controllare il pack e aggiornare `data/world/packs.json`;
  - le build devono scaricare il pack in StreamingAssets, e il controllo dei 60 MB del web va fatto escludendo i tile;
  - foto di prova con la GPU e correzioni grafiche;
  - globo base con passaggio continuo;
  - prestazioni su iPhone e web; PR, prova di Samuele, tag `v0.1.0`.
- **Decisione (ambiguità risolta)**: le quote del terreno usano `base + valore×passo` invece di `int16` in cm, che non arriva ai 2.793 m della Majella.

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
0. **Compressione dei tile: Deflate invece di zstd** (`docs/02` §2 punto 6). zstd su iOS, Android e soprattutto web richiederebbe una libreria nativa o codice non sicuro in C#; Deflate è già incluso in .NET/Unity su tutte le piattaforme e comprime poco meno. Proposta: Deflate ora, zstd solo se servirà davvero.
1. **Hosting dei dati pesanti per il web** (CORS GitHub Releases negativo). Proposta: (a) per M1 il pack di Chieti e il globo base, inclusi nelle app, sul web si servono da **GitHub Pages** insieme al gioco (stessa origine, nessun problema CORS; limiti: sito ≤ 1 GB, file ≤ 100 MB); (b) per Italia/mondo (M10) **Cloudflare R2** (piano gratuito 10 GB, traffico in uscita gratuito, richiede un account Cloudflare di Samuele), alimentato dallo stesso workflow. Le app iOS/Android continuano a scaricare dalle GitHub Releases. Nessun account creato.
2. **.NET dei test**: .NET 8 è fuori supporto dal 10/11/2026. Funziona, ma propongo di passare il progetto di test a **.NET 10 (LTS)**.
3. **Versione PHP**: Altervista usa **PHP 8.0.30**, non 7.x. Le regole di `CLAUDE.md` (niente `fn()`, `match`, `?->`, proprietà tipizzate) restano rispettate e i test girano anche su PHP 7.0. Proposta: alzare il minimo a PHP 8.0 (si potrebbero usare `match`, `?->`, proprietà tipizzate), oppure lasciare tutto com'è per sicurezza. Non urgente.
4. **Riferimenti tra assembly**: `Orbis.UI` usa anche `Orbis.Platform` (per profilo del dispositivo e servizi), non solo Core e Gameplay come nella tabella di `docs/01` §2.

## Problemi aperti
- **Unity terminato dal sistema in CI (codice 137)**: due volte (build Android del 15/11 e build web del 16/11) Unity è stato ucciso un secondo dopo l'avvio, sempre con la cartella `Library` ripristinata dalla cache; un'altra build con cache invece è andata bene, e la build web era sola in coda (non è un conflitto di licenza). Causa non ancora certa. Contromisura: `.github/scripts/run-unity.sh` (usato da tutti i job Unity) in quel caso registra memoria e messaggi del kernel, cancella `Library` e riprova una volta da zero. Se la diagnostica indicherà la causa, si correggerà alla radice. Prima prova dopo la correzione (16/11, commit `c16787b`): iOS, web e Android verdi senza bisogno del nuovo tentativo.
- Il modulo **iOS Build Support** non è installato sul Mac: non serve (l'IPA si compila in CI), ma senza non si può provare la build iOS in locale.
- Gli APK compilati prima del 15/11 alle 23:38 sono firmati con una chiave di debug: se ne hai installato uno, va disinstallato prima di installare quello firmato con la chiave di Orbis.

## Cosa deve fare Samuele
- ✅ Fatto il 15/11: script `tools/setup/configura-github.sh` (JWT, admin, keystore Android, GitHub Pages); tolta la password del database.
- ✅ Fatto il 16/11: Stati Uniti aggiunti tra le nazioni FTP consentite su Altervista.
- ⏳ **Provare sui dispositivi** (vedi sotto) e dire a Claude com'è andata: poi tag `v0.0.0` e si passa a M1.

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
