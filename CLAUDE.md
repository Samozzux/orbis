# CLAUDE.md — Regole del progetto Orbis

Orbis è un gioco (Unity) che unisce city-builder (stile SimCity BuildIt / Megapolis) e simulatore di vita (stile The Sims) ambientato nelle **città reali del mondo**, partendo da **Chieti**. Esce su **tre piattaforme dallo stesso codice**: app **iOS** (IPA universale iPhone + iPad), app **Android** (APK, telefoni e tablet) e **versione Web** giocabile da browser su qualsiasi dispositivo (telefono, tablet, desktop). Il proprietario del progetto è Samuele: gioca da solo per ora, installa l'IPA con Sideloadly, non usa Xcode in locale.

Prima di scrivere codice leggi, in quest'ordine:

1. `docs/00-visione.md` — cosa deve fare il gioco (tutte le feature decise)
2. `docs/01-architettura.md` — come è costruito
3. `docs/09-milestones.md` — l'ordine di lavoro e i criteri di completamento
4. `docs/STATO.md` — a che punto siamo
5. Il documento specifico del sistema su cui lavori (`docs/02` … `docs/08`)

## Modo di lavorare

- **Una milestone alla volta**, nell'ordine di `docs/09-milestones.md`. Non iniziare la successiva finché i criteri di completamento della corrente non sono soddisfatti.
- A fine sessione aggiorna **sempre** `docs/STATO.md`: cosa è stato fatto, cosa manca, problemi aperti, decisioni prese.
- Le decisioni già prese in `docs/` non si cambiano di tua iniziativa. Se una scelta si rivela impraticabile, scrivi il problema e l'alternativa proposta in `docs/STATO.md` (sezione "Decisioni da confermare") e chiedi a Samuele.
- Se una specifica è ambigua, scegli l'opzione più semplice che rispetta la visione e annotala in `docs/STATO.md`.
- Commit piccoli e descrittivi, in italiano (es. `M2: aggiunto sistema annulla/ripeti per la costruzione`).
- Ogni push su `main` deve lasciare la build verde.

## Stack fissato

