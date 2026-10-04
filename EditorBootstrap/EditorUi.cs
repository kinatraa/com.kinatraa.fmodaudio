using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("kinatraa.fmodaudio.Editor")]

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>Shared look of the package's editor windows and inspectors (one place for menus, titles, colors and styles).</summary>
    internal static class EditorUi
    {
        internal const string MenuRoot = "Tools/kinatraa/FMOD Audio/";
        internal const string DocsUrl = "https://github.com/kinatraa/com.kinatraa.fmodaudio/blob/main/Documentation~/";

        // Window titles match their menu item names.
        internal const string WizardTitle = "Setup Wizard";
        internal const string CueLibraryTitle = "Cue Library";
        internal const string DebuggerTitle = "Runtime Debugger";

        private static GUIStyle wrap;
        private static GUIStyle wrapMini;
        private static GUIStyle badge;
        private static GUIStyle title;

        internal static GUIStyle Wrap => wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        internal static GUIStyle WrapMini => wrapMini ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = false };
        internal static GUIStyle Badge => badge ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
        internal static GUIStyle Title => title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        internal static Color OkColor => new Color(0.45f, 0.85f, 0.45f);
        internal static Color InfoColor => new Color(0.6f, 0.75f, 1f);
        internal static Color WarningColor => new Color(1f, 0.8f, 0.3f);
        internal static Color ErrorColor => new Color(1f, 0.45f, 0.45f);

        internal static Color StatusColor(SetupStatus status)
        {
            switch (status)
            {
                case SetupStatus.OK: return OkColor;
                case SetupStatus.Info: return InfoColor;
                case SetupStatus.Warning: return WarningColor;
                default: return ErrorColor;
            }
        }

        internal static string StatusLabel(SetupStatus status)
        {
            switch (status)
            {
                case SetupStatus.OK: return "OK";
                case SetupStatus.Info: return "Info";
                case SetupStatus.Warning: return "Warning";
                default: return "Missing";
            }
        }

        /// <summary>Colored text label (status column, cue problems).</summary>
        internal static void ColoredLabel(string text, Color color, GUIStyle style, params GUILayoutOption[] options)
        {
            Color old = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, style, options);
            GUI.contentColor = old;
        }

        /// <summary>Window header: title plus a one-line explanation.</summary>
        internal static void Header(string text, string subtitle)
        {
            EditorGUILayout.LabelField(text, Title);
            if (!string.IsNullOrEmpty(subtitle)) EditorGUILayout.LabelField(subtitle, WrapMini);
            EditorGUILayout.Space(2);
        }

        /// <summary>Bold section label with a little spacing above it.</summary>
        internal static void Section(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        /// <summary>Opens a page of the package documentation.</summary>
        internal static void OpenDocs(string page) => Application.OpenURL(DocsUrl + page);
    }
}
