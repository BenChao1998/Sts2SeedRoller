# 第一二三幕 Roll 种版本更新说明

## 目的

这份文档只描述一件事：当 `Slay the Spire 2` 游戏版本变化时，如何更新本项目中与第一幕、第二幕、第三幕 roll 种直接相关的程序和数据。

这里的“roll 种”范围仅包括：

- 第一幕 `Neow` 开局 roll
- 第二幕古神开场选项 roll
- 第三幕古神开场选项 roll

这里明确不包括：

- 事件概率分析
- 遗物概率分析
- 精确路线分析
- 商店筛选
- Sea Glass 预览
- 跑图验证和回放系统

这些功能虽然会复用同一套 `acts.json` 或 RNG 逻辑，但不属于这份文档的维护范围。更新版本时，优先先把第一二三幕 roll 种修准，再决定是否继续扩展到概率分析和路线分析。

## 给 AI 的结论

当游戏版本变化时，先不要默认改代码。

正确顺序是：

1. 先导出新版本的 `data/<version>/neow/options.json`
2. 再导出新版本的 `data/<version>/ancients/options.json`
3. 再导出新版本的 `data/<version>/sts2/acts.json`
4. 用新数据直接跑现有程序
5. 只有在结果与游戏源码或存档不一致时，才修改 `NeowGenerator`、`Sts2RunPreviewer`、`Sts2RunSimulator` 等逻辑代码

也就是说：

- 数据内容变了，但 RNG 消耗顺序没变：通常只需要补 `data/<version>`
- 池子、互斥规则、`NextBool()` 分支、多人限制、幕选择规则、古神事件 RNG 种子来源变了：必须改代码

## 当前程序里，第一二三幕 roll 种依赖哪些文件

### 1. 第一幕 Neow

- 数据文件：`data/<version>/neow/options.json`
- 核心逻辑：`src/SeedModel/Neow/NeowGenerator.cs`
- 结果展开：`src/SeedModel/Neow/NeowRewardPreviewer.cs`
- 数据提取：`src/DataExtractor/SourceVersionExtractor.cs`

第一幕是否准确，取决于两部分：

- `options.json` 里的卡牌、遗物、药水、元数据是否和新版本一致
- `NeowGenerator` 里正面池、负面池、互斥关系、额外 `NextBool()` 分支是否和新版本一致

### 2. 第二幕 / 第三幕古神开场

- 数据文件：`data/<version>/ancients/options.json`
- 数据文件：`data/<version>/sts2/acts.json`
- 核心入口：`src/SeedModel/Sts2/Sts2RunPreviewer.cs`
- 幕池模拟：`src/SeedModel/Sts2/Generation/Sts2RunSimulator.cs`
- 世界数据读取：`src/SeedModel/Sts2/Generation/Sts2WorldData.cs`
- 古神具体选项逻辑：`src/SeedModel/Sts2/Ancients/*.cs`

第二幕和第三幕是否准确，取决于三部分：

- `acts.json` 里的幕定义、事件池、遭遇池、古神池是否正确
- `Sts2RunSimulator` 是否按游戏真实顺序消耗 `up_front` RNG
- `Sts2RunPreviewer` 是否用正确的 `eventRngSeed` 去生成古神选项

### 3. 版本选择与数据装载

- UI 版本目录发现：`src/SeedUi/ViewModels/UiDataPathResolver.cs`
- UI 数据加载：`src/SeedUi/ViewModels/MainWindowViewModel.cs`
- CLI 数据加载：`src/SeedCli/Program.cs`

如果只是增加一个新版本目录，通常这部分不用改。因为 UI 已经会自动扫描 `data/<version>/neow/options.json`，CLI 也会优先从版本化目录读取。

## 新版本更新时的标准流程

## 第一步：准备新版本源码

目标是拿到新版本游戏源码或等价的反编译/提取结果，并确认以下目录或信息可读：

