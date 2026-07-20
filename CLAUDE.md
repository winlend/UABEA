# CLAUDE.md — 本仓库给 Claude / 开发会话的说明

在动手改代码或规划任务前，先读：

1. **[docs/FORK.md](./docs/FORK.md)** — fork 目的、remote、与上游同步、分支约定、实现索引  
2. **[docs/plans/2026-07-16-tuanjie-diff-map.md](./docs/plans/2026-07-16-tuanjie-diff-map.md)** — AssetStudio 系 Tuanjie 字段差异映射  
3. **[docs/plans/2026-07-16-tuanjie-support-design.md](./docs/plans/2026-07-16-tuanjie-support-design.md)** — 适配设计（Accepted 方向）  
4. **[docs/plans/2026-07-16-tuanjie-support-plan.md](./docs/plans/2026-07-16-tuanjie-support-plan.md)** — 分 Task 实现计划  

## 一句话目标

Fork of [nesrak1/UABEA](https://github.com/nesrak1/UABEA) → [winlend/UABEA](https://github.com/winlend/UABEA)。  
在 **不破坏国际版 Unity 兼容** 的前提下，支持 **团结引擎（Tuanjie）** 资源包的查看与修改。

## Remotes

- `origin` = `winlend/UABEA`（推送）  
- `upstream` = `nesrak1/UABEA`（吃官方更新）  

同步：`git fetch upstream` → `master` merge `upstream/master` → `push origin master` → feature 分支 merge `master`。详情见 `docs/FORK.md`。

## 实现约束（摘要）

- UABEA 的读写能力来自 **AssetsTools.NET**（当前为 `Libs/*.dll`），不是 AssetStudio 那套硬编码 class reader。  
- 有 TypeTree 的资产：优先通用路径，勿重复实现字段布局。  
- 无 TypeTree / 插件硬编码：才移植 aelurum/SiMaLaoShi 的 Tuanjie 字段补丁。  
- 所有 Tuanjie 特殊逻辑必须由版本后缀 `t`（及必要的 Build 阈值）守护。  
- 少动 UI 壳与无关模块，便于合 upstream。  

## 当前分支

功能开发默认在：`feature/tuanjie-support`。

## 对照源码（只读，不在本仓库）

- `../AssetStudio_aelurum` — 更完整的版本感知 Tuanjie 适配  
- `../AssetStudio_Tuanjie` — SiMaLaoShi 早期适配  
- `../AssetsTools.NET` — 底层库源码（分析 / 未来可 vendor）  
