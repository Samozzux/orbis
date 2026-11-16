# Stato del progetto

## Milestone attuale
**M0 — Fondamenta**: codice completo e verificato in locale e in CI sul branch `m0-fondamenta`. Mancano le azioni di Samuele (segreti, keystore, GitHub Pages: vedi "Cosa deve fare Samuele"), poi l'unione in `main`, il deploy (web + backend) e la prova sui dispositivi.

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

## Verifiche M0
- **Test locali**: `dotnet test` 42/42; Unity EditMode 7/7, PlayMode 1/1 (avvio fino al menu senza avvisi né errori in console); backend 17/17 senza DB.
- **CI su `m0-fondamenta`**: tutte verdi. Core tests; Backend 21/21 su PHP 7.0.33 e 8.3 con MySQL 5.7; **Android** → `Orbis-apk` 42,7 MB (19 min); **iOS** su `macos-26` con Xcode 26 → `Orbis-ipa` 54,5 MB (20 min); **Web** → 15,2 MB compressi (27 min, limite 60 MB). Le tre build girano in parallelo senza problemi di licenza.
- **Prova della build web in locale** (browser integrato, Chrome 156): WebGPU attivo, testi caricati da Localization (Addressables), logo → caricamento → menu, avviso "in arrivo" su clic/tap e con Invio; layout verificato a 1024×768 (desktop/tablet), 852×393 (telefono orizzontale) e 393×852 (telefono verticale, anche ridimensionando dal vivo). Console senza errori né avvisi; restano solo messaggi informativi di URP sugli shader di post-processing non inclusi (si sistemano in M1 con i profili di stile).
- **Bug trovato dalla CI**: le impostazioni di Localization create da Unity contenevano un selettore di lingua fisso su "en". Sul Mac (sistema in italiano) funzionava, ma su dispositivi o browser in inglese il gioco avrebbe mostrato le chiavi al posto dei testi. Ora il generatore imposta sempre l'italiano, `Loc` ha un ripiego e un test EditMode lo controlla.
- **Nota tecnica**: un clic sintetico con pressione e rilascio nello stesso istante (come quello degli strumenti di automazione) non viene registrato da UI Toolkit; i clic e i tap reali sì (verificato con una pressione di 150 ms).
- **Una build Unity alla volta** (docs/07 §2): tutti i job Unity (iOS, Android, Web, test) sono nel gruppo di concorrenza `unity-license` con `queue: max` (funzione di GitHub del maggio 2026): aspettano in coda senza essere annullati. Prima erano in parallelo e una build Android è stata terminata dal sistema (codice 137) mentre partivano tre build insieme con lo stesso account Personal. Una push completa ora richiede circa 70 minuti di build in sequenza.
- **Licenza Unity in CI**: `buildalon/unity-setup@v3` (installa 6000.6.9f1 dal `ProjectVersion.txt`, moduli per piattaforma) + `buildalon/activate-unity-license@v3` con `license: personal` e `UNITY_EMAIL`/`UNITY_PASSWORD`. Il secret `UNITY_LICENSE` per ora non serve (resta come riserva). Sui runner GitHub macOS/Linux l'azione **non restituisce** la licenza Personal a fine job, per scelta: le macchine condividono la stessa identità e restituirla revocherebbe la licenza agli altri job.
- **HTTPS Altervista**: attivo (certificato valido su `https://giuliaa8.altervista.org`).
- **FTP Altervista**: accetta TLS esplicito; il certificato è per `*.altervista.org` e non copre `ftp.giuliaa8.altervista.org`, quindi il deploy usa FTPS in modalità "loose" (cifrato, senza controllo del nome).
- **`/api/health`**: _da leggere dopo il primo deploy su `main`_ (versione PHP/MySQL, estensioni, limiti).
- **CORS dei download GitHub Releases dal web** (verificato il 15/11/2026): **non utilizzabili**. `github.com/.../releases/download/...` risponde 302 senza `Access-Control-Allow-Origin`; anche passando da `api.github.com` (che ha CORS) il redirect finale su `release-assets.githubusercontent.com` risponde senza header CORS. GitHub Pages invece risponde con `Access-Control-Allow-Origin: *`. → vedi "Decisioni da confermare".

## Decisioni prese
- Motore Unity (non Unreal) per compilare IPA, APK e web gratis su GitHub Actions.
- Piattaforme: iOS (iPhone + iPad), Android (APK) e Web (GitHub Pages).
- Dati sportivi da fonti aperte, niente scraping vietato.
- Tempo predefinito: 1 ora di gioco = 1 minuto reale.
- UI: ultra moderna, minimal, **liquid glass** stile iOS 26 per bottoni, navbar e pannelli (Samuele, 15/11/2026). Componenti riusabili in `Assets/Orbis/UI/Runtime/Glass` (`GlassSurface`, `GlassBackdrop`).
- Testi nei file UXML come chiavi `#chiave` sostituite all'avvio dalla tabella Localization "UI" (scelta più semplice e verificabile da test: un test controlla che ogni chiave usata esista).
- Classi di layout: lato corto < 500 pt → telefono; lato lungo ≥ 1280 pt e lato corto ≥ 700 pt → desktop; altrimenti tablet (scelta semplice, da rivedere se serve).
- Prima del Keychain/Keystore (M3) `ISecureStore` è solo in memoria; salvataggi su file (iOS/Android/Editor) e in memoria sul web fino a M2 (SQLite / IndexedDB).
- Database di produzione: `my_<FTP_USER>` / utente `<FTP_USER>` / host `localhost` (convenzione Altervista), password vuota salvo secret `DB_PASSWORD`.

