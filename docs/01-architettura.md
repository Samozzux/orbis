# 01 — Architettura

## 1. Panoramica

```
┌─────────────────────── Dispositivo (iOS, Android o browser) ───────────────────────┐
│                                                                                      │
│  Presentazione (Unity)                                                               │
│   ├─ Rendering mondo (tile, edifici procedurali, landmark, veicoli, persone)         │
│   ├─ Camera e input (touch + mouse/tastiera)                                         │
│   ├─ UI Toolkit (HUD, menu, telefono nel gioco, editor)                              │
│   └─ Audio (musica, ambiente, radio/TTS)                                             │
│                    ▲ legge stato / invia comandi                                     │
│  Orbis.Core (C# puro, nessun riferimento a UnityEngine, testato con dotnet test)     │
│   ├─ Tempo e scheduler        ├─ Economia           ├─ Cittadini e famiglie          │
│   ├─ Comandi + annulla/ripeti ├─ Edifici e catalogo ├─ Sport (motori di simulazione) │
│   ├─ Salvataggi + snapshot    ├─ Eventi e scenari   ├─ Politica ed elezioni          │
│   └─ Generazione procedurale (interni, popolazione, nomi)                            │
│                    ▲                                                                 │
│  Orbis.Sim.Jobs (Burst/Jobs/Entities): agenti, traffico, pathfinding su larga scala  │
│                    ▲                                                                 │
│  Orbis.Platform (interfacce) → Ios (Swift) | Android (Kotlin) | Web (.jslib) | Editor│
│                    ▲                                                                 │
│  Archiviazione locale: SQLite (iOS/Android) / IndexedDB (Web) + cache tile e asset   │
└──────────────┬───────────────────────────────────────┬───────────────────────────────┘
               │ HTTPS (API JSON)                       │ HTTPS (file statici)
               ▼                                        ▼
   Altervista giuliaa8.altervista.org/orbis       Hosting statico dati pesanti
   ├─ api/   (auth, profili, amici, cloud save,   ├─ tile del mondo (region pack)
   │          content pack, dati sport seed)      ├─ asset pack (Addressables)
   └─ admin/ (pannello web + import massivo)       └─ build web (GitHub Pages)
       MySQL (tabelle orb_*)
```

## 2. Assembly e cartelle in `Assets/Orbis/`

| Cartella / assembly | Contenuto | Riferimenti |
|---|---|---|
| `Core/` → `Orbis.Core` | logica pura: modelli, regole, economia, sim cittadini ad alto livello, sport, comandi, salvataggi, generatori | nessun UnityEngine (`noEngineReferences: true`) |
| `Sim/` → `Orbis.Sim` | sistemi Entities/Jobs/Burst: agenti visibili, traffico, folle, pathfinding | Core, Unity.Entities, Burst, Mathematics |
| `World/` → `Orbis.World` | caricamento tile, generazione mesh di edifici/strade/terreno, landmark, LOD, streaming | Core, Sim |
| `Gameplay/` → `Orbis.Gameplay` | modalità, ruoli, costruzione, selezione, interni, prima persona, veicoli guidabili | Core, World, Sim |
| `UI/` → `Orbis.UI` | UXML/USS e controller, layout adattivi, telefono nel gioco | Core, Gameplay |
| `Sports/` → `Orbis.Sports.View` | visualizzazione 3D delle partite, telecamere TV, telecronaca | Core, World |
| `Audio/` → `Orbis.Audio` | musica, ambiente, radio | Core, Platform |
| `Net/` → `Orbis.Net` | client API, sincronizzazione, download manager | Core, Platform |
| `Platform/` → `Orbis.Platform` | interfacce + implementazioni Ios/Android/Web/Editor | Core |
| `Rendering/` → `Orbis.Rendering` | profili stile (Realistico/Classico/Pixel), post-processing, qualità | — |
| `Editor/` → `Orbis.EditorTools` | generatori di scene/prefab, build script, import dati | tutto (solo Editor) |
| `Tests/` | test EditMode/PlayMode | — |

