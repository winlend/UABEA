using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AssetsTools.NET;

namespace UABEAvalonia
{
    /// <summary>
    /// Runtime patches for classdata templates so TypeTree-stripped Tuanjie assets
    /// deserialize with the extra fields documented in AssetStudio (aelurum / SiMaLaoShi),
    /// plus full PlayerSettings trees dumped from Tuanjie 2022.3.48t5.
    /// Applied after loading a nearest international classdata (e.g. 2022.3.48f1).
    /// </summary>
    public static class TuanjieClassDatabasePatcher
    {
        private const uint AlignMeta = 0x4000;
        private const uint ArrayTypeFlag = 1;

        /// <summary>
        /// Patch in-place. Safe to call on non-Tuanjie versions (no-op).
        /// Prefer full dump trees when present; otherwise apply field-level patches.
        /// </summary>
        public static void Apply(ClassDatabaseFile cldb, string originalVersion)
        {
            if (cldb == null || !TuanjieVersion.IsTuanjie(originalVersion))
                return;

            // Full TypeTree dumps (Tuanjie 2022.3.48t5) replace stock classdata roots when available.
            bool texReplaced = TryReplaceTypeFromDump(cldb, originalVersion, 28, "Texture2D", "m_WebStreaming");
            bool meshReplaced = TryReplaceTypeFromDump(cldb, originalVersion, 43, "Mesh", "rootClusterPage");
            bool shaderReplaced = TryReplaceTypeFromDump(cldb, originalVersion, 48, "Shader", "m_IndexInCB");
            bool playerReplaced = TryReplaceTypeFromDump(cldb, originalVersion, 129, "PlayerSettings", "openHarmonyStartInFullscreen");

            if (!texReplaced)
                PatchTexture2D(cldb, originalVersion);
            PatchGameObject(cldb, originalVersion);
            if (!meshReplaced)
                PatchMesh(cldb, originalVersion);
            if (!shaderReplaced)
                PatchShader(cldb, originalVersion);
            PatchQualitySettings(cldb, originalVersion);
            if (!playerReplaced)
                PatchPlayerSettings(cldb, originalVersion);
        }

        private static void PatchTexture2D(ClassDatabaseFile cldb, string version)
        {
            // 2022.3.2t8 (1.1.0)+ : web streaming block after m_MipsStripped
            if (!TuanjieVersion.IsAtLeast(version, 2022, 3, 2, 8))
                return;

            ClassDatabaseType? type = cldb.FindAssetClassByID(28); // Texture2D
            ClassDatabaseTypeNode? root = type?.GetPreferredNode(false);
            if (root == null)
                return;

            if (FindChildIndex(cldb, root, "m_WebStreaming") >= 0)
                return; // already patched

            int afterMips = FindChildIndex(cldb, root, "m_MipsStripped");
            if (afterMips < 0)
                return;

            List<ClassDatabaseTypeNode> insert = new List<ClassDatabaseTypeNode>
            {
                Leaf(cldb, "bool", "m_WebStreaming", 1, AlignMeta),
                Leaf(cldb, "int", "m_PriorityLevel", 4, 0),
                Leaf(cldb, "int", "m_UploadedMode", 4, 0),
                MakeDataStreamingInfo(cldb),
            };

            root.Children.InsertRange(afterMips + 1, insert);

            // 2022.3.62t1 (1.7.0)+ : multi-format blob after m_TextureFormat (not in our samples)
            if (TuanjieVersion.IsAtLeast(version, 2022, 3, 62, 1))
            {
                int afterFmt = FindChildIndex(cldb, root, "m_TextureFormat");
                if (afterFmt >= 0 && FindChildIndex(cldb, root, "m_TextureManagerMultiFormatSetting") < 0)
                {
                    root.Children.Insert(afterFmt + 1, MakeByteVector(cldb, "m_TextureManagerMultiFormatSetting", AlignMeta));
                }
            }
        }

        private static void PatchGameObject(ClassDatabaseFile cldb, string version)
        {
            // 2022.3.2t11 (1.1.3)+ : m_HasEditorInfo between m_Layer and m_Name
            if (!TuanjieVersion.IsAtLeast(version, 2022, 3, 2, 11))
                return;

            ClassDatabaseType? type = cldb.FindAssetClassByID(1); // GameObject
            ClassDatabaseTypeNode? root = type?.GetPreferredNode(false);
            if (root == null)
                return;

            if (FindChildIndex(cldb, root, "m_HasEditorInfo") >= 0)
                return;

            int afterLayer = FindChildIndex(cldb, root, "m_Layer");
            if (afterLayer < 0)
                return;

            root.Children.Insert(afterLayer + 1, Leaf(cldb, "bool", "m_HasEditorInfo", 1, AlignMeta));
        }