| Area | Scelta |
|---|---|
| Motore | **Unity 6000.6.9f1** (Unity 6), pipeline **URP** |
| Linguaggio gioco | C# (versione supportata da Unity 6) |
| Piattaforme | **iOS** (IPA universale iPhone + iPad, iOS minimo **26.0**), **Android** (APK firmato, Android minimo **10 / API 29**, ARM64, Vulkan con fallback OpenGL ES 3) e **Web** (Unity Web, WebGPU con fallback WebGL 2) ospitata su GitHub Pages |
| Orientamento | orizzontale per il gioco; il telefono nel gioco e i menu si adattano anche in verticale |
| Input | touch (telefono/tablet) **e** mouse + tastiera (desktop), sempre entrambi supportati |
| Dispositivi di riferimento | iPhone 15 Pro (A17 Pro, 8 GB RAM); Android di fascia medio-alta con 8 GB RAM; browser desktop Chrome/Safari/Edge recenti; iPad recente |
| Pacchetti Unity | URP, Input System (EnhancedTouch), Addressables, Entities + Entities Graphics, Burst, Collections, Mathematics, Cinemachine, UI Toolkit, Localization, AI Navigation, Test Framework |
| Plugin nativi iOS | Swift in `Assets/Plugins/iOS/` (Foundation Models, Face ID/Keychain, MediaPlayer, sintesi vocale) |
| Plugin nativi Android | Kotlin in `Assets/Plugins/Android/` (IA di sistema dove disponibile, BiometricPrompt/Keystore, sintesi vocale, file audio dell'utente) |
| Backend | PHP 7.x **senza framework** + MySQL su Altervista (piano gratuito) |
| Strumenti dati | Python 3.12 (pipeline mondo, import sport), Blender 4.x headless (asset procedurali) |
| CI/CD | GitHub Actions (repo **pubblico**) |

## Struttura del repository

Il progetto Unity sta nella **radice** del repo (il `.gitignore` Unity è già configurato per questo).

```
/Assets/Orbis/            codice e contenuti del gioco (vedi docs/01)
/Assets/Plugins/iOS/      plugin nativi Swift
/Assets/Plugins/Android/  plugin nativi Kotlin
/Assets/Plugins/WebGL/    plugin JavaScript (.jslib) per la versione web
/Packages/  /ProjectSettings/
/backend/                 API PHP + pannello admin (deploy su Altervista /orbis/)
/tools/world-pipeline/    Python: OpenStreetMap → tile del mondo
/tools/sports-import/     Python: import dati sportivi (fonti aperte)
/tools/population/        Python: popolazione sintetica da dati ISTAT
/tools/blender/           script Blender per asset procedurali e monumenti
/tests/Orbis.Core.Tests/  test .NET della logica pura (eseguibili senza Unity)
/data/                    dati seed versionati (catalogo, sport, landmark, eventi)
/docs/                    specifica
/.github/workflows/       CI/CD
CREDITS.md                attribuzioni di tutte le risorse esterne
```

## Regole tecniche importanti

### Unity senza Editor
Claude Code non può aprire l'Editor Unity. Quindi:
- **Scene, prefab e asset di configurazione si generano da codice**: script Editor in `Assets/Orbis/Editor/` eseguiti in batchmode (`-executeMethod`). Non scrivere a mano file YAML di scene/prefab complessi.
- La UI si fa con **UI Toolkit** (UXML + USS, file di testo).
- Il gioco costruisce il più possibile a runtime partendo dai dati (`/data` e content pack).
- La logica di gioco pura (economia, simulazione, regole, sport, salvataggi) vive in `Assets/Orbis/Core/` con assembly definition **`noEngineReferences: true`**, così si compila e si testa con `dotnet test` su Linux senza licenza Unity.

### Tre piattaforme, un solo codice
- Tutto ciò che dipende dalla piattaforma (IA locale, login biometrico, musica utente, sintesi vocale, archiviazione, download) passa da **interfacce** in `Assets/Orbis/Platform/` con implementazioni `Ios`, `Android`, `Web` ed `Editor`. Nessuna chiamata nativa fuori da quella cartella.
- Plugin iOS in Swift (`Assets/Plugins/iOS/`), plugin Android in Kotlin (`Assets/Plugins/Android/`), plugin Web in JavaScript (`Assets/Plugins/WebGL/*.jslib`).
- Ogni funzione deve avere un **ripiego** quando la piattaforma non la offre (tabella in `docs/01-architettura.md` §10).
- La UI è **adattiva** (telefono, tablet, desktop) e ogni azione è possibile sia col touch sia con mouse/tastiera.
- Ogni milestone è completa solo se funziona **su tutte e tre** le build (IPA, APK, web).

### Test
- Logica pura: `dotnet test tests/Orbis.Core.Tests` (deve passare prima di ogni push).
- Unity: test EditMode/PlayMode nel workflow di CI.
- Backend: test PHP minimi (script `backend/tests/run.php`) per auth e API principali.

### Performance
- iPhone 15 Pro e Android di fascia medio-alta: 60 fps obiettivo in vista città, **minimo 30 fps**; 30 fps fissi ammessi in stile Realistico. Memoria app sotto **3 GB**. Su Android di fascia bassa la qualità scende automaticamente.
- Web: minimo 30 fps su desktop medio; memoria sotto **2 GB** (limiti dei browser, più bassi su iPad/telefoni); download iniziale della build web sotto **60 MB** compressi, il resto in streaming.
- La qualità si adatta automaticamente al dispositivo (profili in `docs/08-asset-e-grafica.md`).
- Nessun lavoro pesante sul main thread: simulazione su Jobs/Burst o thread dedicati.
- Caricamenti progressivi a tile: mai bloccare l'interazione per più di 100 ms.

### Backend Altervista (vincoli reali)
- PHP 7.x: **niente arrow function `fn()`**, niente `match`, niente nullsafe `?->`, niente proprietà tipizzate se la versione è < 7.4 (verificare con `/orbis/api/health`).
- MySQL compatibile 5.7: niente `NULLS LAST`, charset `utf8mb4` se supportato altrimenti `utf8`, niente tipo `JSON` (usare `MEDIUMTEXT` con JSON serializzato).
- Database unico condiviso con altri progetti: **tutte le tabelle hanno prefisso `orb_`**. Mai toccare tabelle senza questo prefisso.
- Il codice va nella cartella `/orbis/` del sito `giuliaa8.altervista.org`.
- Nessun processo in background lato server: tutta la simulazione gira sul dispositivo del giocatore.
- Le API devono rispondere con gli header **CORS** per l'origine della versione web (`https://samozzux.github.io`).

### Sicurezza
- **Mai** scrivere password, token o chiavi nel codice o nei commit. Il repo è pubblico.
- I segreti stanno nei GitHub Secrets (elenco in `docs/07-pipeline-build-deploy.md`).
- La configurazione con segreti del backend viene generata dal workflow di deploy, non è nel repo.

### Contenuti e licenze
- Usare solo risorse con licenza che ne permette l'uso (CC0, CC-BY, pubblico dominio, ODbL per OSM). Ogni risorsa CC-BY/ODbL va citata in `CREDITS.md` e nella schermata Crediti del gioco.
- Nomi reali di sportivi e squadre: ammessi (uso personale), ma sempre isolati nei **data pack** sostituibili e disattivabili con l'opzione "Nomi reali" (vedi `docs/05-sport.md`).
- Musica commerciale: mai inclusa nell'app (solo libreria libera + "La mia musica": Apple Music su iOS, file dell'utente su Android e web).
- Niente scraping di siti che lo vietano nei termini d'uso: solo fonti aperte o API che lo permettono.

## Lingua
- Testi del gioco: **solo italiano** per ora, ma sempre tramite tabelle Localization (mai stringhe hardcoded).
- Documentazione e commit: italiano.
- Nomi di classi, metodi, variabili: inglese.
- Commenti nel codice: italiano, brevi, solo dove servono.
