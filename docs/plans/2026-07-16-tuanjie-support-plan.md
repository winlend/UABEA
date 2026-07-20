# 实现计划：UABEA Tuanjie 支持

**日期：** 2026-07-16  
**分支：** `feature/tuanjie-support`  
**设计：** [2026-07-16-tuanjie-support-design.md](./2026-07-16-tuanjie-support-design.md)  
**差异：** [2026-07-16-tuanjie-diff-map.md](./2026-07-16-tuanjie-diff-map.md)

---

## Task 0 — Fork 骨架与文档（本轮）

- [x] `gh repo fork nesrak1/UABEA` → `winlend/UABEA`
- [x] 本地 remote：`origin=winlend`，`upstream=nesrak1`
- [x] 分支 `feature/tuanjie-support`
- [x] `docs/FORK.md`、`CLAUDE.md`、plans 三件套
- [ ] push 文档到 origin（需用户确认后执行）

## Task 1 — 基线可编译与国际版回归

1. [x] .NET 9 SDK 可用；`dotnet build UABEAvalonia.csproj -c Release` → 0 error
2. [ ] 国际版样本回归（打开 / 导出）— 待补样本

**完成标准：** 上游功能在 fork 上可运行。

## Task 2 — 样本与失败矩阵

样本目录：`F:\AI Works\Projects\UnityAssets\samples\tuanjie\`

| 文件 | 类型 | 版本 | TypeTree | 备注 |
|------|------|------|----------|------|
| `data.tj3d` | UnityFS (~73MB) | `2022.3.48t5` | **False** | 含 globalgamemanagers / sharedassets0 / level0 / resources |
| `tuanjie default resources` | SerializedFile (~6.5MB) | `2022.3.48t3` | **False** | TargetPlatform=48 |

### 修复前（原版 UABEA / 原 DLL）

- Bundle 容器可读，Assets 列表可读
- `LoadClassDatabaseFromPackage("2022.3.48t5")` → **FormatException: '48t5'**
- 全部 `GetBaseField` → NullReferenceException（classdb=null）

### 修复后（`t` → `f1` classdata 映射）

用同 major.minor.patch 的 `2022.3.48f1` classdata：

| 文件/目录 | assets | ok | fail | 主要失败 typeId |
|-----------|--------|----|------|-----------------|
| tuanjie default resources | 123 | **93** | 30 | 28 Texture, 43 Mesh, 48 Shader |
| data.tj3d / globalgamemanagers | 20 | 17 | 3 | 129, 47, script |
| data.tj3d / tuanjie_builtin_extra | 39 | 38 | 1 | 48 Shader |
| data.tj3d / globalgamemanagers.assets | 1556 | **1545** | 11 | 48 Shader |
| data.tj3d / sharedassets0 等 | （探测超时未全量） | 高成功率预期 | Mesh/Shader/部分 Tex | 见 diff map |

**根因确认：** 样本 **全部 TypeTree-stripped**，必须有 classdata；当前阻塞点是 DLL 内 `UnityVersion` 不认 `t`，不是 Bundle 格式本身。

**完成标准：** 有可复现失败记录 ✅

## Task 3 — 文件类型与版本路径

1. [ ] `TuanjieWebData1.0`（本批样本未用到）
2. [x] VersionWindow 接受 `t` 版本字符串
3. [x] `TuanjieVersion` helper（字符串检测 + `MapForClassDatabase` + `LoadClassDatabase`）
4. [x] MainWindow / LoadModPackageDialog / AssetWorkspace mono 路径走映射
5. [ ] UI 提示：正在用 f1 classdata 回退（用户可见警告）
6. [ ] 中期：Tuanjie 专用 classdata 或 vendor 新版 AssetsTools（认 `t`）

**完成标准：** Tuanjie 样本能加载 classdata 并反序列化大部分对象 ✅（Mesh/Shader/部分 Tex 仍失败）

## Task 4 — TypeTree 路径验收（P0）

1. 对 TypeTree 启用的 Tuanjie 文件：View Data
2. Texture2D 导出
3. TextAsset 导出/导入/保存
4. 对比保存前后文件大小与关键 head/对象数量

**完成标准：** P0 清单中 TypeTree 相关项全绿。

## Task 5 — 插件容错

1. TexturePlugin：按字段名读取；容忍额外 Tuanjie 子字段
2. AudioClipPlugin：验证 Ambisonic 等额外字段不导致错位
3. 任何 `ReadInt32` 顺序硬编码处加版本分支（参考 diff map）

**完成标准：** 常用插件在 Tuanjie+TypeTree 样本上不崩。

## Task 6 — stripped / classdata（P1）

1. [x] 从团结编辑器 TypeTreeRipper 导出 2022.3.48t5 TypeTree（PlayerSettings/Texture2D/Mesh/Shader 等完整 release/editor）
2. [x] 运行时 classdata 补丁：`TuanjieClassDatabasePatcher` 加载 dump JSON 整树替换 typeId 129/28/43/48
3. [x] 独立包：`ReleaseFiles/classdata_tuanjie.tpk`（+ `.cldb`）由 `tools/TuanjieTpkBuilder` 生成；UABEA 对 Tuanjie 版本优先加载
4. [x] 验证：`globalgamemanagers-windows` PlayerSettings 完整反序列化 + productName 改写回读
5. [x] 验证：`data.tj3d` 内 Texture2D/Shader 反序列化；Texture2D `m_Name` 改写回读（样本无 typeId 43 Mesh 资产）
6. [ ] 用户文档：加载顺序 / 限制说明

**完成标准：** stripped Tuanjie 2022.3.48t5 可正确反序列化 PlayerSettings/Texture2D/Shader，并可对 PS/Tex 做字段改写回读 ✅

### 工具

```powershell
# 重新打包 tpk
dotnet run -c Release --project tools/TuanjieTpkBuilder -- "F:/AI Works/Projects/UnityAssets"

# 回归
dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" validate-types
dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" validate-tj3d
dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" rewrite-tex "HelloTex"
dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" rewrite-ps "HelloPS"
```

**产物：**
- `UABEA/ReleaseFiles/classdata_tuanjie.tpk`
- `UABEA/ReleaseFiles/classdata_tuanjie_2022.3.48t5.cldb`
- `UABEA/ReleaseFiles/classdata_tuanjie/2022.3.48t5/*.json`

## Task 7 —（可选）Vendor AssetsTools.NET

当且仅当 Task 3–6 被 DLL 边界阻塞：

1. 将 AssetsTools.NET 以 submodule 引入（或同步维护 `winlend/AssetsTools.NET`）
2. 工程改 ProjectReference
3. 修 `UnityVersion.ToUInt64` 的 `t` 编码
4. 修 Web 签名枚举
5. 回推 DLL 到 `Libs/` 或改为源码引用

## Task 8 — 文档与发布

1. 更新 `readme.md` 增加 Tuanjie 说明与限制
2. 写最小用户指引：如何打开/修改/保存
3. Release 构建与 smoke test

---

## 实现顺序（依赖）

```
Task0 → Task1 → Task2 → Task3 → Task4 → Task5 → Task6 → Task8
                              ↘ Task7（仅阻塞时）
```

## 非目标（本计划不包含）

- UABEANext 迁移
- 完整 VG Mesh 导出
- 自动破解加密 bundle（UnityCN 等）
- 商业资源侵权用途说明以外的“绕过保护”功能
