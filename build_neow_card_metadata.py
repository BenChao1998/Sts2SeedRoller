# -*- coding: utf-8 -*-
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent

GAME_ROOT = None
for entry in sorted(ROOT.iterdir()):
    if entry.is_dir() and entry.name.startswith("Slay the Spire 2"):
        GAME_ROOT = entry
        break

if GAME_ROOT is None:
    raise FileNotFoundError("Unable to locate 'Slay the Spire 2' source directory")

GAME_SRC = GAME_ROOT / "src"
CARD_POOL_DIR = GAME_SRC / "Core" / "Models" / "CardPools"
CARD_DIR = GAME_SRC / "Core" / "Models" / "Cards"
POTION_POOL_DIR = GAME_SRC / "Core" / "Models" / "PotionPools"
POTION_DIR = GAME_SRC / "Core" / "Models" / "Potions"
EPOCH_DIR = GAME_SRC / "Core" / "Timeline" / "Epochs"
TARGET_PATH = ROOT / "data" / "neow" / "options.json"

CARD_POOL_SPECS = {
    "Ironclad": CARD_POOL_DIR / "IroncladCardPool.cs",
    "Silent": CARD_POOL_DIR / "SilentCardPool.cs",
    "Defect": CARD_POOL_DIR / "DefectCardPool.cs",
    "Necrobinder": CARD_POOL_DIR / "NecrobinderCardPool.cs",
    "Regent": CARD_POOL_DIR / "RegentCardPool.cs",
}

POTION_POOL_SPECS = {
    "Ironclad": POTION_POOL_DIR / "IroncladPotionPool.cs",
    "Silent": POTION_POOL_DIR / "SilentPotionPool.cs",
    "Defect": POTION_POOL_DIR / "DefectPotionPool.cs",
    "Necrobinder": POTION_POOL_DIR / "NecrobinderPotionPool.cs",
    "Regent": POTION_POOL_DIR / "RegentPotionPool.cs",
}

COLORLESS_POOL_PATH = CARD_POOL_DIR / "ColorlessCardPool.cs"
SHARED_POTION_POOL_PATH = POTION_POOL_DIR / "SharedPotionPool.cs"

CARD_REGEX = re.compile(r"ModelDb\.Card<(?P<name>[A-Za-z0-9_]+)>\(\)")
RARITY_REGEX = re.compile(r":\s*base\s*\([^)]*CardRarity\.(?P<rarity>[A-Za-z0-9_]+)", re.DOTALL)
MULTIPLAYER_REGEX = re.compile(
    r"override\s+CardMultiplayerConstraint\s+MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.(?P<constraint>[A-Za-z0-9_]+)"
)
POTION_REGEX = re.compile(r"ModelDb\.Potion<(?P<name>[A-Za-z0-9_]+)>\(")
EPOCH_REF_REGEX = re.compile(r"return\s+(?P<epoch>[A-Za-z0-9_]+)\.Potions;")
POTION_RARITY_REGEX = re.compile(r"public\s+override\s+PotionRarity\s+Rarity\s*=>\s*PotionRarity\.(?P<rarity>[A-Za-z0-9_]+);")
CAMEL_RE = re.compile(r"([A-Za-z0-9]|(?<!^))([A-Z])")
WHITESPACE_RE = re.compile(r"\s+")
SPECIAL_RE = re.compile(r"[^A-Z0-9_]")


def slugify(name: str) -> str:
    text = CAMEL_RE.sub(r"\1_\2", name.strip())
    text = WHITESPACE_RE.sub("_", text.upper())
    return SPECIAL_RE.sub("", text)


