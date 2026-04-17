# -*- coding: utf-8 -*-
"""Simple test to find exact seed by brute-forcing small search space."""
import json

with open("E:/github project/Sts2SeedRoller/data/neow/options.json", "r", encoding="utf-8") as f:
    data = json.load(f)

def get_hash(text):
    hash1 = 352654597
    hash2 = hash1
    for i in range(0, len(text), 2):
        hash1 = ((hash1 << 5) + hash1) ^ ord(text[i])
        if i == len(text) - 1:
            break
        hash2 = ((hash2 << 5) + hash2) ^ ord(text[i + 1])
    return hash1 + hash2 * 1566083941

class DotNetRng:
    def __init__(self, seed):
        self.state = seed & 0x7FFFFFFF
        self.counter = 0

    def next_int(self, max_exclusive):
        self.counter += 1
        self.state = (self.state * 1103515245 + 12345) & 0x7FFFFFFF
        return self.state % max_exclusive

    def next_float(self):
        self.counter += 1
        self.state = (self.state * 1103515245 + 12345) & 0x7FFFFFFF
        return self.state / 0x7FFFFFFF

    def shuffle(self, lst):
        for i in range(len(lst) - 1, 0, -1):
            j = self.next_int(i + 1)
            lst[i], lst[j] = lst[j], lst[i]

    def scale_float(self, t, min_v, max_v):
        return min_v + t * (max_v - min_v)


card_meta = {c["id"]: c for c in data["cardMetadata"]}
relic_meta = {r["id"]: r for r in data["relicMetadata"]}

def roll_card_rarity(rng):
    r = rng.next_float()
    return "Common" if r < 0.54 else "Uncommon" if r < 0.91 else "Rare"

def simulate_minimal(seed_text, raw_seed, shuffle_gb=True):
    """Minimal simulation - just colored and colorless cards."""
    rng = DotNetRng(raw_seed)

    # Build grab bag
    all_relics = []
    for v in data["relicPools"].values():
        all_relics.extend(v)
    by_rarity = {}
    for rid in all_relics:
        if rid in ("THE_COURIER", "OLD_COIN"):
            continue
        if rid not in relic_meta:
            continue
        rarity = relic_meta[rid]["rarity"]
        if rarity not in ("Common", "Uncommon", "Rare", "Shop"):
            continue
        by_rarity.setdefault(rarity, []).append(rid)

    if shuffle_gb:
        for lst in by_rarity.values():
            rng.shuffle(lst)

    disc_slot = rng.next_int(5)

    # Colored cards
    pool = data["cardPools"]["Ironclad"]
    selected = set()
    colored_types = ["Attack", "Attack", "Skill", "Skill", "Power"]
    colored = []
    for i in range(5):
        ctype = colored_types[i]
        rarity = roll_card_rarity(rng)
        cands = [c for c in pool if c not in selected]
        picks = [c for c in cands
                 if card_meta.get(c, {}).get("type") == ctype
                 and card_meta.get(c, {}).get("rarity") == rarity]
        if not picks:
            nr = {"Common": "Uncommon", "Uncommon": "Rare", "Rare": "Rare"}.get(rarity, "Rare")
            picks = [c for c in cands
                     if card_meta.get(c, {}).get("type") == ctype
                     and card_meta.get(c, {}).get("rarity") == nr]
        if not picks:
            picks = [c for c in cands if card_meta.get(c, {}).get("type") == ctype]
        if not picks:
            picks = cands[:5]
        card = picks[rng.next_int(len(picks))]
        selected.add(card)
        colored.append(card)

    # Colorless cards
    cl_pool = data["colorlessCardPool"]
    cl_selected = set()
    colorless = []
    for rarity in ["Uncommon", "Rare"]:
        cands = [c for c in cl_pool if c not in cl_selected
                 and card_meta.get(c, {}).get("rarity") == rarity]
        if not cands:
            cands = [c for c in cl_pool if c not in cl_selected]
        card = cands[rng.next_int(len(cands))]
        cl_selected.add(card)
        colorless.append(card)

    return {
        "colored": colored,
        "colorless": colorless,
        "disc_slot": disc_slot,
    }


seed_text = "8KEN3ARCS7"
h_seed = get_hash(seed_text)
h_shops = get_hash("shops")
MASK = 0x7FFFFFFF

# The hash is a huge number. Let me check: maybe the shops RNG seed is derived
# from a DIFFERENT hash function. Let me also check what the ACTUAL seed values look like.

# Key question: what's the relationship between the Neow RNG and shops RNG?
# Neow uses: SeedFormatter.ToUIntSeed(seed_text) -> hash(normalized)
# Neow generator then uses this for everything.

