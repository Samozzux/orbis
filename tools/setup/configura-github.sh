#!/usr/bin/env bash
# Configurazione una tantum di Orbis su GitHub (milestone M0). Da lanciare sul Mac di Samuele:
#   bash tools/setup/configura-github.sh
# Fa, chiedendo conferma passo per passo:
#   1. secret ORBIS_JWT_SECRET (chiave casuale, non viene mai mostrata)
#   2. variabile ORBIS_ADMIN_USERNAME (lo username admin che userai nel gioco)
#   3. secret DB_PASSWORD (solo se Altervista mostra una password per il database)
#   4. keystore di firma Android + i 4 secret ANDROID_*
#   5. GitHub Pages con sorgente "GitHub Actions"
# Le password restano sul Mac e su GitHub: questo script non le salva da nessun'altra parte.
set -euo pipefail

REPO="Samozzux/orbis"
KEY_DIR="$HOME/Documents/Orbis-chiavi"
KEYSTORE="$KEY_DIR/orbis-release.keystore"
KEY_ALIAS="orbis"

say() { printf '\n\033[1m%s\033[0m\n' "$*"; }
ask_yes() { read -r -p "$1 [s/N] " a; [[ "$a" =~ ^[sSyY] ]]; }

command -v gh >/dev/null || { echo "Serve GitHub CLI: brew install gh"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Prima esegui: gh auth login"; exit 1; }
existing_secrets=$(gh secret list --repo "$REPO" --json name -q '.[].name' 2>/dev/null || true)
has_secret() { grep -qx "$1" <<<"$existing_secrets"; }

# 1 ─ chiave JWT
say "1/5  Chiave di firma dei token (ORBIS_JWT_SECRET)"
if has_secret ORBIS_JWT_SECRET; then
  echo "Già presente: la lascio com'è."
else
  openssl rand -hex 32 | gh secret set ORBIS_JWT_SECRET --repo "$REPO"
  echo "Creata."
fi

# 2 ─ username admin
say "2/5  Username dell'account admin del gioco (ORBIS_ADMIN_USERNAME)"
current=$(gh variable get ORBIS_ADMIN_USERNAME --repo "$REPO" 2>/dev/null || true)
if [ -n "$current" ]; then
  echo "Già impostato: $current"
else
  read -r -p "Scegli lo username (3-20 caratteri, lettere/numeri/_): " admin
  if [[ "$admin" =~ ^[A-Za-z0-9_]{3,20}$ ]]; then
    gh variable set ORBIS_ADMIN_USERNAME --repo "$REPO" --body "$admin"
    echo "Impostato."
  else
    echo "Username non valido: salto (puoi rilanciare lo script)."
  fi
fi

# 3 ─ password del database
say "3/5  Password del database Altervista (DB_PASSWORD) — di solito NON serve"
if has_secret DB_PASSWORD; then
  echo "Già presente."
elif ask_yes "Il pannello Altervista (sezione Database) ti mostra una password?"; then
  read -r -s -p "Incollala (non viene mostrata): " dbp; echo
  printf '%s' "$dbp" | gh secret set DB_PASSWORD --repo "$REPO"
  echo "Salvata."
else
  echo "Ok, nessuna password."
fi

# 4 ─ keystore Android
say "4/5  Chiave di firma dell'APK Android"
if has_secret ANDROID_KEYSTORE_BASE64; then
  echo "Già presente su GitHub: non la ricreo (cambiarla impedirebbe di aggiornare l'app)."
else
  command -v keytool >/dev/null || { echo "Serve Java: brew install openjdk  (poi rilancia lo script)"; exit 1; }
  if [ -f "$KEYSTORE" ]; then
    echo "Uso il keystore già creato: $KEYSTORE"
  else
    mkdir -p "$KEY_DIR"; chmod 700 "$KEY_DIR"
    while true; do
      read -r -s -p "Scegli una password per la chiave (almeno 8 caratteri): " p1; echo
      read -r -s -p "Ripetila: " p2; echo
      if [ "$p1" = "$p2" ] && [ ${#p1} -ge 8 ]; then break; fi
      echo "Le password non coincidono o sono troppo corte, riprova."
    done
    ORBIS_KS_PASS="$p1" keytool -genkeypair -keystore "$KEYSTORE" -storetype PKCS12 \
      -alias "$KEY_ALIAS" -keyalg RSA -keysize 4096 -validity 10000 \
      -storepass:env ORBIS_KS_PASS -dname "CN=Orbis, O=Orbis, C=IT" >/dev/null
    chmod 600 "$KEYSTORE"
    echo "Keystore creato in: $KEYSTORE"
  fi
  if [ -z "${p1:-}" ]; then
    read -r -s -p "Password del keystore esistente: " p1; echo
  fi
  base64 -i "$KEYSTORE" | gh secret set ANDROID_KEYSTORE_BASE64 --repo "$REPO"
  printf '%s' "$p1" | gh secret set ANDROID_KEYSTORE_PASS --repo "$REPO"
  printf '%s' "$KEY_ALIAS" | gh secret set ANDROID_KEY_ALIAS --repo "$REPO"
  printf '%s' "$p1" | gh secret set ANDROID_KEY_PASS --repo "$REPO"   # PKCS12: stessa password
  echo "Caricati i 4 secret ANDROID_*."
  echo "IMPORTANTE: fai una copia della cartella $KEY_DIR (es. su una chiavetta o nel cloud)"
  echo "e salva la password nel tuo gestore di password. Senza, non potrai aggiornare l'APK."
fi

# 5 ─ GitHub Pages
say "5/5  GitHub Pages (versione web)"
if gh api "repos/$REPO/pages" >/dev/null 2>&1; then
  gh api -X PUT "repos/$REPO/pages" -f build_type=workflow >/dev/null && echo "Già attivo: sorgente impostata su GitHub Actions."
else
  gh api -X POST "repos/$REPO/pages" -f build_type=workflow >/dev/null && echo "Attivato con sorgente GitHub Actions."
fi

say "Fatto! Torna da Claude e scrivi: \"configurazione fatta\"."
