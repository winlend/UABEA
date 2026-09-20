// Tuanjie regression tools.
// Modes:
//   validate-ps / validate-types / rewrite-ps
//   validate-tj3d  - load data.tj3d and deserialize Texture2D/Mesh/Shader samples
//   rewrite-tex    - rename first Texture2D in data.tj3d assets, save, reload
//
//   dump-tex       - list Texture2D stream vs inline layout
//   rewrite-ress   - ResourceFileBuffer + keep-streaming rewrite test
//
// Usage examples:
//   dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" validate-types
//   dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" validate-tj3d
//   dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" rewrite-tex "HelloTex"
//   dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" dump-tex "F:/Harmony/Tuanjie/11/globalgamemanagers.assets"
//   dotnet run -c Release --project tools/TuanjiePsValidate -- "F:/AI Works/Projects/UnityAssets" rewrite-ress "F:/Harmony/Tuanjie/11/globalgamemanagers.assets"
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UABEAvalonia;

string repoRoot = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string mode = args.Length > 1 ? args[1] : "validate-ps";
string newName = args.Length > 2 ? args[2] : $"TuanjieTest_{DateTime.Now:HHmmss}";

string ggm = Path.Combine(repoRoot, "samples", "tuanjie", "globalgamemanagers-windows");
string tj3d = Path.Combine(repoRoot, "samples", "tuanjie", "data.tj3d");
string tpk = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata.tpk");
string tuanjieTpk = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata_tuanjie.tpk");
string dumpDir = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata_tuanjie", "2022.3.48t5");

Console.WriteLine($"repo={repoRoot}");
Console.WriteLine($"mode={mode}");
Console.WriteLine($"classdata.tpk={File.Exists(tpk)} tuanjie.tpk={File.Exists(tuanjieTpk)} dumpDir={Directory.Exists(dumpDir)}");

