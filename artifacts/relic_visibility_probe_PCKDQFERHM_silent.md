# Relic Visibility Probe

- Seed: `PCKDQFERHM` (`0x7A2C62A9`)
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

### Act 2: Pael (`PAEL`)

- 佩尔之角 (`PAELS_HORN`)
- 佩尔之翼 (`PAELS_WING`)
- 佩尔之眼 (`PAELS_EYE`)

### Act 3: Nonupeipe (`NONUPEIPE`)

- 皮草大衣 (`FUR_COAT`)
- 钻石头冠 (`DIAMOND_DIADEM`)
- 华美手镯 (`BEAUTIFUL_BRACELET`)

## 路线画像结果

### Balanced

平衡路线：默认会进少量商店、打中等数量精英，古神有中等概率看到。

- Act 1: treasure `2 (45%), 3 (40%), 1 (15%)` / elite `1 (35%), 2 (50%), 3 (15%)` / shop `0 (25%), 1 (60%), 2 (15%)` / ancient chance `0%`
- Act 2: treasure `1 (25%), 2 (55%), 3 (20%)` / elite `1 (25%), 2 (50%), 3 (25%)` / shop `0 (30%), 1 (55%), 2 (15%)` / ancient chance `68%`
- Act 3: treasure `1 (35%), 2 (50%), 3 (15%)` / elite `1 (30%), 2 (45%), 3 (25%)` / shop `0 (35%), 1 (50%), 2 (15%)` / ancient chance `62%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 开心小花 (`HAPPY_FLOWER`) | 100.0% | 100.0% | 1.25 | Treasure |
| 2 | 锁镰 (`KUSARIGAMA`) | 95.2% | 100.0% | 3.74 | Treasure |
| 3 | 小血瓶 (`BLOOD_VIAL`) | 85.9% | 99.0% | 3.64 | Elite |
| 4 | 面包 (`BREAD`) | 79.4% | 97.6% | 3.47 | Shop |
| 5 | 铜质鳞片 (`BRONZE_SCALES`) | 54.2% | 89.2% | 4.89 | Shop |
| 6 | 闪亮口红 (`SPARKLING_ROUGE`) | 53.1% | 82.7% | 4.65 | Shop |
| 7 | 小邮箱 (`TINY_MAILBOX`) | 38.2% | 86.0% | 7.12 | Elite |
| 8 | 餐券 (`MEAL_TICKET`) | 33.6% | 96.8% | 6.04 | Treasure |
| 9 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 32.1% | 90.9% | 7.82 | Elite |
| 10 | 药水腰带 (`POTION_BELT`) | 23.0% | 71.6% | 7.73 | Shop |
| 11 | 烛台 (`CANDELABRA`) | 21.5% | 58.2% | 7.32 | Shop |
| 12 | 铲子 (`SHOVEL`) | 14.6% | 43.2% | 6.97 | Elite |
| 13 | 风箱 (`BELLOWS`) | 10.3% | 43.3% | 8.12 | Shop |
| 14 | 三角铃鼓 (`RINGING_TRIANGLE`) | 8.5% | 83.0% | 8.47 | Shop |
| 15 | 熔火之蛋 (`MOLTEN_EGG`) | 8.4% | 13.7% | 5.44 | Shop |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 开心小花 (`HAPPY_FLOWER`) | 100.0% | 100.0% | 1.25 | Treasure |
| 2 | 锁镰 (`KUSARIGAMA`) | 100.0% | 95.2% | 3.74 | Treasure |
| 3 | 小血瓶 (`BLOOD_VIAL`) | 99.0% | 85.9% | 3.64 | Elite |
| 4 | 赤牛 (`AKABEKO`) | 98.7% | 2.0% | 9.00 | Treasure |
| 5 | 面包 (`BREAD`) | 97.6% | 79.4% | 3.47 | Shop |
| 6 | 餐券 (`MEAL_TICKET`) | 96.8% | 33.6% | 6.04 | Treasure |
| 7 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 90.9% | 32.1% | 7.82 | Elite |
| 8 | 金纸 (`JOSS_PAPER`) | 89.9% | 0.0% | 12.03 | Treasure |
| 9 | 铜质鳞片 (`BRONZE_SCALES`) | 89.2% | 54.2% | 4.89 | Shop |
| 10 | 小邮箱 (`TINY_MAILBOX`) | 86.0% | 38.2% | 7.12 | Elite |
| 11 | 三角铃鼓 (`RINGING_TRIANGLE`) | 83.0% | 8.5% | 8.47 | Shop |
| 12 | 闪亮口红 (`SPARKLING_ROUGE`) | 82.7% | 53.1% | 4.65 | Shop |
| 13 | 药水腰带 (`POTION_BELT`) | 71.6% | 23.0% | 7.73 | Shop |
| 14 | 弹珠袋 (`BAG_OF_MARBLES`) | 70.7% | 0.0% | 13.96 | Treasure |
| 15 | 佩尔之眼 (`PAELS_EYE`) | 67.3% | 8.0% | 7.54 | Ancient Act2 |
| 16 | 佩尔之角 (`PAELS_HORN`) | 67.3% | 8.0% | 7.54 | Ancient Act2 |
| 17 | 佩尔之翼 (`PAELS_WING`) | 67.3% | 8.0% | 7.54 | Ancient Act2 |
| 18 | 打击木偶 (`STRIKE_DUMMY`) | 63.4% | 0.0% | 12.74 | Elite |
| 19 | 华美手镯 (`BEAUTIFUL_BRACELET`) | 62.6% | 0.0% | 12.84 | Ancient Act3 |
| 20 | 钻石头冠 (`DIAMOND_DIADEM`) | 62.6% | 0.0% | 12.84 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 开心小花 (`HAPPY_FLOWER`) / 小血瓶 (`BLOOD_VIAL`) / 风箱 (`BELLOWS`) / 熔火之蛋 (`MOLTEN_EGG`) / 面包 (`BREAD`) / 小邮箱 (`TINY_MAILBOX`) / 意外光滑的石头 (`ODDLY_SMOOTH_STONE`)
- Sample 2: 铜质鳞片 (`BRONZE_SCALES`) / 闪亮口红 (`SPARKLING_ROUGE`) / 面包 (`BREAD`) / 开心小花 (`HAPPY_FLOWER`) / 小邮箱 (`TINY_MAILBOX`) / 锁镰 (`KUSARIGAMA`) / 铲子 (`SHOVEL`)
- Sample 3: 铜质鳞片 (`BRONZE_SCALES`) / 闪亮口红 (`SPARKLING_ROUGE`) / 面包 (`BREAD`) / 开心小花 (`HAPPY_FLOWER`) / 小邮箱 (`TINY_MAILBOX`) / 锁镰 (`KUSARIGAMA`) / 铲子 (`SHOVEL`)

### Aggressive

多精英路线：更偏向提早打精英，商店更少，古神概率略低。

- Act 1: treasure `1 (35%), 2 (50%), 3 (15%)` / elite `2 (45%), 3 (40%), 1 (15%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `0%`
- Act 2: treasure `1 (35%), 2 (45%), 3 (20%)` / elite `2 (45%), 3 (40%), 1 (15%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `60%`
- Act 3: treasure `1 (45%), 2 (40%), 3 (15%)` / elite `2 (45%), 3 (35%), 1 (20%)` / shop `0 (45%), 1 (45%), 2 (10%)` / ancient chance `56%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 开心小花 (`HAPPY_FLOWER`) | 100.0% | 100.0% | 1.85 | Treasure |
| 2 | 锁镰 (`KUSARIGAMA`) | 84.9% | 100.0% | 4.48 | Treasure |
| 3 | 小血瓶 (`BLOOD_VIAL`) | 79.6% | 99.0% | 3.98 | Elite |
| 4 | 小邮箱 (`TINY_MAILBOX`) | 78.7% | 95.4% | 2.65 | Elite |
| 5 | 面包 (`BREAD`) | 54.5% | 90.1% | 5.96 | Shop |
| 6 | 铜质鳞片 (`BRONZE_SCALES`) | 37.8% | 80.3% | 7.28 | Shop |
| 7 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 35.4% | 91.8% | 7.12 | Elite |
| 8 | 药水腰带 (`POTION_BELT`) | 22.0% | 58.3% | 8.47 | Shop |
| 9 | 风箱 (`BELLOWS`) | 19.0% | 41.5% | 7.05 | Shop |
| 10 | 铲子 (`SHOVEL`) | 17.5% | 46.9% | 6.40 | Elite |
| 11 | 闪亮口红 (`SPARKLING_ROUGE`) | 15.7% | 67.1% | 8.89 | Shop |
| 12 | 餐券 (`MEAL_TICKET`) | 13.1% | 88.8% | 7.07 | Treasure |
| 13 | 熔火之蛋 (`MOLTEN_EGG`) | 12.6% | 15.0% | 3.89 | Shop |
| 14 | 赤牛 (`AKABEKO`) | 10.3% | 96.3% | 9.55 | Treasure |
| 15 | 佩尔之眼 (`PAELS_EYE`) | 9.9% | 59.8% | 7.02 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 开心小花 (`HAPPY_FLOWER`) | 100.0% | 100.0% | 1.85 | Treasure |
| 2 | 锁镰 (`KUSARIGAMA`) | 100.0% | 84.9% | 4.48 | Treasure |
| 3 | 小血瓶 (`BLOOD_VIAL`) | 99.0% | 79.6% | 3.98 | Elite |
| 4 | 赤牛 (`AKABEKO`) | 96.3% | 10.3% | 9.55 | Treasure |
| 5 | 小邮箱 (`TINY_MAILBOX`) | 95.4% | 78.7% | 2.65 | Elite |
| 6 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 91.8% | 35.4% | 7.12 | Elite |
| 7 | 面包 (`BREAD`) | 90.1% | 54.5% | 5.96 | Shop |
| 8 | 餐券 (`MEAL_TICKET`) | 88.8% | 13.1% | 7.07 | Treasure |
| 9 | 铜质鳞片 (`BRONZE_SCALES`) | 80.3% | 37.8% | 7.28 | Shop |
| 10 | 金纸 (`JOSS_PAPER`) | 79.0% | 0.0% | 12.47 | Treasure |
| 11 | 打击木偶 (`STRIKE_DUMMY`) | 68.6% | 0.5% | 11.84 | Elite |
| 12 | 闪亮口红 (`SPARKLING_ROUGE`) | 67.1% | 15.7% | 8.89 | Shop |
| 13 | 三角铃鼓 (`RINGING_TRIANGLE`) | 62.9% | 2.7% | 10.60 | Shop |
| 14 | 佩尔之眼 (`PAELS_EYE`) | 59.8% | 9.9% | 7.02 | Ancient Act2 |
| 15 | 佩尔之角 (`PAELS_HORN`) | 59.8% | 9.9% | 7.02 | Ancient Act2 |
| 16 | 佩尔之翼 (`PAELS_WING`) | 59.8% | 9.9% | 7.02 | Ancient Act2 |
| 17 | 奥利哈钢 (`ORICHALCUM`) | 58.3% | 0.2% | 11.72 | Elite |
| 18 | 药水腰带 (`POTION_BELT`) | 58.3% | 22.0% | 8.47 | Shop |
| 19 | 弹珠袋 (`BAG_OF_MARBLES`) | 55.9% | 0.0% | 13.77 | Treasure |
| 20 | 华美手镯 (`BEAUTIFUL_BRACELET`) | 55.8% | 0.0% | 11.93 | Ancient Act3 |

#### 早期样本示例

- Sample 1: 开心小花 (`HAPPY_FLOWER`) / 小邮箱 (`TINY_MAILBOX`) / 锁镰 (`KUSARIGAMA`) / 小血瓶 (`BLOOD_VIAL`) / 餐券 (`MEAL_TICKET`)
- Sample 2: 小邮箱 (`TINY_MAILBOX`) / 开心小花 (`HAPPY_FLOWER`) / 锁镰 (`KUSARIGAMA`) / 佩尔之角 (`PAELS_HORN`) / 佩尔之翼 (`PAELS_WING`) / 佩尔之眼 (`PAELS_EYE`) / 小血瓶 (`BLOOD_VIAL`)
- Sample 3: 小邮箱 (`TINY_MAILBOX`) / 开心小花 (`HAPPY_FLOWER`) / 锁镰 (`KUSARIGAMA`) / 小血瓶 (`BLOOD_VIAL`) / 意外光滑的石头 (`ODDLY_SMOOTH_STONE`)

### Shopper

多商店路线：更容易看到商店陈列 relic，精英数量更保守，古神概率略高。

- Act 1: treasure `2 (40%), 3 (45%), 1 (15%)` / elite `1 (50%), 2 (35%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `0%`
- Act 2: treasure `1 (25%), 2 (50%), 3 (25%)` / elite `1 (45%), 2 (40%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `74%`
- Act 3: treasure `1 (30%), 2 (45%), 3 (25%)` / elite `1 (50%), 2 (35%), 3 (15%)` / shop `1 (45%), 2 (40%), 0 (15%)` / ancient chance `70%`

#### 更容易先碰到什么 relic

| Rank | Relic | Early % | Seen % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 开心小花 (`HAPPY_FLOWER`) | 100.0% | 100.0% | 1.56 | Treasure |
| 2 | 面包 (`BREAD`) | 91.4% | 99.7% | 2.05 | Shop |
| 3 | 锁镰 (`KUSARIGAMA`) | 91.2% | 100.0% | 3.70 | Treasure |
| 4 | 闪亮口红 (`SPARKLING_ROUGE`) | 82.4% | 95.6% | 2.48 | Shop |
| 5 | 铜质鳞片 (`BRONZE_SCALES`) | 80.8% | 97.7% | 2.80 | Shop |
| 6 | 小血瓶 (`BLOOD_VIAL`) | 78.3% | 98.6% | 4.60 | Elite |
| 7 | 三角铃鼓 (`RINGING_TRIANGLE`) | 47.0% | 96.3% | 6.31 | Shop |
| 8 | 药水腰带 (`POTION_BELT`) | 43.1% | 91.5% | 6.49 | Shop |
| 9 | 烛台 (`CANDELABRA`) | 38.8% | 83.3% | 6.55 | Shop |
| 10 | 餐券 (`MEAL_TICKET`) | 33.9% | 97.2% | 6.24 | Treasure |
| 11 | 小邮箱 (`TINY_MAILBOX`) | 24.1% | 81.1% | 8.56 | Elite |
| 12 | 古茶具套装 (`VENERABLE_TEA_SET`) | 14.0% | 77.2% | 8.63 | Shop |
| 13 | 风箱 (`BELLOWS`) | 12.6% | 54.1% | 8.78 | Shop |
| 14 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 11.7% | 87.9% | 9.38 | Elite |
| 15 | 佩尔之眼 (`PAELS_EYE`) | 10.2% | 74.3% | 7.08 | Ancient Act2 |

#### 这局大概率会出现哪些 relic

| Rank | Relic | Seen % | Early % | Avg first opportunity | 常见首见来源 |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | 开心小花 (`HAPPY_FLOWER`) | 100.0% | 100.0% | 1.56 | Treasure |
| 2 | 锁镰 (`KUSARIGAMA`) | 100.0% | 91.2% | 3.70 | Treasure |
| 3 | 面包 (`BREAD`) | 99.7% | 91.4% | 2.05 | Shop |
| 4 | 赤牛 (`AKABEKO`) | 98.9% | 1.1% | 9.47 | Treasure |
| 5 | 小血瓶 (`BLOOD_VIAL`) | 98.6% | 78.3% | 4.60 | Elite |
| 6 | 铜质鳞片 (`BRONZE_SCALES`) | 97.7% | 80.8% | 2.80 | Shop |
| 7 | 餐券 (`MEAL_TICKET`) | 97.2% | 33.9% | 6.24 | Treasure |
| 8 | 三角铃鼓 (`RINGING_TRIANGLE`) | 96.3% | 47.0% | 6.31 | Shop |
| 9 | 闪亮口红 (`SPARKLING_ROUGE`) | 95.6% | 82.4% | 2.48 | Shop |
| 10 | 金纸 (`JOSS_PAPER`) | 92.0% | 0.0% | 12.26 | Treasure |
| 11 | 药水腰带 (`POTION_BELT`) | 91.5% | 43.1% | 6.49 | Shop |
| 12 | 意外光滑的石头 (`ODDLY_SMOOTH_STONE`) | 87.9% | 11.7% | 9.38 | Elite |
| 13 | 幽灵种子 (`GHOST_SEED`) | 84.3% | 1.1% | 9.43 | Shop |
| 14 | 烛台 (`CANDELABRA`) | 83.3% | 38.8% | 6.55 | Shop |
| 15 | 小邮箱 (`TINY_MAILBOX`) | 81.1% | 24.1% | 8.56 | Elite |
| 16 | 弹珠袋 (`BAG_OF_MARBLES`) | 79.6% | 0.0% | 13.60 | Treasure |
| 17 | 古茶具套装 (`VENERABLE_TEA_SET`) | 77.2% | 14.0% | 8.63 | Shop |
| 18 | 佩尔之眼 (`PAELS_EYE`) | 74.3% | 10.2% | 7.08 | Ancient Act2 |
| 19 | 佩尔之角 (`PAELS_HORN`) | 74.3% | 10.2% | 7.08 | Ancient Act2 |
| 20 | 佩尔之翼 (`PAELS_WING`) | 74.3% | 10.2% | 7.08 | Ancient Act2 |

#### 早期样本示例

- Sample 1: 铜质鳞片 (`BRONZE_SCALES`) / 闪亮口红 (`SPARKLING_ROUGE`) / 面包 (`BREAD`) / 开心小花 (`HAPPY_FLOWER`) / 小血瓶 (`BLOOD_VIAL`) / 烛台 (`CANDELABRA`) / 药水腰带 (`POTION_BELT`) / 三角铃鼓 (`RINGING_TRIANGLE`)
- Sample 2: 开心小花 (`HAPPY_FLOWER`) / 铜质鳞片 (`BRONZE_SCALES`) / 闪亮口红 (`SPARKLING_ROUGE`) / 面包 (`BREAD`) / 锁镰 (`KUSARIGAMA`) / 小血瓶 (`BLOOD_VIAL`) / 佩尔之角 (`PAELS_HORN`) / 佩尔之翼 (`PAELS_WING`)
- Sample 3: 开心小花 (`HAPPY_FLOWER`) / 锁镰 (`KUSARIGAMA`) / 铲子 (`SHOVEL`) / 餐券 (`MEAL_TICKET`) / 闪亮口红 (`SPARKLING_ROUGE`) / 烛台 (`CANDELABRA`) / 面包 (`BREAD`)

