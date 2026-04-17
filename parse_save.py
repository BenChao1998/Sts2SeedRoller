# -*- coding: utf-8 -*-
"""Parse the save file JSON and extract shop data."""
import json

with open("存档/1776344476.run", "r", encoding="utf-8") as f:
    data = json.load(f)

print(f"Top-level keys: {list(data.keys())}")
print()

# Check players
players = data.get('players', [])
print(f"Players count: {len(players)}")
if players:
    p = players[0]
    print(f"  Player keys: {list(p.keys())}")

    rs = p.get('run_state', {})
    print(f"  run_state keys: {list(rs.keys())}")

    # Event history
    events = rs.get('event_history', [])
    print(f"\n=== Event History ({len(events)} events) ===")
    for e in events:
        room_type = e.get('room_type', 'N/A')
        map_type = e.get('map_point_type', 'N/A')
        print(f"  {room_type} / {map_type}")

    # Check for shop event
    print("\n=== Shop Event Details ===")
    for e in events:
        if e.get('room_type') == 'shop' or e.get('map_point_type') == 'shop':
            print(json.dumps(e, indent=2))

    # Neow data
    neow = rs.get('neow', {})
    print(f"\n=== Neow Data ===")
    for k, v in neow.items():
        print(f"  {k}: {v}")

    # Check for player_odds or rng state
    for key in ['player_odds', 'rng', 'seed_state', 'card_odds']:
        if key in rs:
            print(f"\n=== {key} ===")
            print(json.dumps(rs[key], indent=2)[:500])
