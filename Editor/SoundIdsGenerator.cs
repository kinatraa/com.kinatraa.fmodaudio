using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace kinatraa.FmodAudio.Editor
{
    /// <summary>Editor-only project settings (ProjectSettings/kinatraaFmodAudioSettings.asset, meant to be committed).</summary>
    [FilePath("ProjectSettings/kinatraaFmodAudioSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FmodAudioProjectSettings : ScriptableSingleton<FmodAudioProjectSettings>
    {
        /// <summary>Output path of the generated constants file.</summary>
        public string SoundIdsPath = "Assets/Scripts/Generated/SoundIds.cs";
        /// <summary>Namespace of the generated class (empty = global namespace).</summary>
        public string SoundIdsNamespace = string.Empty;
        /// <summary>Name of the generated class.</summary>
        public string SoundIdsClassName = "SoundIds";
        /// <summary>Regenerate the file automatically whenever a cue library asset is saved.</summary>
        public bool AutoGenerateSoundIds;

        internal void SaveNow() => Save(true);
    }

    /// <summary>Generates a constants class with one const string per cue id. Output is deterministic (sorted, no timestamps).</summary>
    public static class SoundIdsGenerator
    {
        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal",
            "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        /// <summary>True for a valid C# identifier (ASCII) that is not a keyword.</summary>
        public static bool IsValidIdentifier(string name) => !string.IsNullOrEmpty(name) && Identifier.IsMatch(name) && !Keywords.Contains(name);

        /// <summary>True for an empty namespace or dot-separated valid identifiers.</summary>
        public static bool IsValidNamespace(string ns) => string.IsNullOrEmpty(ns) || ns.Split('.').All(IsValidIdentifier);

        /// <summary>Generates the file at the configured path. Returns the path, or null when nothing was written.</summary>
        [MenuItem(EditorUi.MenuRoot + "Generate SoundIds.cs", priority = 30)]
        public static string Generate() => Generate(true);

        internal static string Generate(bool logUpToDate)
        {
            FmodAudioProjectSettings settings = FmodAudioProjectSettings.instance;
            string error = Validate(settings.SoundIdsPath, settings.SoundIdsNamespace, settings.SoundIdsClassName);
            if (error != null)
            {
                Debug.LogError($"[kinatraa.fmodaudio] Cannot generate SoundIds: {error} (Project Settings > kinatraa FMOD Audio).");
                return null;
            }
            string path = settings.SoundIdsPath;
            string code = BuildSource(EditorCues.AllCueIds(), settings.SoundIdsNamespace, settings.SoundIdsClassName);
            string full = Path.GetFullPath(path);
            if (File.Exists(full) && File.ReadAllText(full) == code)
            {
                if (logUpToDate) Debug.Log($"[kinatraa.fmodaudio] {path} is up to date.");
                return path;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
            File.WriteAllText(full, code, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            Debug.Log($"[kinatraa.fmodaudio] Generated {path}.");
            return path;
        }

        /// <summary>Returns an error message for invalid generator settings, or null when they are valid.</summary>
        public static string Validate(string path, string ns, string className)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.Ordinal)) return "the output path must end with .cs";
            if (!path.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)) return "the output path must be inside Assets/";
            if (!IsValidNamespace(ns)) return $"'{ns}' is not a valid namespace";
            if (!string.IsNullOrEmpty(className) && !IsValidIdentifier(className)) return $"'{className}' is not a valid class name";
            return null;
        }

        /// <summary>Builds the source code (pure function; used by <see cref="Generate()"/>).</summary>
        public static string BuildSource(IEnumerable<string> ids, string ns, string className)
        {
            if (string.IsNullOrEmpty(className)) className = "SoundIds";
            var sb = new StringBuilder();
            sb.Append("// <auto-generated>\n// Generated by kinatraa FMOD Audio (Tools > kinatraa > FMOD Audio > Generate SoundIds.cs). Do not edit.\n// </auto-generated>\n");
            string indent = string.Empty;
            if (!string.IsNullOrEmpty(ns))
            {
                sb.Append("namespace ").Append(ns).Append("\n{\n");
                indent = "    ";
            }
            sb.Append(indent).Append("/// <summary>Cue ids for kinatraa.FmodAudio.Sound.Play.</summary>\n");
            sb.Append(indent).Append("public static class ").Append(className).Append("\n").Append(indent).Append("{\n");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids.Where(i => !string.IsNullOrEmpty(i)).Distinct().OrderBy(i => i, StringComparer.Ordinal))
            {
                string name = EditorCues.ToPascalCase(id);
                if (name == className) name += "_"; // a member cannot share the class name
                string unique = name;
                for (int n = 2; !used.Add(unique); n++) unique = name + "_" + n;
                sb.Append(indent).Append("    public const string ").Append(unique).Append(" = \"").Append(id.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\";\n");
            }
            sb.Append(indent).Append("}\n");
            if (!string.IsNullOrEmpty(ns)) sb.Append("}\n");
            return sb.ToString();
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/kinatraa FMOD Audio", SettingsScope.Project)
            {
                guiHandler = _ =>
                {
                    FmodAudioProjectSettings s = FmodAudioProjectSettings.instance;
                    EditorGUILayout.LabelField("SoundIds.cs constants", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    s.SoundIdsPath = EditorGUILayout.TextField(new GUIContent("Output path", "Project-relative path of the generated file (inside Assets/)."), s.SoundIdsPath);
                    s.SoundIdsNamespace = EditorGUILayout.TextField(new GUIContent("Namespace", "Empty = global namespace."), s.SoundIdsNamespace);
                    s.SoundIdsClassName = EditorGUILayout.TextField(new GUIContent("Class name"), s.SoundIdsClassName);
                    s.AutoGenerateSoundIds = EditorGUILayout.Toggle(new GUIContent("Auto-generate", "Regenerate whenever a cue library is saved."), s.AutoGenerateSoundIds);
                    if (EditorGUI.EndChangeCheck()) s.SaveNow();

                    string error = Validate(s.SoundIdsPath, s.SoundIdsNamespace, s.SoundIdsClassName);
                    if (error != null) EditorGUILayout.HelpBox("Invalid settings: " + error + ".", MessageType.Error);
                    else EditorGUILayout.LabelField($"{EditorCues.AllCueIds().Count} cue id(s) will be generated.", EditorStyles.miniLabel);

                    EditorGUILayout.Space();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(error != null))
                        {
                            if (GUILayout.Button("Generate SoundIds.cs", GUILayout.Width(170))) Generate();
                        }
                        using (new EditorGUI.DisabledScope(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(s.SoundIdsPath) == null))
                        {
                            if (GUILayout.Button("Ping file", GUILayout.Width(80))) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(s.SoundIdsPath));
                        }
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(EditorUi.WizardTitle, GUILayout.Width(120))) SoundSetupWizardWindow.Open();
                        if (GUILayout.Button(EditorUi.CueLibraryTitle, GUILayout.Width(120))) CueLibraryWindow.Open();
                        if (GUILayout.Button(EditorUi.DebuggerTitle, GUILayout.Width(120))) RuntimeDebuggerWindow.Open();
                    }
                },
                keywords = new HashSet<string> { "kinatraa", "FMOD", "Sound", "Audio", "SoundIds", "Cue" },
            };
        }

        /// <summary>Regenerates SoundIds.cs after a cue library is saved, when Auto-generate is on.</summary>
        private sealed class LibraryWatcher : AssetPostprocessor
        {
            private static bool scheduled;

            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (scheduled || !FmodAudioProjectSettings.instance.AutoGenerateSoundIds) return;
                bool touched = imported.Concat(deleted).Concat(moved).Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                                                                                 (AssetDatabase.LoadAssetAtPath<SoundCueLibrary>(p) != null || deleted.Contains(p)));
                if (!touched) return;
                scheduled = true;
                EditorApplication.delayCall += () =>
                {
                    scheduled = false;
                    EditorCues.Invalidate();
                    Generate(false);
                };
            }
        }
    }
}
