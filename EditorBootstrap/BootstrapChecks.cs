using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace kinatraa.FmodAudio.Editor
{
    /// <summary>1. FMOD for Unity installed (and which version).</summary>
    internal sealed class FmodInstalledCheck : SetupCheck
    {
        public override int Order => 10;
        public override string Title => "FMOD for Unity installed";

        public override SetupCheckResult Evaluate()
        {
            switch (FmodDetector.Detect())
            {
                case FmodInstallState.InstalledWithoutAsmdef:
                    return SetupCheckResult.Missing(
                        $"FMOD types were found in assembly '{FmodDetector.RuntimeManagerAssemblyName}', not in '{FmodDetector.RuntimeAssembly}'. " +
                        "This package needs FMOD for Unity 2.03, which ships FMODUnity.asmdef and FMODUnityEditor.asmdef. Re-import FMOD for Unity 2.03.");
                case FmodInstallState.NotInstalled:
                    return SetupCheckResult.Missing(
                        "FMOD for Unity is not installed. Download 'FMOD for Unity' 2.03.15 from fmod.com (free account required), then " +
                        "Assets > Import Package > Custom Package... and import fmodstudio20315.unitypackage. FMOD cannot be redistributed by this package.");
            }

            uint version = FmodDetector.InstalledVersion;
            string where = Path.GetDirectoryName(FmodDetector.RuntimeAsmdefPath);
            if (version == 0)
            {
                return SetupCheckResult.Warn($"FMOD for Unity detected ({where}) but its version could not be read. This package was tested with " +
                                             $"{FmodDetector.FormatVersion(FmodDetector.TestedVersion)}.");
            }
            if (version == FmodDetector.TestedVersion) return SetupCheckResult.Ok($"FMOD for Unity {FmodDetector.FormatVersion(version)} detected ({where}).");
            if (FmodDetector.IsSupportedLine(version))
            {
                return SetupCheckResult.Warn($"FMOD for Unity {FmodDetector.FormatVersion(version)} detected ({where}). This package was tested with " +
                                             $"{FmodDetector.FormatVersion(FmodDetector.TestedVersion)}; other 2.03 versions should work but are untested.");
            }
            return SetupCheckResult.Warn($"FMOD for Unity {FmodDetector.FormatVersion(version)} detected ({where}). This package was written and tested for " +
                                         $"{FmodDetector.FormatVersion(FmodDetector.TestedVersion)} (2.03 line). Other versions are not supported.");
        }

        public override bool HasDetails => true;

        public override void DrawGUI()
        {
            if (GUILayout.Button("Open FMOD download page (fmod.com)", GUILayout.Width(260))) Application.OpenURL(FmodDetector.DownloadUrl);
        }
    }

    /// <summary>2. KINATRAA_FMOD defined for every build target.</summary>
    internal sealed class ScriptingDefineCheck : SetupCheck
    {
        public override int Order => 20;
        public override string Title => "Scripting define " + FmodDetector.Define;

        public override SetupCheckResult Evaluate()
        {
            bool installed = FmodDetector.IsInstalled;
            if (installed && DefineSync.IsDefinedEverywhere) return SetupCheckResult.Ok($"{FmodDetector.Define} is defined for every build target.");
            if (installed) return SetupCheckResult.Missing($"{FmodDetector.Define} is missing for some build targets; the package's runtime and editor assemblies do not compile without it. It is normally added automatically.");
            if (DefineSync.IsDefinedAnywhere) return SetupCheckResult.Warn($"{FmodDetector.Define} is defined but FMOD is not installed; this causes compile errors. Fix removes it.");
            return SetupCheckResult.Ok($"FMOD is not installed and {FmodDetector.Define} is not defined (expected).");
        }

        public override bool CanFix(SetupCheckResult result) => result.Status != SetupStatus.OK;
        public override string FixLabel => FmodDetector.IsInstalled ? "Add define" : "Remove define";
        public override void Fix() => DefineSync.Sync();
    }

    /// <summary>5. Built-in Unity audio disabled so two audio engines do not run.</summary>
    internal sealed class UnityAudioCheck : SetupCheck
    {
        private const string AudioManagerPath = "ProjectSettings/AudioManager.asset";

        public override int Order => 50;
        public override string Title => "Unity built-in audio disabled";

        public override SetupCheckResult Evaluate()
        {
            SerializedProperty prop = DisableAudioProperty(out _);
            if (prop == null) return SetupCheckResult.Warn("Could not read Project Settings > Audio > Disable Unity Audio. Set it manually.");
            if (prop.boolValue) return SetupCheckResult.Ok("Project Settings > Audio > Disable Unity Audio is on.");
            string why = "Unity's audio engine is enabled alongside FMOD and consumes system audio resources. FMOD for Unity 2.03.15 has no own switch for this; " +
                         "the fix sets Project Settings > Audio > Disable Unity Audio (the same setting FMOD's own wizard uses).";
            if (UsesUnityAudioAssets())
            {
                why += " This project contains AudioClips or AudioMixers, so it may still play audio through Unity AudioSources; \"Fix All\" skips this item. " +
                       "Use Fix only if nothing needs Unity audio.";
            }
            return SetupCheckResult.Warn(why);
        }

        public override bool CanFix(SetupCheckResult result) => result.Status == SetupStatus.Warning && DisableAudioProperty(out _) != null;
        public override string FixLabel => "Disable Unity audio";
        public override bool IncludedInFixAll => !UsesUnityAudioAssets();

        public override void Fix()
        {
            SerializedProperty prop = DisableAudioProperty(out SerializedObject so);
            if (prop == null || prop.boolValue) return;
            prop.boolValue = true;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            LogChange("Project Settings > Audio > Disable Unity Audio turned ON (undo with Edit > Undo or untick it in Project Settings).");
        }

        private static SerializedProperty DisableAudioProperty(out SerializedObject so)
        {
            so = null;
            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(AudioManagerPath);
            if (objects == null || objects.Length == 0) return null;
            so = new SerializedObject(objects[0]);
            return so.FindProperty("m_DisableAudio");
        }

        private static bool UsesUnityAudioAssets()
        {
            return AssetDatabase.FindAssets("t:AudioMixer", new[] { "Assets" }).Length > 0 ||
                   AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }).Length > 0;
        }
    }

    /// <summary>9. Recommended folders (created only after confirmation; nothing is moved).</summary>
    internal sealed class FoldersCheck : SetupCheck
    {
        internal const string AudioFolder = "Assets/Audio";

        public override int Order => 90;
        public override string Title => "Recommended folders";

        public override SetupCheckResult Evaluate()
        {
            string studio = SetupCheckRegistry.StudioProjectFolderProvider?.Invoke();
            string studioNote = string.IsNullOrEmpty(studio)
                ? " Suggested FMOD Studio project location: FMODProject/ next to Assets/ (create it from FMOD Studio: File > New, then Save As)."
                : $" FMOD Studio project folder: {studio}/.";
            if (AssetDatabase.IsValidFolder(AudioFolder)) return SetupCheckResult.Ok($"{AudioFolder} exists (cue libraries).{studioNote}");
            return SetupCheckResult.Warn($"{AudioFolder} (for cue libraries) does not exist.{studioNote}");
        }

        public override bool CanFix(SetupCheckResult result) => !AssetDatabase.IsValidFolder(AudioFolder);
        public override string FixLabel => "Create Assets/Audio";

        public override void Fix()
        {
            if (AssetDatabase.IsValidFolder(AudioFolder)) return;
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Create folder", $"Create {AudioFolder}? Existing files are never moved.", "Create", "Cancel")) return;
            AssetDatabase.CreateFolder("Assets", "Audio");
            LogChange($"Created folder {AudioFolder}.");
        }
    }

    /// <summary>10. .gitignore entries for FMOD Studio's generated folders (appended only; user lines are never removed).</summary>
    internal sealed class GitIgnoreCheck : SetupCheck
    {
        private const string Header = "# FMOD Studio (added by kinatraa FMOD Audio setup wizard)";

        public override int Order => 100;
        public override string Title => ".gitignore entries for FMOD Studio";

        private static string GitIgnorePath => Path.Combine(Directory.GetCurrentDirectory(), ".gitignore");

        internal static List<string> MissingLines()
        {
            string folder = SetupCheckRegistry.StudioProjectFolderProvider?.Invoke();
            if (string.IsNullOrEmpty(folder) || folder.StartsWith("..", StringComparison.Ordinal)) folder = "FMODProject";
            folder = folder.Replace('\\', '/').TrimEnd('/');
            string[] wanted = { folder + "/Build/", folder + "/.cache/", folder + "/.unsaved/", folder + "/.user/" };
            var existing = new HashSet<string>(File.ReadAllLines(GitIgnorePath).Select(l => l.Trim().TrimStart('/')));
            return wanted.Where(w => !existing.Contains(w) && !existing.Contains(w.TrimEnd('/'))).ToList();
        }

        public override SetupCheckResult Evaluate()
        {
            if (!File.Exists(GitIgnorePath)) return SetupCheckResult.Ok("No .gitignore at the project root; nothing to do.");
            List<string> missing = MissingLines();
            if (missing.Count == 0) return SetupCheckResult.Ok(".gitignore already ignores FMOD Studio's Build, .cache, .unsaved and .user folders.");
            return SetupCheckResult.Warn(".gitignore is missing FMOD Studio entries (preview below). Note: ignoring Build/ means banks are rebuilt from the Studio project on each machine.");
        }

        public override bool CanFix(SetupCheckResult result) => result.Status == SetupStatus.Warning;
        public override string FixLabel => "Append entries";
        public override bool HasDetails => File.Exists(GitIgnorePath) && MissingLines().Count > 0;

        public override void DrawGUI()
        {
            if (!File.Exists(GitIgnorePath)) return;
            List<string> missing = MissingLines();
            if (missing.Count == 0) return;
            EditorGUILayout.LabelField("Lines to append:", EditorStyles.miniBoldLabel);
            foreach (string line in missing) EditorGUILayout.LabelField("+ " + line, EditorStyles.miniLabel);
        }

        public override void Fix()
        {
            if (!File.Exists(GitIgnorePath)) return;
            List<string> missing = MissingLines();
            if (missing.Count == 0) return;
            string text = File.ReadAllText(GitIgnorePath);
            string prefix = text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty;
            File.AppendAllText(GitIgnorePath, prefix + "\n" + Header + "\n" + string.Join("\n", missing) + "\n");
            LogChange("Appended to .gitignore: " + string.Join(", ", missing));
        }
    }
}