## Decisioni da confermare
1. **Hosting dei dati pesanti per il web** (CORS GitHub Releases negativo). Proposta: (a) per M1 il pack di Chieti e il globo base, inclusi nelle app, sul web si servono da **GitHub Pages** insieme al gioco (stessa origine, nessun problema CORS; limiti: sito ≤ 1 GB, file ≤ 100 MB); (b) per Italia/mondo (M10) **Cloudflare R2** (piano gratuito 10 GB, traffico in uscita gratuito, richiede un account Cloudflare di Samuele), alimentato dallo stesso workflow. Le app iOS/Android continuano a scaricare dalle GitHub Releases. Nessun account creato.
2. **.NET dei test**: .NET 8 è fuori supporto dal 10/11/2026. Funziona, ma propongo di passare il progetto di test a **.NET 10 (LTS)**.
3. **Riferimenti tra assembly**: `Orbis.UI` usa anche `Orbis.Platform` (per profilo del dispositivo e servizi), non solo Core e Gameplay come nella tabella di `docs/01` §2.

## Problemi aperti
- **Password del database (`DB_PASSWORD`)**: Samuele ne ha impostata una, ma su Altervista di solito il database non ha password e una password sbagliata impedisce l'accesso. Si verifica al primo deploy con `/api/health` (`database.connected`); se fallisce, basta cancellare il secret.
- Il modulo **iOS Build Support** non è installato sul Mac: non serve (l'IPA si compila in CI), ma senza non si può provare la build iOS in locale.
- Fino a quando non c'è il keystore, l'APK di CI è firmato con una chiave di debug: quella versione andrà disinstallata prima di installare l'APK firmato.

## Cosa deve fare Samuele (M0)
Una sola volta, dal Terminale del Mac nella cartella del progetto:
```
bash tools/setup/configura-github.sh
```
Lo script chiede conferma passo per passo e: crea `ORBIS_JWT_SECRET` (casuale, mai mostrato); imposta la variabile `ORBIS_ADMIN_USERNAME` (chiede lo username); chiede se Altervista mostra una password del database (di solito no → Invio); crea il keystore Android in `~/Documents/Orbis-chiavi/` (chiede una password) e carica i 4 secret `ANDROID_*`; attiva GitHub Pages con sorgente "GitHub Actions". **Dopo**: copiare la cartella `~/Documents/Orbis-chiavi/` in un posto sicuro e salvare la password del keystore nel gestore di password.

Alternativa senza script per Pages: GitHub → repository `orbis` → **Settings** → **Pages** → **Source: GitHub Actions**.

## Cosa provare (fine M0)
- **Web**: aprire `https://samozzux.github.io/orbis/` da Mac, iPhone (anche in verticale) e iPad → logo animato, caricamento, menu con bottoni di vetro e navbar; toccare una voce → avviso "In arrivo nelle prossime versioni"; su Mac anche Tab/frecce + Invio.
- **iPhone**: scaricare `Orbis-ipa` dall'ultima esecuzione verde di "iOS build" e installarlo con Sideloadly (istruzioni nel README) → stesso menu, in orizzontale, rispettando la Dynamic Island.
- **Android**: scaricare `Orbis-apk` dall'ultima esecuzione verde di "Android build" e installarlo (istruzioni nel README).
- **Backend**: aprire `https://giuliaa8.altervista.org/orbis/api/health` → JSON con `"status": "ok"`.

## Registro sessioni
### 15/11/2026 — M0 Fondamenta
- **Fatto**: tutto il codice di M0 (vedi sopra) sul branch `m0-fondamenta`, tutte le pipeline verdi (core, backend, IPA, APK, web), build web provata in locale. Su richiesta di Samuele l'interfaccia è in stile liquid glass iOS 26.
- **Corretto durante la prova**: l'avviso "in arrivo" non compariva (elemento cercato nel posto sbagliato; sul web i riferimenti nulli non danno errore) → ora gli elementi mancanti fanno fallire subito l'avvio con un messaggio chiaro e il test PlayMode preme davvero "Nuova partita". Il vetro era troppo "lattiginoso" (spazio colore lineare) → sfocatura resa più viva e velature più leggere.
- **Resta**: azioni di Samuele (sopra); unione in `main` con primo deploy di web e backend; lettura dei valori di `/api/health`; prova sui dispositivi; tag `v0.0.0`.
