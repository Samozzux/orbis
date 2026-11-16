#!/usr/bin/env bash
# Rigenera il codice FlatBuffers da schema/orbis_tile.fbs:
#  - Python: tools/world-pipeline/generated/Orbis/Core/World/Fb/
#  - C#:     Assets/Orbis/Core/World/Fb/
# Serve flatc 25.12.19 (stessa versione del runtime in Assets/Orbis/Core/ThirdParty/FlatBuffers).
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd ../.. && pwd)"
WANT="25.12.19"
GOT="$(flatc --version | awk '{print $3}')"
if [ "$GOT" != "$WANT" ]; then
  echo "Serve flatc $WANT (trovato $GOT)" >&2
  exit 1
fi
rm -rf generated "$ROOT/Assets/Orbis/Core/World/Fb"
flatc --python -o generated schema/orbis_tile.fbs
flatc --csharp --gen-onefile -o "$ROOT/Assets/Orbis/Core/World/Fb" schema/orbis_tile.fbs
mv "$ROOT/Assets/Orbis/Core/World/Fb/orbis_tile_generated.cs" "$ROOT/Assets/Orbis/Core/World/Fb/OrbisTile.g.cs"
echo "Codice rigenerato."
