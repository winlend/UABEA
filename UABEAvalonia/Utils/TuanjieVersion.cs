using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace UABEAvalonia
{
    /// <summary>
    /// Helpers for Unity China Tuanjie (团结) engine version strings (suffix "t").
    /// Field-layout rules: docs/plans/2026-07-16-tuanjie-diff-map.md.
    ///
    /// UABEA currently ships an AssetsTools.NET build whose <see cref="UnityVersion"/>
    /// constructor throws on "t" (e.g. 2022.3.48t5). Prefer string-based helpers that
    /// do not construct UnityVersion for Tuanjie detection/mapping.
    /// </summary>
    public static class TuanjieVersion
    {
        public const string WebDataSignature = "TuanjieWebData1.0";

        // 2022.3.48t5, 2022.3.2t11, etc.
        private static readonly Regex TuanjieVersionRegex = new Regex(
            @"^(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)t(?<build>\d*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True when the version string is a Tuanjie build (type letter t after the patch number).
        /// Does not require a working UnityVersion parser for "t".
        /// </summary>
        public static bool IsTuanjie(string? versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString) || versionString == "0.0.0")
                return false;

            return TuanjieVersionRegex.IsMatch(versionString.Trim());
        }

        /// <summary>
        /// Map a Tuanjie engine version to a classdata.tpk lookup key that the shipped
        /// AssetsTools package can parse. Example: 2022.3.48t5 -> 2022.3.48f1.
        /// Non-Tuanjie strings are returned unchanged.
        ///
        /// This is a best-effort fallback for TypeTree-stripped files. Tuanjie-specific
        /// extra fields (Texture web streaming, Mesh VG, etc.) may still fail until a
        /// real Tuanjie classdata dump is available.
        /// </summary>
        public static string MapForClassDatabase(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                return versionString;

            Match m = TuanjieVersionRegex.Match(versionString.Trim());
            if (!m.Success)
                return versionString;

            return $"{m.Groups["maj"].Value}.{m.Groups["min"].Value}.{m.Groups["pat"].Value}f1";
        }

        /// <summary>
        /// Load classdata for <paramref name="versionString"/>, automatically mapping
        /// Tuanjie "t" versions to a parseable "f1" key when needed, then applying
        /// Tuanjie field patches / dump trees for stripped assets.
        ///
        /// Preference order for Tuanjie:
        /// 1. classdata_tuanjie.tpk (if present next to the app)
        /// 2. classdata_tuanjie_*.cldb (if present)
        /// 3. stock ClassPackage (usually classdata.tpk already loaded) + runtime dump/patch
        /// </summary>
        public static ClassDatabaseFile? LoadClassDatabase(AssetsManager am, string versionString)
        {
            string lookup = MapForClassDatabase(versionString);
            ClassDatabaseFile? cldb = null;

            // For Tuanjie, prefer loading the dedicated package into THIS manager so
            // GetBaseField (which reads am.ClassDatabase) sees the patched trees.
            if (IsTuanjie(versionString))
            {
                if (TryUseTuanjiePackage(am, lookup, out cldb) && cldb != null)
                {
                    TuanjieClassDatabasePatcher.Apply(cldb, versionString);
                    return cldb;
                }
                if (TryUseTuanjieCldb(am, out cldb) && cldb != null)
                {
                    TuanjieClassDatabasePatcher.Apply(cldb, versionString);
                    return cldb;
                }
            }

            try
            {
                cldb = am.LoadClassDatabaseFromPackage(lookup);
            }
            catch
            {
                if (!ReferenceEquals(lookup, versionString) && lookup != versionString)
                {
                    try { cldb = am.LoadClassDatabaseFromPackage(versionString); }
                    catch { throw; }
                }
                else
                {
                    throw;
                }
            }

            if (cldb != null && IsTuanjie(versionString))
                TuanjieClassDatabasePatcher.Apply(cldb, versionString);

            return cldb;
        }

        private static bool TryUseTuanjiePackage(AssetsManager am, string lookupVersion, out ClassDatabaseFile? cldb)
        {
            cldb = null;
            foreach (string path in CandidatePaths("classdata_tuanjie.tpk"))
            {
                if (!File.Exists(path))
                    continue;
                try
                {
                    am.LoadClassPackage(path);
                    cldb = am.LoadClassDatabaseFromPackage(lookupVersion);
                    if (cldb != null)
                        return true;
                }
                catch
                {
                }
            }
            return false;
        }

        private static bool TryUseTuanjieCldb(AssetsManager am, out ClassDatabaseFile? cldb)
        {
            cldb = null;
            string[] names =
            {
                "classdata_tuanjie_2022.3.48t5.cldb",
                "classdata_tuanjie.cldb",
            };
            foreach (string name in names)
            {
                foreach (string path in CandidatePaths(name))
                {
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        cldb = am.LoadClassDatabase(path);
                        if (cldb != null)
                            return true;
                    }
                    catch
                    {
                    }
                }
            }
            return false;
        }

        private static IEnumerable<string> CandidatePaths(string fileName)
        {
            string baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, fileName);
            yield return Path.Combine(baseDir, "ReleaseFiles", fileName);
            yield return Path.Combine(baseDir, "..", "ReleaseFiles", fileName);
            // From bin/Debug|Release/netX.Y/ back to repo ReleaseFiles/
            yield return Path.Combine(baseDir, "..", "..", "..", "..", "ReleaseFiles", fileName);
        }

        /// <summary>
        /// Compare Tuanjie product thresholds as (unityMajor, unityMinor, unityPatch, tBuild).
        /// Example: 1.1.3 ≈ 2022.3.2t11.
        /// </summary>
        public static bool IsAtLeast(string versionString, int major, int minor, int patch, int tBuild)
        {
            Match m = TuanjieVersionRegex.Match(versionString?.Trim() ?? string.Empty);
            if (!m.Success)
                return false;

            int vMaj = int.Parse(m.Groups["maj"].Value);
            int vMin = int.Parse(m.Groups["min"].Value);
            int vPat = int.Parse(m.Groups["pat"].Value);
            int vBuild = m.Groups["build"].Success && m.Groups["build"].Length > 0
                ? int.Parse(m.Groups["build"].Value)
                : 0;

            if (vMaj != major) return vMaj > major;
            if (vMin != minor) return vMin > minor;
            if (vPat != patch) return vPat > patch;
            return vBuild >= tBuild;
        }
    }
}
