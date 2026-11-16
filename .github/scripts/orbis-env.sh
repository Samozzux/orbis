#!/usr/bin/env bash
# Imposta le variabili di versione per le build Unity (docs/07 §7):
#   ORBIS_VERSION      tag "vX.Y.Z" se la build parte da un tag, altrimenti l'ultimo tag vX.Y.Z (o 0.0.0)
#   ORBIS_BUILD_NUMBER numero dell'esecuzione del workflow
#   ORBIS_COMMIT       commit della build
set -euo pipefail
if [ "${GITHUB_REF_TYPE:-}" = "tag" ] && [[ "${GITHUB_REF_NAME:-}" == v* ]]; then
  version="${GITHUB_REF_NAME#v}"
else
  last=$(git tag --list 'v*' --sort=-v:refname | head -n 1 || true)
  version="${last#v}"
  version="${version:-0.0.0}"
fi
{
  echo "ORBIS_VERSION=$version"
  echo "ORBIS_BUILD_NUMBER=${GITHUB_RUN_NUMBER:-0}"
  echo "ORBIS_COMMIT=${GITHUB_SHA:-}"
} >> "$GITHUB_ENV"
echo "Versione $version, build ${GITHUB_RUN_NUMBER:-0}"
