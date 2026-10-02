using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>Editor lookups for configs, libraries, channels and cue ids.</summary>
    internal static class EditorCues
    {
        internal const string AudioFolder = "Assets/Audio";
        internal const string ConfigAssetPath = "Assets/Resources/" + SoundSystemConfig.ResourcesName + ".asset";

        // ponytail: asset lookups are cached for one second because drawers call them every repaint.
        private static double cacheTime = -10;
        private static SoundSystemConfig cachedConfig;
        private static List<SoundCueLibrary> cachedLibraries;

        private static void RefreshCache()
        {
            if (EditorApplication.timeSinceStartup - cacheTime < 1.0 && cachedLibraries != null && cachedLibraries.All(l => l != null)) return;
            cacheTime = EditorApplication.timeSinceStartup;
            cachedConfig = LookupResourcesConfig();
            cachedLibraries = AssetDatabase.FindAssets("t:" + nameof(SoundCueLibrary))
                .Select(g => AssetDatabase.LoadAssetAtPath<SoundCueLibrary>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null)
                .OrderBy(l => AssetDatabase.GetAssetPath(l), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Drops cached lookups (call after creating or deleting assets).</summary>
        internal static void Invalidate() => cacheTime = -10;

        /// <summary>The config the runtime auto-loads (any asset named kinatraaFmodAudioConfig inside a Resources folder), or null.</summary>
        internal static SoundSystemConfig FindResourcesConfig()
        {
            RefreshCache();
            return cachedConfig;
        }

        private static SoundSystemConfig LookupResourcesConfig()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SoundSystemConfig)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == SoundSystemConfig.ResourcesName && path.Replace('\\', '/').Contains("/Resources/"))
                {
                    return AssetDatabase.LoadAssetAtPath<SoundSystemConfig>(path);
                }
            }
            return null;
        }

        internal static List<SoundCueLibrary> FindAllLibraries()
        {
            RefreshCache();
            return cachedLibraries.Where(l => l != null).ToList();
        }

        /// <summary>Channel names from the Resources config, or the built-in defaults.</summary>
        internal static List<string> ChannelNames()
        {
            SoundSystemConfig config = FindResourcesConfig();
            List<SoundChannelDefinition> defs = config != null && config.Channels.Count > 0 ? config.Channels : SoundSystemConfig.CreateDefaultChannels();
            return defs.Where(d => d != null && !string.IsNullOrEmpty(d.Name)).Select(d => d.Name).Distinct().ToList();
        }

        /// <summary>All cue ids in all libraries of the project, sorted and unique.</summary>
        internal static List<string> AllCueIds()
        {
            return FindAllLibraries().SelectMany(l => l.Cues).Where(c => c != null && !string.IsNullOrEmpty(c.Id))
                .Select(c => c.Id).Distinct().OrderBy(i => i, StringComparer.Ordinal).ToList();
        }

        /// <summary>Ids that appear more than once across all libraries.</summary>
        internal static HashSet<string> DuplicateIds(IEnumerable<SoundCueLibrary> libraries)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dupes = new HashSet<string>(StringComparer.Ordinal);
            foreach (SoundCue cue in libraries.Where(l => l != null).SelectMany(l => l.Cues))
            {
                if (cue == null || string.IsNullOrEmpty(cue.Id)) continue;
                if (!seen.Add(cue.Id)) dupes.Add(cue.Id);
            }
            return dupes;
        }

        /// <summary>"BattleTheme" -> "battle_theme", "UI Click-2" -> "ui_click_2".</summary>
        internal static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c))
                {
                    bool boundary = char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) ||
                                                                  (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
                    if (boundary && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>"ui_click" -> "UiClick"; prefixes "_" when it would start with a digit.</summary>
        internal static string ToPascalCase(string id)
        {
            var sb = new StringBuilder();
            bool upper = true;
            foreach (char c in id)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    upper = true;
                    continue;
                }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            if (sb.Length == 0) return "_";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        /// <summary>Guesses a channel from an event path ("event:/Music/..." -> "Music").</summary>
        internal static string GuessChannel(string eventPath, List<string> channels)
        {
            string[] parts = (eventPath ?? string.Empty).Replace("event:/", string.Empty).Split('/');
            if (parts.Length > 1)
            {
                string folder = parts[0];
                string match = channels.FirstOrDefault(c => string.Equals(c, folder, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                if (folder.Equals("Dialogue", StringComparison.OrdinalIgnoreCase) || folder.Equals("VO", StringComparison.OrdinalIgnoreCase)) return "Voice";
                if (folder.Equals("Ambience", StringComparison.OrdinalIgnoreCase) || folder.Equals("Ambiance", StringComparison.OrdinalIgnoreCase)) return "Ambient";
            }
            return "SFX";
        }
    }
}