        private static void PatchMesh(ClassDatabaseFile cldb, string version)
        {
            ClassDatabaseType? type = cldb.FindAssetClassByID(43); // Mesh
            ClassDatabaseTypeNode? root = type?.GetPreferredNode(false);
            if (root == null)
                return;

            // SharedCluster after m_KeepIndices, before m_IndexFormat (all Tuanjie meshes)
            if (FindChildIndex(cldb, root, "m_SharedClusterData") < 0)
            {
                int afterKeep = FindChildIndex(cldb, root, "m_KeepIndices");
                if (afterKeep >= 0)
                {
                    // Our samples are 2022.3.48t3 / t5 → SharedCluster rev 2
                    // rev1: build < 2022.3.48t3
                    // rev2: 2022.3.48t3 .. 2022.3.61t1
                    // rev3: 2022.3.61t2+
                    int rev = 2;
                    if (!TuanjieVersion.IsAtLeast(version, 2022, 3, 48, 3))
                        rev = 1;
                    else if (TuanjieVersion.IsAtLeast(version, 2022, 3, 61, 2))
                        rev = 3;

                    root.Children.Insert(afterKeep + 1, MakeSharedClusterData(cldb, rev));
                }
            }

            // Trailing flags after m_StreamData (1.2.0+)
            if (TuanjieVersion.IsAtLeast(version, 2022, 3, 2, 13)
                && FindChildIndex(cldb, root, "m_GenerateGeometryBuffer") < 0)
            {
                int afterStream = FindChildIndex(cldb, root, "m_StreamData");
                if (afterStream >= 0)
                {
                    root.Children.Insert(afterStream + 1, Leaf(cldb, "bool", "m_GenerateGeometryBuffer", 1, 0));
                    root.Children.Insert(afterStream + 2, Leaf(cldb, "bool", "m_HasVirtualGeometryMesh", 1, 0));
                }
            }
        }

        /// <summary>
        /// Shader: Tuanjie adds m_IndexInCB on Vector/MatrixParameter and
        /// m_totalParameterCount before m_IsPartialCB on ConstantBuffer
        /// (SiMaLaoShi AssetStudio_Tuanjie).
        /// Nested types appear many times under m_ParsedForm — walk the whole tree.
        /// </summary>
        private static void PatchShader(ClassDatabaseFile cldb, string version)
        {
            ClassDatabaseType? type = cldb.FindAssetClassByID(48); // Shader
            ClassDatabaseTypeNode? root = type?.GetPreferredNode(false);
            if (root == null)
                return;

            PatchShaderNodesRecursive(cldb, root);
        }

        private static void PatchShaderNodesRecursive(ClassDatabaseFile cldb, ClassDatabaseTypeNode node)
        {
            string typeName = cldb.GetString(node.TypeName);

            if (typeName is "VectorParameter" or "MatrixParameter")
            {
                // Insert m_IndexInCB (int) after m_ArraySize, before m_Type
                if (FindChildIndex(cldb, node, "m_IndexInCB") < 0)
                {
                    int afterArray = FindChildIndex(cldb, node, "m_ArraySize");
                    if (afterArray >= 0)
                        node.Children.Insert(afterArray + 1, Leaf(cldb, "int", "m_IndexInCB", 4, 0));
                }
            }
            else if (typeName == "ConstantBuffer")
            {
                // Insert m_totalParameterCount (int) before m_IsPartialCB
                if (FindChildIndex(cldb, node, "m_totalParameterCount") < 0)
                {
                    int partial = FindChildIndex(cldb, node, "m_IsPartialCB");
                    if (partial >= 0)
                        node.Children.Insert(partial, Leaf(cldb, "int", "m_totalParameterCount", 4, 0));
                }
            }

            foreach (ClassDatabaseTypeNode child in node.Children)
                PatchShaderNodesRecursive(cldb, child);
        }

