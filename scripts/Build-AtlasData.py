"""Build the offline atlas from the official Natural Earth v5.1.2 GeoJSON files.

Pass a folder containing the four ne_50m_*.geojson files named below.
See docs/SUBJECT_BOARD.md for the pinned source and cartographic conventions.
"""

import json
import sys
from pathlib import Path


CURRENT_NAMES = {
    "CIV": "Côte d’Ivoire",
    "CPV": "Cabo Verde",
    "CZE": "Czechia",
    "NRU": "Naoero",
    "TLS": "Timor-Leste",
    "TUR": "Türkiye",
}


def read_features(source, name):
    return json.loads((source / f"ne_50m_{name}.geojson").read_text(encoding="utf-8"))["features"]


def rounded(coordinates):
    if isinstance(coordinates[0], (int, float)):
        return [round(value, 4) for value in coordinates[:2]]
    return [rounded(part) for part in coordinates]


def clockwise(ring):
    return sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(ring, ring[1:])) < 0


def rings(polygon):
    # D3 uses the spherical convention with clockwise exterior rings.
    return [ring if clockwise(ring) == (index == 0) else ring[::-1]
            for index, ring in enumerate(polygon)]


def geometry(feature):
    source = feature["geometry"]
    kind = source["type"]
    coords = rounded(source["coordinates"])
    if kind == "Polygon":
        coords = rings(coords)
    if kind == "MultiPolygon":
        coords = [rings(polygon) for polygon in coords]
    return {"type": kind, "coordinates": coords}


def feature(source, **properties):
    return {"type": "Feature", "properties": properties, "geometry": geometry(source)}


def build(source):
    countries = []
    for item in read_features(source, "admin_0_countries"):
        properties = item["properties"]
        code = properties["ADM0_A3"]
        original = properties["NAME_EN"] or properties["NAME"]
        countries.append(feature(
            item, name=CURRENT_NAMES.get(code, original), code=code, alias=original,
            color=properties["MAPCOLOR7"], rank=properties["LABELRANK"],
            label=rounded([properties["LABEL_X"], properties["LABEL_Y"]])))
    places = []
    for item in read_features(source, "populated_places_simple"):
        properties = item["properties"]
        places.append(feature(
            item, name=properties["name"], country=CURRENT_NAMES.get(properties["adm0_a3"], properties["adm0name"]),
            rank=properties["scalerank"], capital=properties["featurecla"].startswith("Admin-0 capital")))
    water = [feature(item, rank=item["properties"]["scalerank"])
             for item in read_features(source, "lakes")]
    disputes = [feature(item) for item in read_features(source, "admin_0_boundary_lines_disputed_areas")]
    return {"countries": countries, "places": places, "lakes": water, "disputes": disputes}


if __name__ == "__main__":
    destination = Path(__file__).resolve().parents[1] / "apps/desktop/TeachingResources/world-data.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("w", encoding="utf-8", newline="\n") as output:
        output.write("{\n")
        for index, (layer, features) in enumerate(build(Path(sys.argv[1])).items()):
            output.write((",\n" if index else "") + f'  "{layer}": [\n')
            output.write(",\n".join(json.dumps(item, ensure_ascii=False, separators=(",", ":")) for item in features))
            output.write("\n  ]")
        output.write("\n}\n")