- `src/Core/Models/Acts`
- `src/Core/Models/Events`
- `src/Core/Models/Cards`
- `src/Core/Models/Relics`
- `src/Core/Models/RelicPools`
- `src/Core/Models/PotionPools`
- `localization/zhs`
- `localization/eng`

如果没有完整源码，至少要能确认：

- 第一幕 Neow 候选池
- 第二幕 / 第三幕可出现的古神
- 各幕事件池和遭遇池
- 新增/删除/改名的卡牌、遗物、药水

## 第二步：先生成数据，不要先改逻辑

本项目的数据提取入口是：

- `src/DataExtractor/SourceVersionExtractor.cs`

它会生成：

- `data/<version>/neow/options.json`
- `data/<version>/ancients/options.json`
- `data/<version>/ancients/options.zhs.json`
- `data/<version>/sts2/acts.json`
- `data/<version>/sts2/localization/zhs/*.json`

先完成这一步，再看程序结果是否已经正确。

原因是：很多版本变化只是池内容变动，不是算法变动。

## 第三步：判断这是“数据变化”还是“逻辑变化”

这是最重要的判断。

### 只需要补数据的情况

满足下面条件时，通常不需要改 C# 逻辑：

- 只是新增/删除卡牌、遗物、药水
- 只是某一幕的事件池、遭遇池、古神池内容有增删
- 本地化文本变化
- `acts.json` 结构没变，只是内容变了
- Neow 候选项本体没变，只是候选结果里引用的卡牌/遗物/药水池变了

这类情况通常只要新增：

- `data/<new-version>/neow/options.json`
- `data/<new-version>/ancients/options.json`
- `data/<new-version>/sts2/acts.json`

### 必须改逻辑的情况

只要出现下面任一情况，就不要只停留在补数据：

- Neow 正面池/负面池的基础候选列表变了
- Neow 某个负面选项会移除哪些正面选项，这种互斥规则变了
- Neow 新增或删除额外 `NextBool()` 分支
- 单人/多人下候选池不同，且规则发生变化
- Scroll Boxes、Massive Scroll、Silver Crucible 这类多人限制或资格判断变了
- 第一幕地图选择规则变了，例如 `Underdocks/Overgrowth` 的决定方式变了
- 第二幕 / 第三幕古神的事件 RNG 种子来源变了
- `up_front` RNG 在幕池模拟中的消费顺序变了
- 多人模式导致房间数、弱怪数、池子长度或抽取顺序变化

这类情况必须检查并可能修改：

- `src/SeedModel/Neow/NeowGenerator.cs`
- `src/SeedModel/Neow/NeowRewardPreviewer.cs`
- `src/SeedModel/Sts2/Sts2RunPreviewer.cs`
- `src/SeedModel/Sts2/Generation/Sts2RunSimulator.cs`
- `src/SeedModel/Sts2/Generation/Sts2WorldData.cs`
- `src/SeedModel/Sts2/Ancients/*.cs`

## 需要重点对比的源码内容

### A. 第一幕 Neow

更新版本时，优先对比这些点：

1. 正面池基础候选是否变化
2. 负面池基础候选是否变化
3. 某个负面选项是否会排除某个正面选项
4. 是否新增额外的随机二选一分支
5. 单人 / 多人是否使用不同池
6. 某些奖励是否需要特殊展开预览

当前程序里，这些规则主要写死在：

- `src/SeedModel/Neow/NeowGenerator.cs`

如果新版本只是多了一个新 relic，但 `Neow` 抽取流程没变，通常只要更新 `options.json`。

如果新版本让 `Neow` 的候选列表本身变了，就必须改 `NeowGenerator` 里的版本分支。

### B. 第二幕 / 第三幕古神

更新版本时，优先对比这些点：

1. `Acts/*.cs` 中每一幕的 `AllAncients`
2. 每一幕的 `AllEvents`
3. 每一幕普通遭遇、弱怪遭遇、精英遭遇列表
4. `BaseNumberOfRooms` 和 `NumberOfWeakEncounters`
5. 幕选择规则是否变化
6. 是否新增共享古神或新古神
7. 古神选项的实际生成逻辑是否变化

