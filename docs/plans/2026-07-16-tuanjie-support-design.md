# 设计：UABEA Tuanjie（团结引擎）支持

**日期：** 2026-07-16  
**状态：** Accepted（方向）  
**Fork：** winlend/UABEA ← nesrak1/UABEA  

---

## 1. 问题

用户需要 **可修改** 团结引擎资源包。现有工具链：

| 工具 | 查看 | 修改 | 团结支持 |
|------|------|------|----------|
| UABEA / AssetsTools.NET | ✅ | ✅ | ❌ / 不完整 |
| aelurum AssetStudioMod | ✅ | ❌ | ✅（较完整） |
| SiMaLaoShi AssetStudio_Tuanjie | ✅ | ❌ | ✅（早期） |

目标：把 AssetStudio 侧已摸清的布局差异，落到 UABEA 的可读写栈。

---

## 2. 架构现实（关键约束）

UABEA **不是** AssetStudio 的 fork。它：

1. 用 **AssetsTools.NET** 解析 Bundle / Assets
2. 用 **TypeTree**（文件内）或 **classdata.tpk**（外部数据库）描述对象布局
3. 用 **插件** 处理 Texture / Audio / Text / Font 的导入导出

因此 **不能** 把 `MeshTuanjie()` 一类方法原样粘贴进 UABEA 就完成任务。正确做法是：

- 能走 TypeTree 的，**不要**重复硬编码
- 不能走 TypeTree 的，再引入最小补丁

---

## 3. 方案对比

### 方案 A — 仅改 UABEA 上层（拒绝作为唯一方案）

只改 GUI、插件、文件对话框。  
**问题：** 版本匹配、Web 签名、stripped classdata 仍在 DLL 层失败。

### 方案 B — Vendor AssetsTools.NET 源码进 fork（推荐中期）

把 AssetsTools.NET 源码以 submodule / 子目录引入，UABEA 工程改为 ProjectReference。  
**优点：** 可修 `UnityVersion.ToUInt64`、`TuanjieWebData1.0`、classdata 查找。  
**缺点：** 与上游同步成本上升。

### 方案 C — 分层最小补丁（**MVP 采用**）

1. **UABEA 层**：签名探测、版本提示文案、Tuanjie 探测工具、插件容错  
2. **AssetsTools 层（按需）**：先继续用 DLL；若阻塞再 vendor  
3. **classdata 层**：对 stripped Tuanjie 文件，用编辑器 TypeTree dump 生成补充条目，或运行时字段补丁  
4. **有 TypeTree 的文件优先** 作为第一验收路径（多数现代游戏 bundle 属于此类）

### 方案 D — 放弃 UABEA，改 AssetStudio 加写回（拒绝）

AssetStudio 写回能力弱，与用户“修改资源包”目标不符。

---

## 4. MVP 范围

### 必须有（P0）

- [ ] 打开含 `t` 版本字符串的 SerializedFile / UnityFS bundle
- [ ] 识别 `TuanjieWebData1.0`（若样本需要）
- [ ] 资源列表正常（pathId / type / name）
- [ ] TypeTree 启用的文件：View Data / Dump 正常
- [ ] Texture2D：预览与导出 PNG（TypeTree 路径）
- [ ] TextAsset：导出/导入
- [ ] 修改后 Save 不损坏未改资源
- [ ] 国际版 Unity 样本回归通过

### 应该有（P1）

- [ ] stripped 文件：Tuanjie 版本可匹配 classdata 或给出明确错误/手动版本映射
- [ ] AudioClip 导出
- [ ] GameObject 树显示不被 `m_HasEditorInfo` 带偏

### 可以后做（P2）

- [ ] Mesh Virtual Geometry 导出
- [ ] Shader 完整解析
- [ ] AnimationClip ACL / muscle 变体全覆盖
- [ ] 完整 Tuanjie classdata.tpk 生成流水线

---

## 5. 技术设计

### 5.1 版本

- 解析：沿用 AssetsTools `UnityVersion`（已认 `t`）
- 比较：需要 `IsTuanjie` 语义时，增加 helper：

```csharp
public static bool IsTuanjie(UnityVersion v)
    => v.type == "t" && (v.major > 2022 || (v.major == 2022 && v.minor >= 3 && v.patch >= 2));
```

- `ToUInt64`：若 classdata 查找依赖它，应给 `t` 独立 typeByte（避免与 `a` 冲突的 `default 0`）

### 5.2 打开路径

```
Open file
  → detect Bundle / Assets / Web / Zip
  → if Web signature TuanjieWebData1.0 → treat as WebFile
  → read Metadata.UnityVersion
  → if TypeTreeEnabled → deserialize via TypeTree
  → else LoadClassDatabaseFromPackage(version)
       → if null && IsTuanjie → fallback map or prompt
```

### 5.3 插件

- TexturePlugin：优先 `AssetTypeValueField` 按字段名取值，避免写死偏移
- 若字段名在 Tuanjie 有新增，忽略未知子节点，只读需要的字段

### 5.4 兼容性守护

```csharp
if (!IsTuanjie(version)) {
    // 100% 原路径
}
```

禁止用 `Contains("t")` 误伤版本字符串其它部分（aelurum 的 BuildType 解析更安全）。

---

## 6. 测试策略

| 用例 | 期望 |
|------|------|
| 国际 Unity 2021/2022 bundle | 与上游行为一致 |
| Tuanjie + TypeTree bundle | 打开、列资源、导出贴图 |
| Tuanjie stripped assets | 明确成功或可操作错误 |
| 改 TextAsset 后写回 | 游戏/引擎可再读 |
| 改 Texture 后写回 | 可选 P1 |

样本目录约定（本地，不进 git）：

`F:\AI Works\Projects\UnityAssets\samples\tuanjie\`  
`F:\AI Works\Projects\UnityAssets\samples\unity-intl\`

---

## 7. 决策记录

| 决策 | 选择 | 原因 |
|------|------|------|
| 基线仓库 | UABEA 非 UABEANext | 用户点名 UABEA；结构更成熟稳定 |
| 差异主参考 | aelurum > SiMaLaoShi | 版本阈值更细、维护更活跃 |
| MVP 路径 | TypeTree 优先 | 改动面小、写回安全 |
| AssetsTools | 先 DLL，阻塞再 vendor | 降低 fork 复杂度 |
| UI | 不新增大导航 | 对齐 skills-manager“少动壳”原则 |
