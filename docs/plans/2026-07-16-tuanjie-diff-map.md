# Tuanjie 格式差异映射（AssetStudio → UABEA/AssetsTools）

**日期：** 2026-07-16  
**状态：** 调研完成，供实现引用  
**主要参考：**

- `aelurum/AssetStudio` @ `AssetStudioMod`（**优先**）
- `SiMaLaoShi/AssetStudio_Tuanjie`（早期全量分支）
- 本机对照：`F:\AI Works\Projects\UnityAssets\AssetStudio_*`

---

## 1. 版本识别

| 项 | 国际 Unity | 中国 Unity | 团结 Tuanjie |
|----|------------|------------|--------------|
| 版本后缀 | `f` / `p` / `a` / `b` / `x` | 常见 `c` | **`t`** |
| 示例 | `2022.3.48f1` | `2022.3.48c1` | `2022.3.48t3` |
| 团结产品版本映射（不完全） | — | — | `2022.3.2t11 ≈ 1.1.3`，`2022.3.2t13 ≈ 1.2.0`，`2022.3.48t3 ≈ 1.4.0`，`2022.3.61t1 ≈ 1.6.0`，`2022.3.62t1 ≈ 1.7.0` |

### aelurum 判定（推荐移植语义）

```csharp
// BuildType == "t" && version >= 2022.3.2
public bool IsTuanjie => BuildType == "t" && this >= (2022, 3, 2);
```

许多字段还要求 **Build 号阈值**（例如 `2022.3.2t11`），不能只看 major/minor/patch。

### SiMaLaoShi 判定（粗）

```csharp
// unityVersion.Contains("t")
public bool IsTuanJie() => unityVersion.Contains("t");
```

### AssetsTools.NET 现状

`AssetsTools.NET.Extra.UnityVersion` **已能解析** `t` 字符：

```csharp
int verTypeIndex = versionSplit[2].IndexOfAny(new[] { 'f', 'p', 'a', 'b', 'c', 't', 'x' });
```

但 `ToUInt64()` 的 type 编码 **未包含 `t`**（落入 `0xff` / 解码为 `?`）。  
classdata 按版本查找时，`t` 系列可能匹配失败或落到邻近 `f` 布局。

**UABEA 影响：** stripped 文件依赖 `am.LoadClassDatabaseFromPackage(uVer)`；Tuanjie 版本若无专用 classdata，会解析错位。

---

## 2. 容器 / 文件签名

| 签名 | 类型 | aelurum | SiMaLaoShi | 标准 UABEA/AssetsTools |
|------|------|---------|-----------|------------------------|
| `UnityFS` / `UnityWeb` / `UnityRaw` / `UnityArchive` | Bundle | ✅ | ✅ | ✅ |
| `UnityWebData1.0` | WebFile | ✅ | ✅ | 需确认 |
| **`TuanjieWebData1.0`** | WebFile | ✅ | ✅ | ❌ 需补 |

移植点：AssetsTools / UABEA 文件类型探测路径（`FileTypeDetector` 或 AssetsTools 的 Web/Bundle 头解析）。

---

## 3. 按类型字段差异

> 说明：AssetStudio 是**硬编码顺序读字段**；UABEA 对多数类型走 **TypeTree / classdata**。  
> 下表描述的是 **序列化布局差异**，在 TypeTree 存在时应已体现在 TypeTree 节点中；  
> 仅在 **无 TypeTree** 或 **插件手写布局** 时必须手动补丁。

### 3.1 Texture2D

| 条件（aelurum） | 额外字段 | 位置（相对标准布局） |
|-----------------|----------|----------------------|
| `IsTuanjie && (ver > 2022.3.2 \|\| Build >= 8)` ≈ 1.1.0+ | `m_WebStreaming: bool` + align | 在 `m_CompleteImageSize` / `m_MipsStripped` 之后、`m_TextureFormat` 之前 |
| 同上 | `m_PriorityLevel: int32` | 紧随 |
| 同上 | `m_UploadedMode: int32` | 紧随 |
| 同上 | `m_DataStreamData: StreamingInfo` | 紧随（SiMaLaoShi 用独立 `DataStreamingInfo`：`uint size` + string path） |
| `IsTuanjie && ver >= 2022.3.62` ≈ 1.7.0+ | 后续还有额外字段（见 aelurum Texture2D.cs ~L100） | 需对照源码完整移植 |

**UABEA 相关：** `TexturePlugin` 读 `m_Width/m_Height/m_TextureFormat/image data` 等；若用 `GetBaseField`+TypeTree，通常自动对齐；若有硬编码 offset 则要改。

### 3.2 GameObject

| 条件 | 额外字段 |
|------|----------|
| aelurum: `IsTuanjie && (ver > 2022.3.2 \|\| Build >= 11)` ≈ 1.1.3+ | `m_HasEditorInfo: bool` + align，插在 `m_Layer` 与 `m_Name` 之间 |
| SiMaLaoShi: `IsTuanJie() && version[3] >= 13` | 同上（用 version[3] 近似 Build） |

### 3.3 Mesh

团结引入 **Virtual Geometry / SharedCluster** 相关数据：

