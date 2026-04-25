# Relic Visibility Probe

- Seed: `Y7T3FK52HA` (`0xEB92B470`)
- Character: `Silent`
- Samples per profile: `8000`
- Early window: first `5` relic opportunities
- Bag init: `official DLL RelicGrabBag.Populate`
- Shared bag size: `115`
- Player bag size: `122`
- Act3-only gate tracked relics: `17`

## 脚本边界

- 这是“概率版曝光预测”，不是整局掉落顺序的精确还原。
- 当前只统计 4 类玩家最容易感知到的 relic 曝光：宝箱、精英、商店陈列、Act2/Act3 古神选项。
- 初始 relic bag 使用官方 DLL 生成。
- 精英/商店会消费 `Rewards` RNG，但这里仍然是路线画像下的 Monte Carlo，不是完整地图与奖励链还原。

## 第二幕 / 第三幕古神

### Act 2: Tezcatara (`TEZCATARA`)

- 营养汤 (`NUTRITIOUS_SOUP`)
- 大～抱抱 (`BIIIG_HUG`)
- 黄金罗盘 (`GOLDEN_COMPASS`)

### Act 3: Vakuu (`VAKUU`)

- 血染玫瑰 (`BLOOD_SOAKED_ROSE`)
- 卓越斗篷 (`DISTINGUISHED_CAPE`)
- 领主阳伞 (`LORDS_PARASOL`)

## 路线画像结果

### Balanced

平衡路线：默认会进少量商店、打中等数量精英，古神有中等概率看到。

