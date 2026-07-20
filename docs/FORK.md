# Fork 说明与上游同步指引

> 本文档面向 **本仓库维护者与后续 AI 会话**。  
> 目的：说明为什么 fork、当前目标、与官方仓库的关系、如何安全同步。

**最后更新：** 2026-07-16  
**维护者 GitHub：** [winlend](https://github.com/winlend)  
**本 Fork：** https://github.com/winlend/UABEA  
**上游官方：** https://github.com/nesrak1/UABEA  
**底层库上游：** https://github.com/nesrak1/AssetsTools.NET

---

## 1. 为什么 Fork

官方 **UABEA**（Avalonia + AssetsTools.NET）能查看并**修改** Unity Asset Bundle / Serialized File。  
官方与国际版 Unity 资源兼容，但 **团结引擎（Tuanjie）** 使用版本号后缀 `t`，并在若干序列化类型上插入额外字段，导致：

1. 版本字符串解析 / classdata 匹配可能失败或回退到错误布局
2. Texture2D / Mesh / AnimationClip / GameObject 等字段偏移错误
3. Web 资源签名 `TuanjieWebData1.0` 无法识别
4. Mesh Virtual Geometry（VG）等团结特有结构无法读取

本 fork **不是**另起炉灶，而是在跟进上游的前提下，把 AssetStudio 系仓库中已验证的 Tuanjie 差异移植到 **可读写** 的 UABEA/AssetsTools 栈。

### 参考实现（只读提取）

| 仓库 | 角色 | 备注 |
|------|------|------|
| [aelurum/AssetStudio](https://github.com/aelurum/AssetStudio) (`AssetStudioMod`) | **首选差异源** | 版本感知的 Tuanjie 适配（含 Build 号阈值） |
| [SiMaLaoShi/AssetStudio_Tuanjie](https://github.com/SiMaLaoShi/AssetStudio_Tuanjie) | 早期 Tuanjie 分支 | `IsTuanJie()` 粗粒度分支，覆盖 1.1.x–1.3.0 |
| [wx11-00-1/AssetStudio_Tuanjie](https://github.com/wx11-00-1/AssetStudio_Tuanjie) | SiMaLaoShi 的 fork | 用户最初提及；以 SiMaLaoShi 为源 |

### 选定方案（已定稿方向）

| 层 | 策略 |
|----|------|
| Bundle / 文件签名 | 识别 `TuanjieWebData1.0` |
| 版本字符串 | 完整支持 `YYYY.M.NtB`（`t` 已在 AssetsTools `UnityVersion` 解析器中出现） |
| 有 TypeTree 的文件 | 优先走 TypeTree（多数现代 bundle 可直接读写） |
| 无 TypeTree / 插件强类型 | 需要 Tuanjie 字段补丁或专用 classdata |
| 编辑/写回 | 保持 UABEA 既有 AssetsReplacer 路径，不破坏国际版兼容 |

### 明确不做（MVP）

- 完整 Virtual Geometry Mesh 网格重建导出（可先跳过/提示 unsupported）
- 重写整套 AssetStudio 硬编码反序列化进 UABEA
- 替换 Avalonia UI 或大改插件 API
- 默认破坏国际版 Unity 资源的读写行为

---

## 2. 仓库与 Remote 约定

| Remote | URL | 用途 |
|--------|-----|------|
| **origin** | `https://github.com/winlend/UABEA.git` | 推送本 fork 的分支、PR 基线 |
| **upstream** | `https://github.com/nesrak1/UABEA.git` | 拉取官方新功能与修复 |

本地检查：

```bash
git remote -v
# origin    → winlend/UABEA
# upstream  → nesrak1/UABEA
```

若缺少 remote：

```bash
git remote add origin https://github.com/winlend/UABEA.git
git remote add upstream https://github.com/nesrak1/UABEA.git
```

### 分支习惯

| 分支 | 说明 |
|------|------|
| `master` | 跟踪官方默认分支；定期与 `upstream/master` 对齐后推送到 `origin/master` |
| `feature/tuanjie-support` | 当前功能开发分支 |

**不要**在 `master` 上堆长期未合并的大改；功能在 feature 分支完成后再合入本 fork 的 `master`。

### 本地对照仓库（不提交到 git）

工作区父目录 `F:\AI Works\Projects\UnityAssets\` 下另有只读对照克隆：

- `AssetStudio_aelurum/` — aelurum/AssetStudio
- `AssetStudio_Tuanjie/` — SiMaLaoShi/AssetStudio_Tuanjie
- `AssetsTools.NET/` — nesrak1/AssetsTools.NET 源码（分析用；UABEA 当前通过 `Libs/*.dll` 引用）

这些目录 **不属于** 本 fork 仓库内容。

---

## 3. 日常开发流程

```bash
# 在功能分支上工作
git checkout feature/tuanjie-support
# ... 改代码、提交 ...
git push -u origin HEAD
```

实现时优先：

1. 先保证 **能打开** Tuanjie bundle（文件类型 + 版本 + TypeTree）。
2. 再保证 **常见类型** 预览/导出（Texture2D / TextAsset / AudioClip）。
3. 最后保证 **写回** 不破坏未修改对象。
4. 少动 UI 壳与无关插件，降低与上游 merge 冲突。

---

## 4. 如何把上游新功能合进本 Fork

原则：**先更新 `master`，再把 `master` 合进功能分支**。

### 4.1 推荐：命令行 merge

```bash
git status
git fetch upstream
git checkout master
git merge upstream/master
# 冲突解决后
git push origin master

git checkout feature/tuanjie-support
git merge master
git push origin feature/tuanjie-support
```

### 4.2 可选：rebase（仅个人 fork）

```bash
git checkout master
git fetch upstream
git rebase upstream/master
git push --force-with-lease origin master

git checkout feature/tuanjie-support
git rebase master
git push --force-with-lease origin feature/tuanjie-support
```

### 4.3 建议频率

- 每个官方 release 或至少每周 `fetch upstream` 一次
- 不要攒数月再合

---

## 5. 冲突与风险

| 风险 | 应对 |
|------|------|
| 与上游同时改插件 / MainWindow | 小步提交；冲突时优先保留上游结构，再挂回 Tuanjie 分支 |
| 误推到 upstream | 确认 `git remote -v`；**只 push origin** |
| 仅改 UABEA 不够（逻辑在 DLL） | 要么 vendor AssetsTools 源码，要么继续用 TypeTree 绕开强类型 |
| 把 `t` 版本映射成错误的 `f` classdata | 对 stripped 文件必须有 Tuanjie 专用 classdata 或字段补丁 |
| 破坏国际版兼容 | 所有 Tuanjie 分支必须以 `type == "t"` / 版本阈值守护 |

---

## 6. 给后续会话的快速上下文

```text
本仓库是 winlend fork 的 UABEA（origin），upstream 为 nesrak1/UABEA。
目标：支持团结引擎（Tuanjie，版本后缀 t）资源的查看与修改。
差异参考：aelurum/AssetStudio（优先）与 SiMaLaoShi/AssetStudio_Tuanjie。
UABEA 通过 Libs/AssetsTools.NET*.dll + classdata.tpk + 插件工作；
有 TypeTree 的文件优先走通用反序列化；无 TypeTree / 强类型插件需补丁。
同步上游：fetch upstream → merge 到 master → push origin → merge master 进 feature 分支。
当前功能分支：feature/tuanjie-support。
设计与差异见 docs/plans/。
```

### 关键路径索引

| 文档 | 路径 |
|------|------|
| 本说明 | `docs/FORK.md` |
| 差异映射 | `docs/plans/2026-07-16-tuanjie-diff-map.md` |
| 设计 | `docs/plans/2026-07-16-tuanjie-support-design.md` |
| 实现计划 | `docs/plans/2026-07-16-tuanjie-support-plan.md` |
| 官方 README | `readme.md` |

### 实现时优先阅读的代码

| 区域 | 路径 |
|------|------|
| 启动 / classdata 加载 | `UABEAvalonia/Forms/MainWindow.axaml.cs` |
| 版本输入 | `UABEAvalonia/Forms/VersionWindow.axaml.cs` |
| 文件类型检测 | `UABEAvalonia/Logic/FileTypeDetector.cs` |
| Bundle 工作区 | `UABEAvalonia/Workspace/BundleWorkspace.cs` |
| Asset 工作区 | `UABEAvalonia/Workspace/AssetWorkspace.cs` |
| 贴图插件 | `TexturePlugin/*` |
| 预编译 AssetsTools | `Libs/AssetsTools.NET*.dll` |
| classdata | `ReleaseFiles/classdata.tpk` |

---

## 7. 环境与工具（本机记录）

- 项目路径：`F:\AI Works\Projects\UnityAssets\UABEA`
- 对照路径：`F:\AI Works\Projects\UnityAssets\{AssetStudio_aelurum,AssetStudio_Tuanjie,AssetsTools.NET}`
- GitHub CLI 账号：`winlend`
- 参考 fork 流程模板：`F:\AI Works\Projects\skills-manager\docs\FORK.md`

---

## 8. 变更日志（本 fork 文档层）

| 日期 | 事项 |
|------|------|
| 2026-07-16 | Fork 建立；remote origin/upstream；分支 `feature/tuanjie-support`；差异/设计/计划文档落盘 |