如果 `acts.json` 内容不同，但读取结构未变，通常只需重新提取。

如果游戏版本改变了“先抽什么、后抽什么、房间数怎么算、多人如何减房间、Act1 地图名怎么决定”，就必须修改 `Sts2RunSimulator` 或 `Sts2RunPreviewer`。

## 历史版本对比结论

下面这些历史提交已经说明了本项目里哪些变化属于“只补数据”，哪些变化属于“必须改逻辑”。

### 1. `f809392` 对齐 `0.103.2`

提交信息：`feat: 对齐 0.103.2 的 Neow 与商店 roll 种逻辑`

这次提交的关键信号是：不仅加了 `data/0.103.2/*`，还改了逻辑代码。

具体说明：

- 新增了 `data/0.103.2/neow/options.json`
- 新增了 `data/0.103.2/ancients/options.json`
- 新增了 `data/0.103.2/sts2/acts.json`
- `NeowGenerator` 新增了 modern rules 分支
- `SeedRunEvaluator` 与 `Sts2RunPreviewer` 扩展了第一二三幕以外的商店能力

对第一二三幕 roll 种来说，真正重要的是：

- `0.103.2` 不能只加数据，还要改 `NeowGenerator`
- 原因是 `Neow` 的正负池和额外随机分支已经不再等同于旧版本

这说明：

- 如果未来新版本也像 `0.103.2` 一样改变了 Neow 候选规则，就必须新增新的版本逻辑分支

### 2. `142fb92` 修正第二幕、第三幕准确性

提交信息：`真正修正二、三幕的结果准确性（大概，未经大批量测试）`

这次提交的关键信号是：没有新增新版本数据目录，但修改了第二幕、第三幕相关逻辑。

主要涉及：

- `src/SeedModel/Sts2/Generation/Sts2RunSimulator.cs`
- `src/SeedModel/Sts2/Sts2RunPreviewer.cs`
- `src/SeedModel/Run/SeedRunEvaluator.cs`
- `src/SeedModel/Sts2/Sts2PoolFilter.cs`

对这份文档范围来说，核心结论是：

- 第二幕 / 第三幕不准时，常见原因不是数据错，而是 RNG 消耗顺序不对
- 也可能是幕池分析与实际 preview 走了不同的路径

所以如果新版本下 Act2/Act3 古神结果不对，优先检查：

1. `up_front` RNG 是否和游戏一致
2. 幕池模拟是否按真实房间数和弱怪数消费 RNG
3. 古神事件 `eventRngSeed` 是否仍然正确

### 3. `cae9c46` 适配 `0.106.1`

提交信息：`适配106.1`

这次提交既新增了数据，也新增了逻辑分支。

和第一二三幕 roll 种直接相关的变化包括：

- 新增 `data/0.106.1/neow/options.json`
- 新增 `data/0.106.1/ancients/options.json`
- 新增 `data/0.106.1/sts2/acts.json`
- `NeowGenerator` 新增 `V01061PositiveOptions` / `V01061NegativeOptions`
- `NeowGenerator` 新增 `IsV01061RulesVersion()`
- `Sts2RunSimulator` 增加多人房间数修正和 `forcedActOneName`
- `Sts2RunPreviewer` 不再写死 `DefaultPlayerNetId = 1`，改为使用 `request.PlayerNetId`
- `Sts2RunPreviewer` 新增 `ResolveActOneName()`，用于处理 `Underdocks/Overgrowth` 选择规则

这说明：

- `0.106.1` 的变化不是单纯的池子内容更新
- 至少包含了 Neow 候选规则变化
- 还包含了第一幕地图选择和多人相关 RNG/房间消费变化

所以未来如果新版本同时影响了 `Neow` 和 `Act1 -> Act2/Act3` 的过渡 RNG，必须同时检查第一幕与二三幕逻辑，而不是只看某一侧。

## 具体改哪里

### 情况 1：只新增版本目录即可

满足条件：

