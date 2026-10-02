using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>Editor helpers around the FMOD for Unity settings and the FMOD Studio project (FMOD 2.03.15 public API only).</summary>
    internal static class FmodProject
    {
        /// <summary>Resources name of FMOD's settings asset (FMODUnity.Settings.SettingsAssetName, internal in 2.03.15).</summary>
        internal const string SettingsResourceName = "FMODStudioSettings";

        internal static string UnityProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>FMOD settings asset if it exists. Unlike Settings.Instance this never creates the asset.</summary>
        internal static Settings LoadSettings() => Resources.Load<Settings>(SettingsResourceName);

        /// <summary>Absolute path of the configured .fspro, or null.</summary>
        internal static string StudioProjectPath
        {
            get
            {
                Settings settings = LoadSettings();
                if (settings == null || !settings.HasSourceProject || string.IsNullOrEmpty(settings.SourceProjectPath)) return null;
                return Path.GetFullPath(Path.Combine(UnityProjectRoot, settings.SourceProjectPath));
            }
        }

        /// <summary>Absolute folder of the configured Studio project, or null.</summary>
        internal static string StudioProjectFolder
        {
            get
            {
                string fspro = StudioProjectPath;
                return fspro == null ? null : Path.GetDirectoryName(fspro);
            }
        }

        /// <summary>Absolute folder FMOD reads built banks from (Studio project Build/ or the configured build folder).</summary>
        internal static string BuildFolder
        {
            get
            {
                Settings settings = LoadSettings();
                if (settings == null) return null;
                if (settings.HasSourceProject) return StudioProjectFolder == null ? null : Path.Combine(StudioProjectFolder, EditorUtils.BuildFolder);
                return string.IsNullOrEmpty(settings.SourceBankPath) ? null : Path.GetFullPath(Path.Combine(UnityProjectRoot, settings.SourceBankPath));
            }
        }

        /// <summary>Platform sub folder of <see cref="BuildFolder"/> used in the editor (e.g. "Desktop").</summary>
        internal static string EditorPlatformBuildFolder
        {
            get
            {
                Settings settings = LoadSettings();
                string build = BuildFolder;
                if (settings == null || build == null) return null;
                if (!settings.HasSourceProject && !settings.HasPlatforms) return build;
                Platform platform = FMODUnity.EditorSettings.Instance.CurrentEditorPlatform;
                return platform == null ? null : Path.Combine(build, platform.BuildDirectory);
            }
        }

        internal static string ToProjectRelative(string absolutePath)
        {
            string root = UnityProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            if (full.StartsWith(root, StringComparison.Ordinal)) return full.Substring(root.Length);
            Uri rel = new Uri(root).MakeRelativeUri(new Uri(full));
            return Uri.UnescapeDataString(rel.ToString());
        }

        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Library", "Temp", "Logs", "obj", "Packages", "ProjectSettings", "UserSettings", "Builds", "Build", ".git", ".vs", ".idea",
        };

        /// <summary>.fspro files next to Assets/ (searched two folders deep, never inside Library/Temp/etc.).</summary>
        internal static List<string> FindStudioProjects()
        {
            var results = new List<string>();
            void Scan(string folder, int depth)
            {
                try
                {
                    results.AddRange(Directory.GetFiles(folder, "*.fspro"));
                    if (depth == 0) return;
                    foreach (string sub in Directory.GetDirectories(folder))
                    {
                        string name = Path.GetFileName(sub);
                        if (SkippedFolders.Contains(name) || name.StartsWith(".", StringComparison.Ordinal) || name == "Assets") continue;
                        Scan(sub, depth - 1);
                    }
                }
                catch (Exception)
                {
                    // unreadable folder: ignore
                }
            }
            Scan(UnityProjectRoot, 2);
            results.Sort(StringComparer.Ordinal);
            return results;
        }

        /// <summary>Points the FMOD settings at a Studio project, mirroring FMOD's own "Browse" logic. Supports Undo.</summary>
        internal static void SetStudioProject(Settings settings, string fsproAbsolutePath)
        {
            string relative = ToProjectRelative(fsproAbsolutePath);
            Undo.RecordObject(settings, "Set FMOD Studio project");
            settings.HasSourceProject = true;
            settings.HasPlatforms = true;
            settings.SourceProjectPath = relative;
            settings.SourceBankPath = RuntimeUtils.GetCommonPlatformPath(Path.Combine(Path.GetDirectoryName(relative) ?? string.Empty, EditorUtils.BuildFolder));
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            EventManager.RefreshBanks();
        }
    }

    /// <summary>Read-only parse of an FMOD Studio project's Metadata XML (best effort; Studio 2.03 layout).</summary>
    internal sealed class StudioProjectInfo
    {
        public string FsproPath;
        public string SerializationModel;
        public readonly HashSet<string> BusPaths = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> SnapshotPaths = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> SnapshotNames = new HashSet<string>(StringComparer.Ordinal);
        public DateTime NewestMetadataUtc;
        public string Error;

        /// <summary>True when the project was saved by a 2.03-line FMOD Studio ("Studio.02.03.xx").</summary>
        public bool IsStudio203 => SerializationModel != null && SerializationModel.StartsWith("Studio.02.03.", StringComparison.Ordinal);

        private static readonly Dictionary<string, (int count, DateTime newest, StudioProjectInfo info)> cache =
            new Dictionary<string, (int, DateTime, StudioProjectInfo)>();

        /// <summary>Scans the project, reusing the previous result while no Metadata file was added, removed or modified.</summary>
        public static StudioProjectInfo Scan(string fsproPath)
        {
            string metadata = Path.Combine(Path.GetDirectoryName(fsproPath) ?? string.Empty, "Metadata");
            int count = 0;
            DateTime newest = File.Exists(fsproPath) ? File.GetLastWriteTimeUtc(fsproPath) : DateTime.MinValue;
            if (Directory.Exists(metadata))
            {
                foreach (string file in Directory.EnumerateFiles(metadata, "*.xml", SearchOption.AllDirectories))
                {
                    count++;
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    if (written > newest) newest = written;
                }
            }
            if (cache.TryGetValue(fsproPath, out var hit) && hit.count == count && hit.newest == newest) return hit.info;
            StudioProjectInfo info = Parse(fsproPath);
            cache[fsproPath] = (count, newest, info);
            return info;
        }

        private static StudioProjectInfo Parse(string fsproPath)
        {
            var info = new StudioProjectInfo { FsproPath = fsproPath };
            try
            {
                info.SerializationModel = (string)XDocument.Load(fsproPath).Root?.Attribute("serializationModel");
                string metadata = Path.Combine(Path.GetDirectoryName(fsproPath) ?? string.Empty, "Metadata");
                if (!Directory.Exists(metadata))
                {
                    info.Error = "Metadata folder not found next to the .fspro.";
                    return info;
                }

                var names = new Dictionary<string, string>();
                var parents = new Dictionary<string, string>();
                var classes = new Dictionary<string, string>();
                foreach (string file in Directory.GetFiles(metadata, "*.xml", SearchOption.AllDirectories))
                {
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    if (written > info.NewestMetadataUtc) info.NewestMetadataUtc = written;
                    XElement root = XDocument.Load(file).Root;
                    if (root == null) continue;
                    foreach (XElement obj in root.Elements("object"))
                    {
                        string id = (string)obj.Attribute("id");
                        string cls = (string)obj.Attribute("class");
                        if (id == null || cls == null) continue;
                        classes[id] = cls;
                        string name = obj.Elements("property").FirstOrDefault(p => (string)p.Attribute("name") == "name")?.Element("value")?.Value;
                        if (name != null) names[id] = name;
                        foreach (XElement rel in obj.Elements("relationship"))
                        {
                            string relName = (string)rel.Attribute("name");
                            if (relName == "output" && (cls == "MixerGroup" || cls == "MixerReturn"))
                            {
                                string dest = rel.Element("destination")?.Value;
                                if (dest != null) parents[id] = dest;
                            }
                            else if (relName == "items" && (cls == "SnapshotList" || cls == "SnapshotGroup"))
                            {
                                foreach (XElement d in rel.Elements("destination")) parents[d.Value] = id;
                            }
                        }
                    }
                }

                foreach (KeyValuePair<string, string> kv in classes)
                {
                    if (kv.Value == "MixerGroup" || kv.Value == "MixerReturn")
                    {
                        string path = BuildPath(kv.Key, names, parents, classes, "MixerMaster");
                        if (path != null) info.BusPaths.Add("bus:/" + path);
                    }
                    else if (kv.Value == "Snapshot" && names.TryGetValue(kv.Key, out string snapName))
                    {
                        info.SnapshotNames.Add(snapName);
                        string path = BuildPath(kv.Key, names, parents, classes, "SnapshotList");
                        info.SnapshotPaths.Add("snapshot:/" + (path ?? snapName));
                    }
                }
            }
            catch (Exception e)
            {
                info.Error = e.Message;
            }
            return info;
        }

        private static string BuildPath(string id, Dictionary<string, string> names, Dictionary<string, string> parents,
            Dictionary<string, string> classes, string rootClass)
        {
            var parts = new List<string>();
            string current = id;
            for (int guard = 0; guard < 64; guard++)
            {
                if (!names.TryGetValue(current, out string name)) return null;
                parts.Add(name);
                if (!parents.TryGetValue(current, out string parent)) return null;
                if (classes.TryGetValue(parent, out string cls) && cls == rootClass)
                {
                    parts.Reverse();
                    return string.Join("/", parts);
                }
                current = parent;
            }
            return null;
        }
    }
}
