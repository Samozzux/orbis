# 07 — Pipeline: build, deploy, dati

Tutto gira su **GitHub Actions** (repo pubblico → minuti gratuiti, anche macOS). Nessuno strumento in locale è obbligatorio per Samuele: scarica l'IPA e lo installa con Sideloadly; la versione web si apre dal browser.

## 1. Segreti e variabili

### Già creati (Settings → Secrets and variables → Actions → Secrets)
| Nome | Uso |
|---|---|
| `UNITY_EMAIL` | attivazione licenza Unity Personal in CI |
| `UNITY_PASSWORD` | attivazione licenza Unity Personal in CI |
| `UNITY_LICENSE` | contenuto di `~/Library/Unity/licenses/UnityEntitlementLicense.xml` (riserva, vedi §3) |
| `FTP_HOST` | `ftp.giuliaa8.altervista.org` |
| `FTP_USER` | `giuliaa8` |
| `FTP_PASSWORD` | password FTP Altervista |

### Da aggiungere in M0 (Claude Code deve chiederlo a Samuele con istruzioni passo passo)
| Nome | Tipo | Uso |
|---|---|---|
| `ORBIS_JWT_SECRET` | Secret | chiave di firma dei token (generare sul Mac con `openssl rand -hex 32`) |
| `DB_PASSWORD` | Secret (facoltativo) | password MySQL se il pannello Altervista ne mostra una; altrimenti non serve |
| `ORBIS_ADMIN_USERNAME` | Variable | username dell'account admin nel gioco |
| `FOOTBALL_DATA_API_KEY`, `THESPORTSDB_API_KEY` | Secret (M8) | chiavi gratuite per l'import sport |

GitHub Pages va attivato da Samuele: **Settings → Pages → Source: GitHub Actions** (istruzioni da dare in M0).

## 2. Workflow (`.github/workflows/`)

| File | Quando | Cosa fa |
|---|---|---|
| `core-tests.yml` | ogni push e PR | `dotnet test tests/Orbis.Core.Tests` su Ubuntu (veloce, senza Unity) |
| `backend.yml` | push su `main` che tocca `backend/**` | test PHP con MySQL in container → genera `config.php` dai segreti → deploy FTP in `/orbis/` → chiama `/orbis/api/health` per verifica |
| `ios-build.yml` | push su `main` (Assets, Packages, ProjectSettings), manuale, tag `v*` | build Unity iOS → progetto Xcode → `xcodebuild` senza firma → **`Orbis.ipa`** come artifact (30 giorni); sui tag crea una Release con l'IPA |
| `web-build.yml` | come sopra | build Unity Web → deploy su **GitHub Pages** (`https://samozzux.github.io/orbis/`) |
| `unity-tests.yml` | PR e manuale | test EditMode/PlayMode Unity |
| `world-data.yml` | manuale (input: regione/paese) | pipeline OSM → region pack → Release `world-*` → aggiorna manifest |
| `sports-import.yml` | manuale (input: sport) | import da fonti aperte → JSON in `data/sports/` → commit su branch `data/sports-import` per revisione |
| `assets.yml` | manuale | Blender headless: genera kit di facciate, landmark scriptati, LOD → asset pack Addressables per iOS e Web → Release `assets-*` |

Regole comuni:
- `concurrency` per workflow Unity: **una build Unity alla volta** (la licenza Personal ha un numero limitato di attivazioni contemporanee), con restituzione della licenza a fine job anche in caso di errore.
- Cache della cartella `Library/` per piattaforma (chiave su hash di `Packages/` e `ProjectSettings/`).
- Timeout massimo 120 minuti per le build Unity.

## 3. Build Unity in CI

### Licenza Unity 6 (importante)
Con Unity 6 la licenza Personal sul Mac è in formato **XML** (`UnityEntitlementLicense.xml`), non `.ulf`. Strategia:
1. **Prima scelta**: attivazione online con email e password, con un'azione che supporta la licenza Personal (es. `buildalon/activate-unity-license`, `license: Personal`, `username`/`password` dai segreti) e installazione di Unity con un'azione dedicata (es. `buildalon/unity-setup`, versione `6000.6.9f1`, moduli `ios` e `webgl`).
2. **Ripiego**: GameCI (`game-ci/unity-builder`) con `UNITY_EMAIL`, `UNITY_PASSWORD`, `UNITY_LICENSE`.
3. Verificare in M0 nomi, versioni e parametri attuali delle azioni nella loro documentazione e scegliere quella che funziona; annotare in `docs/STATO.md`.

### Script di build
`Assets/Orbis/Editor/Build/BuildScript.cs` con metodi `BuildIOS`, `BuildWeb`, eseguiti con `-batchmode -nographics -quit -executeMethod`. Impostano: scene generate, bundle id **`com.samuelecozza.orbis`**, nome **Orbis**, versione da tag/numero di run, iOS minimo 26.0, dispositivi iPhone + iPad, orientamenti, chiavi Info.plist (`docs/01-architettura.md` §10), Addressables.

### IPA senza firma (macOS runner)
```bash
xcodebuild -project build/ios/Unity-iPhone.xcodeproj -scheme Unity-iPhone \
  -configuration Release -sdk iphoneos -derivedDataPath build/dd \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY="" build
mkdir -p Payload && cp -R build/dd/Build/Products/Release-iphoneos/*.app Payload/
zip -qr Orbis.ipa Payload
```
(Adattare il nome dello schema/app a quello generato da Unity.)

### Web
- Build Web con WebGPU abilitato e WebGL 2 come ripiego, compressione Brotli + **Decompression Fallback** (GitHub Pages non imposta `Content-Encoding`).
- Pagina HTML personalizzata: schermo intero, barra di caricamento, avviso se il browser non è supportato, meta per "Aggiungi a schermata Home".
- Pubblicazione con `actions/upload-pages-artifact` + `actions/deploy-pages`.

## 4. Installare l'IPA (istruzioni per Samuele, da mettere nel README)
1. GitHub → scheda **Actions** → ultima esecuzione verde di "iOS build" → sezione **Artifacts** → scarica `Orbis-ipa` e decomprimi.
2. Apri **Sideloadly**, collega l'iPhone (o l'iPad), trascina `Orbis.ipa`, inserisci il tuo Apple ID, **Start**.
3. Sull'iPhone: Impostazioni → Generali → VPN e gestione dispositivi → autorizza il profilo; Modalità sviluppatore attiva.
4. Con Apple ID gratuito l'app scade dopo **7 giorni**: rinnovo da Sideloadly (anche automatico).

## 5. Dati pesanti
- Region pack del mondo e asset pack: GitHub Releases (file ≤ 2 GB), manifest con URL, hash, dimensioni.
- Versione web: verifica CORS e eventuale hosting alternativo come da `docs/01-architettura.md` §13.
- Il pack di Chieti e il globo base sono inclusi nella build (iOS) e scaricati per primi (web).

## 6. Versioni
- Versione app: `MAJOR.MINOR.PATCH` (tag `v0.1.0` = fine M1, ecc.), build number = numero di run.
- Content pack, region pack e asset pack hanno versioni proprie; l'app verifica la compatibilità tramite il campo `minAppVersion` nel manifest.
