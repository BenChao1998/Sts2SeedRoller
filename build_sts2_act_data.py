import argparse
import json
import re
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent


@dataclass(frozen=True)
class ActSpec:
    name: str
    number: int
    path: Path


@dataclass(frozen=True)
class SourceLayout:
    version: str
    src_root: Path
    act_dir: Path
    encounter_dir: Path
    modeldb_path: Path
    relic_pools_dir: Path
    relic_models_dir: Path


LIST_PATTERN = re.compile(r"(?:ModelDb\.)?(?:Event|Encounter|AncientEvent)<(?P<name>[A-Za-z0-9_]+)>")
ROOM_TYPE_PATTERN = re.compile(r"public override RoomType RoomType\s*=>\s*RoomType\.(?P<type>\w+);")
IS_WEAK_PATTERN = re.compile(r"public override bool IsWeak\s*=>\s*true", re.IGNORECASE)
TAGS_PATTERN = re.compile(r"public override IEnumerable<EncounterTag> Tags\s*=>(?P<body>.*?);", re.DOTALL)
ENCOUNTER_TAG_PATTERN = re.compile(r"EncounterTag\.([A-Za-z0-9_]+)")
RELIC_PATTERN = re.compile(r"ModelDb\.Relic<(?P<name>[A-Za-z0-9_]+)>")
RELIC_CLASS_PATTERN = re.compile(r"class\s+(?P<name>[A-Za-z0-9_]+)\s*:\s*RelicModel")
RARITY_PATTERN = re.compile(r"public\s+override\s+RelicRarity\s+Rarity\s*=>\s*RelicRarity\.(?P<rarity>\w+);")
CAMEL_RE = re.compile(r"([A-Za-z0-9]|(?<!^))([A-Z])")
WHITESPACE_RE = re.compile(r"\s+")
SPECIAL_RE = re.compile(r"[^A-Z0-9_]")


def resolve_source_layout(version: str) -> SourceLayout:
    candidates = [
        ROOT / f"Slay the Spire 2 版本{version}源码（游戏源码，读取用）",
        *sorted(ROOT.glob(f"Slay the Spire 2*{version}*源码*")),
    ]

    source_dir = next((path for path in candidates if (path / "src").exists()), None)
    if source_dir is None:
        raise FileNotFoundError(f"Could not locate game source directory for version {version}.")

    src_root = source_dir / "src"
    return SourceLayout(
        version=version,
        src_root=src_root,
        act_dir=src_root / "Core" / "Models" / "Acts",
        encounter_dir=src_root / "Core" / "Models" / "Encounters",
        modeldb_path=src_root / "Core" / "Models" / "ModelDb.cs",
        relic_pools_dir=src_root / "Core" / "Models" / "RelicPools",
        relic_models_dir=src_root / "Core" / "Models" / "Relics",
    )


def build_act_specs(layout: SourceLayout) -> list[ActSpec]:
    return [
        ActSpec("Overgrowth", 1, layout.act_dir / "Overgrowth.cs"),
        ActSpec("Hive", 2, layout.act_dir / "Hive.cs"),
        ActSpec("Glory", 3, layout.act_dir / "Glory.cs"),
    ]


def parse_shared_events(text: str) -> list[str]:
    pattern = r"AllSharedEvents\s*=>\s*[^;]+?{(?P<body>.*?)}\)\);"
    match = re.search(pattern, text, re.DOTALL)
    if not match:
        raise RuntimeError("Failed to locate AllSharedEvents block.")
    return LIST_PATTERN.findall(match.group("body"))


def get_shared_ancients(text: str) -> list[str]:
    pattern = r"AllSharedAncients\s*=>\s*[^;]+?\((?P<body>.*?)\);"
    match = re.search(pattern, text, re.DOTALL)
    if not match:
        raise RuntimeError("Failed to locate AllSharedAncients block.")
    ancients = LIST_PATTERN.findall(match.group("body"))
    return [normalize_id(name) for name in ancients]


def normalize_id(name: str) -> str:
    return name.upper()


def slugify(name: str) -> str:
    text = CAMEL_RE.sub(r"\1_\2", name.strip())
    text = WHITESPACE_RE.sub("_", text.upper())
    return SPECIAL_RE.sub("", text)


def extract_property_models(text: str, property_name: str) -> list[str]:
    pattern = rf"{property_name}\s*=>\s*[^;]+;"
    match = re.search(pattern, text, re.DOTALL)
    if not match:
        raise RuntimeError(f"Failed to locate {property_name} definition.")
    return LIST_PATTERN.findall(match.group(0))


