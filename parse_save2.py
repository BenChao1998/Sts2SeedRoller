# -*- coding: utf-8 -*-
"""Parse the save file JSON and extract shop data from map_point_history."""
import json

with open("存档/1776344476.run", "r", encoding="utf-8") as f:
    data = json.load(f)

print(f"Top-level keys: {list(data.keys())}")
print(f"Seed: {data.get('seed')}")
print(f"Ascension: {data.get('ascension')}")
print()

# Check map_point_history
mph = data.get('map_point_history', [])
print(f"map_point_history: {len(mph)} acts")

for act_idx, act_points in enumerate(mph):
    print(f"\n  Act {act_idx+1}: {len(act_points)} points")
    for point_idx, point in enumerate(act_points):
        room_type = point.get('room_type', 'N/A')
        map_type = point.get('map_point_type', 'N/A')
        player_stats = point.get('player_stats', [])

        # Check for shop data
        if room_type == 'shop' or map_type == 'shop':
            print(f"\n  *** SHOP FOUND at Act {act_idx+1}, Point {point_idx} ***")
            print(f"  Full point: {json.dumps(point, indent=4)}")
        elif room_type == 'ancient' or map_type == 'ancient':
            pass  # Skip ancients for now
        elif room_type == 'event':
            pass  # Skip regular events
