# 06 — Backend (Altervista) e API

## 1. Ambiente
- Sito: `giuliaa8.altervista.org`, cartella **`/orbis/`** (piano gratuito).
- Base URL API: `https://giuliaa8.altervista.org/orbis/api/` (HTTPS obbligatorio: se non è attivo, attivarlo dal pannello Altervista — chiedere a Samuele).
- Database MySQL unico del sito, condiviso con altri progetti: di norma nome `my_giuliaa8`, host `localhost`, utente `giuliaa8` (verificare nel pannello). **Solo tabelle con prefisso `orb_`.**
- PHP 7.x senza framework, PDO, prepared statements ovunque. Vincoli in `CLAUDE.md`.
- Prima cosa da fare in M0: endpoint `GET /api/health` che restituisce versione PHP, versione MySQL, estensioni disponibili (openssl, zlib, mbstring, pdo_mysql, sodium), `upload_max_filesize`, `post_max_size`, `memory_limit`, `max_execution_time`, HTTPS attivo. Le scelte successive si adattano a questi valori e vanno annotate in `docs/STATO.md`.

## 2. Struttura cartelle (`/backend` → `/orbis/` sul server)
```
backend/
  api/index.php          router unico (es. /api/auth/login → controller)
  api/.htaccess          rewrite verso index.php
  src/                   controller, servizi, repository, utilità (JWT, validazione)
  admin/                 pannello admin (HTML+JS semplice, chiama le API admin)
  migrations/            001_init.sql, 002_….sql (applicate in ordine)
  storage/               salvataggi, content pack, upload (protetto da .htaccess "Deny from all")
  config.example.php     modello di configurazione (senza segreti)
  tests/run.php          test minimi
```
- `config.php` **non è nel repo**: lo genera il workflow di deploy dai GitHub Secrets.
- **Migrazioni automatiche**: alla prima chiamata dopo un deploy, `migrate` applica le migrazioni mancanti (tabella `orb_migrations`), con lock per evitare esecuzioni doppie.

## 3. Schema database (iniziale)