`tests/Orbis.Core.Tests/` (fuori da Assets) è un progetto .NET 8 che include i file di `Assets/Orbis/Core/**/*.cs` tramite glob e li testa con xUnit.

## 3. Principio "dati prima di tutto"
- Tutti i contenuti (catalogo oggetti, varianti, livelli, prezzi, sport, eventi, scenari, missioni, landmark, testi) sono **dati** (JSON) caricati a runtime, non codice.
- Fonte: `data/` nel repo (seed) → importati nel DB Altervista → distribuiti come **content pack** versionati (JSON compresso) che l'app scarica e conserva offline. L'editor admin modifica il DB e pubblica un nuovo content pack.
- Il gioco funziona sempre con l'ultimo content pack in cache, o con quello incluso nella build.

## 4. Tempo e scheduler
- Orologio di gioco con velocità configurabile (predefinito: 1 ora di gioco = 1 minuto reale) e pausa, x2, x4, x10.
- Scheduler a eventi (coda di priorità per timestamp di gioco) per tutto ciò che accade a orari precisi: turni di lavoro, partite, eventi, scadenze di affitti/tasse, manutenzione.
- Tick di simulazione a frequenze diverse per livello di dettaglio (§6).
- **Tempo ad app chiusa**: alla riapertura si calcola il tempo trascorso (fino al limite configurato) e si esegue una simulazione **riassuntiva** (statistica, a passi di 1 ora di gioco) con un riepilogo: incassi, eventi, risultati sportivi, notizie.

