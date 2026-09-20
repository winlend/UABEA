using System;
using System.IO;

namespace UABEAvalonia
{
    /// <summary>
    /// In-memory copy of a sidecar resource file (.resS / .resource).
    /// Texture imports patch this buffer; <see cref="AssetWorkspace"/> flushes it on Save.
    /// </summary>
    public class ResourceFileBuffer
    {
        public string OriginalFullPath { get; }
        public string OwnerAssetsPath { get; }

        private byte[] _data;

        public int Length => _data.Length;

        public ResourceFileBuffer(string originalFullPath, string ownerAssetsPath, byte[] data)
        {
            OriginalFullPath = originalFullPath;
            OwnerAssetsPath = ownerAssetsPath;
            _data = data ?? Array.Empty<byte>();
        }

        public static ResourceFileBuffer Load(string originalFullPath, string ownerAssetsPath)
        {
            return new ResourceFileBuffer(originalFullPath, ownerAssetsPath, File.ReadAllBytes(originalFullPath));
        }

        public static string? ResolveStandalonePath(string assetsPath, string streamPath)
        {
            if (string.IsNullOrEmpty(assetsPath) || string.IsNullOrEmpty(streamPath))
                return null;

            if (streamPath.StartsWith("archive:/", StringComparison.OrdinalIgnoreCase))
                streamPath = Path.GetFileName(streamPath);

            if (Path.IsPathRooted(streamPath))
                return Path.GetFullPath(streamPath);

            string? root = Path.GetDirectoryName(assetsPath);
            if (string.IsNullOrEmpty(root))
                return null;

            return Path.GetFullPath(Path.Combine(root, streamPath));
        }

        public static string NormalizeKey(string fullPath)
        {
            return Path.GetFullPath(fullPath).ToLowerInvariant();
        }

        public static int Align16(int length)
        {
            return (length + 15) & ~15;
        }

        /// <summary>
        /// Overwrite at <paramref name="originalOffset"/> when the new payload fits the
        /// original slot; otherwise append at a 16-byte-aligned end. Returns the offset
        /// that should be stored in StreamingInfo.
        /// </summary>
        public ulong WriteSlot(ulong originalOffset, uint originalSize, byte[] newData)
        {
            if (newData == null || newData.Length == 0)
                throw new ArgumentException("newData must be non-empty", nameof(newData));

            bool fitsInPlace = (ulong)newData.Length <= originalSize
                && originalOffset <= (ulong)_data.Length
                && originalOffset + (ulong)newData.Length <= (ulong)_data.Length;

            if (fitsInPlace)
            {
                WriteAt((long)originalOffset, newData);
                return originalOffset;
            }

            long appendAt = Align16(_data.Length);
            WriteAt(appendAt, newData);
            return (ulong)appendAt;
        }

        public byte[]? Read(ulong offset, uint size)
        {
            if (size == 0 || offset + size > (ulong)_data.Length)
                return null;

            byte[] dst = new byte[size];
            Buffer.BlockCopy(_data, (int)offset, dst, 0, (int)size);
            return dst;
        }

        public byte[] GetBytes()
        {
            return _data;
        }

        public void WriteToFile(string destPath)
        {
            string? dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = destPath + ".tmp";
            File.WriteAllBytes(tmp, _data);
            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tmp, destPath);
        }

        private void WriteAt(long offset, byte[] src)
        {
            long needed = offset + src.Length;
            if (needed > _data.Length)
            {
                byte[] grown = new byte[needed];
                Buffer.BlockCopy(_data, 0, grown, 0, _data.Length);
                _data = grown;
            }
            Buffer.BlockCopy(src, 0, _data, (int)offset, src.Length);
        }
    }
}