| Tabella | Campi principali |
|---|---|
| `orb_migrations` | id, name, applied_at |
| `orb_users` | id, username (unico, 3–20 caratteri), password_hash, display_name, is_admin, created_at, last_login_at, status |
| `orb_refresh_tokens` | id, user_id, token_hash, device_id, device_name, platform (ios/android/web), created_at, expires_at, revoked_at, replaced_by |
| `orb_passkeys` | id, user_id, credential_id, public_key, sign_count, device_name, created_at |
| `orb_login_attempts` | id, ip_hash, username, success, created_at (limite tentativi) |
| `orb_friendships` | id, user_id, friend_id, status (pending/accepted/blocked), created_at |
| `orb_saves` | id, user_id, save_uuid, name, city_name, revision, device_id, size_bytes, checksum, storage_path, updated_at, is_shared_snapshot |
| `orb_save_chunks` | upload_id, save_uuid, chunk_index, size, checksum, created_at (upload a pezzi) |
| `orb_shared_blueprints` | id, user_id, name, data (MEDIUMTEXT JSON), created_at |
| `orb_likes` | id, user_id, target_type, target_id, created_at |
| `orb_content_items` | id, pack (catalog/sports/events/scenarios/landmarks/music/…), type, key (unico per pack), data (MEDIUMTEXT JSON), version, updated_at, deleted |
| `orb_content_packs` | id, pack, version, checksum, size_bytes, storage_path, published_at, notes |
| `orb_import_jobs` | id, user_id, pack, format, status (preview/applied/rolled_back/failed), file_path, report (MEDIUMTEXT), created_at, applied_at |
| `orb_import_changes` | job_id, item_key, action (insert/update/delete), before_data, after_data (per l'annullamento) |
| `orb_admin_log` | id, user_id, action, target, details, created_at |
| `orb_app_config` | key, value (impostazioni remote: versione minima app, URL manifest dati, messaggi) |

Contenuti sport, catalogo, eventi ecc. sono nel modello generico `orb_content_items` (JSON per elemento) per restare flessibili con MySQL 5.7; il gioco usa i content pack pubblicati, non legge le tabelle direttamente.

## 4. Autenticazione

### Registrazione e accesso
- `POST /api/auth/register` {username, password, displayName} → crea utente (password min 8 caratteri, hash `password_hash` bcrypt).
- `POST /api/auth/login` {username, password, deviceId, deviceName, platform} → `{accessToken, refreshToken, user}`.
- **Access token**: JWT HS256 firmato con `ORBIS_JWT_SECRET`, durata **1 ora**.
- **Refresh token**: stringa casuale 256 bit, durata **90 giorni**, salvata solo come hash; **ruotata** a ogni `POST /api/auth/refresh` (il vecchio viene revocato; se un token revocato viene riusato → revoca tutta la catena di quel dispositivo).
- `POST /api/auth/logout` (revoca il token del dispositivo), `GET /api/auth/sessions`, `DELETE /api/auth/sessions/{id}`.
- Limite: 10 tentativi di login falliti per IP/username ogni 15 minuti.

### Face ID (iOS)
- Dopo il primo login, l'app chiede se attivare Face ID. Se sì, il refresh token viene salvato nel **Keychain** con controllo di accesso biometrico (`.biometryCurrentSet`).
- Ai successivi avvii: Face ID → lettura del refresh token → `/auth/refresh` → dentro. Nessuna password.
- Se cambia la configurazione di Face ID o il token scade: login con password.

### Sblocco biometrico (Android)
Come su iOS: il refresh token viene salvato cifrato con una chiave dell'**Android Keystore** che richiede l'autenticazione biometrica (BiometricPrompt, impronta o volto). Ai successivi avvii: sblocco biometrico → `/auth/refresh` → dentro.

### Passkey (web)
- `POST /api/auth/passkey/register/options`, `.../register/verify`, `POST /api/auth/passkey/login/options`, `.../login/verify` (WebAuthn: challenge, verifica firma ES256/RS256 con openssl, contatore).
- Implementazione compatibile con la versione PHP di Altervista (libreria leggera compatibile o implementazione minimale con CBOR decoder). Se impraticabile sul piano gratuito → ripiego: sessione lunga con refresh token in IndexedDB. Annotare la scelta in `docs/STATO.md`.
- **Fatto in M3 (18/11/2026).** Implementazione minimale in PHP puro: decoder CBOR, chiavi COSE ES256/RS256 convertite in PEM, verifica con `openssl_verify`. openssl c'è sul piano gratuito.
  - **RP ID.** È l'host della pagina: `samozzux.github.io`, `localhost` in sviluppo.
  - **Attestazione.** Si chiede `none`.
  - **Opzioni richieste.** Passkey "discoverable" (si entra senza scrivere il nome utente) e verifica dell'utente obbligatoria.
  - **Sicurezza.** Sfide monouso da 5 minuti; il contatore che non cresce viene rifiutato.
  - **Endpoint in più.** `GET /api/auth/passkeys` e `DELETE /api/auth/passkeys/{id}`.
  - **Uso.** Si crea dal profilo dopo l'accesso con la password; sul web la si propone subito dopo il primo accesso.

### CORS
Tutte le risposte API includono `Access-Control-Allow-Origin: https://samozzux.github.io` (+ localhost per sviluppo), gestione `OPTIONS` preflight, header `Authorization` e `Content-Type` ammessi.

## 5. API di gioco

| Metodo e percorso | Descrizione |
|---|---|
| `GET /api/health` | stato e capacità del server |
| `GET /api/config` | configurazione remota (URL manifest mondo/asset, versioni minime, messaggi) |
| `GET /api/me` / `PATCH /api/me` | profilo |
| `GET /api/users/search?q=` | cerca utenti per username |
| `GET /api/friends` · `POST /api/friends/request` · `POST /api/friends/{id}/accept` · `DELETE /api/friends/{id}` | amici |
| `GET /api/saves` | elenco salvataggi cloud (metadati) |
| `POST /api/saves/{uuid}/upload/start` → `PUT .../upload/{uploadId}/chunk/{n}` → `POST .../upload/{uploadId}/finish` | upload a pezzi (dimensione pezzo da `/health`, predefinita 512 KB) con `baseRevision` |
| `GET /api/saves/{uuid}` · `GET /api/saves/{uuid}/download` | metadati e download (supporto Range) |
| `DELETE /api/saves/{uuid}` | elimina |
| `GET /api/friends/{id}/city` | istantanea condivisa della città di un amico (sola lettura) |
| `POST /api/blueprints` · `GET /api/blueprints?user=` | progetti di edifici condivisi |
| `POST /api/likes` | mi piace a città/progetti |
| `GET /api/content/manifest` | elenco content pack con versione e checksum |
| `GET /api/content/packs/{pack}/{version}` | download content pack (gzip, ETag, cache) |

### Sincronizzazione e conflitti
- Ogni salvataggio ha `revision` (intero crescente) e `deviceId` dell'ultimo upload.
- Upload con `baseRevision`: se sul server la revisione è più alta → `409 Conflict` con i metadati della versione cloud.
- L'app propone: **Tieni questo dispositivo**, **Tieni il cloud**, **Tienili entrambi** (copia).
- Sincronizzazione automatica: alla chiusura/messa in pausa dell'app, ogni 15 minuti reali di gioco, e manuale.

## 6. Pannello admin (`/orbis/admin/`)
Accessibile solo a utenti con `is_admin = 1` (Samuele, impostato dal workflow di deploy alla prima esecuzione tramite la variabile `ORBIS_ADMIN_USERNAME`).

Funzioni:
- **Gestione contenuti** per ogni pack: elenco con ricerca/filtri, crea, modifica (editor a form generato da uno schema JSON per tipo + editor JSON grezzo), duplica, elimina.
  - Catalogo: edifici, oggetti, mobili, veicoli, cibi, animali, piante, varianti, livelli, prezzi, effetti, requisiti.
  - Sport: sport, competizioni, stagioni iniziali, squadre, atleti (con attributi), impianti.
  - Landmark, eventi/feste, scenari/missioni/sfide, profili architettonici, playlist e brani della libreria musicale, testi dei modelli (notizie, dialoghi).
- **Import massivo** CSV o JSON:
  1. carica file → scegli pack/tipo → mappatura colonne (salvabile);
  2. **anteprima** con validazione (errori e avvisi per riga, differenze rispetto ai dati esistenti: nuovi/modificati/eliminati);
  3. **applica** (transazione) → job registrato in `orb_import_jobs` con tutte le modifiche;
  4. **annulla** un job applicato (ripristina `before_data`).
- **Esporta** qualsiasi pack in CSV/JSON.
- **Pubblica content pack**: genera un nuovo pack versionato (JSON gzip) e aggiorna il manifest; l'app lo scarica al prossimo avvio online.
- **Utenti**: elenco, disattiva, reimposta password, revoca sessioni.
- **Registro** delle azioni admin.
- Limiti del piano gratuito: import grandi elaborati **a blocchi** (es. 500 righe per richiesta) con barra di avanzamento, per non superare `max_execution_time`.

## 7. Test
`backend/tests/run.php` (eseguibile in locale con PHP CLI e MySQL/MariaDB in Docker nel workflow): registrazione, login, refresh con rotazione e riuso rilevato, CORS, upload/download salvataggio con conflitto, import con anteprima/applica/annulla.
