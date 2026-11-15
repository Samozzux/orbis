# Orbis

Simulatore di città e di vita ambientato nel mondo reale, a partire da Chieti. Gioco Unity per **iPhone/iPad**, **Android** e **web**.

- 🎮 Versione web: https://samozzux.github.io/orbis/ (attiva da M0)
- 📱 IPA: scheda **Actions** → ultima esecuzione verde di "iOS build" → **Artifacts** → `Orbis-ipa`, da installare con Sideloadly (istruzioni qui sotto)
- 🤖 APK Android: scheda **Actions** → ultima esecuzione verde di "Android build" → **Artifacts** → `Orbis-apk` (istruzioni qui sotto)
- 📚 Specifica: cartella [`docs/`](docs/) — si parte da [`docs/00-visione.md`](docs/00-visione.md)
- 🤖 Regole per Claude Code: [`CLAUDE.md`](CLAUDE.md)
- 📈 Avanzamento: [`docs/STATO.md`](docs/STATO.md)

## Installare l'app su iPhone / iPad (IPA)
1. GitHub → scheda **Actions** → ultima esecuzione verde di **"iOS build"** → sezione **Artifacts** → scarica `Orbis-ipa` e decomprimi.
2. Apri **Sideloadly**, collega l'iPhone (o l'iPad), trascina `Orbis.ipa`, inserisci il tuo Apple ID, premi **Start**.
3. Sull'iPhone: Impostazioni → Generali → VPN e gestione dispositivi → autorizza il profilo; Modalità sviluppatore attiva.
4. Con Apple ID gratuito l'app scade dopo **7 giorni**: si rinnova da Sideloadly (anche in automatico).

## Installare l'app su Android (APK)
1. GitHub → scheda **Actions** → ultima esecuzione verde di **"Android build"** → **Artifacts** → scarica `Orbis-apk` e decomprimi (oppure scarica l'APK dalla Release).
2. Copia `Orbis.apk` sul telefono (o scaricalo direttamente dal telefono) e aprilo.
3. Quando Android lo chiede, consenti "Installa app sconosciute" per l'app da cui lo apri (browser o file manager).
4. Nessuna scadenza: gli aggiornamenti si installano sopra la versione precedente (stessa chiave di firma).

## Struttura
- `Assets/Orbis/` — codice del gioco, diviso in assembly (`Core` = logica pura testabile senza Unity, `Platform`, `UI`, …); `Assets/Orbis/Editor/` genera scene e impostazioni da codice (**Orbis → Genera progetto**) e contiene `BuildScript`.
- `tests/Orbis.Core.Tests/` — test .NET della logica pura: `dotnet test tests/Orbis.Core.Tests`.
- `backend/` — API PHP per Altervista (`/orbis/api/health`); test: `php backend/tests/run.php`.
- `data/localization/ui.json` — tutti i testi dell'interfaccia (italiano).
- `.github/workflows/` — build IPA, APK e web, test, deploy del backend.
- `tools/setup/configura-github.sh` — configurazione una tantum dei segreti GitHub.

## Come si lavora con Claude Code
Prompt di partenza: **"Leggi CLAUDE.md e realizza la milestone M0"**. Alla fine di ogni sessione Claude Code aggiorna `docs/STATO.md` con cosa provare.

## Crediti
Dati © OpenStreetMap contributors (ODbL) e altre fonti elencate in [`CREDITS.md`](CREDITS.md).
