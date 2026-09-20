using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using System;
using System.IO;
using System.Linq;
using UABEAvalonia;

namespace TexturePlugin
{
    public static class TextureHelper
    {
        public static AssetTypeValueField GetByteArrayTexture(AssetWorkspace workspace, AssetContainer tex)
        {
            AssetTypeTemplateField textureTemp = workspace.GetTemplateField(tex);
            AssetTypeTemplateField image_data = textureTemp.Children.FirstOrDefault(f => f.Name == "image data");
            if (image_data == null)
                return null;
            image_data.ValueType = AssetValueType.ByteArray;

            AssetTypeTemplateField m_PlatformBlob = textureTemp.Children.FirstOrDefault(f => f.Name == "m_PlatformBlob");
            if (m_PlatformBlob != null)
            {
                AssetTypeTemplateField m_PlatformBlob_Array = m_PlatformBlob.Children[0];
                m_PlatformBlob_Array.ValueType = AssetValueType.ByteArray;
            }

            AssetTypeValueField baseField = textureTemp.MakeValue(tex.FileReader, tex.FilePosition);
            return baseField;
        }

        public static bool GetResSTexture(TextureFile texFile, AssetsFileInstance fileInst)
        {
            TextureFile.StreamingInfo streamInfo = texFile.m_StreamData;
            if (streamInfo.path != null && streamInfo.path != "" && fileInst.parentBundle != null)
            {
                //some versions apparently don't use archive:/
                string searchPath = streamInfo.path;
                if (searchPath.StartsWith("archive:/"))
                    searchPath = searchPath.Substring(9);

                searchPath = Path.GetFileName(searchPath);

                AssetBundleFile bundle = fileInst.parentBundle.file;

                AssetsFileReader reader = bundle.DataReader;
                AssetBundleDirectoryInfo[] dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirInf.Length; i++)
                {
                    AssetBundleDirectoryInfo info = dirInf[i];
                    if (info.Name == searchPath)
                    {
                        reader.Position = info.Offset + (long)streamInfo.offset;
                        texFile.pictureData = reader.ReadBytes((int)streamInfo.size);
                        texFile.m_StreamData.offset = 0;
                        texFile.m_StreamData.size = 0;
                        texFile.m_StreamData.path = "";
                        return true;
                    }
                }
                return false;
            }
            else
            {
                return true;
            }
        }

        public static byte[] GetRawTextureBytes(TextureFile texFile, AssetsFileInstance inst, AssetWorkspace workspace = null)
        {
            if (texFile.m_StreamData.size != 0 && texFile.m_StreamData.path != string.Empty
                && workspace != null
                && workspace.TryReadStreamingData(inst, texFile.m_StreamData.path, texFile.m_StreamData.offset, texFile.m_StreamData.size, out byte[] pendingData)
                && pendingData != null)
            {
                texFile.pictureData = pendingData;
                return pendingData;
            }

            string rootPath = Path.GetDirectoryName(inst.path);
            if (texFile.m_StreamData.size != 0 && texFile.m_StreamData.path != string.Empty)
            {
                string fixedStreamPath = texFile.m_StreamData.path;
                if (inst.parentBundle == null && fixedStreamPath.StartsWith("archive:/"))
                {
                    fixedStreamPath = Path.GetFileName(fixedStreamPath);
                }
                if (!Path.IsPathRooted(fixedStreamPath) && rootPath != null)
                {
                    fixedStreamPath = Path.Combine(rootPath, fixedStreamPath);
                }
                if (File.Exists(fixedStreamPath))
                {
                    Stream stream = File.OpenRead(fixedStreamPath);
                    stream.Position = (long)texFile.m_StreamData.offset;
                    texFile.pictureData = new byte[texFile.m_StreamData.size];
                    stream.Read(texFile.pictureData, 0, (int)texFile.m_StreamData.size);
                }
                else
                {
                    return null;
                }
            }
            return texFile.pictureData;
        }

        public static void SetImageData(AssetTypeValueField baseField, byte[] data)
        {
            AssetTypeValueField image_data = baseField["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = data ?? Array.Empty<byte>();
        }

        public static bool TryGetStreamData(AssetTypeValueField baseField, out string path, out ulong offset, out uint size)
        {
            path = "";
            offset = 0;
            size = 0;
            AssetTypeValueField m_StreamData = baseField["m_StreamData"];
            if (m_StreamData.IsDummy)
                return false;

            if (!m_StreamData["path"].IsDummy)
                path = m_StreamData["path"].AsString ?? "";
            if (!m_StreamData["offset"].IsDummy)
                offset = m_StreamData["offset"].AsULong;
            if (!m_StreamData["size"].IsDummy)
                size = m_StreamData["size"].AsUInt;
            return !string.IsNullOrEmpty(path) && size != 0;
        }

        public static void SetStreamData(AssetTypeValueField baseField, ulong offset, uint size, string path)
        {
            AssetTypeValueField m_StreamData = baseField["m_StreamData"];
            if (m_StreamData.IsDummy)
                return;
            m_StreamData["offset"].AsULong = offset;
            m_StreamData["size"].AsUInt = size;
            m_StreamData["path"].AsString = path ?? "";
        }

        /// <summary>
        /// Write encoded pixels back to a sibling .resS when this is a standalone
        /// serialized file that already streamed. Otherwise inline into image data
        /// (bundle / missing resS / originally inlined textures).
        /// </summary>
        public static void WriteTextureData(AssetWorkspace workspace, AssetsFileInstance fileInst, AssetTypeValueField baseField, byte[] encImageBytes)
        {
            TryGetStreamData(baseField, out string streamPath, out ulong streamOffset, out uint streamSize);

            if (workspace != null
                && workspace.TryQueueStreamingWrite(fileInst, streamPath, streamOffset, streamSize, encImageBytes, out ulong newOffset, out uint newSize))
            {
                SetStreamData(baseField, newOffset, newSize, streamPath);
                SetImageData(baseField, Array.Empty<byte>());
                return;
            }

            SetStreamData(baseField, 0, 0, "");
            SetImageData(baseField, encImageBytes);
        }

        public static byte[] GetPlatformBlob(AssetTypeValueField texBaseField)
        {
            AssetTypeValueField m_PlatformBlob = texBaseField["m_PlatformBlob"];
            byte[] platformBlob = null;
            if (!m_PlatformBlob.IsDummy)
            {
                platformBlob = m_PlatformBlob["Array"].AsByteArray;
            }
            return platformBlob;
        }

        public static bool IsPo2(int n)
        {
            return n > 0 && ((n & (n - 1)) == 0);
        }

        // assuming width and height are po2
        public static int GetMaxMipCount(int width, int height)
        {
            int widthMipCount = (int)Math.Log2(width) + 1;
            int heightMipCount = (int)Math.Log2(height) + 1;
            // if the texture is 512x1024 for example, select the height (1024)
            // I guess the width would stay 1 while the height resizes down
            return Math.Max(widthMipCount, heightMipCount);
        }
    }
}
