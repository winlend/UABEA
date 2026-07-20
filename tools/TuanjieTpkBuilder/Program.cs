// Builds classdata_tuanjie.tpk (+ .cldb) from stock classdata + Tuanjie dumps.
//
// Package version key is 2022.3.48f1 (parseable by shipped AssetsTools),
// while type trees are Tuanjie-patched for 2022.3.48t5 content.
//
// Usage:
//   dotnet run -c Release --project tools/TuanjieTpkBuilder -- "F:/AI Works/Projects/UnityAssets"
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UABEAvalonia;

string repoRoot = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

string stockTpk = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata.tpk");
string dumpDir = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata_tuanjie", "2022.3.48t5");
string outTpk = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata_tuanjie.tpk");
string outCldb = Path.Combine(repoRoot, "UABEA", "ReleaseFiles", "classdata_tuanjie_2022.3.48t5.cldb");

Console.WriteLine($"repo={repoRoot}");
if (!File.Exists(stockTpk) || !Directory.Exists(dumpDir))
{
    Console.WriteLine("FAIL missing inputs");
    return 2;
}

string localDump = Path.Combine(AppContext.BaseDirectory, "classdata_tuanjie", "2022.3.48t5");
Directory.CreateDirectory(localDump);
foreach (string f in Directory.GetFiles(dumpDir, "*.json"))
    File.Copy(f, Path.Combine(localDump, Path.GetFileName(f)), true);

var am = new AssetsManager();
am.LoadClassPackage(stockTpk);
ClassDatabaseFile cldb = am.LoadClassDatabaseFromPackage("2022.3.48f1")
    ?? throw new Exception("failed to load 2022.3.48f1 classdata");
TuanjieClassDatabasePatcher.Apply(cldb, "2022.3.48t5");

foreach (var (id, marker) in new[]
{
    (129, "openHarmonyStartInFullscreen"),
    (28, "m_WebStreaming"),
    (43, "rootClusterPage"),
    (48, "m_IndexInCB"),
})
{
    ClassDatabaseType? t = cldb.FindAssetClassByID(id);
    ClassDatabaseTypeNode? root = t?.ReleaseRootNode ?? t?.GetPreferredNode(false);
    bool ok = root != null && HasField(cldb, root, marker);
    Console.WriteLine($"type {id} marker {marker}: {(ok ? "OK" : "MISSING")}");
    if (!ok) return 3;
}

UnityVersion packageVersion = new UnityVersion("2022.3.48f1");

// CLDB
using (FileStream fs = File.Create(outCldb))
using (AssetsFileWriter w = new AssetsFileWriter(fs))
{
    cldb.Header.Magic = "CLDB";
    cldb.Header.FileVersion = 1;
    cldb.Header.Version = packageVersion;
    cldb.Header.CompressionType = ClassFileCompressionType.Uncompressed;
    cldb.Write(w, ClassFileCompressionType.Uncompressed);
}
Console.WriteLine($"wrote CLDB {new FileInfo(outCldb).Length} bytes -> {outCldb}");

// TPK (custom writer: stock Write has stream-position/ushort issues on this DLL path)
byte[] tpkBytes = BuildTpkBytes(cldb, packageVersion);
File.WriteAllBytes(outTpk, tpkBytes);
Console.WriteLine($"wrote TPK {tpkBytes.Length} bytes -> {outTpk}");

// Verify TPK reload
var am2 = new AssetsManager();
am2.LoadClassPackage(outTpk);
ClassDatabaseFile reloaded = am2.LoadClassDatabaseFromPackage("2022.3.48f1")
    ?? throw new Exception("tpk reload null");
Console.WriteLine($"tpk reload classes={reloaded.Classes.Count}");
bool psOk = HasField(reloaded, Root(reloaded, 129), "openHarmonyStartInFullscreen");
bool texOk = HasField(reloaded, Root(reloaded, 28), "m_WebStreaming");
bool meshOk = HasField(reloaded, Root(reloaded, 43), "rootClusterPage");
bool shaderOk = HasField(reloaded, Root(reloaded, 48), "m_IndexInCB");
Console.WriteLine($"tpk markers ps={psOk} tex={texOk} mesh={meshOk} shader={shaderOk}");
if (!(psOk && texOk && meshOk && shaderOk)) return 5;

// Verify CLDB reload
var am3 = new AssetsManager();
ClassDatabaseFile cldb2 = am3.LoadClassDatabase(outCldb);
Console.WriteLine($"cldb reload classes={cldb2.Classes.Count}");
Console.WriteLine("PASS build classdata_tuanjie.tpk");
return 0;

static ClassDatabaseTypeNode? Root(ClassDatabaseFile db, int id)
{
    ClassDatabaseType? t = db.FindAssetClassByID(id);
    return t?.ReleaseRootNode ?? t?.GetPreferredNode(false);
}

static bool HasField(ClassDatabaseFile cldb, ClassDatabaseTypeNode? node, string field)
{
    if (cldb == null || node == null) return false;
    if (cldb.GetString(node.FieldName) == field) return true;
    foreach (var c in node.Children)
        if (HasField(cldb, c, field)) return true;
    return false;
}

