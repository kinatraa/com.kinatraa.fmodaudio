using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace kinatraa.FmodAudio.Editor
{
    /// <summary>
    /// Runs the Setup Wizard checks without UI. Usable from CI:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod kinatraa.FmodAudio.Editor.SetupValidator.RunAndExit</c>
    /// (exit code 0 = no Missing items, 1 = something is missing). <see cref="Run"/> returns the result object instead.
    /// </summary>
    public static class SetupValidator
    {
        /// <summary>One evaluated check.</summary>
        public readonly struct Entry
        {
            /// <summary>Check title.</summary>
            public readonly string Title;
            /// <summary>Result.</summary>
            public readonly SetupCheckResult Result;

            internal Entry(string title, SetupCheckResult result)
            {
                Title = title;
                Result = result;
            }
        }

        /// <summary>Validation result.</summary>
        public sealed class Result
        {
            /// <summary>Evaluated checks in wizard order.</summary>
            public readonly List<Entry> Entries = new List<Entry>();
            /// <summary>Number of Missing items.</summary>
            public int MissingCount => Entries.Count(e => e.Result.Status == SetupStatus.Missing);
            /// <summary>Number of Warning items.</summary>
            public int WarningCount => Entries.Count(e => e.Result.Status == SetupStatus.Warning);
            /// <summary>True when nothing is Missing (warnings do not fail).</summary>
            public bool Passed => MissingCount == 0;
            /// <summary>Process exit code for CI: 0 when passed, 1 otherwise.</summary>
            public int ExitCode => Passed ? 0 : 1;

            /// <summary>Multi-line report.</summary>
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"kinatraa FMOD Audio setup: {(Passed ? "PASSED" : "FAILED")} ({MissingCount} missing, {WarningCount} warning(s))");
                foreach (Entry e in Entries) sb.AppendLine($"  [{e.Result.Status}] {e.Title}: {e.Result.Message}");
                return sb.ToString();
            }
        }

        /// <summary>Evaluates every check. When FMOD is not installed only the FMOD and define checks are evaluated.</summary>
        public static Result Validate()
        {
            var result = new Result();
            bool fmod = FmodDetector.IsInstalled;
            foreach (SetupCheck check in SetupCheckRegistry.All)
            {
                if (!fmod && !(check is FmodInstalledCheck) && !(check is ScriptingDefineCheck)) continue;
                SetupCheckResult r;
                try
                {
                    r = check.Evaluate();
                }
                catch (Exception e)
                {
                    r = SetupCheckResult.Warn("Check failed to run: " + e.Message);
                }
                result.Entries.Add(new Entry(check.Title, r));
            }
            if (fmod && !SetupCheckRegistry.FmodChecksRegistered)
            {
                result.Entries.Add(new Entry("FMOD-dependent checks", SetupCheckResult.Missing(
                    $"The package's FMOD editor assembly is not compiled yet (waiting for {FmodDetector.Define}). Let Unity finish compiling and run again.")));
            }
            return result;
        }

        /// <summary>Validates and logs the report. Returns the result object.</summary>
        public static Result Run()
        {
            Result result = Validate();
            if (result.Passed) Debug.Log("[kinatraa.fmodaudio] " + result);
            else Debug.LogWarning("[kinatraa.fmodaudio] " + result);
            return result;
        }

        /// <summary>For -executeMethod in batch mode: validates, logs, and exits with <see cref="Result.ExitCode"/>.</summary>
        public static void RunAndExit()
        {
            Result result = Run();
            EditorApplication.Exit(result.ExitCode);
        }

        [MenuItem(EditorUi.MenuRoot + "Validate Setup", priority = 1)]
        private static void ValidateMenu()
        {
            Result result = Run();
            string summary = result.Passed
                ? $"Setup is valid ({result.WarningCount} warning(s)). Details are in the Console."
                : $"{result.MissingCount} item(s) missing, {result.WarningCount} warning(s). Details are in the Console.";
            if (EditorUtility.DisplayDialog("kinatraa FMOD Audio", summary, "Open Setup Wizard", "Close")) SoundSetupWizardWindow.Open();
        }
    }
}