- 新版本只是池内容更新
- 现有逻辑跑出来的第一幕 / 第二幕 / 第三幕结果已经和游戏一致

要做的事：

1. 新增 `data/<new-version>/neow/options.json`
2. 新增 `data/<new-version>/ancients/options.json`
3. 新增 `data/<new-version>/ancients/options.zhs.json`
4. 新增 `data/<new-version>/sts2/acts.json`
5. 如需 UI 显示中文名，补 `data/<new-version>/sts2/localization/zhs/*.json`

通常不需要改：

- `UiDataPathResolver`
- `MainWindowViewModel`
- `SeedCli/Program.cs`

因为它们已经支持扫描版本目录。

### 情况 2：第一幕 Neow 不准

优先改：

- `src/SeedModel/Neow/NeowGenerator.cs`

必要时一起改：

- `src/SeedModel/Neow/NeowRewardPreviewer.cs`
- `src/SeedModel/Neow/NeowOptionIds.cs`
- `src/SeedModel/Neow/NeowOptionDataset.cs`

判断原则：

- 候选项列表不对：先改 `NeowGenerator`
- 选项能抽到，但展示出的卡牌/药水/遗物详情不对：再改 `NeowRewardPreviewer`
- 新版本出现全新选项 ID：补 `NeowOptionIds`

### 情况 3：第二幕 / 第三幕古神不准

优先改：

- `src/SeedModel/Sts2/Sts2RunPreviewer.cs`
- `src/SeedModel/Sts2/Generation/Sts2RunSimulator.cs`

必要时一起改：

- `src/SeedModel/Sts2/Generation/Sts2WorldData.cs`
- `src/SeedModel/Sts2/Ancients/*.cs`

判断原则：

- 古神池不对：先检查 `acts.json`
- 古神名字对，但给出的选项不对：检查 `Ancients/*.cs` 或 `eventRngSeed`
- 第二幕对、第三幕错，或单人对、多人错：优先检查 `Sts2RunSimulator` 的 RNG 消耗和房间数逻辑

## 更新时不要做的事

- 不要在还没生成 `data/<version>` 之前就盲改逻辑
- 不要把事件概率、遗物概率、商店、精确路线的问题和第一二三幕 roll 种问题混为一谈
- 不要因为 UI 没显示中文就去改核心 RNG 逻辑，那通常只是本地化文件缺失
- 不要把某个单一 seed 的异常直接当成算法错误，先确认它是不是多人、特定角色、特定 Act1 地图分支导致的

## 最小验证要求

更新一个新游戏版本后，至少完成下面验证。

### 第一幕验证

至少验证多个 seed，确认：

1. `Neow` 三个选项的 relic id 与游戏一致
2. 选项下展开的卡牌 / 遗物 / 药水细节与游戏一致
3. 单人与多人不会错误共用同一套候选规则

### 第二幕 / 第三幕验证

至少验证多个 seed，确认：

1. 第二幕古神 id 与游戏一致
2. 第三幕古神 id 与游戏一致
3. 古神给出的选项集合与游戏一致
4. 单人 / 多人结果都正确
5. 如果版本涉及 `Underdocks/Overgrowth`，两种 Act1 起始地图都要验证

## AI 执行模板

当 AI 以后需要适配新游戏版本时，按下面顺序执行：

1. 找到新版本游戏源码或提取结果
2. 用 `SourceVersionExtractor` 生成 `data/<new-version>`
3. 对照上一版数据，查看 `neow/options.json`、`ancients/options.json`、`sts2/acts.json` 是否只是内容变化
4. 用现有程序直接验证第一幕、第二幕、第三幕结果
5. 如果第一幕不准，先改 `NeowGenerator`
6. 如果第二幕 / 第三幕不准，先改 `Sts2RunPreviewer` 和 `Sts2RunSimulator`
7. 只在结果展示细节不对时，再改 `NeowRewardPreviewer` 或本地化数据

一句话总结：

- 先补数据
- 再验结果
- 结果不准时只改和第一二三幕 roll 种直接相关的逻辑文件
