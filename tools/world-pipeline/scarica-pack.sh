#!/usr/bin/env bash
# Scarica i region pack indicati in data/world/packs.json in Assets/StreamingAssets/world/<regione>/
# (usato dalle build in CI e, se serve, sul Mac per provare il gioco nell'Editor).
# Richiede gh (GitHub CLI) autenticato oppure GH_TOKEN.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
python3 - "$ROOT" <<'PY' > /tmp/orbis-packs.txt
import json, sys
packs = json.load(open(sys.argv[1] + "/data/world/packs.json"))["packs"]
for key, p in packs.items():
    if p.get("tag"):
        print(key, p["tag"], p["file"], p["sha256"])
PY
if [ ! -s /tmp/orbis-packs.txt ]; then
  echo "Nessun region pack pubblicato: il gioco userà il mondo vuoto."
  exit 0
fi
while read -r key tag file sha; do
  dest="Assets/StreamingAssets/world/$key"
  if [ -f "$dest/.sha256" ] && [ "$(cat "$dest/.sha256")" = "$sha" ]; then
    echo "$key: già presente ($tag)"
    continue
  fi
  tmp="$(mktemp -d)"
  gh release download "$tag" --repo "${GITHUB_REPOSITORY:-Samozzux/orbis}" -p "$file" -D "$tmp"
  echo "$sha  $tmp/$file" | shasum -a 256 -c -
  rm -rf "$dest"
  mkdir -p "$dest"
  unzip -q "$tmp/$file" -d "$dest"
  echo "$sha" > "$dest/.sha256"
  rm -rf "$tmp"
  echo "$key: $tag ($(du -sh "$dest" | cut -f1))"
done < /tmp/orbis-packs.txt
