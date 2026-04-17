import argparse
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent

CAMEL_RE = re.compile(r"([A-Za-z0-9]|(?<!^))([A-Z])")
WHITESPACE_RE = re.compile(r"\s+")
SPECIAL_RE = re.compile(r"[^A-Z0-9_]")
LIST_OPTION_RE = re.compile(r"RelicOption<(?P<name>[A-Za-z0-9_]+)>\(")

POSITIVE_SINGLE_PROPERTIES = [
    "LavaRockOption",
    "NeowsTalismanOption",
    "NutritiousOysterOption",
    "PomanderOption",
    "SmallCapsuleOption",
    "StoneHumidifierOption",
]
NEGATIVE_SINGLE_PROPERTIES = [
    "ScrollBoxesOption",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate Neow option metadata from official source files.")
    parser.add_argument("--version", default="0.103.2", help="Game data version to generate.")
    parser.add_argument("--locale", default="zhs", help="Localization code (default: zhs).")
    parser.add_argument(
        "--output",
        default=None,
        help="Output dataset path. Defaults to data/<version>/neow/options.json.",
    )
    return parser.parse_args()


def slugify(name: str) -> str:
    text = CAMEL_RE.sub(r"\1_\2", name.strip())
    text = WHITESPACE_RE.sub("_", text.upper())
    return SPECIAL_RE.sub("", text)


def resolve_game_root(version: str) -> pathlib.Path:
    preferred = [entry for entry in sorted(ROOT.iterdir()) if entry.is_dir() and entry.name.startswith("Slay the Spire 2") and version in entry.name]
    if preferred:
        return preferred[0]

    fallback = [entry for entry in sorted(ROOT.iterdir()) if entry.is_dir() and entry.name.startswith("Slay the Spire 2")]
    if fallback:
        return fallback[0]

    raise FileNotFoundError("Unable to locate 'Slay the Spire 2' source directory")


def load_localization(game_root: pathlib.Path, locale: str) -> dict[str, str]:
    path = game_root / "localization" / locale / "relics.json"
    with path.open("r", encoding="utf-8") as source:
        return json.load(source)


def extract_option_list(source_text: str, property_name: str) -> list[str]:
    marker = f"private IEnumerable<EventOption> {property_name}"
    start = source_text.find(marker)
    if start < 0:
        raise RuntimeError(f"Unable to locate property: {property_name}")

    body_start = source_text.find("{", start)
    if body_start < 0:
        raise RuntimeError(f"Unable to locate body for property: {property_name}")

    body_end = source_text.find("});", body_start)
    if body_end < 0:
        raise RuntimeError(f"Unable to locate end of property: {property_name}")

    body = source_text[body_start:body_end]
    return [slugify(name) for name in LIST_OPTION_RE.findall(body)]


def extract_single_option(source_text: str, property_name: str) -> str:
    pattern = rf"private EventOption {re.escape(property_name)}\s*=>\s*RelicOption<(?P<name>[A-Za-z0-9_]+)>\("
    match = re.search(pattern, source_text)
    if not match:
        raise RuntimeError(f"Unable to locate property: {property_name}")
    return slugify(match.group("name"))


def extract_note_map(options: list[dict]) -> dict[str, str | None]:
    return {option["id"]: option.get("note") for option in options if "id" in option}


def build_options(version: str, locale: str, note_map: dict[str, str | None] | None = None) -> list[dict]:
    note_map = note_map or {}
    game_root = resolve_game_root(version)
    source_path = game_root / "src" / "Core" / "Models" / "Events" / "Neow.cs"
    source_text = source_path.read_text(encoding="utf-8")
    loc = load_localization(game_root, locale)

    positive_ids = extract_option_list(source_text, "PositiveOptions")
    positive_ids.extend(extract_single_option(source_text, name) for name in POSITIVE_SINGLE_PROPERTIES)

    negative_ids = extract_option_list(source_text, "CurseOptions")
    negative_ids.extend(extract_single_option(source_text, name) for name in NEGATIVE_SINGLE_PROPERTIES)

    records: list[dict] = []
    for kind, option_ids in (("Positive", positive_ids), ("Negative", negative_ids)):
        for option_id in option_ids:
            records.append(
                {
                    "id": option_id,
                    "relic_id": option_id,
                    "kind": kind,
                    "title": loc.get(f"{option_id}.title", option_id.replace("_", " ").title()),
                    "description": loc.get(f"{option_id}.eventDescription") or loc.get(f"{option_id}.description") or "",
                    "note": note_map.get(option_id),
                }
            )
    return records


def write_dataset(target_path: pathlib.Path, version: str, options: list[dict]) -> None:
    data = {}
    if target_path.exists():
        data = json.loads(target_path.read_text(encoding="utf-8"))
    data["version"] = version
    data["options"] = options
    target_path.parent.mkdir(parents=True, exist_ok=True)
    target_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> None:
    args = parse_args()
    target_path = pathlib.Path(args.output) if args.output else ROOT / "data" / args.version / "neow" / "options.json"
    existing_options: list[dict] = []
    if target_path.exists():
        existing = json.loads(target_path.read_text(encoding="utf-8"))
        existing_options = existing.get("options", [])

    options = build_options(args.version, args.locale, extract_note_map(existing_options))
    write_dataset(target_path, args.version, options)
    print(f"Wrote {len(options)} Neow options to {target_path}")


if __name__ == "__main__":
    main()
