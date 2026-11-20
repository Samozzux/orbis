"""Popolazione reale per il generatore del gioco (docs/03 §2).

  cd tools/population
  python -m orbis_population --region regions/chieti.json --cache cache --out ../../data/population/chieti.json
"""
import argparse
import json
import sys

from . import build


def main(argv=None):
    p = argparse.ArgumentParser(prog="orbis_population")
    p.add_argument("--region", required=True)
    p.add_argument("--cache", default="cache")
    p.add_argument("--out", required=True)
    a = p.parse_args(argv)
    with open(a.region, encoding="utf-8") as f:
        region = json.load(f)
    data = build.build(region, a.cache)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(build.dumps(data))
    pop = data["population"]
    print(f"{data['municipality']['name']}: {pop['total']} abitanti, {data['income']['taxpayers']} contribuenti, "
          f"{len(data['province']['municipalities'])} comuni in provincia → {a.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
