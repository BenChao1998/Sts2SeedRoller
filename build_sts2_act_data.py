import json
import re
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent
GAME_SRC = ROOT / "Slay the Spire 2 源码（游戏源码，读取用）" / "src"
ACT_DIR = GAME_SRC / "Core" / "Models" / "Acts"
ENCOUNTER_DIR = GAME_SRC / "Core" / "Models" / "Encounters"
MODELDB_PATH = GAME_SRC / "Core" / "Models" / "ModelDb.cs"
RELIC_POOLS_DIR = GAME_SRC / "Core" / "Models" / "RelicPools"
RELIC_MODELS_DIR = GAME_SRC / "Core" / "Models" / "Relics"


@dataclass(frozen=True)
class ActSpec:
    name: str
    number: int
    path: Path


ACT_SPECS = [
    ActSpec("Overgrowth", 1, ACT_DIR / "Overgrowth.cs"),
    ActSpec("Hive", 2, ACT_DIR / "Hive.cs"),
    ActSpec("Glory", 3, ACT_DIR / "Glory.cs"),
]

LIST_PATTERN = re.compile(r"(?:ModelDb\.)?(?:Event|Encounter|AncientEvent)<(?P<name>[A-Za-z0-9_]+)>")
ROOM_TYPE_PATTERN = re.compile(r"public override RoomType RoomType\s*=>\s*RoomType\.(?P<type>\w+);")
IS_WEAK_PATTERN = re.compile(r"public override bool IsWeak\s*=>\s*true", re.IGNORECASE)
TAGS_PATTERN = re.compile(r"public override IEnumerable<EncounterTag> Tags\s*=>(?P<body>.*?);", re.DOTALL)
ENCOUNTER_TAG_PATTERN = re.compile(r"EncounterTag\.([A-Za-z0-9_]+)")
RELIC_PATTERN = re.compile(r"ModelDb\.Relic<(?P<name>[A-Za-z0-9_]+)>")
RELIC_CLASS_PATTERN = re.compile(r"class\s+(?P<name>[A-Za-z0-9_]+)\s*:\s*RelicModel")
RARITY_PATTERN = re.compile(r"public\s+override\s+RelicRarity\s+Rarity\s*=>\s*RelicRarity\.(?P<rarity>\w+);")


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


def parse_encounter_meta(name: str) -> dict:
    path = ENCOUNTER_DIR / f"{name}.cs"
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


def build_relic_pool_data() -> dict:
    rarity_map = load_relic_rarities()
    shared_sequence = parse_relic_pool(RELIC_POOLS_DIR / "SharedRelicPool.cs")
    characters = {
        "Ironclad": parse_relic_pool(RELIC_POOLS_DIR / "IroncladRelicPool.cs"),
        "Silent": parse_relic_pool(RELIC_POOLS_DIR / "SilentRelicPool.cs"),
        "Defect": parse_relic_pool(RELIC_POOLS_DIR / "DefectRelicPool.cs"),
        "Necrobinder": parse_relic_pool(RELIC_POOLS_DIR / "NecrobinderRelicPool.cs"),
        "Regent": parse_relic_pool(RELIC_POOLS_DIR / "RegentRelicPool.cs"),
    }

    return {
        "sharedSequence": shared_sequence,
        "characters": characters,
        "rarities": rarity_map,
    }


def parse_relic_pool(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return RELIC_PATTERN.findall(text)


def load_relic_rarities() -> dict[str, str]:
    rarities: dict[str, str] = {}
    for file in RELIC_MODELS_DIR.glob("*.cs"):
        text = file.read_text(encoding="utf-8")
        class_match = RELIC_CLASS_PATTERN.search(text)
        rarity_match = RARITY_PATTERN.search(text)
        if not class_match or not rarity_match:
            continue
        rarities[class_match.group("name")] = rarity_match.group("rarity")
    return rarities


def main() -> None:
    modeldb_text = MODELDB_PATH.read_text(encoding="utf-8")
    shared_events = parse_shared_events(modeldb_text)
    shared_ancients = get_shared_ancients(modeldb_text)
    acts = [parse_act(spec) for spec in ACT_SPECS]
    required_encounters = sorted({enc for act in acts for enc in act["encounters"]})
    encounter_meta = {name: parse_encounter_meta(name) for name in required_encounters}
    relic_pools = build_relic_pool_data()

    output = {
        "sharedEvents": shared_events,
        "sharedAncients": shared_ancients,
        "encounters": encounter_meta,
        "acts": acts,
        "relicPools": relic_pools,
    }

    target_dir = ROOT / "data" / "sts2"
    target_dir.mkdir(parents=True, exist_ok=True)
    target_path = target_dir / "acts.json"
    target_path.write_text(json.dumps(output, indent=2, sort_keys=True, ensure_ascii=False))
    print(f"Wrote {target_path} with {len(acts)} acts, {len(encounter_meta)} encounters.")


if __name__ == "__main__":
    main()