// Ensure dumps + tpk available beside tool
foreach (string f in new[] { tuanjieTpk, Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata_tuanjie_2022.3.48t5.cldb") })
{
    if (File.Exists(f))
        File.Copy(f, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(f)), true);
}
string localDump = Path.Combine(AppContext.BaseDirectory, "classdata_tuanjie", "2022.3.48t5");
Directory.CreateDirectory(localDump);
if (Directory.Exists(dumpDir))
{
    foreach (string f in Directory.GetFiles(dumpDir, "*.json"))
        File.Copy(f, Path.Combine(localDump, Path.GetFileName(f)), true);
}

return mode switch
{
    "validate-ps" => ValidatePlayerSettings(ggm, tpk),
    "rewrite-ps" => RewriteProductName(ggm, tpk, newName),
    "validate-types" => ValidateTypeDumps(ggm, tpk),
    "validate-tj3d" => ValidateTj3d(tj3d, tpk),
    "rewrite-tex" => RewriteTextureName(tj3d, tpk, newName),
    "dump-tex" => DumpTextures(args.Length > 2 ? args[2] : ggm, tpk),
    "rewrite-ress" => RewriteResS(args.Length > 2 ? args[2] : ggm, tpk),
    _ => FailUnknown(mode)
};

static int DumpTextures(string assetsPath, string tpk)
{
    Console.WriteLine("assets=" + assetsPath);
    if (!File.Exists(assetsPath))
    {
        Console.WriteLine("missing assets file");
        return 2;
    }

    var am = CreateManager(tpk);
    AssetsFileInstance inst = am.LoadAssetsFile(assetsPath, false);
    AssetsFile file = inst.file;
    Console.WriteLine("unityVersion=" + file.Metadata.UnityVersion);
    Console.WriteLine("hasTypeTree=" + file.Metadata.TypeTreeEnabled);
    Console.WriteLine("assetCount=" + file.AssetInfos.Count);
    _ = RequireClassDb(am, file.Metadata.UnityVersion);

    long inlineBytes = 0;
    long streamBytes = 0;
    long dataStreamBytes = 0;
    int texCount = 0, streamed = 0, inlined = 0, dataStreamed = 0, fail = 0;
    var rows = new List<string>();

    foreach (AssetFileInfo info in file.AssetInfos)
    {
        if (info.TypeId != 28)
            continue;
        texCount++;
        AssetTypeValueField? bf = am.GetBaseField(inst, info);
        if (bf == null)
        {
            fail++;
            rows.Add($"FAIL pathId={info.PathId} size={info.ByteSize}");
            continue;
        }

        string name = SafeString(bf["m_Name"]);
        int w = bf["m_Width"].IsDummy ? -1 : bf["m_Width"].AsInt;
        int h = bf["m_Height"].IsDummy ? -1 : bf["m_Height"].AsInt;
        int fmt = bf["m_TextureFormat"].IsDummy ? -1 : bf["m_TextureFormat"].AsInt;
        int complete = bf["m_CompleteImageSize"].IsDummy ? -1 : bf["m_CompleteImageSize"].AsInt;

        int imgLen = 0;
        AssetTypeValueField img = bf["image data"];
        if (!img.IsDummy)
        {
            if (img.Value != null && img.Value.ValueType == AssetValueType.ByteArray)
                imgLen = img.AsByteArray?.Length ?? 0;
            else if (!img["Array"].IsDummy)
                imgLen = img["Array"].AsByteArray?.Length ?? img["Array"].Children.Count;
            else if (!img["size"].IsDummy)
                imgLen = img["size"].AsInt;
        }

        AssetTypeValueField stream = bf["m_StreamData"];
        ulong soff = 0; uint ssize = 0; string spath = "";
        if (!stream.IsDummy)
        {
            soff = stream["offset"].IsDummy ? 0UL : stream["offset"].AsULong;
            ssize = stream["size"].IsDummy ? 0u : stream["size"].AsUInt;
            spath = stream["path"].IsDummy ? "" : stream["path"].AsString;
        }

        AssetTypeValueField dstream = bf["m_DataStreamData"];
        uint dsize = 0; string dpath = "";
        if (!dstream.IsDummy)
        {
            dsize = dstream["size"].IsDummy ? 0u : dstream["size"].AsUInt;
            dpath = dstream["path"].IsDummy ? "" : dstream["path"].AsString;
        }

        bool web = !bf["m_WebStreaming"].IsDummy && bf["m_WebStreaming"].AsBool;

        inlineBytes += imgLen;
        streamBytes += ssize;
        dataStreamBytes += dsize;
        if (!string.IsNullOrEmpty(spath) && ssize > 0) streamed++;
        else if (imgLen > 0) inlined++;
        if (!string.IsNullOrEmpty(dpath) && dsize > 0) dataStreamed++;

        rows.Add($"pathId={info.PathId,-6} {w}x{h} fmt={fmt,-3} complete={complete,-8} img={imgLen,-8} stream={ssize}@{soff} path='{spath}' dataStream={dsize} dpath='{dpath}' web={web} name={name}");
    }

    foreach (string r in rows)
        Console.WriteLine(r);

    Console.WriteLine($"texCount={texCount} streamed={streamed} inlined={inlined} dataStreamed={dataStreamed} fail={fail}");
    Console.WriteLine($"inlineBytes={inlineBytes} streamBytes={streamBytes} dataStreamBytes={dataStreamBytes}");
    string ress = assetsPath + ".resS";
    Console.WriteLine($"resS exists={File.Exists(ress)} size={(File.Exists(ress) ? new FileInfo(ress).Length : 0)}");
    return fail == 0 ? 0 : 6;
}

static int RewriteResS(string assetsPath, string tpk)
{
    int fail = 0;
    fail += TestResourceBufferInPlace();
    fail += TestResourceBufferAppend();

    string ressPath = assetsPath + ".resS";
    if (File.Exists(assetsPath) && File.Exists(ressPath))
        fail += TestKeepStreamingRewrite(assetsPath, ressPath, tpk);
    else
        Console.WriteLine("SKIP keep-streaming integration (need assets + .resS): " + assetsPath);

    Console.WriteLine(fail == 0 ? "PASS rewrite-ress" : "FAIL rewrite-ress fail=" + fail);
    return fail == 0 ? 0 : 8;
}

static int TestResourceBufferInPlace()
{
    byte[] orig = new byte[100];
    for (int i = 0; i < orig.Length; i++)
        orig[i] = (byte)i;

    var buf = new ResourceFileBuffer("x.resS", "x.assets", orig);
    byte[] neu = new byte[50];
    Array.Fill(neu, (byte)0xAB);
    ulong off = buf.WriteSlot(0, 100, neu);
    if (off != 0) { Console.WriteLine("FAIL in-place offset " + off); return 1; }
    if (buf.Length != 100) { Console.WriteLine("FAIL in-place length " + buf.Length); return 1; }
    byte[] got = buf.GetBytes();
    if (got[0] != 0xAB || got[49] != 0xAB || got[50] != 50)
    {
        Console.WriteLine("FAIL in-place bytes");
        return 1;
    }
    Console.WriteLine("PASS buffer in-place");
    return 0;
}

static int TestResourceBufferAppend()
{
    byte[] orig = new byte[30];
    var buf = new ResourceFileBuffer("x.resS", "x.assets", orig);
    byte[] neu = new byte[40];
    Array.Fill(neu, (byte)0xCD);
    ulong off = buf.WriteSlot(0, 10, neu);
    int expectOff = ResourceFileBuffer.Align16(30);
    if (off != (ulong)expectOff) { Console.WriteLine($"FAIL append offset {off} expected {expectOff}"); return 1; }
    if (buf.Length != expectOff + 40) { Console.WriteLine("FAIL append length " + buf.Length); return 1; }
    byte[] slice = buf.Read(off, 40) ?? Array.Empty<byte>();
    if (slice.Length != 40 || slice[0] != 0xCD)
    {
        Console.WriteLine("FAIL append bytes");
        return 1;
    }
    Console.WriteLine("PASS buffer append");
    return 0;
}

static int TestKeepStreamingRewrite(string assetsPath, string ressPath, string tpk)
{
    string tmp = Path.Combine(Path.GetTempPath(), "uabea-ress-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
    Directory.CreateDirectory(tmp);
    string tmpAssets = Path.Combine(tmp, Path.GetFileName(assetsPath));
    string tmpResS = tmpAssets + ".resS";
    File.Copy(assetsPath, tmpAssets);
    File.Copy(ressPath, tmpResS);

    long origAssetsSize = new FileInfo(tmpAssets).Length;
    long origResSSize = new FileInfo(tmpResS).Length;
    Console.WriteLine($"keep-streaming tmp={tmp} assets={origAssetsSize} resS={origResSSize}");

    var am = CreateManager(tpk);
    AssetsFileInstance inst = am.LoadAssetsFile(tmpAssets, false);
    _ = RequireClassDb(am, inst.file.Metadata.UnityVersion);

    AssetFileInfo? texInfo = FindType(inst.file, 28);
    if (texInfo == null)
    {
        Console.WriteLine("FAIL no Texture2D");
        return 1;
    }

    AssetTypeValueField bf = am.GetBaseField(inst, texInfo) ?? throw new Exception("baseField null");
    AssetTypeValueField stream = bf["m_StreamData"];
    if (stream.IsDummy || string.IsNullOrEmpty(stream["path"].AsString) || stream["size"].AsUInt == 0)
    {
        Console.WriteLine("FAIL sample is not streamed");
        return 1;
    }

    string streamPath = stream["path"].AsString;
    ulong streamOff = stream["offset"].AsULong;
    uint streamSize = stream["size"].AsUInt;

    byte[] onDisk = File.ReadAllBytes(tmpResS);
    if ((ulong)onDisk.Length < streamOff + streamSize)
    {
        Console.WriteLine("FAIL resS shorter than stream slot");
        return 1;
    }
    byte[] payload = new byte[streamSize];
    Buffer.BlockCopy(onDisk, (int)streamOff, payload, 0, (int)streamSize);
    payload[0] ^= 0xFF;

    var buf = ResourceFileBuffer.Load(tmpResS, tmpAssets);
    ulong newOff = buf.WriteSlot(streamOff, streamSize, payload);
    if (newOff != streamOff)
    {
        Console.WriteLine($"FAIL expected in-place offset {streamOff} got {newOff}");
        return 1;
    }
    buf.WriteToFile(tmpResS);

    AssetTypeValueField imageData = bf["image data"];
    imageData.Value.ValueType = AssetValueType.ByteArray;
    imageData.TemplateField.ValueType = AssetValueType.ByteArray;
    imageData.AsByteArray = Array.Empty<byte>();
    stream["offset"].AsULong = newOff;
    stream["size"].AsUInt = (uint)payload.Length;
    stream["path"].AsString = streamPath;
    bf["m_Name"].AsString = SafeString(bf["m_Name"]) + "_ress";

    byte[] newBytes = bf.WriteToByteArray();
    var replacer = new AssetsReplacerFromMemory(inst.file, texInfo, newBytes);
    string outAssets = tmpAssets + ".out";
    using (FileStream fs = File.Create(outAssets))
    using (AssetsFileWriter w = new AssetsFileWriter(fs))
        inst.file.Write(w, 0, new List<AssetsReplacer> { replacer }, null!);

    long newAssetsSize = new FileInfo(outAssets).Length;
    long newResSSize = new FileInfo(tmpResS).Length;
    Console.WriteLine($"rewritten assets={newAssetsSize} resS={newResSSize}");

    if (newAssetsSize > origAssetsSize + 4096)
    {
        Console.WriteLine("FAIL assets grew as if texture was inlined");
        return 1;
    }
    if (newResSSize != origResSSize)
    {
        Console.WriteLine("FAIL resS size changed on in-place write");
        return 1;
    }

    var am2 = CreateManager(tpk);
    AssetsFileInstance inst2 = am2.LoadAssetsFile(outAssets, false);
    _ = RequireClassDb(am2, inst2.file.Metadata.UnityVersion);
    AssetFileInfo tex2 = RequireType(inst2.file, 28);
    AssetTypeValueField bf2 = am2.GetBaseField(inst2, tex2) ?? throw new Exception("reload null");
    string path2 = bf2["m_StreamData"]["path"].AsString;
    uint size2 = bf2["m_StreamData"]["size"].AsUInt;
    int img2 = 0;
    AssetTypeValueField imgField = bf2["image data"];
    if (!imgField.IsDummy)
    {
        if (imgField.Value != null && imgField.Value.ValueType == AssetValueType.ByteArray)
            img2 = imgField.AsByteArray?.Length ?? 0;
        else if (!imgField["size"].IsDummy)
            img2 = imgField["size"].AsInt;
    }
    if (path2 != streamPath || size2 == 0 || img2 != 0)
    {
        Console.WriteLine($"FAIL reload stream path='{path2}' size={size2} img={img2}");
        return 1;
    }

    byte[] ressBytes = File.ReadAllBytes(tmpResS);
    byte orig0 = File.ReadAllBytes(ressPath)[(int)streamOff];
    if (ressBytes[(int)streamOff] != (byte)(orig0 ^ 0xFF))
    {
        Console.WriteLine("FAIL resS payload not patched");
        return 1;
    }

    Console.WriteLine("PASS keep-streaming rewrite");
    return 0;
}

static int FailUnknown(string mode)
{
    Console.WriteLine("unknown mode: " + mode);
    return 1;
}

static AssetsManager CreateManager(string stockTpk)
{
    var am = new AssetsManager();
    am.LoadClassPackage(stockTpk);
    return am;
}

static ClassDatabaseFile RequireClassDb(AssetsManager am, string version)
{
    ClassDatabaseFile? cldb = TuanjieVersion.LoadClassDatabase(am, version);
    if (cldb == null)
        throw new Exception("classdb null for " + version);
    return cldb;
}

static AssetFileInfo RequireType(AssetsFile file, int typeId)
{
    foreach (var a in file.AssetInfos)
        if (a.TypeId == typeId)
            return a;
    throw new Exception("no asset of typeId " + typeId);
}

static AssetFileInfo? FindType(AssetsFile file, int typeId)
{
    foreach (var a in file.AssetInfos)
        if (a.TypeId == typeId)
            return a;
    return null;
}

static bool TreeHasField(ClassDatabaseFile cldb, ClassDatabaseTypeNode node, string fieldName)
{
    if (cldb.GetString(node.FieldName) == fieldName)
        return true;
    foreach (var c in node.Children)
        if (TreeHasField(cldb, c, fieldName))
            return true;
    return false;
}

static string SafeString(AssetTypeValueField f)
{
    if (f.IsDummy) return "<dummy>";
    try { return f.AsString; }
    catch { return $"<type={f.TemplateField.Type}>"; }
}

static int ValidateTypeDumps(string ggm, string tpk)
{
    var am = CreateManager(tpk);
    AssetsFileInstance inst = am.LoadAssetsFile(ggm, false);
    string version = inst.file.Metadata.UnityVersion;
    Console.WriteLine("unityVersion=" + version);
    ClassDatabaseFile cldb = RequireClassDb(am, version);

    (int id, string name, string marker)[] checks =
    {
        (129, "PlayerSettings", "openHarmonyStartInFullscreen"),
        (28, "Texture2D", "m_WebStreaming"),
        (43, "Mesh", "rootClusterPage"),
        (48, "Shader", "m_IndexInCB"),
    };

    int fail = 0;
    foreach (var (id, name, marker) in checks)
    {
        ClassDatabaseType? t = cldb.FindAssetClassByID(id);
        ClassDatabaseTypeNode? root = t?.ReleaseRootNode ?? t?.GetPreferredNode(false);
        if (root == null) { Console.WriteLine($"FAIL {name}: root null"); fail++; continue; }
        bool has = TreeHasField(cldb, root, marker);
        Console.WriteLine($"{name}: topChildren={root.Children.Count} has {marker}={has}");
        if (!has) fail++;
    }
    Console.WriteLine(fail == 0 ? "PASS validate-types" : "FAIL validate-types");
    return fail == 0 ? 0 : 5;
}

static int ValidatePlayerSettings(string ggm, string tpk)
{
    var am = CreateManager(tpk);
    AssetsFileInstance inst = am.LoadAssetsFile(ggm, false);
    AssetsFile file = inst.file;
    string version = file.Metadata.UnityVersion;
    Console.WriteLine("unityVersion=" + version);
    ClassDatabaseFile cldb = RequireClassDb(am, version);
    ClassDatabaseTypeNode? releaseRoot = cldb.FindAssetClassByID(129)?.ReleaseRootNode
        ?? cldb.FindAssetClassByID(129)?.GetPreferredNode(false);
    if (releaseRoot == null) return 4;

    bool hasCompany = releaseRoot.Children.Any(c => cldb.GetString(c.FieldName) == "companyName");
    bool hasProduct = releaseRoot.Children.Any(c => cldb.GetString(c.FieldName) == "productName");
    bool hasOH = releaseRoot.Children.Any(c => cldb.GetString(c.FieldName) == "openHarmonyStartInFullscreen");
    Console.WriteLine($"layout company={hasCompany} product={hasProduct} openHarmony={hasOH}");
    if (!hasCompany || !hasProduct || !hasOH) return 5;

    AssetFileInfo psInfo = RequireType(file, 129);
    AssetTypeValueField baseField = am.GetBaseField(inst, psInfo) ?? throw new Exception("baseField null");
    Console.WriteLine($"companyName={SafeString(baseField["companyName"])}");
    Console.WriteLine($"productName={SafeString(baseField["productName"])}");
    Console.WriteLine("PASS validate-ps");
    return 0;
}

static int RewriteProductName(string ggm, string tpk, string newName)
{
    string outPath = Path.Combine(Path.GetTempPath(), $"tuanjie-ggm-rewrite-{DateTime.Now:yyyyMMddHHmmss}.dat");
    Console.WriteLine("output=" + outPath);

    var am = CreateManager(tpk);
    AssetsFileInstance inst = am.LoadAssetsFile(ggm, false);
    AssetsFile file = inst.file;
    _ = RequireClassDb(am, file.Metadata.UnityVersion);
    AssetFileInfo psInfo = RequireType(file, 129);
    AssetTypeValueField baseField = am.GetBaseField(inst, psInfo) ?? throw new Exception("baseField null");
    string oldName = SafeString(baseField["productName"]);
    Console.WriteLine("old productName=" + oldName);
    baseField["productName"].AsString = newName;

    byte[] newBytes = baseField.WriteToByteArray();
    var replacer = new AssetsReplacerFromMemory(file, psInfo, newBytes);
    using (FileStream fs = File.Create(outPath))
    using (AssetsFileWriter w = new AssetsFileWriter(fs))
        file.Write(w, 0, new List<AssetsReplacer> { replacer }, null!);

    var am2 = CreateManager(tpk);
    AssetsFileInstance inst2 = am2.LoadAssetsFile(outPath, false);
    _ = RequireClassDb(am2, inst2.file.Metadata.UnityVersion);
    AssetFileInfo ps2 = RequireType(inst2.file, 129);
    AssetTypeValueField base2 = am2.GetBaseField(inst2, ps2) ?? throw new Exception("reload null");
    string readBack = SafeString(base2["productName"]);
    Console.WriteLine("reload productName=" + readBack);
    Console.WriteLine("reload companyName=" + SafeString(base2["companyName"]));
    if (readBack != newName) return 7;
    Console.WriteLine("PASS rewrite-ps");
    return 0;
}

static List<(string cabName, AssetsFileInstance inst)> LoadInterestingCabFiles(AssetsManager am, string tj3d)
{
    BundleFileInstance bun = am.LoadBundleFile(tj3d, false);
    var list = new List<(string, AssetsFileInstance)>();
    var dirs = bun.file.BlockAndDirInfo.DirectoryInfos;
    for (int i = 0; i < dirs.Length; i++)
    {
        string name = dirs[i].Name;
        // skip .resS
        if (name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
            continue;
        try
        {
            AssetsFileInstance? inst = am.LoadAssetsFileFromBundle(bun, i, false);
            if (inst != null)
                list.Add((name, inst));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"skip cab {name}: {ex.Message}");
        }
    }
    return list;
}

static int ValidateTj3d(string tj3d, string tpk)
{
    if (!File.Exists(tj3d))
    {
        Console.WriteLine("FAIL missing data.tj3d");
        return 2;
    }

    var am = CreateManager(tpk);
    var cabs = LoadInterestingCabFiles(am, tj3d);
    Console.WriteLine($"loaded cabs={cabs.Count}");

    int ok = 0, fail = 0;
    var seen = new HashSet<int>();

    foreach (var (cabName, inst) in cabs)
    {
        string version = inst.file.Metadata.UnityVersion;
        ClassDatabaseFile cldb = RequireClassDb(am, version);

        foreach (int typeId in new[] { 28, 43, 48 })
        {
            AssetFileInfo? info = FindType(inst.file, typeId);
            if (info == null) continue;
            if (!seen.Add(typeId))
            {
                // still try read one per type total is enough; continue counting
            }

            try
            {
                AssetTypeValueField? bf = am.GetBaseField(inst, info);
                if (bf == null)
                {
                    Console.WriteLine($"FAIL {cabName} type {typeId}: baseField null");
                    fail++;
                    continue;
                }

                string mName = SafeString(bf["m_Name"]);
                // Type-specific markers
                string markerInfo = typeId switch
                {
                    28 => $"m_WebStreaming={SafeString(bf["m_WebStreaming"])} m_Width={SafeString(bf["m_Width"])}",
                    43 => $"rootClusterPage dummy={bf["rootClusterPage"].IsDummy} m_KeepIndices={SafeString(bf["m_KeepIndices"])}",
                    48 => $"m_ParsedForm dummy={bf["m_ParsedForm"].IsDummy} m_Name={mName}",
                    _ => ""
                };

                int visited = 0;
                void Walk(AssetTypeValueField f)
                {
                    visited++;
                    foreach (var c in f.Children) Walk(c);
                }
                Walk(bf);

                Console.WriteLine($"OK {cabName} typeId={typeId} pathId={info.PathId} name={mName} walked={visited} {markerInfo}");
                ok++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL {cabName} typeId={typeId} pathId={info.PathId}: {ex.GetType().Name}: {ex.Message}");
                if (typeId==28) Console.WriteLine(ex.StackTrace);
                fail++;
            }
        }
    }

    Console.WriteLine($"summary ok={ok} fail={fail}");
    Console.WriteLine(fail == 0 && ok > 0 ? "PASS validate-tj3d" : "FAIL validate-tj3d");
    return fail == 0 && ok > 0 ? 0 : 9;
}

static int RewriteTextureName(string tj3d, string tpk, string newName)
{
    if (!File.Exists(tj3d))
    {
        Console.WriteLine("FAIL missing data.tj3d");
        return 2;
    }

    string outPath = Path.Combine(Path.GetTempPath(), $"tuanjie-tex-rewrite-{DateTime.Now:yyyyMMddHHmmss}.assets");
    Console.WriteLine("output=" + outPath);
    Console.WriteLine("new m_Name=" + newName);

    var am = CreateManager(tpk);
    BundleFileInstance bun = am.LoadBundleFile(tj3d, false);
    var dirs = bun.file.BlockAndDirInfo.DirectoryInfos;

    AssetsFileInstance? targetInst = null;
    AssetFileInfo? targetInfo = null;
    string targetCab = "";

    for (int i = 0; i < dirs.Length; i++)
    {
        string name = dirs[i].Name;
        if (name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
            continue;
        AssetsFileInstance? inst;
        try { inst = am.LoadAssetsFileFromBundle(bun, i, false); }
        catch { continue; }
        if (inst == null) continue;

        AssetFileInfo? tex = FindType(inst.file, 28);
        if (tex != null)
        {
            targetInst = inst;
            targetInfo = tex;
            targetCab = name;
            break;
        }
    }

    if (targetInst == null || targetInfo == null)
    {
        Console.WriteLine("FAIL no Texture2D found in data.tj3d");
        return 6;
    }

    Console.WriteLine($"using cab={targetCab} pathId={targetInfo.PathId}");
    _ = RequireClassDb(am, targetInst.file.Metadata.UnityVersion);
    AssetTypeValueField bf = am.GetBaseField(targetInst, targetInfo) ?? throw new Exception("baseField null");
    string old = SafeString(bf["m_Name"]);
    Console.WriteLine("old m_Name=" + old);
    Console.WriteLine("m_WebStreaming=" + SafeString(bf["m_WebStreaming"]));
    Console.WriteLine("m_Width=" + SafeString(bf["m_Width"]));

    bf["m_Name"].AsString = newName;
    byte[] bytes = bf.WriteToByteArray();
    var replacer = new AssetsReplacerFromMemory(targetInst.file, targetInfo, bytes);
    using (FileStream fs = File.Create(outPath))
    using (AssetsFileWriter w = new AssetsFileWriter(fs))
        targetInst.file.Write(w, 0, new List<AssetsReplacer> { replacer }, null!);

    // Reload standalone assets file
    var am2 = CreateManager(tpk);
    AssetsFileInstance inst2 = am2.LoadAssetsFile(outPath, false);
    _ = RequireClassDb(am2, inst2.file.Metadata.UnityVersion);
    AssetFileInfo tex2 = RequireType(inst2.file, 28);
    AssetTypeValueField bf2 = am2.GetBaseField(inst2, tex2) ?? throw new Exception("reload null");
    string readBack = SafeString(bf2["m_Name"]);
    Console.WriteLine("reload m_Name=" + readBack);
    Console.WriteLine("reload m_WebStreaming=" + SafeString(bf2["m_WebStreaming"]));
    Console.WriteLine("reload m_Width=" + SafeString(bf2["m_Width"]));
    if (readBack != newName) return 7;
    Console.WriteLine("PASS rewrite-tex");
    return 0;
}
