# Sts2SeedRoller

Standalone tooling for recreating Slay the Spire 2 Neow opening rewards without loading Godot or the official MegaCrit assemblies.

## Repository Layout
- `Slay the Spire 2 源码/` - full game source dump used for data extraction (folder name intentionally matches the original instructions).
- `data/neow/options.json` - localized Neow option metadata exported from the game data.
- `src/SeedModel/` - reusable .NET library containing deterministic RNG helpers and the Neow option generator.
- `src/DataExtractor/` - CLI that converts `seed_info.json` from the source dump into our standalone JSON dataset.
- `src/SeedCli/` - thin CLI that loads the dataset and prints Neow options for one or many seeds.
- `src/SeedUi/` - WPF 界面，直接引用 `SeedModel` 实现可视化筛选（不依赖 CLI）。

## Typical Workflow
1. **Export data** (only needed when the game files change):
   ```powershell
   dotnet run --project src/DataExtractor/DataExtractor.csproj -- `
     --source "Slay the Spire 2 源码/seed_info.json" --output data/neow/options.json
   ```
2. **Inspect Neow rolls** (single seed):
   ```powershell
   dotnet run --project src/SeedCli/SeedCli.csproj -- --seed 123456789 --scroll true
   ```
3. **使用 UI**：
   ```powershell
   dotnet run --project src/SeedUi/SeedUi.csproj
   ```
   在界面上填写数据路径（默认 `data/neow/options.json`），配置种子范围 / 筛选条件后点击“开始 Roll”即可，遗物/卡牌/药水列表均来自数据集本身。
4. **批量 roll**（CLI 参数示例）:
   ```powershell
   # 连续 10 个种子，步长 1
   dotnet run --project src/SeedCli/SeedCli.csproj -- --seed 100 --count 10

   # 从十六进制 seed 开始，每次 +0x10，共 5 组
   dotnet run --project src/SeedCli/SeedCli.csproj -- --seed 0xABCDEF01 --count 5 --seed-step 0x10

   # 只保留出现 NEOWS_TORMENT（送 涅奥之怒）的种子
   dotnet run --project src/SeedCli/SeedCli.csproj -- --count 200 --filter-relic NEOWS_TORMENT --filter-card NEOWS_FURY
   ```

### SeedCli 参数速览
| 参数 | 说明 | 默认值 |
| --- | --- | --- |
| `--seed` | 基础种子（十进制或 `0x` 十六进制） | `0` |
| `--count` | 连续 roll 的次数 | `1` |
| `--seed-step` | 每次 roll 种子递增值（支持十六进制） | `1` |
| `--character` | 角色：`ironclad`/`silent`/`defect`/`necrobinder`/`regent` | `ironclad` |
| `--players` | 玩家数量（>1 会启用多人专属选项） | `1` |
| `--scroll` / `--scroll-boxes` | 是否允许 Scroll Boxes（true/false/1/0） | `false` |
| `--data` | 自定义 Neow 数据集路径 | `data/neow/options.json` |
| `--filter-kind` | 只保留 `positive` / `negative` 选项 | *(空，表示不过滤)* |
| `--filter-relic` | 逗号分隔的遗物 ID，例如 `ARCANE_SCROLL,NEOWS_TORMENT` | *(空)* |
| `--filter-relic-term` | 遗物关键字（大小写不敏感，支持中文），例如 `卷轴` | *(空)* |
| `--filter-card` | 按卡牌 ID 匹配奖励（目前支持 NEOWS_FURY / GREED / STRIKE/DEFEND 系列） | *(空)* |
| `--filter-potion` | 按药水 ID 匹配奖励（待后续实现更多奖励详情时生效） | *(空)* |

> **说明**：卡牌筛选基于我们自行重建的 Neow 奖励推导，目前已覆盖确定性的加卡奖励（如 `NEOWS_TORMENT` 送 `NEOWS_FURY`，`CURSED_PEARL` 送 `GREED`，以及 `LARGE_CAPSULE` 根据角色送基础打击/防御）。其它随机奖励会在后续补齐更完整的数据和 RNG 逻辑后陆续支持。

## Next Steps
- Flesh out modifier-driven Neow paths（当前仍未实现，会抛出 NotSupportedException）。
- Extract card/relic lookup tables so the CLI can optionally show multi-language text or richer payloads.
- Mirror the in-game RNG counter offsets for full parity once more systems are ported over, and add regression tests that diff against captured vanilla seeds.
