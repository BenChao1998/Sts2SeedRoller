# Relic Visibility Probe

- Seed: `8H0CTDB7H8` (`0x807BED4C`)
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

### Act 2: Orobas (`OROBAS`)

- 沙堡 (`SAND_CASTLE`)
- 炼金箱 (`ALCHEMICAL_COFFER`)
- 古老牙齿 (`ARCHAIC_TOOTH`)

### Act 3: Nonupeipe (`NONUPEIPE`)

- 珠宝盒 (`JEWELRY_BOX`)
- 艳丽围巾 (`BRILLIANT_SCARF`)
- 娇嫩蕨草 (`DELICATE_FROND`)

## 路线画像结果

### Balanced

平衡路线：默认会进少量商店、打中等数量精英，古神有中等概率看到。

- Act 1: treasure `2 (45%), 3 (40%), 1 (15%)` / elite `1 (35%), 2 (50%), 3 (15%)` / shop `0 (25%), 1 (60%), 2 (15%)` / ancient chance `0%`
- Act 2: treasure `1 (25%), 2 (55%), 3 (20%)` / elite `1 (25%), 2 (50%), 3 (25%)` / shop `0 (30%), 1 (55%), 2 (15%)` / ancient chance `68%`
- Act 3: treasure `1 (35%), 2 (50%), 3 (15%)` / elite `1 (30%), 2 (45%), 3 (25%)` / shop `0 (35%), 1 (50%), 2 (15%)` / ancient chance `62%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 摆动球 (`PENDULUM`) | 100.0% | 100.0% | 1.25 | Treasure |
| 2 | 活动星图 (`PLANISPHERE`) | 94.7% | 100.0% | 3.74 | Treasure |
| 3 | 木札 (`KIFUDA`) | 78.9% | 96.8% | 3.46 | Shop |
| 4 | 百年积木 (`CENTENNIAL_PUZZLE`) | 65.7% | 91.2% | 4.26 | Shop |
| 5 | 紫水晶茄子 (`AMETHYST_AUBERGINE`) | 64.0% | 91.0% | 5.08 | Elite |
| 6 | 永恒羽毛 (`ETERNAL_FEATHER`) | 63.0% | 86.0% | 4.10 | Shop |
| 7 | 永冻冰晶 (`PERMAFROST`) | 54.8% | 89.5% | 5.65 | Elite |
| 8 | 锚 (`ANCHOR`) | 33.2% | 100.0% | 6.25 | Treasure |
| 9 | 苦无 (`KUNAI`) | 28.1% | 67.6% | 7.09 | Elite |
| 10 | 吊灯 (`CHANDELIER`) | 22.8% | 59.7% | 6.92 | Shop |
| 11 | 铜质鳞片 (`BRONZE_SCALES`) | 18.5% | 89.3% | 8.80 | Elite |
| 12 | 餐券 (`MEAL_TICKET`) | 15.5% | 51.4% | 6.74 | Shop |
| 13 | 化学物X (`CHEMICAL_X`) | 8.6% | 82.5% | 8.48 | Shop |
| 14 | 炼金箱 (`ALCHEMICAL_COFFER`) | 8.2% | 67.9% | 7.52 | Ancient Act2 |
| 15 | 古老牙齿 (`ARCHAIC_TOOTH`) | 8.2% | 67.9% | 7.52 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 摆动球 (`PENDULUM`) | 100.0% | 100.0% | 1.25 | Treasure |
| 2 | 活动星图 (`PLANISPHERE`) | 100.0% | 94.7% | 3.74 | Treasure |
| 3 | 锚 (`ANCHOR`) | 100.0% | 33.2% | 6.25 | Treasure |
| 4 | 木札 (`KIFUDA`) | 96.8% | 78.9% | 3.46 | Shop |
| 5 | 百年积木 (`CENTENNIAL_PUZZLE`) | 91.2% | 65.7% | 4.26 | Shop |
| 6 | 紫水晶茄子 (`AMETHYST_AUBERGINE`) | 91.0% | 64.0% | 5.08 | Elite |
| 7 | 不休陀螺 (`UNCEASING_TOP`) | 90.0% | 0.0% | 12.04 | Treasure |
| 8 | 永冻冰晶 (`PERMAFROST`) | 89.5% | 54.8% | 5.65 | Elite |
| 9 | 铜质鳞片 (`BRONZE_SCALES`) | 89.3% | 18.5% | 8.80 | Elite |
| 10 | 永恒羽毛 (`ETERNAL_FEATHER`) | 86.0% | 63.0% | 4.10 | Shop |
| 11 | 化学物X (`CHEMICAL_X`) | 82.5% | 8.6% | 8.48 | Shop |
| 12 | 佛珠手链 (`JUZU_BRACELET`) | 77.1% | 1.8% | 8.11 | Treasure |
| 13 | 炼金箱 (`ALCHEMICAL_COFFER`) | 67.9% | 8.2% | 7.52 | Ancient Act2 |
| 14 | 古老牙齿 (`ARCHAIC_TOOTH`) | 67.9% | 8.2% | 7.52 | Ancient Act2 |
| 15 | 沙堡 (`SAND_CASTLE`) | 67.9% | 8.2% | 7.52 | Ancient Act2 |
| 16 | 苦无 (`KUNAI`) | 67.6% | 28.1% | 7.09 | Elite |
| 17 | 手里剑 (`SHURIKEN`) | 67.0% | 0.0% | 14.36 | Treasure |
| 18 | 艳丽围巾 (`BRILLIANT_SCARF`) | 61.8% | 0.0% | 12.90 | Ancient Act3 |
| 19 | 娇嫩蕨草 (`DELICATE_FROND`) | 61.8% | 0.0% | 12.90 | Ancient Act3 |
| 20 | 珠宝盒 (`JEWELRY_BOX`) | 61.8% | 0.0% | 12.90 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 摆动球 (`PENDULUM`) / 苦无 (`KUNAI`) / 永冻冰晶 (`PERMAFROST`) / 紫水晶茄子 (`AMETHYST_AUBERGINE`) / 活动星图 (`PLANISPHERE`)
- Sample 2: 百年积木 (`CENTENNIAL_PUZZLE`) / 永恒羽毛 (`ETERNAL_FEATHER`) / 木札 (`KIFUDA`) / 摆动球 (`PENDULUM`) / 永冻冰晶 (`PERMAFROST`) / 活动星图 (`PLANISPHERE`) / 锚 (`ANCHOR`)
- Sample 3: 摆动球 (`PENDULUM`) / 永冻冰晶 (`PERMAFROST`) / 活动星图 (`PLANISPHERE`) / 锚 (`ANCHOR`) / 佛珠手链 (`JUZU_BRACELET`)

### Aggressive

多精英路线：更偏向提早打精英，商店更少，古神概率略低。

- Act 1: treasure `1 (35%), 2 (50%), 3 (15%)` / elite `2 (45%), 3 (40%), 1 (15%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `0%`
- Act 2: treasure `1 (35%), 2 (45%), 3 (20%)` / elite `2 (45%), 3 (40%), 1 (15%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `60%`
- Act 3: treasure `1 (45%), 2 (40%), 3 (15%)` / elite `2 (45%), 3 (35%), 1 (20%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `56%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 摆动球 (`PENDULUM`) | 100.0% | 100.0% | 1.83 | Treasure |
| 2 | 永冻冰晶 (`PERMAFROST`) | 93.1% | 98.7% | 1.79 | Elite |
| 3 | 活动星图 (`PLANISPHERE`) | 79.2% | 100.0% | 4.77 | Treasure |
| 4 | 紫水晶茄子 (`AMETHYST_AUBERGINE`) | 71.8% | 93.3% | 4.42 | Elite |
| 5 | 木札 (`KIFUDA`) | 55.2% | 90.4% | 5.97 | Shop |
| 6 | 百年积木 (`CENTENNIAL_PUZZLE`) | 43.5% | 78.9% | 6.64 | Shop |
| 7 | 闪亮口红 (`SPARKLING_ROUGE`) | 32.6% | 85.5% | 6.71 | Elite |
| 8 | 永恒羽毛 (`ETERNAL_FEATHER`) | 29.9% | 68.2% | 7.43 | Shop |
| 9 | 餐券 (`MEAL_TICKET`) | 21.9% | 38.7% | 6.03 | Shop |
| 10 | 苦无 (`KUNAI`) | 21.5% | 65.6% | 7.51 | Elite |
| 11 | 铜质鳞片 (`BRONZE_SCALES`) | 18.5% | 91.0% | 8.53 | Elite |
| 12 | 吊灯 (`CHANDELIER`) | 17.7% | 53.3% | 8.55 | Shop |
| 13 | 锚 (`ANCHOR`) | 12.5% | 100.0% | 7.62 | Treasure |
| 14 | 炼金箱 (`ALCHEMICAL_COFFER`) | 9.9% | 60.0% | 7.02 | Ancient Act2 |
| 15 | 古老牙齿 (`ARCHAIC_TOOTH`) | 9.9% | 60.0% | 7.02 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 摆动球 (`PENDULUM`) | 100.0% | 100.0% | 1.83 | Treasure |
| 2 | 活动星图 (`PLANISPHERE`) | 100.0% | 79.2% | 4.77 | Treasure |
| 3 | 锚 (`ANCHOR`) | 100.0% | 12.5% | 7.62 | Treasure |
| 4 | 永冻冰晶 (`PERMAFROST`) | 98.7% | 93.1% | 1.79 | Elite |
| 5 | 紫水晶茄子 (`AMETHYST_AUBERGINE`) | 93.3% | 71.8% | 4.42 | Elite |
| 6 | 铜质鳞片 (`BRONZE_SCALES`) | 91.0% | 18.5% | 8.53 | Elite |
| 7 | 木札 (`KIFUDA`) | 90.4% | 55.2% | 5.97 | Shop |
| 8 | 闪亮口红 (`SPARKLING_ROUGE`) | 85.5% | 32.6% | 6.71 | Elite |
| 9 | 百年积木 (`CENTENNIAL_PUZZLE`) | 78.9% | 43.5% | 6.64 | Shop |
| 10 | 不休陀螺 (`UNCEASING_TOP`) | 73.7% | 0.0% | 12.86 | Treasure |
| 11 | 永恒羽毛 (`ETERNAL_FEATHER`) | 68.2% | 29.9% | 7.43 | Shop |
| 12 | 苦无 (`KUNAI`) | 65.6% | 21.5% | 7.51 | Elite |
| 13 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 65.0% | 0.0% | 11.53 | Elite |
| 14 | 化学物X (`CHEMICAL_X`) | 63.5% | 3.0% | 10.56 | Shop |
| 15 | 炼金箱 (`ALCHEMICAL_COFFER`) | 60.0% | 9.9% | 7.02 | Ancient Act2 |
| 16 | 古老牙齿 (`ARCHAIC_TOOTH`) | 60.0% | 9.9% | 7.02 | Ancient Act2 |
| 17 | 沙堡 (`SAND_CASTLE`) | 60.0% | 9.9% | 7.02 | Ancient Act2 |
| 18 | 艳丽围巾 (`BRILLIANT_SCARF`) | 55.8% | 0.0% | 11.87 | Ancient Act3 |
| 19 | 娇嫩蕨草 (`DELICATE_FROND`) | 55.8% | 0.0% | 11.87 | Ancient Act3 |
| 20 | 珠宝盒 (`JEWELRY_BOX`) | 55.8% | 0.0% | 11.87 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 永冻冰晶 (`PERMAFROST`) / 摆动球 (`PENDULUM`) / 百年积木 (`CENTENNIAL_PUZZLE`) / 餐券 (`MEAL_TICKET`) / 木札 (`KIFUDA`) / 活动星图 (`PLANISPHERE`) / 紫水晶茄子 (`AMETHYST_AUBERGINE`)
- Sample 2: 摆动球 (`PENDULUM`) / 永冻冰晶 (`PERMAFROST`) / 活动星图 (`PLANISPHERE`) / 沙堡 (`SAND_CASTLE`) / 炼金箱 (`ALCHEMICAL_COFFER`) / 古老牙齿 (`ARCHAIC_TOOTH`) / 紫水晶茄子 (`AMETHYST_AUBERGINE`)
- Sample 3: 永冻冰晶 (`PERMAFROST`) / 摆动球 (`PENDULUM`) / 活动星图 (`PLANISPHERE`) / 锚 (`ANCHOR`) / 紫水晶茄子 (`AMETHYST_AUBERGINE`)

### Shopper

多商店路线：更容易看到商店陈列 relic，精英数量更保守，古神概率略高。

- Act 1: treasure `2 (40%), 3 (45%), 1 (15%)` / elite `1 (50%), 2 (35%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `0%`
- Act 2: treasure `1 (25%), 2 (50%), 3 (25%)` / elite `1 (45%), 2 (40%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `74%`
- Act 3: treasure `1 (30%), 2 (45%), 3 (25%)` / elite `1 (50%), 2 (35%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `70%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 摆动球 (`PENDULUM`) | 100.0% | 100.0% | 1.57 | Treasure |
| 2 | 木札 (`KIFUDA`) | 91.5% | 99.6% | 2.00 | Shop |
| 3 | 活动星图 (`PLANISPHERE`) | 90.4% | 100.0% | 3.74 | Treasure |
| 4 | 永恒羽毛 (`ETERNAL_FEATHER`) | 86.1% | 96.8% | 2.32 | Shop |
| 5 | 百年积木 (`CENTENNIAL_PUZZLE`) | 83.4% | 98.4% | 2.69 | Shop |
| 6 | 紫水晶茄子 (`AMETHYST_AUBERGINE`) | 59.7% | 87.0% | 5.20 | Elite |
| 7 | 化学物X (`CHEMICAL_X`) | 48.0% | 96.7% | 6.29 | Shop |
| 8 | 永冻冰晶 (`PERMAFROST`) | 45.7% | 87.0% | 6.73 | Elite |
| 9 | 餐券 (`MEAL_TICKET`) | 38.6% | 75.0% | 5.85 | Shop |
| 10 | 精致折扇 (`ORNAMENTAL_FAN`) | 37.3% | 82.5% | 7.14 | Shop |
| 11 | 锚 (`ANCHOR`) | 31.7% | 100.0% | 6.60 | Treasure |
| 12 | 吊灯 (`CHANDELIER`) | 24.1% | 71.2% | 7.21 | Shop |
| 13 | 炼金箱 (`ALCHEMICAL_COFFER`) | 10.2% | 73.9% | 7.12 | Ancient Act2 |
| 14 | 古老牙齿 (`ARCHAIC_TOOTH`) | 10.2% | 73.9% | 7.12 | Ancient Act2 |
| 15 | 沙堡 (`SAND_CASTLE`) | 10.2% | 73.9% | 7.12 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 摆动球 (`PENDULUM`) | 100.0% | 100.0% | 1.57 | Treasure |
| 2 | 活动星图 (`PLANISPHERE`) | 100.0% | 90.4% | 3.74 | Treasure |
| 3 | 锚 (`ANCHOR`) | 100.0% | 31.7% | 6.60 | Treasure |
| 4 | 木札 (`KIFUDA`) | 99.6% | 91.5% | 2.00 | Shop |
| 5 | 百年积木 (`CENTENNIAL_PUZZLE`) | 98.4% | 83.4% | 2.69 | Shop |
| 6 | 永恒羽毛 (`ETERNAL_FEATHER`) | 96.8% | 86.1% | 2.32 | Shop |
| 7 | 化学物X (`CHEMICAL_X`) | 96.7% | 48.0% | 6.29 | Shop |
| 8 | 不休陀螺 (`UNCEASING_TOP`) | 92.1% | 0.0% | 12.28 | Treasure |
| 9 | 永冻冰晶 (`PERMAFROST`) | 87.0% | 45.7% | 6.73 | Elite |
| 10 | 紫水晶茄子 (`AMETHYST_AUBERGINE`) | 87.0% | 59.7% | 5.20 | Elite |
| 11 | 铜质鳞片 (`BRONZE_SCALES`) | 85.4% | 8.9% | 10.64 | Elite |
| 12 | 面包 (`BREAD`) | 85.1% | 1.0% | 9.41 | Shop |
| 13 | 精致折扇 (`ORNAMENTAL_FAN`) | 82.5% | 37.3% | 7.14 | Shop |
| 14 | 皇家枕头 (`REGAL_PILLOW`) | 79.1% | 4.2% | 9.56 | Shop |
| 15 | 佛珠手链 (`JUZU_BRACELET`) | 78.4% | 1.2% | 8.84 | Treasure |
| 16 | 餐券 (`MEAL_TICKET`) | 75.0% | 38.6% | 5.85 | Shop |
| 17 | 炼金箱 (`ALCHEMICAL_COFFER`) | 73.9% | 10.2% | 7.12 | Ancient Act2 |
| 18 | 古老牙齿 (`ARCHAIC_TOOTH`) | 73.9% | 10.2% | 7.12 | Ancient Act2 |
| 19 | 沙堡 (`SAND_CASTLE`) | 73.9% | 10.2% | 7.12 | Ancient Act2 |
| 20 | 手里剑 (`SHURIKEN`) | 72.5% | 0.0% | 14.39 | Treasure |

#### 早期样本示例

- Sample 1: 摆动球 (`PENDULUM`) / 永恒羽毛 (`ETERNAL_FEATHER`) / 吊灯 (`CHANDELIER`) / 木札 (`KIFUDA`) / 紫水晶茄子 (`AMETHYST_AUBERGINE`) / 精致折扇 (`ORNAMENTAL_FAN`) / 百年积木 (`CENTENNIAL_PUZZLE`) / 化学物X (`CHEMICAL_X`)
- Sample 2: 百年积木 (`CENTENNIAL_PUZZLE`) / 永恒羽毛 (`ETERNAL_FEATHER`) / 木札 (`KIFUDA`) / 摆动球 (`PENDULUM`) / 活动星图 (`PLANISPHERE`) / 永冻冰晶 (`PERMAFROST`) / 餐券 (`MEAL_TICKET`) / 皇家枕头 (`REGAL_PILLOW`)
- Sample 3: 百年积木 (`CENTENNIAL_PUZZLE`) / 永恒羽毛 (`ETERNAL_FEATHER`) / 木札 (`KIFUDA`) / 摆动球 (`PENDULUM`) / 活动星图 (`PLANISPHERE`) / 永冻冰晶 (`PERMAFROST`) / 锚 (`ANCHOR`)

