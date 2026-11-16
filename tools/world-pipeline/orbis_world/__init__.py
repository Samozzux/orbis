"""Pipeline del mondo di Orbis: OpenStreetMap + terreno → region pack di tile (docs/02 §2)."""
import os
import sys

# codice FlatBuffers generato da schema/orbis_tile.fbs (genera-codice.sh)
_GEN = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "generated")
if _GEN not in sys.path:
    sys.path.insert(0, _GEN)