        /// <summary>
        /// QualitySettings / QualitySetting: Tuanjie 2022.3.48t samples match the 2023.2
        /// adaptive VSync fields (confirmed against data.tj3d).
        /// </summary>
        private static void PatchQualitySettings(ClassDatabaseFile cldb, string version)
        {
            ClassDatabaseType? type = cldb.FindAssetClassByID(47);
            ClassDatabaseTypeNode? root = type?.GetPreferredNode(false);
            if (root == null)
                return;

            // vector m_QualitySettings → Array → QualitySetting data
            int qsVec = FindChildIndex(cldb, root, "m_QualitySettings");
            if (qsVec < 0 || root.Children[qsVec].Children.Count == 0)
                return;

            ClassDatabaseTypeNode arrayNode = root.Children[qsVec].Children[0];
            if (arrayNode.Children.Count < 2)
                return;

            ClassDatabaseTypeNode data = arrayNode.Children[1];
            if (FindChildIndex(cldb, data, "adaptiveVsync") >= 0)
                return;

            int legacy = FindChildIndex(cldb, data, "useLegacyDetailDistribution");
            if (legacy < 0)
                return;

            // 2023.2: legacy has no align; adaptiveVsync bool+align; then vSyncCount...
            data.Children[legacy].MetaFlag = 0;
            data.Children.Insert(legacy + 1, Leaf(cldb, "bool", "adaptiveVsync", 1, AlignMeta));

            int realtime = FindChildIndex(cldb, data, "realtimeGICPUUsage");
            if (realtime >= 0)
            {
                data.Children.Insert(realtime + 1, Leaf(cldb, "int", "adaptiveVsyncExtraA", 4, 0));
                data.Children.Insert(realtime + 2, Leaf(cldb, "int", "adaptiveVsyncExtraB", 4, 0));
            }
        }

        /// <summary>
        /// PlayerSettings fallback when no dump JSON is available: after numberOfMipsStripped
        /// the Tuanjie layout diverges from international classdata; collapse the unknown tail
        /// into one TypelessData so companyName/productName still deserialize.
        /// </summary>
        private static void PatchPlayerSettings(ClassDatabaseFile cldb, string version)
        {
            ClassDatabaseType? type = cldb.FindAssetClassByID(129);
            ClassDatabaseTypeNode? root = type?.GetPreferredNode(false);
            if (root == null)
                return;

            if (FindChildIndex(cldb, root, "m_TuanjieTailBlob") >= 0)
                return;
            // Already full dump
            if (FindChildIndex(cldb, root, "openHarmonyStartInFullscreen") >= 0)
                return;

            int afterMips = FindChildIndex(cldb, root, "numberOfMipsStripped");
            if (afterMips < 0)
                return;

            int removeFrom = afterMips + 1;
            if (removeFrom < root.Children.Count)
                root.Children.RemoveRange(removeFrom, root.Children.Count - removeFrom);

            root.Children.Add(MakeByteVector(cldb, "m_TuanjieTailBlob", AlignMeta));
        }

        /// <summary>
        /// Replace a class database type's release/editor roots from dumped TypeTree JSON.
        /// <paramref name="idempotentMarkerField"/> is a Tuanjie-specific field used to detect
        /// that a full dump has already been applied.
        /// </summary>
        private static bool TryReplaceTypeFromDump(
            ClassDatabaseFile cldb,
            string version,
            int typeId,
            string typeName,
            string idempotentMarkerField)
        {
            ClassDatabaseType? type = cldb.FindAssetClassByID(typeId);
            if (type == null)
                return false;

            ClassDatabaseTypeNode? existing = type.ReleaseRootNode ?? type.GetPreferredNode(false);
            if (existing != null && FindChildIndex(cldb, existing, idempotentMarkerField) >= 0)
                return true;

            string? dumpDir = ResolveDumpDirectory(version);
            if (dumpDir == null)
                return false;

            string releasePath = Path.Combine(dumpDir, $"{typeName}.release.json");
            string editorPath = Path.Combine(dumpDir, $"{typeName}.editor.json");
            if (!File.Exists(releasePath) && !File.Exists(editorPath))
                return false;

            ClassDatabaseTypeNode? releaseRoot = null;
            ClassDatabaseTypeNode? editorRoot = null;
            try
            {
                if (File.Exists(releasePath))
                    releaseRoot = BuildTreeFromDumpJson(cldb, File.ReadAllText(releasePath));
                if (File.Exists(editorPath))
                    editorRoot = BuildTreeFromDumpJson(cldb, File.ReadAllText(editorPath));
            }
            catch
            {
                return false;
            }

            if (releaseRoot == null && editorRoot == null)
                return false;

            // Sanity: dump root should expose the marker field somewhere in the tree.
            ClassDatabaseTypeNode? check = releaseRoot ?? editorRoot;
            if (check != null && !TreeContainsField(cldb, check, idempotentMarkerField))
                return false;

            if (releaseRoot != null)
                type.ReleaseRootNode = releaseRoot;
            if (editorRoot != null)
                type.EditorRootNode = editorRoot;
            else if (releaseRoot != null && type.EditorRootNode == null)
                type.EditorRootNode = releaseRoot;

            ClassFileTypeFlags flags = type.Flags;
            if (type.EditorRootNode != null)
                flags |= ClassFileTypeFlags.HasEditorRootNode;
            if (type.ReleaseRootNode != null)
                flags |= ClassFileTypeFlags.HasReleaseRootNode;
            type.Flags = flags;
            return true;
        }