| 来源 | 行为 |
|------|------|
| aelurum | `SharedClusterData` 多 revision；`IsTuanjie` 时读取 VG 相关块；导出时可能报 `Unsupported mesh type: Virtual Geometry` |
| SiMaLaoShi | 独立 `MeshTuanjie()`：在压缩标志后插入 `m_LightmapUseUV1`、`m_fileScale`、NumInput*、RootClusterPage、ImposterAtlas、Hierarchy*、PageStreaming* 等，末尾 `m_GenerateGeometryBuffer: bool` |

**UABEA MVP：** 打开/列出 Mesh 元数据即可；完整网格导出可后续再做。

### 3.4 AnimationClip

| 条件 | 差异 |
|------|------|
| Tuanjie 通用 | `FloatCurve` / `PPtrCurve` 末尾多 `flags: int32`；`GenericBinding` 多 `isSerializeReferenceCurve: byte` |
| aelurum ≥ 1.4.0 (`2022.3.48t3+`) | `ACLClip` 块 |
| aelurum 1.0–1.5 | muscle/anim data 字段命名与是否内联有差异 |
| aelurum ≥ 1.6.0 (`2022.3.61t1+`) | muscle clip 布局再变 |
| SiMaLaoShi | 整段 `AnimationClipTuanjiej()` 替换构造路径 |

### 3.5 AudioClip

| 条件 | 差异 |
|------|------|
| SiMaLaoShi `IsTuanJie()` | 在 `m_IsTrackerFormat` + align 后多读 `m_Ambisonic: bool`（注意 align 位置） |
| aelurum | 需对照当前分支是否已合入 |

### 3.6 Shader

| 条件 | 差异 |
|------|------|
| SiMaLaoShi | `VectorParameter` / `MatrixParameter` 多 `m_IndexInCB`；ConstantBuffer 可能多 `m_totalParameterCount`；独立 `ShaderTuanjie()` |
| aelurum | 需按版本核对（Shader 导出本就脆弱） |

### 3.7 Material

| 条件 | 差异 |
|------|------|
| SiMaLaoShi `MaterialTuanjie()` | 跳过 4.x keywords 数组路径；**看起来省略** 2020+ `m_BuildTextureStacks`（与国际布局不同，写回时要小心） |
| aelurum | 需按版本核对 |

### 3.8 Animator / Renderer / BuildTarget

| 类型 | aelurum 要点 |
|------|----------------|
| Animator | `2022.3.48t7+` 额外字段 |
| Renderer | 团结从 1.0.0 起有额外字段 |
| BuildTarget | `TuanjieBuildTarget` 枚举与国际 `BuildTarget` 并存 |

---

## 4. 对 UABEA 分层的含义

```
┌─────────────────────────────────────────────┐
│ UABEA UI / 插件 (Texture/Audio/Text/Font)   │  ← 仅当插件手写字段时要改
├─────────────────────────────────────────────┤
│ AssetsTools.NET (Libs/*.dll)                │  ← 版本、TypeTree、Bundle/Web 头
├─────────────────────────────────────────────┤
│ classdata.tpk                               │  ← stripped 文件布局数据库
└─────────────────────────────────────────────┘
```

| 场景 | 是否需要 Tuanjie 补丁 |
|------|------------------------|
| Bundle 含完整 TypeTree | 通常 **否**（通用读写即可） |
| TypeTree 被剥离 + classdata 无 `t` 版本 | **是**（生成/注入 Tuanjie classdata 或字段补丁） |
| TexturePlugin 等用 `GetBaseField` | TypeTree/classdata 对则 **否** |
| 插件或工具手写二进制布局 | **是** |
| `TuanjieWebData1.0` | **是**（签名识别） |

---

## 5. 建议的证据采集（实现前）

用真实团结游戏/项目资源验证，而不是只靠静态 diff：

1. 取 1 个 `*.bundle` / `*.assets`，记录 `Metadata.UnityVersion` 与 `TypeTreeEnabled`
2. 在 AssetStudio_Tuanjie / aelurum 打开，确认 Texture2D 预览是否正常
3. 在未改 UABEA 中打开，记录失败点（打不开 / 列表错 / 预览花屏 / 导出崩溃）
4. 用 UABEA 导出 TypeTree dump，对照上表字段名是否已存在

---

## 6. 源码锚点（本机）

| 主题 | 路径 |
|------|------|
| aelurum 版本 | `AssetStudio_aelurum/AssetStudio/UnityVersion.cs` |
| aelurum Texture2D | `AssetStudio_aelurum/AssetStudio/Classes/Texture2D.cs` |
| aelurum Mesh VG | `AssetStudio_aelurum/AssetStudio/Classes/Mesh.cs` |
| aelurum AnimationClip | `AssetStudio_aelurum/AssetStudio/Classes/AnimationClip.cs` |
| SiMaLaoShi IsTuanJie | `AssetStudio_Tuanjie/AssetStudio/ObjectReader.cs` |
| SiMaLaoShi MeshTuanjie | `AssetStudio_Tuanjie/AssetStudio/Classes/Mesh.cs` |
| AssetsTools UnityVersion | `AssetsTools.NET/AssetTools.NET/Extra/UnityVersion.cs` |
| UABEA classdata 加载 | `UABEA/UABEAvalonia/Forms/MainWindow.axaml.cs` |