# What if shops RNG uses the SAME uint seed?
neow_seed = h_seed & MASK
print(f"Neow uint seed (masked): {neow_seed}")

# What if shops RNG seed is just the masked hash?
# shopsSeed = hash(seed) & 0x7FFFFFFF
# Let me test this as well

derivations_simple = [
    ("hash(seed)", h_seed),
    ("hash(seed) [31bit]", h_seed & MASK),
    ("hash(seed)+1", h_seed + 1),
    ("hash(seed)+1 [31bit]", (h_seed + 1) & MASK),
    ("hash(seed)+hshops", h_seed + h_shops),
    ("hash(seed)+hshops [31bit]", (h_seed + h_shops) & MASK),
    ("hash(seed)+1+hshops", h_seed + 1 + h_shops),
    ("hash(seed)+1+hshops [31bit]", (h_seed + 1 + h_shops) & MASK),
    ("hash(seed+shops)", get_hash(seed_text + "shops")),
    ("hash(seed+shops) [31bit]", get_hash(seed_text + "shops") & MASK),
    ("hash(shops+seed)", get_hash("shops" + seed_text)),
    ("hash(shops+seed) [31bit]", get_hash("shops" + seed_text) & MASK),
]

exp_colored = ["RAMPAGE", "FIGHT_ME", "BLOODLETTING", "TREMBLE", "VICIOUS"]
exp_colorless = ["SEEKER_STRIKE", "ALCHEMIZE"]

print(f"\nGround truth:")
print(f"  Colored:   {exp_colored}")
print(f"  Colorless: {exp_colorless}")
print()

# Let me also verify: what rarity does SEEKER_STRIKE have in our data?
seeker = card_meta.get("SEEKER_STRIKE", {})
alchemy = card_meta.get("ALCHEMIZE", {})
print(f"SEEKER_STRIKE rarity: {seeker.get('rarity')}, type: {seeker.get('type')}")
print(f"ALCHEMIZE rarity: {alchemy.get('rarity')}, type: {alchemy.get('type')}")

# What about the colorless pool size?
print(f"\nColorless pool: {len(data['colorlessCardPool'])} cards")
cl_uncommon = [c for c in data['colorlessCardPool'] if card_meta.get(c, {}).get('rarity') == 'Uncommon']
cl_rare = [c for c in data['colorlessCardPool'] if card_meta.get(c, {}).get('rarity') == 'Rare']
print(f"  Uncommon: {len(cl_uncommon)}")
print(f"  Rare: {len(cl_rare)}")
print(f"  SEEKER_STRIKE index in Uncommon: {cl_uncommon.index('SEEKER_STRIKE') if 'SEEKER_STRIKE' in cl_uncommon else 'NOT FOUND'}")
print(f"  ALCHEMIZE index in Rare: {cl_rare.index('ALCHEMIZE') if 'ALCHEMIZE' in cl_rare else 'NOT FOUND'}")

print(f"\n{'='*60}")
print("Testing derivations:")
for name, shops_seed in derivations_simple:
    for shuffle in [True, False]:
        mode = "S" if shuffle else "_"
        r = simulate_minimal(seed_text, shops_seed, shuffle)
        cm = r["colored"] == exp_colored
        clm = r["colorless"] == exp_colorless
        if cm or clm:
            print(f"[{mode}] {name}: colored={r['colored']}, colorless={r['colorless']}")
            if cm and clm:
                print(f"  *** FULL MATCH ***")

print("\nLet's check what SEEKER_STRIKE's position would be in a fresh colorless pool:")
cl_pool_sorted = sorted(data['colorlessCardPool'], key=lambda c: card_meta.get(c, {}).get('rarity', ''))
cl_uncommon_sorted = [c for c in data['colorlessCardPool'] if card_meta.get(c, {}).get('rarity') == 'Uncommon']
cl_rare_sorted = [c for c in data['colorlessCardPool'] if card_meta.get(c, {}).get('rarity') == 'Rare']
print(f"Uncommon colorless pool ({len(cl_uncommon_sorted)}): {cl_uncommon_sorted}")
print(f"SEEKER_STRIKE index: {cl_uncommon_sorted.index('SEEKER_STRIKE')}")
print(f"Rare colorless pool ({len(cl_rare_sorted)}): {cl_rare_sorted}")
print(f"ALCHEMIZE index: {cl_rare_sorted.index('ALCHEMIZE')}")

print("\nLet's also check: what's the colorless pool order in the JSON?")
cl_json = data['colorlessCardPool']
print(f"SEEKER_STRIKE JSON index: {cl_json.index('SEEKER_STRIKE')}")
print(f"ALCHEMIZE JSON index: {cl_json.index('ALCHEMIZE')}")