        private static bool TreeContainsField(ClassDatabaseFile cldb, ClassDatabaseTypeNode node, string fieldName)
        {
            if (cldb.GetString(node.FieldName) == fieldName)
                return true;
            foreach (ClassDatabaseTypeNode child in node.Children)
            {
                if (TreeContainsField(cldb, child, fieldName))
                    return true;
            }
            return false;
        }

        private static string? ResolveDumpDirectory(string version)
        {
            // Current dump is for 2022.3.48t5; reuse for any 2022.3.48t*
            if (!TuanjieVersion.IsTuanjie(version))
                return null;
            if (!version.StartsWith("2022.3.48t", StringComparison.OrdinalIgnoreCase))
                return null;

            string folder = "2022.3.48t5";
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "classdata_tuanjie", folder),
                Path.Combine(baseDir, "ReleaseFiles", "classdata_tuanjie", folder),
                Path.Combine(baseDir, "..", "ReleaseFiles", "classdata_tuanjie", folder),
                // From bin/Debug|Release/netX.Y/ back to repo ReleaseFiles/
                Path.Combine(baseDir, "..", "..", "..", "..", "ReleaseFiles", "classdata_tuanjie", folder),
            };

            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full)
                    && (File.Exists(Path.Combine(full, "PlayerSettings.release.json"))
                        || File.Exists(Path.Combine(full, "Texture2D.release.json"))))
                {
                    return full;
                }
            }
            return null;
        }

        private sealed class DumpTypeDto
        {
            public int typeId { get; set; }
            public int size { get; set; }
            public List<DumpNodeDto>? nodes { get; set; }
        }

        private sealed class DumpNodeDto
        {
            public int level { get; set; }
            public string? type { get; set; }
            public string? name { get; set; }
            public int byteSize { get; set; }
            public int index { get; set; }
            public int version { get; set; }
            public uint metaFlag { get; set; }
            public uint flags { get; set; }
        }

        private static ClassDatabaseTypeNode BuildTreeFromDumpJson(ClassDatabaseFile cldb, string json)
        {
            DumpTypeDto? dto = JsonSerializer.Deserialize<DumpTypeDto>(json);
            if (dto?.nodes == null || dto.nodes.Count == 0)
                throw new InvalidDataException("Type dump JSON has no nodes");

            var stack = new List<(int level, ClassDatabaseTypeNode node)>();
            ClassDatabaseTypeNode? root = null;

            foreach (DumpNodeDto n in dto.nodes)
            {
                string typeName = n.type ?? string.Empty;
                string fieldName = n.name ?? string.Empty;
                uint typeFlags = n.flags;
                // AssetsTools treats TypelessData as ByteArray only when IsArray is set
                // (TypeFlags bit0). Dump often leaves flags=0 for TypelessData/"Array".
                if (typeName == "Array" || typeName == "TypelessData")
                    typeFlags |= ArrayTypeFlag;

                var node = new ClassDatabaseTypeNode
                {
                    TypeName = cldb.StringTable.AddString(typeName),
                    FieldName = cldb.StringTable.AddString(fieldName),
                    ByteSize = n.byteSize,
                    Version = (ushort)Math.Clamp(n.version, 0, ushort.MaxValue),
                    TypeFlags = (byte)(typeFlags & 0xFF),
                    MetaFlag = n.metaFlag,
                    Children = new List<ClassDatabaseTypeNode>()
                };

                while (stack.Count > 0 && stack[^1].level >= n.level)
                    stack.RemoveAt(stack.Count - 1);

                if (stack.Count == 0)
                    root = node;
                else
                    stack[^1].node.Children.Add(node);

                stack.Add((n.level, node));
            }

            if (root == null)
                throw new InvalidDataException("Type dump JSON produced no root");
            return root;
        }

        /// <summary>
        /// Tuanjie DataStreamingInfo is size+path only (no offset), unlike StreamingInfo.
        /// </summary>
        private static ClassDatabaseTypeNode MakeDataStreamingInfo(ClassDatabaseFile cldb)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("DataStreamingInfo"),
                FieldName = cldb.StringTable.AddString("m_DataStreamData"),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0,
                Children = new List<ClassDatabaseTypeNode>
                {
                    Leaf(cldb, "unsigned int", "size", 4, 0),
                    MakeString(cldb, "path"),
                }
            };
        }

        private static ClassDatabaseTypeNode MakeSharedClusterData(ClassDatabaseFile cldb, int rev)
        {
            // After SharedCluster AssetStudio always AlignStream — mark last/meta on container.
            var children = new List<ClassDatabaseTypeNode>
            {
                Leaf(cldb, "int", "m_LightmapUseUV1", 4, 0),
                Leaf(cldb, "float", "m_fileScale", 4, 0),
            };

            if (rev == 1)
            {
                children.Add(Leaf(cldb, "unsigned int", "NumInputTriangles", 4, 0));
                children.Add(Leaf(cldb, "unsigned int", "NumInputVertices", 4, 0));
                children.Add(Leaf(cldb, "UInt16", "NumInputMeshes", 2, 0));
                children.Add(Leaf(cldb, "UInt16", "NumInputTexCoords", 2, 0));
                children.Add(Leaf(cldb, "unsigned int", "ResourceFlags", 4, 0));
            }

            children.Add(MakeByteVector(cldb, "rootClusterPage", 0));

            if (rev == 1)
                children.Add(MakeUInt16Vector(cldb, "imposterAtlas"));

            children.Add(MakeArrayOf(cldb, "hierarchyNodes", MakeVGPackedHierarchyNode(cldb)));

            if (rev == 1)
                children.Add(MakeUInt32Vector(cldb, "hierarchyRootOffsets"));

            children.Add(MakeArrayOf(cldb, "pageStreamingInfos", MakeVGPageStreamingInfo(cldb)));
            children.Add(MakeUInt32Vector(cldb, "pageIndicesOfDependencies"));

            if (rev == 2 || rev == 3)
            {
                // rev3 shares the tail layout of rev2 after optional align handled by insert order
                children.Add(Leaf(cldb, "unsigned int", "inputTrianglesCount", 4, 0));
                children.Add(Leaf(cldb, "unsigned int", "inputVerticesCount", 4, 0));
                children.Add(Leaf(cldb, "UInt16", "inputMeshesCount", 2, 0));
                children.Add(Leaf(cldb, "UInt16", "inputTexCoordsCount", 2, 0));
                children.Add(Leaf(cldb, "unsigned int", "resourceFlags", 4, 0));
                children.Add(MakeUInt16Vector(cldb, "imposterAtlas"));
                children.Add(MakeUInt32Vector(cldb, "hierarchyRootOffsets"));
            }

            children.Add(MakeByteVector(cldb, "streamableClusterPage", AlignMeta));

            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("SharedClusterData"),
                FieldName = cldb.StringTable.AddString("m_SharedClusterData"),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = AlignMeta, // AlignStream after block
                Children = children
            };
        }

        private static ClassDatabaseTypeNode MakeVGPackedHierarchyNode(ClassDatabaseFile cldb)
        {
            // AssetStudio reads per-slot interleaved fields (not SoA):
            // for i in 0..7: Vector4, Vector3, uint, Vector3, uint, uint
            var children = new List<ClassDatabaseTypeNode>(48);
            for (int i = 0; i < 8; i++)
            {
                children.Add(new ClassDatabaseTypeNode
                {
                    TypeName = cldb.StringTable.AddString("Vector4f"),
                    FieldName = cldb.StringTable.AddString($"LODBounds[{i}]"),
                    ByteSize = 16,
                    Version = 1,
                    TypeFlags = 0,
                    MetaFlag = 0,
                    Children = new List<ClassDatabaseTypeNode>
                    {
                        Leaf(cldb, "float", "x", 4, 0),
                        Leaf(cldb, "float", "y", 4, 0),
                        Leaf(cldb, "float", "z", 4, 0),
                        Leaf(cldb, "float", "w", 4, 0),
                    }
                });
                children.Add(new ClassDatabaseTypeNode
                {
                    TypeName = cldb.StringTable.AddString("Vector3f"),
                    FieldName = cldb.StringTable.AddString($"AABBCenter[{i}]"),
                    ByteSize = 12,
                    Version = 1,
                    TypeFlags = 0,
                    MetaFlag = 0,
                    Children = new List<ClassDatabaseTypeNode>
                    {
                        Leaf(cldb, "float", "x", 4, 0),
                        Leaf(cldb, "float", "y", 4, 0),
                        Leaf(cldb, "float", "z", 4, 0),
                    }
                });
                children.Add(Leaf(cldb, "unsigned int", $"MinLODError_MaxParentLODError[{i}]", 4, 0));
                children.Add(new ClassDatabaseTypeNode
                {
                    TypeName = cldb.StringTable.AddString("Vector3f"),
                    FieldName = cldb.StringTable.AddString($"AABBExtent[{i}]"),
                    ByteSize = 12,
                    Version = 1,
                    TypeFlags = 0,
                    MetaFlag = 0,
                    Children = new List<ClassDatabaseTypeNode>
                    {
                        Leaf(cldb, "float", "x", 4, 0),
                        Leaf(cldb, "float", "y", 4, 0),
                        Leaf(cldb, "float", "z", 4, 0),
                    }
                });
                children.Add(Leaf(cldb, "unsigned int", $"ChildStartIndex[{i}]", 4, 0));
                children.Add(Leaf(cldb, "unsigned int", $"PageIndex_PageCount_PageBlockCount[{i}]", 4, 0));
            }

            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("VGPackedHierarchyNode"),
                FieldName = cldb.StringTable.AddString("data"),
                ByteSize = 416,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0,
                Children = children
            };
        }

        // FixedArrayField removed — VG hierarchy uses interleaved layout above.

        private static ClassDatabaseTypeNode MakeVGPageStreamingInfo(ClassDatabaseFile cldb)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("VGPageStreamingInfo"),
                FieldName = cldb.StringTable.AddString("data"),
                ByteSize = 24,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0,
                Children = new List<ClassDatabaseTypeNode>
                {
                    Leaf(cldb, "unsigned int", "offset", 4, 0),
                    Leaf(cldb, "unsigned int", "wholeSize", 4, 0),
                    Leaf(cldb, "unsigned int", "dataSize", 4, 0),
                    Leaf(cldb, "unsigned int", "dependencyOffset", 4, 0),
                    Leaf(cldb, "unsigned int", "dependencyCount", 4, 0),
                    Leaf(cldb, "unsigned int", "flags", 4, 0),
                }
            };
        }

        private static ClassDatabaseTypeNode MakeArrayOf(
            ClassDatabaseFile cldb, string fieldName, ClassDatabaseTypeNode elementTemplate)
        {
            // Clone element with field name "data"
            ClassDatabaseTypeNode data = CloneNode(elementTemplate);
            data.FieldName = cldb.StringTable.AddString("data");

            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("vector"),
                FieldName = cldb.StringTable.AddString(fieldName),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0,
                Children = new List<ClassDatabaseTypeNode>
                {
                    new ClassDatabaseTypeNode
                    {
                        TypeName = cldb.StringTable.AddString("Array"),
                        FieldName = cldb.StringTable.AddString("Array"),
                        ByteSize = -1,
                        Version = 1,
                        TypeFlags = 1, // array
                        MetaFlag = 0,
                        Children = new List<ClassDatabaseTypeNode>
                        {
                            Leaf(cldb, "int", "size", 4, 0),
                            data
                        }
                    }
                }
            };
        }

        private static ClassDatabaseTypeNode MakeByteVector(ClassDatabaseFile cldb, string fieldName, uint meta)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("vector"),
                FieldName = cldb.StringTable.AddString(fieldName),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = meta,
                Children = new List<ClassDatabaseTypeNode>
                {
                    new ClassDatabaseTypeNode
                    {
                        TypeName = cldb.StringTable.AddString("Array"),
                        FieldName = cldb.StringTable.AddString("Array"),
                        ByteSize = -1,
                        Version = 1,
                        TypeFlags = 1,
                        MetaFlag = meta != 0 ? AlignMeta : 0u,
                        Children = new List<ClassDatabaseTypeNode>
                        {
                            Leaf(cldb, "int", "size", 4, 0),
                            Leaf(cldb, "UInt8", "data", 1, 0),
                        }
                    }
                }
            };
        }

        private static ClassDatabaseTypeNode MakeUInt16Vector(ClassDatabaseFile cldb, string fieldName)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("vector"),
                FieldName = cldb.StringTable.AddString(fieldName),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0,
                Children = new List<ClassDatabaseTypeNode>
                {
                    new ClassDatabaseTypeNode
                    {
                        TypeName = cldb.StringTable.AddString("Array"),
                        FieldName = cldb.StringTable.AddString("Array"),
                        ByteSize = -1,
                        Version = 1,
                        TypeFlags = 1,
                        MetaFlag = 0,
                        Children = new List<ClassDatabaseTypeNode>
                        {
                            Leaf(cldb, "int", "size", 4, 0),
                            Leaf(cldb, "UInt16", "data", 2, 0),
                        }
                    }
                }
            };
        }

        private static ClassDatabaseTypeNode MakeUInt32Vector(ClassDatabaseFile cldb, string fieldName)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("vector"),
                FieldName = cldb.StringTable.AddString(fieldName),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0,
                Children = new List<ClassDatabaseTypeNode>
                {
                    new ClassDatabaseTypeNode
                    {
                        TypeName = cldb.StringTable.AddString("Array"),
                        FieldName = cldb.StringTable.AddString("Array"),
                        ByteSize = -1,
                        Version = 1,
                        TypeFlags = 1,
                        MetaFlag = 0,
                        Children = new List<ClassDatabaseTypeNode>
                        {
                            Leaf(cldb, "int", "size", 4, 0),
                            Leaf(cldb, "unsigned int", "data", 4, 0),
                        }
                    }
                }
            };
        }

        private static ClassDatabaseTypeNode MakeString(ClassDatabaseFile cldb, string fieldName)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString("string"),
                FieldName = cldb.StringTable.AddString(fieldName),
                ByteSize = -1,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = 0x8000,
                Children = new List<ClassDatabaseTypeNode>
                {
                    new ClassDatabaseTypeNode
                    {
                        TypeName = cldb.StringTable.AddString("Array"),
                        FieldName = cldb.StringTable.AddString("Array"),
                        ByteSize = -1,
                        Version = 1,
                        TypeFlags = 1,
                        MetaFlag = AlignMeta | 0x1,
                        Children = new List<ClassDatabaseTypeNode>
                        {
                            Leaf(cldb, "int", "size", 4, 0x1),
                            Leaf(cldb, "char", "data", 1, 0x1),
                        }
                    }
                }
            };
        }

        private static ClassDatabaseTypeNode Leaf(
            ClassDatabaseFile cldb, string typeName, string fieldName, int byteSize, uint meta)
        {
            return new ClassDatabaseTypeNode
            {
                TypeName = cldb.StringTable.AddString(typeName),
                FieldName = cldb.StringTable.AddString(fieldName),
                ByteSize = byteSize,
                Version = 1,
                TypeFlags = 0,
                MetaFlag = meta,
                Children = new List<ClassDatabaseTypeNode>()
            };
        }

        private static int FindChildIndex(ClassDatabaseFile cldb, ClassDatabaseTypeNode root, string fieldName)
        {
            for (int i = 0; i < root.Children.Count; i++)
            {
                if (cldb.GetString(root.Children[i].FieldName) == fieldName)
                    return i;
            }
            return -1;
        }

        private static ClassDatabaseTypeNode CloneNode(ClassDatabaseTypeNode src)
        {
            var children = new List<ClassDatabaseTypeNode>(src.Children.Count);
            foreach (var c in src.Children)
                children.Add(CloneNode(c));

            return new ClassDatabaseTypeNode
            {
                TypeName = src.TypeName,
                FieldName = src.FieldName,
                ByteSize = src.ByteSize,
                Version = src.Version,
                TypeFlags = src.TypeFlags,
                MetaFlag = src.MetaFlag,
                Children = children
            };
        }
    }
}
