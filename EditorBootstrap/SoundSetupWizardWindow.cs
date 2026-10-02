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
        private GUIStyle wrap;

        /// <summary>Opens the wizard.</summary>
        [MenuItem("Tools/kinatraa/FMOD Audio/Setup Wizard", priority = 0)]
        public static void Open()
        {
            SoundSetupWizardWindow window = GetWindow<SoundSetupWizardWindow>();
            window.titleContent = new GUIContent("FMOD Audio Setup");
            window.minSize = new Vector2(520, 420);
            window.Refresh();
            window.Show();
        }

        private void OnEnable() => Refresh();

        private void OnFocus() => Refresh();

        /// <summary>Re-evaluates every check.</summary>
        public void Refresh()
        {
            rows.Clear();
            fmodInstalled = FmodDetector.IsInstalled;
            foreach (SetupCheck check in SetupCheckRegistry.All)
            {
                if (!fmodInstalled && !(check is FmodInstalledCheck) && !(check is ScriptingDefineCheck)) continue;
                SetupCheckResult result;
                try
                {
                    result = check.Evaluate();
                }
                catch (Exception e)
                {
                    result = SetupCheckResult.Warn("Check failed to run: " + e.Message);
                }
                rows.Add((check, result));
            }
            Repaint();
        }

        private void OnGUI()
        {
            if (wrap == null) wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };

            EditorGUILayout.LabelField("kinatraa FMOD Audio: project setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Each item shows its status, why it matters, and a Fix button when it can be fixed automatically. " +
                                       "Fixes are idempotent and logged to the Console; scene and settings changes support Undo.", wrap);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Re-scan", GUILayout.Width(90))) Refresh();
                using (new EditorGUI.DisabledScope(!fmodInstalled))
                {
                    if (GUILayout.Button("Fix All", GUILayout.Width(90))) FixAll();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Validate (log report)", GUILayout.Width(150))) SetupValidator.Run();
            }
            EditorGUILayout.Space();

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
            foreach ((SetupCheck check, SetupCheckResult result) in rows) DrawRow(check, result);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(SetupCheck check, SetupCheckResult result)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color old = GUI.color;
                    GUI.color = StatusColor(result.Status);
                    GUILayout.Label(StatusLabel(result.Status), EditorStyles.boldLabel, GUILayout.Width(70));
                    GUI.color = old;
                    GUILayout.Label($"{check.Order / 10}. {check.Title}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (check.CanFix(result) && GUILayout.Button(check.FixLabel, GUILayout.MinWidth(90)))
                    {
                        RunFix(check);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.LabelField(result.Message, wrap);
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

        private static string StatusLabel(SetupStatus status)
        {
            switch (status)
            {
                case SetupStatus.OK: return "OK";
                case SetupStatus.Info: return "Info";
                case SetupStatus.Warning: return "Warning";
                default: return "Missing";
            }
        }

        private static Color StatusColor(SetupStatus status)
        {
            switch (status)
            {
                case SetupStatus.OK: return new Color(0.45f, 0.85f, 0.45f);
                case SetupStatus.Info: return new Color(0.6f, 0.75f, 1f);
                case SetupStatus.Warning: return new Color(1f, 0.8f, 0.3f);
                default: return new Color(1f, 0.45f, 0.45f);
            }
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
