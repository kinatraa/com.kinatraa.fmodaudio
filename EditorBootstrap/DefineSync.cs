using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>
    /// Keeps the KINATRAA_FMOD scripting define in sync with FMOD's presence for every build target.
    /// Runs on every domain reload, after asmdef imports/deletions, and after every compilation (which also fires when
    /// a compile fails because FMOD was removed and no domain reload happens).
    /// </summary>
    [InitializeOnLoad]
    public static class DefineSync
    {
        static DefineSync()
        {
            Sync();
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object context) => EditorApplication.delayCall += () => Sync();

        /// <summary>Build targets the define is written to.</summary>
        public static IEnumerable<NamedBuildTarget> Targets
        {
            get
            {
                var seen = new HashSet<string>();
                foreach (FieldInfo field in typeof(BuildTargetGroup).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.IsDefined(typeof(ObsoleteAttribute), false)) continue;
                    var group = (BuildTargetGroup)field.GetValue(null);
                    if (group == BuildTargetGroup.Unknown) continue;
                    NamedBuildTarget target;
                    try
                    {
                        target = NamedBuildTarget.FromBuildTargetGroup(group);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (seen.Add(target.TargetName)) yield return target;
                }
                if (seen.Add(NamedBuildTarget.Server.TargetName)) yield return NamedBuildTarget.Server;
            }
        }

        /// <summary>True when every build target has the define.</summary>
        public static bool IsDefinedEverywhere => Targets.All(HasDefine);

        /// <summary>True when at least one build target has the define.</summary>
        public static bool IsDefinedAnywhere => Targets.Any(HasDefine);

        private static bool HasDefine(NamedBuildTarget target)
        {
            return PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Contains(FmodDetector.Define);
        }

        /// <summary>Adds the define when FMOD is installed, removes it otherwise. Idempotent. Returns true if anything changed.</summary>
        public static bool Sync()
        {
            bool want = FmodDetector.IsInstalled;
            int changed = 0;
            foreach (NamedBuildTarget target in Targets)
            {
                List<string> defines = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
                bool has = defines.Contains(FmodDetector.Define);
                if (want == has) continue;
                if (want) defines.Add(FmodDetector.Define);
                else defines.RemoveAll(d => d == FmodDetector.Define);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                changed++;
            }
            if (changed > 0)
            {
                // Persist ProjectSettings right away so a crash cannot leave a stale define behind.
                EditorApplication.delayCall += AssetDatabase.SaveAssets;
                Debug.Log(want
                    ? $"[kinatraa.fmodaudio] FMOD for Unity detected: added scripting define {FmodDetector.Define} to {changed} build target(s)."
                    : $"[kinatraa.fmodaudio] FMOD for Unity not found: removed scripting define {FmodDetector.Define} from {changed} build target(s).");
            }
            return changed > 0;
        }

        private sealed class AsmdefWatcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (Touches(imported) || Touches(deleted) || Touches(moved) || Touches(movedFrom)) EditorApplication.delayCall += () => Sync();
            }

            private static bool Touches(string[] paths)
            {
                foreach (string p in paths)
                {
                    if (p.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }
    }
}
