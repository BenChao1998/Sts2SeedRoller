# Sts2SeedRoller

Slay the Spire 2 开局种子离线分析与 Roll 工具，支持 Neow、古神、概率预测、铺种和查看器。

不需要启动游戏，即可基于本地数据离线模拟 `Slay the Spire 2` 的第一幕 Neow 开局、第二/第三幕开场古神、第一幕商店，以及事件/遗物在不同路线画像下的高概率可见性。

项目同时提供 WPF 图形界面、CLI 和数据提取工具，适合手动 Roll 种、批量筛种、铺种入库、复盘存档和验证概率逻辑。

## 内容列表

- [背景](#背景)
- [安装](#安装)
- [使用说明](#使用说明)
- [功能概览](#功能概览)
- [数据与版本支持](#数据与版本支持)
- [开发](#开发)
- [维护者](#维护者)
- [鸣谢](#鸣谢)
- [如何贡献](#如何贡献)
- [使用许可](#使用许可)

## 背景

这个项目最初是为了回答一个很具体的问题：给定一个种子，我开局会看到什么，哪些种子更容易出现我想要的起手组合，以及“只给种子、不跑图”时，后续大概率会遇到哪些事件、遗物和古神。

随着功能扩展，它已经不只是一个 Neow 预览器，而是一个完整的离线种子研究工具：

- 批量扫描种子，筛出满足第一幕开局条件的结果。
- 结合第二/第三幕古神、第一幕商店和固定事件池做更贴近实战的过滤。
- 用路线画像和 Monte Carlo 采样估算高概率事件、遗物的可见性。
- 将命中的种子写入本地 SQLite 数据库，后续再用查看器检索和复盘。

## 安装

### 依赖

- Windows 10/11 x64：图形界面基于 WPF，官方发布产物面向 `win-x64`。
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)：从源码运行或构建时需要。
- 仓库自带 `data/` 目录；若要刷新数据，需额外准备对应版本的游戏源码或提取文件。

### 获取发布版

前往 [Releases](https://github.com/BenChao1998/Sts2SeedRoller/releases) 页面下载：

- `sts2seedroller-win-x64-<version>.zip`：完整 UI 包，包含 `data/` 目录。
- `Sts2SeedRollerUi-<version>.exe`：单文件 UI。
- `sts2seedroller-cli-win-x64-<version>.zip`：完整 CLI 包。
- `Sts2SeedRoller-<version>.exe`：单文件 CLI。

### 从源码构建

```powershell
git clone https://github.com/BenChao1998/Sts2SeedRoller.git
cd Sts2SeedRoller
dotnet restore Sts2SeedRoller.sln
dotnet build Sts2SeedRoller.sln -c Release
```

## 使用说明

### 图形界面

```powershell
dotnet run --project src/SeedUi/SeedUi.csproj
```

常见使用流程：

1. 在“配置”页选择游戏版本、角色、进阶等级和 Roll 模式。
2. 按需添加第一幕遗物、卡牌、药水筛选；也可以启用第二幕、第三幕古神筛选和第一幕商店筛选。
3. 如需“只给种子时的概率预测”，再配置固定事件池，以及“高概率出现事件 / 高概率出现遗物”的条件。
4. 点击“开始 Roll”，命中结果会出现在“运行结果”页，并自动保存到 `results/`。
5. 需要复盘某个具体种子时，切到“种子分析”页查看开局选项、事件概率和遗物概率画像。

运行时会在程序目录下生成这些文件：

- `config.json`：保存或加载当前配置。
- `results/results_*.json`：自动保存或手动导出的结果。
- `logs/ui-debug.log`：界面运行日志。
- `data/seed_cache/seed_archive_*.db`：铺种数据库。

### 命令行

```powershell
dotnet run --project src/SeedCli/SeedCli.csproj -- `
  --event act1-neow `
  --seed 1777165965 `
  --count 200 `
  --character ironclad `
  --include-all-acts true
```

常用参数：

- `--event act1-neow`：当前 CLI 仅支持第一幕 Neow 主事件。
- `--seed`：支持十进制、`0x` 十六进制和游戏种子字符串。
- `--count`、`--seed-step`：控制扫描数量和顺序步长。
- `--character`：可选 `ironclad`、`silent`、`defect`、`necrobinder`、`regent`。
- `--filter-relic`、`--filter-card`、`--filter-potion`：按 ID 精确筛选。
- `--filter-relic-term`：按遗物关键字筛选。
- `--include-act2`、`--include-act3`、`--include-all-acts`：附带输出第二/第三幕古神开局信息。
- `--data`、`--ancient-data`、`--sts2-act-data`：覆盖默认数据路径。

## 功能概览

- 离线 Roll 种：支持随机模式、顺序递增、命中即停；内部按分片并行扫描，适合大批量筛种。
- 第一幕开局筛选：可按遗物、卡牌、药水和遗物关键字筛选 Neow 结果。
- 固定池过滤：可限定第一幕、第二幕、第三幕事件池，以及第二/第三幕古神的具体选项。
- 第一幕商店模拟：支持限制“第一家商店最远层数”，并按商店卡牌、遗物、药水过滤。
- 概率预测模式：在只给种子时，展示高概率事件和高概率遗物，而不是把固定池误写成“确定顺序”。
- 事件路线画像：输出“综合预测（推荐）”以及多条路线画像下的事件可见性，核心指标包括前期概率、整局概率、平均首次事件位和最常来源。
- 遗物路线画像：输出不同路线画像下的遗物可见性，核心指标包括出现概率、非商店概率、商店概率、前期概率、平均首次机会和最常来源。
- 种子分析页：给定单个种子，集中展示第一幕开局、第二/第三幕古神开场选项、事件概率、遗物概率，以及 `progress.save` 相关说明。
- 铺种数据库：支持顺序递增和随机铺种，结果写入 SQLite，可中断后继续，并对重复种子去重。
- 查看器：可按角色、进阶、种子区间、第一幕奖励、第二/第三幕古神和古神选项查询已入库结果。
- 结果管理：支持复制命中种子、导出 JSON、清空结果和查看运行日志。

需要说明的是：当前“主事件入口”仍然是第一幕 Neow；第二/第三幕古神、事件概率、遗物概率和商店更多是作为预览、过滤和分析能力接入。

## 数据与版本支持

| 游戏版本 | 数据来源 | 当前用途 |
| --- | --- | --- |
| `0.103.2` | 从本地游戏源码和版本化 `data/0.103.2/` 读取 | UI 默认版本，适合当前主要分析流程 |
| `0.99.1` | 内置提取数据和 `data/0.99.1/` | 兼容旧版本数据验证 |

补充说明：

- 第二/第三幕古神预览、事件概率分析、遗物概率分析和商店模拟依赖 `data/<version>/ancients/` 与 `data/<version>/sts2/acts.json`。
- 如果这些文件缺失，程序会自动退化为只展示第一幕能力，并在界面中提示古神预览不可用。
- 官方发布包会附带运行所需数据；从源码运行时，请确保 `data/` 目录完整。

## 开发

项目结构：

```text
src/
  DataExtractor/  # 从原始提取文件生成 data/neow/options.json
  SeedCli/        # 命令行入口
  SeedModel/      # RNG、Neow、古神、商店、事件/遗物可见性分析
  SeedUi/         # WPF 图形界面、铺种数据库、查看器
data/             # 版本化数据集与本地化文本
```

常用开发命令：

```powershell
dotnet build Sts2SeedRoller.sln
dotnet run --project src/SeedUi/SeedUi.csproj
dotnet run --project src/SeedCli/SeedCli.csproj -- --event act1-neow --seed 0 --count 1
```

如果你需要重新生成第一幕 Neow 数据，可以使用提取器：

```powershell
dotnet run --project src/DataExtractor/DataExtractor.csproj -- `
  --source "<path-to-seed_info.json>" `
  --output "data/0.103.2/neow/options.json"
```

## 维护者

- [@BenChao1998](https://github.com/BenChao1998)

## 鸣谢

- [HandyControl](https://github.com/HandyOrg/HandyControl) 提供 WPF UI 组件。
- [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/) 用于本地铺种数据库。
- 感谢用于验证概率逻辑、事件可见性和铺种流程的测试存档与反馈样本。

## 如何贡献

欢迎提交 Issue 和 Pull Request。

如果你准备贡献代码，比较有帮助的做法是：

- 说明你验证的是哪个游戏版本、哪个角色和哪类种子。
- 如果改动了概率逻辑、事件池逻辑或数据文件，请附上最小复现样本或存档。
- 如果改动了用户可见行为，请同步更新 `README.md` 和相关界面文案。

## 使用许可

仓库当前未附带 `LICENSE` 文件，因此默认按 `UNLICENSED` 处理。

`UNLICENSED © BenChao1998 and contributors`
