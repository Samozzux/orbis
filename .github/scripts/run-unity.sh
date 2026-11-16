#!/usr/bin/env bash
# Avvia Unity in batchmode per la CI. Uso: .github/scripts/run-unity.sh <argomenti Unity>
# (aggiunge -batchmode -nographics -projectPath . -logFile -).
# Se Unity viene terminato dal sistema (codice 137, già successo all'avvio con la cartella Library
# ripristinata dalla cache), registra memoria e messaggi del kernel, cancella Library e riprova una volta.
set -uo pipefail

run() {
  "$UNITY_EDITOR_PATH" -batchmode -nographics -projectPath . "$@" -logFile -
}

run "$@"
code=$?
if [ "$code" -eq 137 ]; then
  echo "::warning::Unity terminato dal sistema (137): diagnostica, poi nuovo tentativo con Library da zero"
  free -m || true
  sudo dmesg 2>/dev/null | grep -iE "killed process|out of memory|oom" | tail -n 5 || true
  rm -rf Library
  run "$@"
  code=$?
fi
exit "$code"