static byte[] BuildTpkBytes(ClassDatabaseFile cldb, UnityVersion version)
{
    // Flatten trees into package node pool (matching ClassPackageTypeTree.Read format)
    var nodes = new List<NodeRec>();
    ushort AddNode(ClassDatabaseTypeNode src)
    {
        ushort idx = (ushort)nodes.Count;
        nodes.Add(new NodeRec()); // placeholder
        var childIdx = new List<ushort>(src.Children.Count);
        foreach (var child in src.Children)
            childIdx.Add(AddNode(child));
        nodes[idx] = new NodeRec
        {
            TypeName = src.TypeName,
            FieldName = src.FieldName,
            ByteSize = src.ByteSize,
            Version = src.Version,
            TypeFlags = src.TypeFlags,
            MetaFlag = src.MetaFlag,
            SubNodes = childIdx.ToArray()
        };
        return idx;
    }

    var classes = new List<ClassRec>();
    foreach (ClassDatabaseType t in cldb.Classes)
    {
        ushort editor = ushort.MaxValue;
        ushort release = ushort.MaxValue;
        byte flags = (byte)t.Flags;
        if (t.EditorRootNode != null)
        {
            editor = AddNode(t.EditorRootNode);
            flags |= (byte)ClassFileTypeFlags.HasEditorRootNode;
        }
        if (t.ReleaseRootNode != null)
        {
            release = AddNode(t.ReleaseRootNode);
            flags |= (byte)ClassFileTypeFlags.HasReleaseRootNode;
        }
        classes.Add(new ClassRec
        {
            ClassId = t.ClassId,
            Name = t.Name,
            BaseName = t.BaseName,
            Flags = flags,
            EditorRoot = editor,
            ReleaseRoot = release
        });
    }

    // Body stream
    using var body = new MemoryStream();
    using (var bw = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
    {
        bw.Write(DateTime.UtcNow.ToBinary()); // CreationTime

        // Versions
        bw.Write(1);
        bw.Write(version.ToUInt64());

        // ClassInformation
        bw.Write(classes.Count);
        foreach (var c in classes)
        {
            bw.Write(c.ClassId);
            bw.Write(1); // one version entry
            bw.Write(version.ToUInt64());
            bw.Write((byte)1); // hasClassData
            bw.Write(c.Name);
            bw.Write(c.BaseName);
            bw.Write(c.Flags);
            if ((c.Flags & (byte)ClassFileTypeFlags.HasEditorRootNode) != 0)
                bw.Write(c.EditorRoot);
            if ((c.Flags & (byte)ClassFileTypeFlags.HasReleaseRootNode) != 0)
                bw.Write(c.ReleaseRoot);
        }

        // CommonString: one version with length 0; empty indices (or copy stock)
        bw.Write(1);
        bw.Write(version.ToUInt64());
        bw.Write((byte)0);
        var indices = cldb.CommonStringBufferIndices ?? new List<ushort>();
        bw.Write(indices.Count);
        foreach (ushort ix in indices)
            bw.Write(ix);

        // Nodes
        bw.Write(nodes.Count);
        foreach (var n in nodes)
        {
            bw.Write(n.TypeName);
            bw.Write(n.FieldName);
            bw.Write(n.ByteSize);
            bw.Write(n.Version);
            bw.Write(n.TypeFlags);
            bw.Write(n.MetaFlag);
            bw.Write((ushort)n.SubNodes.Length); // MUST be ushort to match Read
            foreach (ushort s in n.SubNodes)
                bw.Write(s);
        }
    }

    // StringTable: reuse cldb string table writer
    using (var aw = new AssetsFileWriter(body))
    {
        // AssetsFileWriter writes at current position
        cldb.StringTable.Write(aw);
    }

    byte[] bodyBytes = body.ToArray();

    // Header + body (uncompressed)
    using var ms = new MemoryStream();
    using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
    {
        bw.Write(Encoding.ASCII.GetBytes("TPK*"));
        bw.Write((byte)1); // FileVersion
        bw.Write((byte)0); // Uncompressed
        bw.Write((byte)1); // DataType
        bw.Write((byte)0); // reserved
        bw.Write(0);       // reserved u32
        bw.Write((uint)bodyBytes.Length); // CompressedSize
        bw.Write((uint)bodyBytes.Length); // DecompressedSize
        bw.Write(bodyBytes);
    }
    return ms.ToArray();
}

sealed class NodeRec
{
    public ushort TypeName;
    public ushort FieldName;
    public int ByteSize;
    public ushort Version;
    public byte TypeFlags;
    public uint MetaFlag;
    public ushort[] SubNodes = Array.Empty<ushort>();
}

sealed class ClassRec
{
    public int ClassId;
    public ushort Name;
    public ushort BaseName;
    public byte Flags;
    public ushort EditorRoot;
    public ushort ReleaseRoot;
}
