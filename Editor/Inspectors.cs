using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FMODUnity;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace kinatraa.FmodAudio.Editor
{
    /// <summary>Dropdown of cue ids for string fields marked with <see cref="SoundCueIdAttribute"/>.</summary>
    [CustomPropertyDrawer(typeof(SoundCueIdAttribute))]
    internal sealed class SoundCueIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[SoundCueId] needs a string field");
                return;
            }
            EditorGUI.BeginProperty(position, label, property);
            Rect field = new Rect(position.x, position.y, position.width - 22, position.height);
            Rect button = new Rect(position.xMax - 20, position.y, 20, position.height);

            string id = property.stringValue;
            bool unknown = !string.IsNullOrEmpty(id) && !EditorCues.AllCueIds().Contains(id);
            Color old = GUI.color;
            if (unknown) GUI.color = new Color(1f, 0.6f, 0.5f);
            property.stringValue = EditorGUI.TextField(field, new GUIContent(label.text, unknown ? $"Unknown cue id '{id}'" : label.tooltip), id);
            GUI.color = old;

            if (GUI.Button(button, "▾", EditorStyles.miniButton))
            {
                SerializedProperty copy = property.Copy();
                new CueIdDropdown(new AdvancedDropdownState(), picked =>
                {
                    copy.serializedObject.Update();
                    copy.stringValue = picked;
                    copy.serializedObject.ApplyModifiedProperties();
                }).Show(position);
            }
            EditorGUI.EndProperty();
        }

        private sealed class CueIdDropdown : AdvancedDropdown
        {
            private readonly Action<string> onPicked;

            public CueIdDropdown(AdvancedDropdownState state, Action<string> onPicked) : base(state)
            {
                this.onPicked = onPicked;
                minimumSize = new Vector2(240, 300);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Cue ids");
                foreach (SoundCueLibrary library in EditorCues.FindAllLibraries())
                {
                    var group = new AdvancedDropdownItem(library.name);
                    foreach (SoundCue cue in library.Cues.Where(c => c != null && !string.IsNullOrEmpty(c.Id)).OrderBy(c => c.Id, StringComparer.Ordinal))
                    {
                        group.AddChild(new AdvancedDropdownItem(cue.Id));
                    }
                    root.AddChild(group);
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) => onPicked(item.name);
        }
    }

    /// <summary>Popup of configured channel names for <see cref="SoundChannel"/> fields.</summary>
    [CustomPropertyDrawer(typeof(SoundChannel))]
    internal sealed class SoundChannelDrawer : PropertyDrawer
    {
        private const string Custom = "Custom...";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty nameProp = property.FindPropertyRelative("name");
            EditorGUI.BeginProperty(position, label, property);
            List<string> options = EditorCues.ChannelNames();
            string current = nameProp.stringValue ?? string.Empty;
            bool known = options.Contains(current);
            if (!known && current.Length > 0) options.Add(current);
            options.Add(Custom);
            int index = Mathf.Max(0, options.IndexOf(current));
            if (current.Length == 0) index = options.Count - 1;

            Rect popupRect = position;
            if (!known)
            {
                popupRect.width = EditorGUIUtility.labelWidth + (position.width - EditorGUIUtility.labelWidth) * 0.5f;
                Rect text = new Rect(popupRect.xMax + 2, position.y, position.xMax - popupRect.xMax - 2, position.height);
                nameProp.stringValue = EditorGUI.TextField(text, current);
            }
            int picked = EditorGUI.Popup(popupRect, label.text, index, options.ToArray());
            if (picked != index && options[picked] != Custom) nameProp.stringValue = options[picked];
            else if (picked != index) nameProp.stringValue = string.Empty;
            EditorGUI.EndProperty();
        }
    }

    /// <summary>Inspector for <see cref="SoundCueLibrary"/>: validation summary and a shortcut to the Cue Library window.</summary>
    [CustomEditor(typeof(SoundCueLibrary))]
    internal sealed class SoundCueLibraryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var library = (SoundCueLibrary)target;
            if (GUILayout.Button("Open in Cue Library window", GUILayout.Height(24))) CueLibraryWindow.Open(library);
            SoundSystemConfig config = EditorCues.FindResourcesConfig();
            if (config != null && !config.Libraries.Contains(library))
            {
                EditorGUILayout.HelpBox("Not in the config's Libraries: its cues are unknown at runtime unless registered with Sound.RegisterLibrary.", MessageType.Info);
                if (GUILayout.Button("Add to config")) LibraryRegistration.AddToConfig(config, library);
            }
            HashSet<string> dupes = EditorCues.DuplicateIds(EditorCues.FindAllLibraries());
            int empty = library.Cues.Count(c => c == null || string.IsNullOrEmpty(c.Id));
            int noEvent = library.Cues.Count(c => c != null && c.Event.IsNull);
            int notFound = library.Cues.Count(c => c != null && !c.Event.IsNull && EventManager.EventFromGUID(c.Event.Guid) == null);
            int noChannel = library.Cues.Count(c => c != null && !c.Channel.IsValid);
            List<string> mine = library.Cues.Where(c => c != null && dupes.Contains(c.Id)).Select(c => c.Id).Distinct().ToList();
            EditorGUILayout.LabelField($"{library.Cues.Count} cue(s)", EditorStyles.miniBoldLabel);
            if (empty > 0) EditorGUILayout.HelpBox($"{empty} cue(s) have an empty id.", MessageType.Error);
            if (mine.Count > 0) EditorGUILayout.HelpBox("Duplicate ids (across all libraries): " + string.Join(", ", mine), MessageType.Error);
            if (noEvent > 0) EditorGUILayout.HelpBox($"{noEvent} cue(s) have no FMOD event assigned.", MessageType.Warning);
            if (notFound > 0) EditorGUILayout.HelpBox($"{notFound} cue(s) reference events not in FMOD's cache (deleted in Studio or banks not built).", MessageType.Warning);
            if (noChannel > 0) EditorGUILayout.HelpBox($"{noChannel} cue(s) have no channel.", MessageType.Warning);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }

    /// <summary>Inspector for <see cref="SoundSystemConfig"/> with validation help boxes.</summary>
    [CustomEditor(typeof(SoundSystemConfig))]
    internal sealed class SoundSystemConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (SoundSystemConfig)target;
            string path = AssetDatabase.GetAssetPath(config);
            bool autoLoaded = Path.GetFileNameWithoutExtension(path) == SoundSystemConfig.ResourcesName && path.Replace('\\', '/').Contains("/Resources/");
            if (!autoLoaded) EditorGUILayout.HelpBox($"Not loaded automatically: the runtime loads Resources/{SoundSystemConfig.ResourcesName}. Pass this asset to Sound.Initialize() or move/rename it.", MessageType.Info);

            List<string> names = config.Channels.Where(c => c != null).Select(c => c.Name).ToList();
            if (names.Any(string.IsNullOrEmpty)) EditorGUILayout.HelpBox("A channel has an empty name.", MessageType.Error);
            List<string> dupes = names.Where(n => !string.IsNullOrEmpty(n)).GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) EditorGUILayout.HelpBox("Duplicate channel names: " + string.Join(", ", dupes), MessageType.Error);
            if (!names.Contains("Master")) EditorGUILayout.HelpBox("No 'Master' channel (bus:/); master volume cannot be set through settings.", MessageType.Warning);
            List<string> badPaths = config.Channels
                .Where(c => c != null && !string.IsNullOrEmpty(c.BusPath) && !c.BusPath.StartsWith("bus:/", StringComparison.Ordinal) && !c.BusPath.StartsWith("vca:/", StringComparison.Ordinal))
                .Select(c => c.Name).ToList();
            if (badPaths.Count > 0)
            {
                EditorGUILayout.HelpBox("Bus Path must start with bus:/ (a mixer bus) or vca:/ (a VCA): " + string.Join(", ", badPaths), MessageType.Error);
            }
            if (config.Libraries.Any(l => l == null)) EditorGUILayout.HelpBox("Libraries contains empty entries.", MessageType.Warning);
            if (!config.Banks.Contains("Master") || !config.Banks.Contains("Master.strings"))
            {
                EditorGUILayout.HelpBox("Banks should include Master.strings and Master unless FMOD's own settings load them.", MessageType.Info);
            }
            HashSet<string> cueDupes = EditorCues.DuplicateIds(config.Libraries);
            if (cueDupes.Count > 0) EditorGUILayout.HelpBox("Duplicate cue ids across libraries: " + string.Join(", ", cueDupes), MessageType.Error);
            if (config.PauseMode != SoundPauseMode.MasterBus && !config.TryGetSnapshotPath("Pause", out _))
            {
                EditorGUILayout.HelpBox("PauseMode uses a snapshot but no 'Pause' snapshot key is mapped.", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Setup Wizard")) SoundSetupWizardWindow.Open();
                if (GUILayout.Button("Cue Library")) CueLibraryWindow.Open(config.Libraries.FirstOrDefault(l => l != null));
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }

    /// <summary>Inspector for <see cref="SoundEmitter"/> with validation and Play Mode controls.</summary>
    [CustomEditor(typeof(SoundEmitter))]
    [CanEditMultipleObjects]
    internal sealed class SoundEmitterEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var emitter = (SoundEmitter)target;
            if (string.IsNullOrEmpty(emitter.CueId))
            {
                EditorGUILayout.HelpBox("No cue id set.", MessageType.Warning);
            }
            else if (!EditorCues.TryFindCue(emitter.CueId, out SoundCue cue, out SoundCueLibrary library))
            {
                EditorGUILayout.HelpBox($"Cue id '{emitter.CueId}' is not in any SoundCueLibrary.", MessageType.Error);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string path = cue.Event.IsNull ? "(no event)" : EventManager.EventFromGUID(cue.Event.Guid)?.Path ?? cue.Event.Path;
                    EditorGUILayout.LabelField($"{path}  |  {cue.Channel.Name}  |  {library.name}", EditorStyles.miniLabel);
                    if (GUILayout.Button("Edit cue", EditorStyles.miniButton, GUILayout.Width(60))) CueLibraryWindow.Open(library);
                }
            }

            if (emitter.PlayOn == SoundEmitterTrigger.TriggerEnter)
            {
#if kinatraa_fmodaudio_physics
                Collider c = emitter.GetComponent<Collider>();
                if (c == null || !c.isTrigger) EditorGUILayout.HelpBox("TriggerEnter needs a Collider with 'Is Trigger' on this object.", MessageType.Warning);
#endif
            }
            else if (emitter.PlayOn == SoundEmitterTrigger.TriggerEnter2D)
            {
#if kinatraa_fmodaudio_physics2d
                Collider2D c = emitter.GetComponent<Collider2D>();
                if (c == null || !c.isTrigger) EditorGUILayout.HelpBox("TriggerEnter2D needs a Collider2D with 'Is Trigger' on this object.", MessageType.Warning);
#endif
            }
            if (emitter.PlayOn == SoundEmitterTrigger.OnEnable)
            {
                EditorGUILayout.HelpBox("OnEnable runs before per-scene banks load for objects in a newly loaded scene; prefer Start for those.", MessageType.None);
            }

            if (!EditorApplication.isPlaying) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play")) emitter.Play();
                if (GUILayout.Button("Stop")) emitter.Stop();
                GUILayout.Label(emitter.Handle.IsValid ? emitter.Handle.State.ToString() : "-", GUILayout.Width(90));
            }
        }
    }

    /// <summary>Inspector for <see cref="SoundBankLoader"/>: validation and Play Mode load state.</summary>
    [CustomEditor(typeof(SoundBankLoader))]
    [CanEditMultipleObjects]
    internal sealed class SoundBankLoaderEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var loader = (SoundBankLoader)target;
            if (loader.Banks.Count == 0 || loader.Banks.Any(string.IsNullOrEmpty)) EditorGUILayout.HelpBox("Add bank names (empty entries are skipped).", MessageType.Warning);
            SoundSystemConfig config = EditorCues.FindResourcesConfig();
            if (config != null)
            {
                List<string> always = loader.Banks.Where(b => !string.IsNullOrEmpty(b) && config.Banks.Contains(b)).ToList();
                if (always.Count > 0) EditorGUILayout.HelpBox($"Already always loaded by the config: {string.Join(", ", always)}.", MessageType.Info);
            }
            if (loader.LoadOn == SoundBankLoadTiming.Manual && loader.UnloadOn != SoundBankUnloadTiming.Manual)
            {
                EditorGUILayout.HelpBox("Load is manual: call Load() from code (banks unload automatically as configured).", MessageType.None);
            }
            if (!EditorApplication.isPlaying) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(loader.IsLoaded ? "Loaded: " + string.Join(", ", loader.LoadedBanks) : "Not loaded", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(loader.IsLoaded ? "Unload" : "Load", GUILayout.Width(70)))
                {
                    if (loader.IsLoaded) loader.Unload();
                    else loader.Load();
                }
            }
        }
    }
}
