using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>
    /// Tools > kinatraa > FMOD Audio > Setup Wizard. Scans the project and shows a checklist with a Fix button per item
    /// and Fix All. Lives in the bootstrap assembly so it opens (and explains what is missing) even without FMOD.
    /// </summary>
    public sealed class SoundSetupWizardWindow : EditorWindow
    {
        private readonly List<(SetupCheck check, SetupCheckResult result)> rows = new List<(SetupCheck, SetupCheckResult)>();
        private readonly HashSet<SetupCheck> expanded = new HashSet<SetupCheck>();
        private Vector2 scroll;
        private bool fmodInstalled;
        private bool onlyProblems;

        /// <summary>Opens the wizard.</summary>
        [MenuItem(EditorUi.MenuRoot + "Setup Wizard", priority = 0)]
        public static void Open()
        {
            SoundSetupWizardWindow window = GetWindow<SoundSetupWizardWindow>();
            window.titleContent = new GUIContent(EditorUi.WizardTitle);
            window.minSize = new Vector2(540, 420);
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(EditorUi.WizardTitle);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Refresh();
        }

        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        private void OnFocus() => Refresh();

        private void OnPlayModeChanged(PlayModeStateChange change) => Refresh();

        /// <summary>Re-evaluates every check.</summary>
        public void Refresh()
        {
            rows.Clear();
            fmodInstalled = FmodDetector.IsInstalled;
            foreach (SetupCheck check in SetupCheckRegistry.All)
            {
                if (!fmodInstalled && !(check is FmodInstalledCheck) && !(check is ScriptingDefineCheck)) continue;
                rows.Add((check, Evaluate(check)));
            }
            Repaint();
        }

        private static SetupCheckResult Evaluate(SetupCheck check)
        {
            try
            {
                return check.Evaluate();
            }
            catch (Exception e)
            {
                return SetupCheckResult.Warn("Check failed to run: " + e.Message);
            }
        }

        private int FixableCount()
        {
            int n = 0;
            foreach ((SetupCheck check, SetupCheckResult result) in rows)
            {
                if (check.IncludedInFixAll && result.Status != SetupStatus.OK && check.CanFix(result)) n++;
            }
            return n;
        }

        private void OnGUI()
        {
            EditorUi.Header("kinatraa FMOD Audio: project setup",
                "Each item shows its status, why it matters, and a Fix button when it can be fixed automatically. " +
                "Fixes are idempotent and logged to the Console; scene and settings changes support Undo.");

            DrawToolbar();
            DrawSummary();

            if (!fmodInstalled)
            {
                EditorGUILayout.HelpBox("FMOD for Unity not detected. Install FMOD for Unity 2.03.15 first; the rest of the setup appears once FMOD is " +
                                        "imported and the project has recompiled. Nothing in this package is active until then, and the project compiles without errors.",
                    MessageType.Error);
            }
            else if (!SetupCheckRegistry.FmodChecksRegistered)
            {
                EditorGUILayout.HelpBox($"FMOD detected. Waiting for {FmodDetector.Define} and recompilation before the remaining checks appear. " +
                                        "If this persists, use Fix on the define item and wait for Unity to compile.", MessageType.Info);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach ((SetupCheck check, SetupCheckResult result) in rows)
            {
                if (onlyProblems && (result.Status == SetupStatus.OK || result.Status == SetupStatus.Info)) continue;
                DrawRow(check, result);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Re-scan", EditorStyles.toolbarButton, GUILayout.Width(64))) Refresh();
                int fixable = FixableCount();
                using (new EditorGUI.DisabledScope(!fmodInstalled || fixable == 0))
                {
                    if (GUILayout.Button(fixable > 0 ? $"Fix All ({fixable})" : "Fix All", EditorStyles.toolbarButton, GUILayout.Width(80))) FixAll();
                }
                onlyProblems = GUILayout.Toggle(onlyProblems, "Only problems", EditorStyles.toolbarButton, GUILayout.Width(96));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!SetupCheckRegistry.FmodChecksRegistered))
                {
                    if (GUILayout.Button(EditorUi.CueLibraryTitle, EditorStyles.toolbarButton)) EditorApplication.ExecuteMenuItem(EditorUi.MenuRoot + "Cue Library");
                    if (GUILayout.Button(EditorUi.DebuggerTitle, EditorStyles.toolbarButton)) EditorApplication.ExecuteMenuItem(EditorUi.MenuRoot + "Runtime Debugger");
                }
                if (GUILayout.Button("Log report", EditorStyles.toolbarButton)) SetupValidator.Run();
                if (GUILayout.Button("Docs", EditorStyles.toolbarButton)) EditorUi.OpenDocs("Wizard.md");
            }
        }

        private void DrawSummary()
        {
            int ok = 0, info = 0, warn = 0, missing = 0;
            foreach ((SetupCheck _, SetupCheckResult result) in rows)
            {
                switch (result.Status)
                {
                    case SetupStatus.OK: ok++; break;
                    case SetupStatus.Info: info++; break;
                    case SetupStatus.Warning: warn++; break;
                    default: missing++; break;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorUi.ColoredLabel($"{ok} OK", EditorUi.OkColor, EditorStyles.boldLabel, GUILayout.Width(60));
                EditorUi.ColoredLabel($"{warn} warning(s)", EditorUi.WarningColor, EditorStyles.boldLabel, GUILayout.Width(100));
                EditorUi.ColoredLabel($"{missing} missing", EditorUi.ErrorColor, EditorStyles.boldLabel, GUILayout.Width(80));
                if (info > 0) EditorUi.ColoredLabel($"{info} info", EditorUi.InfoColor, EditorStyles.boldLabel, GUILayout.Width(60));
                GUILayout.FlexibleSpace();
                if (fmodInstalled && SetupCheckRegistry.FmodChecksRegistered && missing == 0 && warn == 0)
                {
                    EditorUi.ColoredLabel("Project is set up.", EditorUi.OkColor, EditorStyles.boldLabel);
                }
            }
            EditorGUILayout.Space(2);
        }

        private void DrawRow(SetupCheck check, SetupCheckResult result)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorUi.ColoredLabel(EditorUi.StatusLabel(result.Status), EditorUi.StatusColor(result.Status), EditorStyles.boldLabel, GUILayout.Width(64));
                    GUILayout.Label($"{check.Order / 10}. {check.Title}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (check.CanFix(result) && GUILayout.Button(new GUIContent(check.FixLabel, check.IncludedInFixAll ? "Fix this item" : "Fix this item (not part of Fix All)"), GUILayout.MinWidth(90)))
                    {
                        RunFix(check);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.LabelField(result.Message, EditorUi.Wrap);
                if (!check.HasDetails) return;
                bool open = expanded.Contains(check);
                bool now = EditorGUILayout.Foldout(open, "Details", true);
                if (now != open)
                {
                    if (now) expanded.Add(check);
                    else expanded.Remove(check);
                }
                if (now) check.DrawGUI();
            }
        }

        private void RunFix(SetupCheck check)
        {
            try
            {
                check.Fix();
            }
            catch (Exception e)
            {
                Debug.LogError($"[kinatraa.fmodaudio] Setup: fixing '{check.Title}' failed: {e}");
            }
            Refresh();
        }

        private void FixAll()
        {
            FixAllChecks();
            Refresh();
        }

        /// <summary>Runs every fixable check in order (re-evaluating each before fixing). Returns the number of fixes applied.</summary>
        public static int FixAllChecks()
        {
            int applied = 0;
            foreach (SetupCheck check in SetupCheckRegistry.All)
            {
                if (!check.IncludedInFixAll) continue;
                SetupCheckResult result;
                try
                {
                    result = check.Evaluate();
                    if (result.Status == SetupStatus.OK || !check.CanFix(result)) continue;
                    check.Fix();
                    applied++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[kinatraa.fmodaudio] Setup: fixing '{check.Title}' failed: {e}");
                }
            }
            Debug.Log($"[kinatraa.fmodaudio] Setup: Fix All applied {applied} fix(es).");
            return applied;
        }
    }

    /// <summary>One lightweight, non-blocking console notice per project (stored in UserSettings) on first import.</summary>
    [InitializeOnLoad]
    internal static class FirstImportNotice
    {
        private const string Key = "kinatraa.fmodaudio.firstImportNoticeShown";
        private const string LinkAttribute = "kinatraa_fmodaudio_wizard";

        static FirstImportNotice()
        {
            EditorGUI.hyperLinkClicked -= OnLinkClicked;
            EditorGUI.hyperLinkClicked += OnLinkClicked;
            if (Application.isBatchMode || EditorUserSettings.GetConfigValue(Key) == "1") return;
            EditorApplication.delayCall += Show;
        }

        private static void Show()
        {
            if (EditorUserSettings.GetConfigValue(Key) == "1") return;
            EditorUserSettings.SetConfigValue(Key, "1");
            bool fmod = FmodDetector.IsInstalled;
            bool config = AssetDatabase.FindAssets("kinatraaFmodAudioConfig").Length > 0;
            string state = !fmod ? "FMOD for Unity was not detected - install FMOD for Unity 2.03.15 first."
                : !config ? "FMOD detected; the project is not configured yet."
                : "FMOD detected and a config exists.";
            Debug.Log($"[kinatraa.fmodaudio] kinatraa FMOD Audio installed. {state} " +
                      $"<a {LinkAttribute}=\"1\">Open the Setup Wizard</a> (Tools > kinatraa > FMOD Audio > Setup Wizard).");
        }

        private static void OnLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if (args.hyperLinkData != null && args.hyperLinkData.ContainsKey(LinkAttribute)) SoundSetupWizardWindow.Open();
        }
    }
}