## 5. Comandi, annulla/ripeti, riavvolgi
- Ogni azione del giocatore è un **Command** (`Execute`, `Undo`, serializzabile): costruire, demolire, spostare, ruotare, potenziare, cambiare variante, arredare, comprare/vendere, cambiare tasse, disegnare linee di trasporto, ecc.
- Pila annulla/ripeti **illimitata** e persistente (resta anche chiudendo l'app), per salvataggio.
- Un comando annullato ripristina anche gli effetti economici diretti (costi rimborsati), non le conseguenze simulate nel frattempo (quelle si gestiscono col riavvolgimento).
- **Snapshot automatici** a ogni mezzanotte di gioco: si conservano gli ultimi 30 giorni di gioco + 1 a settimana per 6 mesi di gioco (configurabile). "Riavvolgi" carica uno snapshot e scarta ciò che è successo dopo (con conferma).
- Snapshot = stato serializzato compresso (MessagePack + zstd/Brotli) differenziale rispetto al precedente quando possibile.

## 6. Simulazione a livelli di dettaglio (LOD)
Ogni cittadino esiste **sempre** come record persistente (identità, famiglia, casa, lavoro, reddito, risparmi, bisogni, inventario, relazioni, tratti, ricordi). Cambia solo quanto finemente viene simulato:

| Livello | Dove | Cosa si simula | Frequenza |
|---|---|---|---|
| **L0 — Dettaglio pieno** | vicino alla camera / dentro un edificio / in prima persona | agente 3D animato, navigazione, interazioni con oggetti, dialoghi | ogni frame (Entities) |
| **L1 — Routine** | quartieri caricati | spostamenti come "viaggi" sul grafo stradale, attività con durata, consumi, acquisti | ogni 5 min di gioco |
| **L2 — Statistico** | resto della città | aggregati per famiglia/isolato: reddito, consumi, umore, eventi probabilistici | ogni ora di gioco |
| **L3 — Macro** | altre città scaricate non attive | indicatori di città (popolazione, economia, sport) | ogni giorno di gioco |

Il passaggio tra livelli è trasparente: quando la camera si avvicina, i cittadini di un'area passano da L2/L1 a L0 nella posizione coerente con la loro attività.
Budget indicativi: L0 fino a 2.000 agenti su iPhone 15 Pro / desktop, 500 su browser deboli; L1 fino a 100.000.

## 7. Archiviazione locale
- **iOS / Android**: SQLite (libreria `sqlite-net` o equivalente; su iOS la `libsqlite3` di sistema, su Android la libreria nativa inclusa) in `Application.persistentDataPath`.
- **Web**: stesso livello di astrazione su **IndexedDB** (Unity mappa `persistentDataPath` su IndexedDB; per dati grandi usare un `.jslib` dedicato con IndexedDB diretto).
- Interfaccia unica `ISaveStore` in Core; implementazioni per piattaforma.
- Contenuto di un salvataggio: impostazioni del mondo, stato città (edifici modificati rispetto ai dati reali, proprietà, economia), cittadini, aziende, sport (stagioni, risultati, rose), giornale/diario, journal comandi, snapshot.
- Il mondo reale non modificato non si salva: si rigenera dai tile. Si salvano solo le **differenze** (edifici aggiunti/rimossi/modificati, interni generati e personalizzati).

## 8. Rete
- `ApiClient` con token di accesso (1 ora) + token di rinnovo (90 giorni, ruotato a ogni uso). Vedi `docs/06-backend-api.md`.
- Tutto funziona offline; le operazioni di rete (backup, amici, content pack) vanno in coda e ripartono quando c'è connessione.
- `DownloadManager`: scarica region pack, asset pack e content pack in background con priorità (zona attuale > zone vicine > Italia > mondo), ripresa dei download interrotti, verifica hash, limite di spazio configurabile, solo Wi-Fi opzionale (iOS).

## 9. IA sul dispositivo
- Interfaccia `ILocalLanguageModel` in Platform: `IsAvailable`, `GenerateAsync(prompt, schema, options)`, generazione guidata con schema JSON per output strutturati (decisioni del sindaco, articoli, dialoghi).
- **iOS**: framework **Foundation Models** (iOS 26, Apple Intelligence; iPhone 15 Pro supportato) tramite plugin Swift con funzioni `@_cdecl` e callback verso C#.
- **Android**: IA di sistema sui dispositivi compatibili (es. Gemini Nano tramite le API GenAI di ML Kit), tramite plugin Kotlin; verificare in M7 API e dispositivi supportati.
- **Web**: se il browser espone un modello locale (es. API di IA integrate nel browser), usarlo tramite `.jslib`; altrimenti `IsAvailable = false`.
- **L'intelligenza degli NPC non dipende dal modello linguistico**: routine, decisioni (IA a utilità), memoria, relazioni, sindaco a regole e reazioni sono sempre calcolati dal Core, identici su tutti i dispositivi. Il modello linguistico serve solo a *trasformare in testo libero* ciò che la simulazione ha deciso (dialoghi liberi, articoli, discorsi, DJ).
- Ripiego sempre presente: dialoghi standard a menu + testi generati da **modelli testuali** (template con variabili e varianti casuali) per notizie, social, DJ e sindaco (sindaco a regole).
- Le richieste all'IA girano in coda a bassa priorità, mai sul percorso critico del frame; risultati salvati in cache (un articolo generato non si rigenera).

## 10. Servizi di piattaforma e ripieghi

| Servizio (`Orbis.Platform`) | iOS | Android | Web | Ripiego |
|---|---|---|---|---|
| `ILocalLanguageModel` | Foundation Models | IA di sistema (es. Gemini Nano via ML Kit GenAI) sui dispositivi compatibili | modello locale del browser, se presente | template + regole |
| `IBiometricAuth` | Face ID + Keychain (`SecAccessControl .biometryCurrentSet`) | BiometricPrompt + Android Keystore | **Passkey** WebAuthn (Face ID/Touch ID/Windows Hello) | sessione lunga con token di rinnovo |
| `ISecureStore` | Keychain | Android Keystore (EncryptedSharedPreferences) | IndexedDB (token cifrato con chiave non esportabile WebCrypto) | — |
| `ISpeech` (voce DJ) | AVSpeechSynthesizer (voce italiana) | TextToSpeech (italiano) | Web Speech API `speechSynthesis` | solo testo a schermo |
| `IUserMusic` ("La mia musica") | MediaPlayer `MPMusicPlayerController.applicationQueuePlayer` | file audio del dispositivo (selettore di file / MediaStore) | file audio scelti dall'utente, salvati in IndexedDB | solo libreria inclusa |
| `ISaveStore` | SQLite | SQLite | IndexedDB | — |
| `IHaptics` | UIImpactFeedbackGenerator | Vibrator / HapticFeedback | Vibration API se presente | niente |
| `INotifications` (locali) | UNUserNotificationCenter | NotificationManager (permesso POST_NOTIFICATIONS) | Notification API (con permesso) | niente |
| `IDeviceProfile` | modello, RAM, stato termico | modello, RAM, GPU, stato termico | WebGPU/WebGL, memoria stimata, prestazioni misurate | profilo medio |

Chiavi Info.plist necessarie: `NSFaceIDUsageDescription`, `NSAppleMusicUsageDescription`, `NSPhotoLibraryAddUsageDescription` (salvare foto della modalità foto). Il GPS non serve.
Permessi Android: `INTERNET`, `USE_BIOMETRIC`, `POST_NOTIFICATIONS`, `VIBRATE`, `READ_MEDIA_AUDIO` (solo per "La mia musica", richiesto al momento dell'uso). Niente posizione.

## 11. Rendering e stili grafici
- URP con tre **profili di stile** commutabili a runtime (dettagli in `docs/08-asset-e-grafica.md`):
  - **Realistico**: materiali PBR, ombre di qualità, riflessi, nebbia atmosferica, post-processing (tonemapping, bloom, AO).
  - **Classico**: materiali stilizzati luminosi, ombre morbide, colori saturi, contorni leggeri.
  - **Pixel**: rendering su RenderTexture a bassa risoluzione + quantizzazione palette + contorni, nessun asset aggiuntivo.
- Ogni asset ha **due set di materiali** (Realistico e Stilizzato); il Pixel usa lo Stilizzato.
- GPU instancing / Entities Graphics per edifici ripetuti, alberi, veicoli, persone; impostor (billboard) per gli edifici lontani; LOD a 3–4 livelli.
- Su web: WebGPU dove disponibile, altrimenti WebGL 2 con profilo ridotto (meno ombre, niente effetti costosi).

## 12. Input e UI adattiva
- Input System con mappe: `Touch`, `MouseKeyboard`, `Gamepad` (opzionale). Ogni azione ha almeno un comando touch e uno mouse/tastiera.
- Layout UI per classi di schermo: **compatto** (telefono orizzontale), **medio** (tablet), **ampio** (desktop: pannelli laterali fissi, più informazioni contemporaneamente). Si sceglie dalla dimensione reale in punti/pollici, non dalla piattaforma.
- Safe area rispettata su iPhone (Dynamic Island) e iPad.
- Dimensioni minime dei bersagli touch 44 pt; tooltip al passaggio del mouse su desktop.

## 13. Hosting dei dati pesanti
- **Build web**: GitHub Pages (`https://samozzux.github.io/orbis/`), compressione Brotli/gzip con *decompression fallback* di Unity (GitHub Pages non permette header personalizzati).
- **Tile del mondo e asset pack**: pubblicati come **GitHub Releases** (file fino a 2 GB, nessun costo). Le app iOS e Android li scaricano direttamente.
- **Attenzione CORS per il web**: i browser devono poter scaricare i file da un'altra origine. In **M0 verificare** se i download delle GitHub Releases rispondono con header CORS utilizzabili da `samozzux.github.io`. Se non li hanno, la versione web usa un hosting statico gratuito con CORS (proposta: **Cloudflare R2** piano gratuito, 10 GB, traffico in uscita gratuito; richiede un account Cloudflare di Samuele) alimentato dallo stesso workflow. Annotare la decisione in `docs/STATO.md` e chiedere conferma a Samuele prima di creare account.
- Il **manifest** (`manifest.json`: elenco pack, versioni, hash, dimensioni, URL) è pubblicato insieme ai pack; l'app conosce solo l'URL del manifest.

## 14. Sicurezza e privacy
- Password solo come hash (bcrypt `password_hash`) lato server.
- Token di rinnovo salvati solo come hash nel DB; in chiaro solo sul dispositivo, in archivio sicuro.
- Nessun dato personale reale dei cittadini virtuali: i nomi dei cittadini sono generati; i nomi reali sono usati solo per sportivi e luoghi pubblici.
- HTTPS obbligatorio per API e download.
