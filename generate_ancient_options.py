import argparse
import datetime
import json
import pathlib

ANCIENT_IDS = [
    "ALCHEMICAL_COFFER",
    "ARCHAIC_TOOTH",
    "ASTROLABE",
    "BEAUTIFUL_BRACELET",
    "BIIIG_HUG",
    "BLACK_STAR",
    "BLESSED_ANTLER",
    "BLOOD_SOAKED_ROSE",
    "BRILLIANT_SCARF",
    "CALLING_BELL",
    "CHOICES_PARADOX",
    "CLAWS",
    "CROSSBOW",
    "DELICATE_FROND",
    "DIAMOND_DIADEM",
    "DISTINGUISHED_CAPE",
    "DRIFTWOOD",
    "DUSTY_TOME",
    "ECTOPLASM",
    "ELECTRIC_SHRYMP",
    "EMPTY_CAGE",
    "FIDDLE",
    "FUR_COAT",
    "GLASS_EYE",
    "GLITTER",
    "GOLDEN_COMPASS",
    "IRON_CLUB",
    "JEWELED_MASK",
    "JEWELRY_BOX",
    "LORDS_PARASOL",
    "LOOMING_FRUIT",
    "MEAT_CLEAVER",
    "MUSIC_BOX",
    "NUTRITIOUS_SOUP",
    "PAELS_BLOOD",
    "PAELS_CLAW",
    "PAELS_EYE",
    "PAELS_FLESH",
    "PAELS_GROWTH",
    "PAELS_HORN",
    "PAELS_LEGION",
    "PAELS_TEARS",
    "PAELS_TOOTH",
    "PAELS_WING",
    "PANDORAS_BOX",
    "PHILOSOPHERS_STONE",
    "PRISMATIC_GEM",
    "PRESERVED_FOG",
    "PUMPKIN_CANDLE",
    "RADIANT_PEARL",
    "RUNIC_PYRAMID",
    "SAI",
    "SAND_CASTLE",
    "SEA_GLASS",
    "SEAL_OF_GOLD",
    "SERE_TALON",
    "SIGNET_RING",
    "SNECKO_EYE",
    "SOZU",
    "SPIKED_GAUNTLETS",
    "STORYBOOK",
    "TANXS_WHISTLE",
    "THROWING_AXE",
    "TOASTY_MITTENS",
    "TOUCH_OF_OROBAS",
    "TOY_BOX",
    "TRI_BOOMERANG",
    "VELVET_CHOKER",
    "VERY_HOT_COCOA",
    "WAR_HAMMER",
    "WHISPERING_EARRING",
    "YUMMY_COOKIE",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate Ancient option metadata from localization files.")
    parser.add_argument("--locale", default="eng", help="Localization code (default: eng)")
    parser.add_argument(
        "--output",
        default=None,
        help="Output file. Defaults to data/0.103.2/ancients/options.{locale}.json (options.json for eng).",
    )
    return parser.parse_args()


def load_localization(path: pathlib.Path) -> dict[str, str]:
    with path.open("r", encoding="utf-8") as source:
        return json.load(source)


def build_records(loc: dict[str, str]) -> list[dict]:
    records = []
    for relic_id in ANCIENT_IDS:
        title = loc.get(f"{relic_id}.title", relic_id.replace("_", " ").title())
        description = loc.get(f"{relic_id}.eventDescription") or loc.get(f"{relic_id}.description") or ""
        records.append(
            {
                "id": relic_id,
                "title": title,
                "description": description,
            }
        )
    records.sort(key=lambda item: item["id"])
    return records


def resolve_source_root(repo_root: pathlib.Path) -> pathlib.Path:
    # Try project dir first, then J: drive
    project_loc = repo_root / "Slay the Spire 2 源码（游戏源码，读取用）" / "localization"
    if project_loc.exists():
        return project_loc
    j_loc = pathlib.Path("J:/杀戮尖塔自定义roll种器/Slay the Spire 2 源码（游戏源码，读取用）") / "localization"
    if j_loc.exists():
        return j_loc
    raise SystemExit("Unable to locate the official source directory for localization files.")


def main() -> None:
    args = parse_args()

    repo_root = pathlib.Path(__file__).resolve().parent
    source_root = resolve_source_root(repo_root)
    source = source_root / args.locale / "relics.json"
    if not source.exists():
        raise SystemExit(f"Localization source not found: {source}")

    loc = load_localization(source)
    records = build_records(loc)

    payload = {
        "generatedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "locale": args.locale,
        "options": records,
    }

    suffix = "" if args.locale == "eng" else f".{args.locale}"
    default_name = f"options{suffix}.json"
    target_path = pathlib.Path(args.output) if args.output else repo_root / "data" / "0.103.2" / "ancients" / default_name
    target_path.parent.mkdir(parents=True, exist_ok=True)

    with target_path.open("w", encoding="utf-8") as target:
        json.dump(payload, target, indent=2, ensure_ascii=False)

    print(f"Wrote {len(records)} options to {target_path}")


if __name__ == "__main__":
    main()
