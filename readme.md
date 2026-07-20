# UABEA · Tuanjie（团结引擎）适配 Fork

> **本仓库：** [winlend/UABEA](https://github.com/winlend/UABEA)  
> **上游官方：** [nesrak1/UABEA](https://github.com/nesrak1/UABEA)  
> **开发分支：** `feature/tuanjie-support`

在 **不破坏国际版 Unity 兼容** 的前提下，让 UABEA 能 **打开并修改** 团结引擎（Tuanjie，版本后缀 `t`）资源包。

> UABEA 是可读写工具，不是纯提取器。若只想导出资源，请优先使用 [AssetRipper](https://github.com/AssetRipper/AssetRipper) 或 [AssetStudio](https://github.com/Perfare/AssetStudio/) 系工具。

---

## 为什么需要这个 Fork

官方 UABEA + 随附 AssetsTools.NET 面向国际版 Unity。团结引擎常见差异：

| 问题 | 表现 |
|------|------|
| 版本号后缀 `t`（如 `2022.3.48t5`） | 旧版 `UnityVersion` 解析抛错，classdata 加载失败 |
| TypeTree-stripped 资源 | 依赖 classdata；国际版布局与 Tuanjie 字段不一致时错位 |
| 额外序列化字段 | Texture2D web streaming、Mesh VG、PlayerSettings 扩展等 |
| Web 资源签名 `TuanjieWebData1.0` | 标准探测未识别（**尚未实现**） |

AssetStudio 系（aelurum / SiMaLaoShi）已摸清部分布局差异，但侧重查看。本 fork 把差异落到 **UABEA 可读写栈**。

---

## 当前状态（`feature/tuanjie-support`）

### 已支持

- 识别 `YYYY.M.NtB` 版本字符串（如 `2022.3.48t5`）
- Version 对话框接受 Tuanjie 版本
- 加载 classdata 时自动映射 / 优先使用 Tuanjie 专用数据
- stripped 资源：运行时字段补丁 + TypeTree dump 整树替换
- 验证过（2022.3.48t5 样本）的类型反序列化与字段改写回读：
  - **PlayerSettings**
  - **Texture2D**
  - **Shader**
- 国际版 Unity 路径保持原逻辑（非 `t` 版本不走补丁）

### 加载优先级（Tuanjie 版本）

1. `classdata_tuanjie.tpk`（程序目录 / `ReleaseFiles`）
2. `classdata_tuanjie_2022.3.48t5.cldb`（或 `classdata_tuanjie.cldb`）
3. 随附国际版 `classdata.tpk` 中对应 `…f1` 条目 + 运行时 dump/补丁

相关文件：

```text
ReleaseFiles/classdata_tuanjie.tpk
ReleaseFiles/classdata_tuanjie_2022.3.48t5.cldb
ReleaseFiles/classdata_tuanjie/2022.3.48t5/*.json
```

构建 / 发布时会把 `ReleaseFiles/**` 拷到输出目录；请勿只拷 `UABEAvalonia.exe` 而丢掉这些数据文件。

### 限制 / 未完成

| 项 | 说明 |
|----|------|
| 主要验证版本 | **2022.3.48t\***（dump 与 cldb 基于 48t5） |
| 其他 Tuanjie 版本 | 可能回退到 `…f1` classdata + 通用字段补丁；复杂类型仍可能失败 |
| `TuanjieWebData1.0` | **未实现** |
| Mesh Virtual Geometry 完整导出 | **非目标**（可先打开/列元数据；网格重建后续） |
| 用户可见「正在用 f1 回退」警告 | 未做 |
| 插件层专项容错 | 进行中；优先依赖 `GetBaseField` + TypeTree/classdata |
| 加密 / UnityCN 绕过 | **不做** |

字段差异细节见 [`docs/plans/2026-07-16-tuanjie-diff-map.md`](docs/plans/2026-07-16-tuanjie-diff-map.md)。

---

## 快速开始

### 环境

- .NET 8 SDK（工程 `TargetFramework` 为 `net8.0`）
- Windows / Linux（与上游 UABEA 一致，Avalonia）

### 构建

```bash
git clone https://github.com/winlend/UABEA.git
cd UABEA
git checkout feature/tuanjie-support

dotnet build UABEAvalonia/UABEAvalonia.csproj -c Release
```

运行输出目录中的可执行文件，并确认同目录存在 `classdata.tpk` 与 `classdata_tuanjie*` 相关文件。

### 打开 Tuanjie 资源

1. 启动 UABEA，打开 `.bundle` / SerializedFile / 游戏目录中的资源。
2. 版本串含 `t` 时自动走 Tuanjie classdata 路径。
3. 在 Info 窗口中查看、导出或修改字段，再按原流程 Save / 导出 mod。

### 建议自测样本（不入库）

工作区外样本目录示例（本地对照用，**不要提交游戏资产**）：

```text
samples/tuanjie/
  data.tj3d                 # UnityFS，2022.3.48t5，TypeTree stripped
  tuanjie default resources # SerializedFile，2022.3.48t3
```

---

## 实现概要

| 组件 | 作用 |
|------|------|
| [`UABEAvalonia/Utils/TuanjieVersion.cs`](UABEAvalonia/Utils/TuanjieVersion.cs) | `t` 检测、classdata 键映射、加载入口 |
| [`UABEAvalonia/Utils/TuanjieClassDatabasePatcher.cs`](UABEAvalonia/Utils/TuanjieClassDatabasePatcher.cs) | dump 树替换 + 按版本阈值的字段补丁 |
| `MainWindow` / `LoadModPackageDialog` / `VersionWindow` / `AssetWorkspace` | 接入加载与 Mono 模板版本映射 |
| `tools/TuanjieTpkBuilder` | 从 dump 生成 `classdata_tuanjie.tpk` |
| `tools/TuanjiePsValidate` | PlayerSettings / Texture 等读写回归 |

策略：**有 TypeTree 优先走通用路径**；仅 stripped / 强类型场景才打补丁。少动 UI 与无关插件，便于合上游。

设计与任务拆分：

- [`docs/FORK.md`](docs/FORK.md) — fork 目的、remote、同步流程
- [`docs/plans/2026-07-16-tuanjie-support-design.md`](docs/plans/2026-07-16-tuanjie-support-design.md)
- [`docs/plans/2026-07-16-tuanjie-support-plan.md`](docs/plans/2026-07-16-tuanjie-support-plan.md)
- [`CLAUDE.md`](CLAUDE.md) — 给开发会话的入口说明

### 重建 classdata / 本地回归

```powershell
# 重新打包 tpk（参数为 UnityAssets 工作区根，按本机路径调整）
dotnet run -c Release --project tools/TuanjieTpkBuilder -- "<UnityAssetsRoot>"

# 类型 / 样本回归
dotnet run -c Release --project tools/TuanjiePsValidate -- "<UnityAssetsRoot>" validate-types
dotnet run -c Release --project tools/TuanjiePsValidate -- "<UnityAssetsRoot>" validate-tj3d
```

---

## 与上游的关系

| Remote | URL | 用途 |
|--------|-----|------|
| `origin` | `https://github.com/winlend/UABEA.git` | 本 fork 推送 |
| `upstream` | `https://github.com/nesrak1/UABEA.git` | 吃官方更新 |

```bash
git fetch upstream
git checkout master
git merge upstream/master
git push origin master

git checkout feature/tuanjie-support
git merge master
```

**不要** push 到 `upstream`。对照用的 AssetStudio / AssetsTools 源码克隆放在工作区旁路目录，**不进入**本仓库历史。

### 参考实现（只读）

- [aelurum/AssetStudio](https://github.com/aelurum/AssetStudio)（AssetStudioMod）— 首选字段差异源
- [SiMaLaoShi/AssetStudio_Tuanjie](https://github.com/SiMaLaoShi/AssetStudio_Tuanjie) — 早期 Tuanjie 分支
- [nesrak1/AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) — 底层读写库

---

## 许可与致谢

- 本仓库在上游 UABEA 许可与依赖许可下分发；见原项目说明与 `ReleaseFiles/` 中的第三方许可文本。
- 团结字段语义参考 aelurum / SiMaLaoShi 等社区逆向与适配工作。
- 团结引擎与 Unity 分别为各权利人商标；本工具与官方无隶属关系。

---

# Upstream README（nesrak1/UABEA）

以下内容保留自官方 README，便于对照原项目说明与依赖列表。

## UABEANext (https://github.com/nesrak1/UABEANext)

[Latest Nightly (Windows)](https://nightly.link/nesrak1/UABEANext/workflows/build-windows/master/uabea-windows.zip) | [Latest Nightly (Linux)](https://nightly.link/nesrak1/UABEANext/workflows/build-ubuntu/master/uabea-ubuntu.zip)

The new version of UABEA with docking and multi-bundle opening. Still a work in progress.

## UABEA original

[Latest Nightly (Windows)](https://nightly.link/nesrak1/UABEA/workflows/dotnet-desktop/master/uabea-windows.zip) | [Latest Nightly (Linux)](https://nightly.link/nesrak1/UABEA/workflows/dotnet-ubuntu/master/uabea-ubuntu.zip) | [Latest Release](https://github.com/nesrak1/UABEA/releases)

[![GitHub issues](https://img.shields.io/github/issues/nesrak1/UABEA?logo=GitHub&style=flat-square)](https://github.com/nesrak1/UABEA/issues) [![discord](https://img.shields.io/discord/862035581491478558?label=discord&logo=discord&logoColor=FFFFFF&style=flat-square)](https://discord.gg/hd9VdswwZs)

Cross-platform Asset Bundle/Serialized File reader and writer. Originally based on (but not a fork of) [UABE](https://github.com/SeriousCache/UABE).

## Extracting assets

I develop UABEA as more of a modding/research tool than an extracting tool. Use [AssetRipper](https://github.com/AssetRipper/AssetRipper) or [AssetStudio](https://github.com/Perfare/AssetStudio/) if you only want to extract assets.

## Addressables

Many games are also now using addressables. You can tell if the bundle you're opening is part of addressables because it has the path `StreamingAssets/aa/XXX/something.bundle`. [If you want to edit these bundles, you will need to clear the CRC checks with the CRC cleaning tool here](https://github.com/nesrak1/AddressablesTools/releases). Use `Example patchcrc catalog.json`, then move or rename the old catalog.json file and rename catalog.json.patched to catalog.json.

## Libraries

- [Avalonia](https://github.com/AvaloniaUI/Avalonia) (MIT license)
  - [Dock.Avalonia](https://github.com/wieslawsoltes/Dock) (MIT license)
  - [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) (MIT license)
- [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET/tree/upd21-with-inst) (MIT license)
  - [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) (MIT license)
  - [Mono.Cecil](https://github.com/jbevain/cecil) (MIT license)
  - [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) (MIT license)
- [ISPC Texture Compressor](https://github.com/GameTechDev/ISPCTextureCompressor) (MIT license)
- [Unity crnlib](https://github.com/Unity-Technologies/crunch/tree/unity) (zlib license)
- [PVRTexLib](https://developer.imaginationtech.com/pvrtextool) (PVRTexTool license)
- [ImageSharp](https://github.com/SixLabors/ImageSharp) (Apache License 2.0)
- [Fsb5Sharp](https://github.com/SamboyCoding/Fmod5Sharp) (MIT license)
- [Font Awesome](https://fontawesome.com) (CC BY 4.0 license)