def extract_card_list(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return CARD_REGEX.findall(text)


def extract_rarity(card_name: str) -> str:
    card_path = CARD_DIR / f"{card_name}.cs"
    if not card_path.exists():
        raise FileNotFoundError(f"Card source not found for {card_name}: {card_path}")
    text = card_path.read_text(encoding="utf-8")
    match = RARITY_REGEX.search(text)
    if not match:
        raise RuntimeError(f"Failed to parse rarity for card {card_name}")
    return match.group("rarity")


def extract_multiplayer_constraint(card_name: str) -> str:
    card_path = CARD_DIR / f"{card_name}.cs"
    text = card_path.read_text(encoding="utf-8")
    match = MULTIPLAYER_REGEX.search(text)
    if match:
        return match.group("constraint")
    return "None"


def extract_potion_pool(pool_path: Path) -> list[str]:
    if not pool_path.exists():
        raise FileNotFoundError(f"Missing potion pool file: {pool_path}")
    text = pool_path.read_text(encoding="utf-8")
    if pool_path == SHARED_POTION_POOL_PATH:
        return [slugify(name) for name in POTION_REGEX.findall(text)]

    match = EPOCH_REF_REGEX.search(text)
    if not match:
        raise RuntimeError(f"Unable to locate epoch reference in {pool_path}")
    epoch_name = match.group("epoch")
    epoch_path = EPOCH_DIR / f"{epoch_name}.cs"
    if not epoch_path.exists():
        raise FileNotFoundError(f"Missing epoch file: {epoch_path}")
    epoch_text = epoch_path.read_text(encoding="utf-8")
    return [slugify(name) for name in POTION_REGEX.findall(epoch_text)]


def extract_potion_metadata() -> list[dict[str, str]]:
    metadata: list[dict[str, str]] = []
    for potion_file in POTION_DIR.glob("*.cs"):
        text = potion_file.read_text(encoding="utf-8")
        match = POTION_RARITY_REGEX.search(text)
        if not match:
            continue
        potion_id = slugify(potion_file.stem)
        metadata.append(
            {
                "id": potion_id,
                "rarity": match.group("rarity"),
            }
        )
    return metadata


def build_metadata():
    pools: dict[str, list[str]] = {}
    all_cards: set[str] = set()
    for character, pool_path in CARD_POOL_SPECS.items():
        if not pool_path.exists():
            raise FileNotFoundError(f"Missing card pool file: {pool_path}")
        card_types = extract_card_list(pool_path)
        card_ids = [slugify(name) for name in card_types]
        pools[character] = card_ids
        all_cards.update(card_types)

    colorless_cards = extract_card_list(COLORLESS_POOL_PATH)
    colorless_pool = [slugify(name) for name in colorless_cards]
    all_cards.update(colorless_cards)

    metadata = []
    for card_name in sorted(all_cards):
        card_id = slugify(card_name)
        rarity = extract_rarity(card_name)
        constraint = extract_multiplayer_constraint(card_name)
        metadata.append(
            {
                "id": card_id,
                "rarity": rarity,
                "multiplayerConstraint": constraint,
            }
        )

    potion_pools: dict[str, list[str]] = {}
    for character, pool_path in POTION_POOL_SPECS.items():
        potion_pools[character] = extract_potion_pool(pool_path)
    shared_potions = extract_potion_pool(SHARED_POTION_POOL_PATH)
    potion_metadata = extract_potion_metadata()

    return pools, metadata, colorless_pool, potion_pools, shared_potions, potion_metadata


def main() -> None:
    if not TARGET_PATH.exists():
        raise FileNotFoundError(f"Dataset not found: {TARGET_PATH}")
    pools, metadata, colorless_pool, potion_pools, shared_potions, potion_metadata = build_metadata()
    with TARGET_PATH.open("r", encoding="utf-8") as fh:
        data = json.load(fh)
    output = {
        "options": data.get("options", []),
        "cards": data.get("cards", []),
        "potions": data.get("potions", []),
        "cardPools": pools,
        "cardMetadata": metadata,
        "colorlessCardPool": colorless_pool,
        "potionPools": potion_pools,
        "sharedPotionPool": shared_potions,
        "potionMetadata": potion_metadata,
    }
    TARGET_PATH.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote card metadata for {len(metadata)} cards and {len(potion_metadata)} potions into {TARGET_PATH}")


if __name__ == "__main__":
    main()