- Act 1: treasure `2 (45%), 3 (40%), 1 (15%)` / elite `1 (35%), 2 (50%), 3 (15%)` / shop `0 (25%), 1 (60%), 2 (15%)` / ancient chance `0%`
- Act 2: treasure `1 (25%), 2 (55%), 3 (20%)` / elite `1 (25%), 2 (50%), 3 (25%)` / shop `0 (30%), 1 (55%), 2 (15%)` / ancient chance `68%`
- Act 3: treasure `1 (35%), 2 (50%), 3 (15%)` / elite `1 (30%), 2 (45%), 3 (25%)` / shop `0 (35%), 1 (50%), 2 (15%)` / ancient chance `62%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 小邮箱 (`TINY_MAILBOX`) | 100.0% | 100.0% | 1.25 | Treasure |
| 2 | 红面具 (`RED_MASK`) | 94.6% | 100.0% | 3.74 | Treasure |
| 3 | 化学物X (`CHEMICAL_X`) | 79.1% | 97.4% | 3.50 | Shop |
| 4 | 永冻冰晶 (`PERMAFROST`) | 62.0% | 84.8% | 4.05 | Shop |
| 5 | 弹珠袋 (`BAG_OF_MARBLES`) | 61.9% | 99.7% | 5.32 | Elite |
| 6 | 小血瓶 (`BLOOD_VIAL`) | 61.7% | 99.0% | 4.96 | Shop |
| 7 | 孙子兵法 (`ART_OF_WAR`) | 43.3% | 72.5% | 6.72 | Elite |
| 8 | 闪亮口红 (`SPARKLING_ROUGE`) | 39.8% | 85.2% | 6.66 | Elite |
| 9 | 锚 (`ANCHOR`) | 33.3% | 100.0% | 6.27 | Treasure |
| 10 | 苦无 (`KUNAI`) | 29.5% | 54.5% | 6.71 | Shop |
| 11 | 异蛇头骨 (`SNECKO_SKULL`) | 19.7% | 87.5% | 8.68 | Elite |
| 12 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 10.9% | 73.6% | 8.44 | Shop |
| 13 | 肮脏地毯 (`DINGY_RUG`) | 8.7% | 82.2% | 8.50 | Shop |
| 14 | 大～抱抱 (`BIIIG_HUG`) | 8.2% | 68.6% | 7.50 | Ancient Act2 |
| 15 | 黄金罗盘 (`GOLDEN_COMPASS`) | 8.2% | 68.6% | 7.50 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 小邮箱 (`TINY_MAILBOX`) | 100.0% | 100.0% | 1.25 | Treasure |
| 2 | 红面具 (`RED_MASK`) | 100.0% | 94.6% | 3.74 | Treasure |
| 3 | 锚 (`ANCHOR`) | 100.0% | 33.3% | 6.27 | Treasure |
| 4 | 弹珠袋 (`BAG_OF_MARBLES`) | 99.7% | 61.9% | 5.32 | Elite |
| 5 | 小血瓶 (`BLOOD_VIAL`) | 99.0% | 61.7% | 4.96 | Shop |
| 6 | 斗篷扣 (`CLOAK_CLASP`) | 98.7% | 2.0% | 9.03 | Treasure |
| 7 | 化学物X (`CHEMICAL_X`) | 97.4% | 79.1% | 3.50 | Shop |
| 8 | 异蛇头骨 (`SNECKO_SKULL`) | 87.5% | 19.7% | 8.68 | Elite |
| 9 | 闪亮口红 (`SPARKLING_ROUGE`) | 85.2% | 39.8% | 6.66 | Elite |
| 10 | 永冻冰晶 (`PERMAFROST`) | 84.8% | 62.0% | 4.05 | Shop |
| 11 | 灯笼 (`LANTERN`) | 84.3% | 0.0% | 13.79 | Treasure |
| 12 | 肮脏地毯 (`DINGY_RUG`) | 82.2% | 8.7% | 8.50 | Shop |
| 13 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 73.6% | 10.9% | 8.44 | Shop |
| 14 | 孙子兵法 (`ART_OF_WAR`) | 72.5% | 43.3% | 6.72 | Elite |
| 15 | 大～抱抱 (`BIIIG_HUG`) | 68.6% | 8.2% | 7.50 | Ancient Act2 |
| 16 | 黄金罗盘 (`GOLDEN_COMPASS`) | 68.6% | 8.2% | 7.50 | Ancient Act2 |
| 17 | 营养汤 (`NUTRITIOUS_SOUP`) | 68.6% | 8.2% | 7.50 | Ancient Act2 |
| 18 | 血染玫瑰 (`BLOOD_SOAKED_ROSE`) | 61.7% | 0.0% | 12.86 | Ancient Act3 |
| 19 | 卓越斗篷 (`DISTINGUISHED_CAPE`) | 61.7% | 0.0% | 12.86 | Ancient Act3 |
| 20 | 领主阳伞 (`LORDS_PARASOL`) | 61.7% | 0.0% | 12.86 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 小邮箱 (`TINY_MAILBOX`) / 永冻冰晶 (`PERMAFROST`) / 小血瓶 (`BLOOD_VIAL`) / 化学物X (`CHEMICAL_X`) / 弹珠袋 (`BAG_OF_MARBLES`) / 红面具 (`RED_MASK`) / 闪亮口红 (`SPARKLING_ROUGE`)
- Sample 2: 小邮箱 (`TINY_MAILBOX`) / 苦无 (`KUNAI`) / 永冻冰晶 (`PERMAFROST`) / 化学物X (`CHEMICAL_X`) / 弹珠袋 (`BAG_OF_MARBLES`) / 红面具 (`RED_MASK`) / 异蛇头骨 (`SNECKO_SKULL`)
- Sample 3: 小邮箱 (`TINY_MAILBOX`) / 闪亮口红 (`SPARKLING_ROUGE`) / 红面具 (`RED_MASK`) / 永冻冰晶 (`PERMAFROST`) / 小血瓶 (`BLOOD_VIAL`) / 化学物X (`CHEMICAL_X`) / 双截棍 (`NUNCHAKU`)

### Aggressive

多精英路线：更偏向提早打精英，商店更少，古神概率略低。

- Act 1: treasure `1 (35%), 2 (50%), 3 (15%)` / elite `2 (45%), 3 (40%), 1 (15%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `0%`
- Act 2: treasure `1 (35%), 2 (45%), 3 (20%)` / elite `2 (45%), 3 (40%), 1 (15%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `60%`
- Act 3: treasure `1 (45%), 2 (40%), 3 (15%)` / elite `2 (45%), 3 (35%), 1 (20%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `56%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 小邮箱 (`TINY_MAILBOX`) | 100.0% | 100.0% | 1.86 | Treasure |
| 2 | 孙子兵法 (`ART_OF_WAR`) | 88.9% | 94.3% | 2.00 | Elite |
| 3 | 红面具 (`RED_MASK`) | 79.3% | 100.0% | 4.75 | Treasure |
| 4 | 弹珠袋 (`BAG_OF_MARBLES`) | 57.7% | 99.0% | 5.52 | Elite |
| 5 | 化学物X (`CHEMICAL_X`) | 55.0% | 91.4% | 5.94 | Shop |
| 6 | 闪亮口红 (`SPARKLING_ROUGE`) | 43.0% | 88.2% | 6.08 | Elite |
| 7 | 小血瓶 (`BLOOD_VIAL`) | 40.1% | 94.4% | 7.72 | Shop |
| 8 | 永冻冰晶 (`PERMAFROST`) | 24.8% | 65.8% | 7.62 | Shop |
| 9 | 不休陀螺 (`UNCEASING_TOP`) | 24.2% | 58.9% | 8.05 | Elite |
| 10 | 苦无 (`KUNAI`) | 21.5% | 48.7% | 7.90 | Shop |
| 11 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 20.9% | 62.7% | 8.69 | Shop |
| 12 | 异蛇头骨 (`SNECKO_SKULL`) | 14.6% | 85.1% | 8.42 | Elite |
| 13 | 锚 (`ANCHOR`) | 13.0% | 100.0% | 7.60 | Treasure |
| 14 | 大～抱抱 (`BIIIG_HUG`) | 10.1% | 60.6% | 7.03 | Ancient Act2 |
| 15 | 黄金罗盘 (`GOLDEN_COMPASS`) | 10.1% | 60.6% | 7.03 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 小邮箱 (`TINY_MAILBOX`) | 100.0% | 100.0% | 1.86 | Treasure |
| 2 | 红面具 (`RED_MASK`) | 100.0% | 79.3% | 4.75 | Treasure |
| 3 | 锚 (`ANCHOR`) | 100.0% | 13.0% | 7.60 | Treasure |
| 4 | 弹珠袋 (`BAG_OF_MARBLES`) | 99.0% | 57.7% | 5.52 | Elite |
| 5 | 斗篷扣 (`CLOAK_CLASP`) | 94.4% | 0.4% | 10.54 | Treasure |
| 6 | 小血瓶 (`BLOOD_VIAL`) | 94.4% | 40.1% | 7.72 | Shop |
| 7 | 孙子兵法 (`ART_OF_WAR`) | 94.3% | 88.9% | 2.00 | Elite |
| 8 | 化学物X (`CHEMICAL_X`) | 91.4% | 55.0% | 5.94 | Shop |
| 9 | 闪亮口红 (`SPARKLING_ROUGE`) | 88.2% | 43.0% | 6.08 | Elite |
| 10 | 异蛇头骨 (`SNECKO_SKULL`) | 85.1% | 14.6% | 8.42 | Elite |
| 11 | 永冻冰晶 (`PERMAFROST`) | 65.8% | 24.8% | 7.62 | Shop |
| 12 | 肮脏地毯 (`DINGY_RUG`) | 63.6% | 2.9% | 10.57 | Shop |
| 13 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 62.7% | 20.9% | 8.69 | Shop |
| 14 | 灯笼 (`LANTERN`) | 62.0% | 0.0% | 14.14 | Treasure |
| 15 | 大～抱抱 (`BIIIG_HUG`) | 60.6% | 10.1% | 7.03 | Ancient Act2 |
| 16 | 黄金罗盘 (`GOLDEN_COMPASS`) | 60.6% | 10.1% | 7.03 | Ancient Act2 |
| 17 | 营养汤 (`NUTRITIOUS_SOUP`) | 60.6% | 10.1% | 7.03 | Ancient Act2 |
| 18 | 双截棍 (`NUNCHAKU`) | 60.5% | 8.7% | 9.21 | Elite |
| 19 | 不休陀螺 (`UNCEASING_TOP`) | 58.9% | 24.2% | 8.05 | Elite |
| 20 | 血染玫瑰 (`BLOOD_SOAKED_ROSE`) | 56.2% | 0.0% | 11.90 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 小邮箱 (`TINY_MAILBOX`) / 孙子兵法 (`ART_OF_WAR`) / 红面具 (`RED_MASK`) / 小血瓶 (`BLOOD_VIAL`) / 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) / 化学物X (`CHEMICAL_X`) / 锚 (`ANCHOR`)
- Sample 2: 孙子兵法 (`ART_OF_WAR`) / 小邮箱 (`TINY_MAILBOX`) / 不休陀螺 (`UNCEASING_TOP`) / 小血瓶 (`BLOOD_VIAL`) / 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) / 化学物X (`CHEMICAL_X`) / 闪亮口红 (`SPARKLING_ROUGE`)
- Sample 3: 孙子兵法 (`ART_OF_WAR`) / 苦无 (`KUNAI`) / 永冻冰晶 (`PERMAFROST`) / 化学物X (`CHEMICAL_X`) / 小邮箱 (`TINY_MAILBOX`) / 红面具 (`RED_MASK`) / 锚 (`ANCHOR`)

### Shopper

多商店路线：更容易看到商店陈列 relic，精英数量更保守，古神概率略高。

- Act 1: treasure `2 (40%), 3 (45%), 1 (15%)` / elite `1 (50%), 2 (35%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `0%`
- Act 2: treasure `1 (25%), 2 (50%), 3 (25%)` / elite `1 (45%), 2 (40%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `74%`
- Act 3: treasure `1 (30%), 2 (45%), 3 (25%)` / elite `1 (50%), 2 (35%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `70%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 小邮箱 (`TINY_MAILBOX`) | 100.0% | 100.0% | 1.56 | Treasure |
| 2 | 化学物X (`CHEMICAL_X`) | 90.8% | 99.6% | 2.04 | Shop |
| 3 | 红面具 (`RED_MASK`) | 90.7% | 100.0% | 3.72 | Treasure |
| 4 | 永冻冰晶 (`PERMAFROST`) | 87.4% | 96.4% | 2.14 | Shop |
| 5 | 小血瓶 (`BLOOD_VIAL`) | 79.9% | 99.8% | 2.93 | Shop |
| 6 | 弹珠袋 (`BAG_OF_MARBLES`) | 65.6% | 99.8% | 5.50 | Elite |
| 7 | 肮脏地毯 (`DINGY_RUG`) | 47.6% | 96.7% | 6.32 | Shop |
| 8 | 苦无 (`KUNAI`) | 32.2% | 64.5% | 6.94 | Shop |
| 9 | 锚 (`ANCHOR`) | 32.0% | 100.0% | 6.57 | Treasure |
| 10 | 孙子兵法 (`ART_OF_WAR`) | 30.2% | 64.1% | 8.45 | Elite |
| 11 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 26.9% | 90.6% | 7.34 | Shop |
| 12 | 缩放仪 (`PANTOGRAPH`) | 20.4% | 77.1% | 8.18 | Shop |
| 13 | 闪亮口红 (`SPARKLING_ROUGE`) | 19.8% | 76.9% | 9.15 | Elite |
| 14 | 草莓 (`STRAWBERRY`) | 13.8% | 74.8% | 8.82 | Shop |
| 15 | 精致折扇 (`ORNAMENTAL_FAN`) | 13.7% | 52.1% | 9.36 | Shop |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 小邮箱 (`TINY_MAILBOX`) | 100.0% | 100.0% | 1.56 | Treasure |
| 2 | 红面具 (`RED_MASK`) | 100.0% | 90.7% | 3.72 | Treasure |
| 3 | 锚 (`ANCHOR`) | 100.0% | 32.0% | 6.57 | Treasure |
| 4 | 小血瓶 (`BLOOD_VIAL`) | 99.8% | 79.9% | 2.93 | Shop |
| 5 | 弹珠袋 (`BAG_OF_MARBLES`) | 99.8% | 65.6% | 5.50 | Elite |
| 6 | 化学物X (`CHEMICAL_X`) | 99.6% | 90.8% | 2.04 | Shop |
| 7 | 斗篷扣 (`CLOAK_CLASP`) | 98.8% | 1.4% | 9.50 | Treasure |
| 8 | 肮脏地毯 (`DINGY_RUG`) | 96.7% | 47.6% | 6.32 | Shop |
| 9 | 永冻冰晶 (`PERMAFROST`) | 96.4% | 87.4% | 2.14 | Shop |
| 10 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 90.6% | 26.9% | 7.34 | Shop |
| 11 | 灯笼 (`LANTERN`) | 89.6% | 0.0% | 13.65 | Treasure |
| 12 | 异蛇头骨 (`SNECKO_SKULL`) | 84.9% | 7.9% | 9.70 | Elite |
| 13 | 木札 (`KIFUDA`) | 84.7% | 1.0% | 9.43 | Shop |
| 14 | 缩放仪 (`PANTOGRAPH`) | 77.1% | 20.4% | 8.18 | Shop |
| 15 | 闪亮口红 (`SPARKLING_ROUGE`) | 76.9% | 19.8% | 9.15 | Elite |
| 16 | 草莓 (`STRAWBERRY`) | 74.8% | 13.8% | 8.82 | Shop |
| 17 | 大～抱抱 (`BIIIG_HUG`) | 74.1% | 10.4% | 7.09 | Ancient Act2 |
| 18 | 黄金罗盘 (`GOLDEN_COMPASS`) | 74.1% | 10.4% | 7.09 | Ancient Act2 |
| 19 | 营养汤 (`NUTRITIOUS_SOUP`) | 74.1% | 10.4% | 7.09 | Ancient Act2 |
| 20 | 血染玫瑰 (`BLOOD_SOAKED_ROSE`) | 70.3% | 0.0% | 12.78 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 永冻冰晶 (`PERMAFROST`) / 小血瓶 (`BLOOD_VIAL`) / 化学物X (`CHEMICAL_X`) / 小邮箱 (`TINY_MAILBOX`) / 红面具 (`RED_MASK`) / 闪亮口红 (`SPARKLING_ROUGE`) / 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) / 草莓 (`STRAWBERRY`)
- Sample 2: 永冻冰晶 (`PERMAFROST`) / 小血瓶 (`BLOOD_VIAL`) / 化学物X (`CHEMICAL_X`) / 小邮箱 (`TINY_MAILBOX`) / 红面具 (`RED_MASK`) / 弹珠袋 (`BAG_OF_MARBLES`) / 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) / 草莓 (`STRAWBERRY`)
- Sample 3: 小邮箱 (`TINY_MAILBOX`) / 苦无 (`KUNAI`) / 永冻冰晶 (`PERMAFROST`) / 化学物X (`CHEMICAL_X`) / 红面具 (`RED_MASK`) / 闪亮口红 (`SPARKLING_ROUGE`) / 锚 (`ANCHOR`)