def parse_act(spec: ActSpec) -> dict:
    text = spec.path.read_text(encoding="utf-8")
    encounters_section = re.search(
        r"GenerateAllEncounters\(\)\s*{.*?new.*?{(?P<body>.*?)}\);\s*}",
        text,
        re.DOTALL,
    )
    if not encounters_section:
        raise RuntimeError(f"Failed to parse encounters from {spec.path}")

    weak_count_match = re.search(r"NumberOfWeakEncounters\s*=>\s*(\d+);", text)
    base_rooms_match = re.search(r"BaseNumberOfRooms\s*=>\s*(\d+);", text)
    if not weak_count_match or not base_rooms_match:
        raise RuntimeError(f"Failed to parse room counts from {spec.path}")

    encounters = LIST_PATTERN.findall(encounters_section.group("body"))
    events = extract_property_models(text, "AllEvents")
    ancients = [normalize_id(name) for name in extract_property_models(text, "AllAncients")]

    return {
        "name": spec.name,
        "number": spec.number,
        "baseRooms": int(base_rooms_match.group(1)),
        "weakRooms": int(weak_count_match.group(1)),
        "events": events,
        "encounters": encounters,
        "ancients": ancients,
    }


def parse_encounter_meta(name: str, layout: SourceLayout) -> dict:
    path = layout.encounter_dir / f"{name}.cs"
    if not path.exists():
        raise FileNotFoundError(f"Encounter file not found for {name}: {path}")

    text = path.read_text(encoding="utf-8")
    room_type_match = ROOM_TYPE_PATTERN.search(text)
    if not room_type_match:
        raise RuntimeError(f"RoomType not found for encounter {name}")

    tags_match = TAGS_PATTERN.search(text)
    tags: list[str] = []
    if tags_match:
        tags = sorted(set(ENCOUNTER_TAG_PATTERN.findall(tags_match.group("body"))))

    return {
        "roomType": room_type_match.group("type"),
        "isWeak": bool(IS_WEAK_PATTERN.search(text)),
        "tags": tags,
    }


def build_relic_pool_data(layout: SourceLayout) -> dict:
    rarity_map = load_relic_rarities(layout)
    shared_sequence = parse_relic_pool(layout.relic_pools_dir / "SharedRelicPool.cs")
    characters = {
        "Ironclad": parse_relic_pool(layout.relic_pools_dir / "IroncladRelicPool.cs"),
        "Silent": parse_relic_pool(layout.relic_pools_dir / "SilentRelicPool.cs"),
        "Defect": parse_relic_pool(layout.relic_pools_dir / "DefectRelicPool.cs"),
        "Necrobinder": parse_relic_pool(layout.relic_pools_dir / "NecrobinderRelicPool.cs"),
        "Regent": parse_relic_pool(layout.relic_pools_dir / "RegentRelicPool.cs"),
    }

    return {
        "sharedSequence": shared_sequence,
        "characters": characters,
        "rarities": rarity_map,
    }


def parse_relic_pool(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return [slugify(name) for name in RELIC_PATTERN.findall(text)]


def load_relic_rarities(layout: SourceLayout) -> dict[str, str]:
    rarities: dict[str, str] = {}
    for file in layout.relic_models_dir.glob("*.cs"):
        text = file.read_text(encoding="utf-8")
        class_match = RELIC_CLASS_PATTERN.search(text)
        rarity_match = RARITY_PATTERN.search(text)
        if not class_match or not rarity_match:
            continue
        rarities[slugify(class_match.group("name"))] = rarity_match.group("rarity")
    return rarities


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="0.103.2", help="Game version to extract from.")
    args = parser.parse_args()

    layout = resolve_source_layout(args.version)
    act_specs = build_act_specs(layout)
    modeldb_text = layout.modeldb_path.read_text(encoding="utf-8")
    shared_events = parse_shared_events(modeldb_text)
    shared_ancients = get_shared_ancients(modeldb_text)
    acts = [parse_act(spec) for spec in act_specs]
    required_encounters = sorted({enc for act in acts for enc in act["encounters"]})
    encounter_meta = {name: parse_encounter_meta(name, layout) for name in required_encounters}
    relic_pools = build_relic_pool_data(layout)

    output = {
        "sharedEvents": shared_events,
        "sharedAncients": shared_ancients,
        "encounters": encounter_meta,
        "acts": acts,
        "relicPools": relic_pools,
    }

    target_dir = ROOT / "data" / args.version / "sts2"
    target_dir.mkdir(parents=True, exist_ok=True)
    target_path = target_dir / "acts.json"
    target_path.write_text(json.dumps(output, indent=2, sort_keys=True, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {target_path} with {len(acts)} acts, {len(encounter_meta)} encounters.")


if __name__ == "__main__":
    main()
