using System;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using UnityEditor;
using UnityEngine;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>
    /// Tools > kinatraa > FMOD Audio > Cue Library: searchable, filterable cue list with bulk edit, validation,
    /// drag-and-drop of FMOD events and per-cue preview through FMOD's editor audition system.
    /// </summary>
    public sealed class CueLibraryWindow : EditorWindow
    {
        private SoundCueLibrary library;
        private SerializedObject serialized;
        private string search = string.Empty;
        private string channelFilter = string.Empty;
        private string tagFilter = string.Empty;
        private readonly HashSet<SoundCue> selected = new HashSet<SoundCue>();
        private SoundCue expanded;
        private Vector2 scroll;

        private string bulkChannel = "SFX";
        private float bulkCooldown;
        private int bulkMax;
        private string bulkTag = string.Empty;

        private FMOD.Studio.EventInstance preview;
        private SoundCue previewCue;

        /// <summary>Opens the window.</summary>
        [MenuItem("Tools/kinatraa/FMOD Audio/Cue Library", priority = 20)]
        public static void Open() => Open(null);

        /// <summary>Opens the window on a library.</summary>
        public static void Open(SoundCueLibrary target)
        {
            CueLibraryWindow window = GetWindow<CueLibraryWindow>();
            window.titleContent = new GUIContent("Cue Library");
            window.minSize = new Vector2(640, 360);
            if (target != null) window.SetLibrary(target);
            else if (window.library == null) window.SetLibrary(EditorCues.FindResourcesConfig()?.Libraries.FirstOrDefault(l => l != null) ?? EditorCues.FindAllLibraries().FirstOrDefault());
            window.Show();
        }

        private void SetLibrary(SoundCueLibrary value)
        {
            StopPreview();
            library = value;
            serialized = value != null ? new SerializedObject(value) : null;
            selected.Clear();
            expanded = null;
        }

        private void OnDisable() => StopPreview();

        private void OnGUI()
        {
            DrawLibraryBar();
            if (library == null)
            {
                EditorGUILayout.HelpBox("Pick or create a SoundCueLibrary.", MessageType.Info);
                return;
            }
            if (serialized == null || serialized.targetObject != library) serialized = new SerializedObject(library);
            serialized.Update();

            DrawFilterBar();
            DrawDropArea();
            DrawValidation();
            if (selected.Count > 0) DrawBulkBar();
            DrawList();

            serialized.ApplyModifiedProperties();
            if (previewCue != null && (!preview.isValid() || preview.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state) != FMOD.RESULT.OK ||
                                       state == FMOD.Studio.PLAYBACK_STATE.STOPPED))
            {
                StopPreview();
            }
        }

        private void DrawLibraryBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var picked = (SoundCueLibrary)EditorGUILayout.ObjectField(library, typeof(SoundCueLibrary), false, GUILayout.Width(260));
                if (picked != library) SetLibrary(picked);
                if (GUILayout.Button("Libraries ▾", EditorStyles.toolbarDropDown, GUILayout.Width(90)))
                {
                    var menu = new GenericMenu();
                    foreach (SoundCueLibrary l in EditorCues.FindAllLibraries())
                    {
                        SoundCueLibrary captured = l;
                        menu.AddItem(new GUIContent(AssetDatabase.GetAssetPath(l).Replace('/', '∕')), l == library, () => SetLibrary(captured));
                    }
                    menu.ShowAsContext();
                }
                if (GUILayout.Button("New Library", EditorStyles.toolbarButton, GUILayout.Width(90))) CreateLibrary();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate SoundIds.cs", EditorStyles.toolbarButton)) SoundIdsGenerator.Generate();
            }
        }

        private void CreateLibrary()
        {
            string folder = AssetDatabase.IsValidFolder(EditorCues.AudioFolder) ? EditorCues.AudioFolder : "Assets";
            string path = EditorUtility.SaveFilePanelInProject("New Sound Cue Library", "SoundCueLibrary", "asset", "Create a cue library", folder);
            if (string.IsNullOrEmpty(path)) return;
            var created = CreateInstance<SoundCueLibrary>();
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            EditorCues.Invalidate();
            SetLibrary(created);
        }

        private void DrawFilterBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160));
                List<string> channels = new List<string> { "All channels" };
                channels.AddRange(EditorCues.ChannelNames());
                int ci = Mathf.Max(0, channels.IndexOf(channelFilter));
                ci = EditorGUILayout.Popup(ci, channels.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(110));
                channelFilter = ci == 0 ? string.Empty : channels[ci];
                List<string> tags = new List<string> { "All tags" };
                tags.AddRange(library.Cues.Where(c => c?.Tags != null).SelectMany(c => c.Tags).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t));
                int ti = Mathf.Max(0, tags.IndexOf(tagFilter));
                ti = EditorGUILayout.Popup(ti, tags.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(100));
                tagFilter = ti == 0 ? string.Empty : tags[ti];
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Cue", EditorStyles.toolbarButton, GUILayout.Width(50))) AddCue(new SoundCue { Id = UniqueId("new_cue") });
                if (GUILayout.Button("+ From FMOD event ▾", EditorStyles.toolbarDropDown, GUILayout.Width(140))) ShowEventMenu();
            }
        }

        private void ShowEventMenu()
        {
            var menu = new GenericMenu();
            List<EditorEventRef> events = EventManager.Events?.Where(e => e != null && e.Path.StartsWith("event:/", StringComparison.Ordinal)).OrderBy(e => e.Path).ToList();
            if (events == null || events.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No FMOD events (link the Studio project and build banks)"));
            }
            else
            {
                HashSet<FMOD.GUID> used = new HashSet<FMOD.GUID>(library.Cues.Where(c => c != null).Select(c => c.Event.Guid));
                foreach (EditorEventRef e in events)
                {
                    EditorEventRef captured = e;
                    string label = e.Path.Substring("event:/".Length);
                    if (used.Contains(e.Guid)) menu.AddDisabledItem(new GUIContent(label + "  (in library)"));
                    else menu.AddItem(new GUIContent(label), false, () => AddFromEvents(new[] { captured }));
                }
            }
            menu.ShowAsContext();
        }

        private void DrawDropArea()
        {
            Rect area = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            GUI.Box(area, "Drop FMOD events here (from the FMOD Event Browser) to create cues", EditorStyles.helpBox);
            Event e = Event.current;
            if (!area.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            List<EditorEventRef> refs = DragAndDrop.objectReferences.OfType<EditorEventRef>().ToList();
            if (refs.Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddFromEvents(refs);
            }
            e.Use();
        }

        private void DrawValidation()
        {
            HashSet<string> dupes = EditorCues.DuplicateIds(EditorCues.FindAllLibraries().Union(new[] { library }));
            int invalid = library.Cues.Count(c => c != null && Problem(c, dupes) != null);
            if (invalid > 0) EditorGUILayout.HelpBox($"{invalid} cue(s) need attention (marked in red).", MessageType.Warning);
        }

        private static string Problem(SoundCue cue, HashSet<string> dupes)
        {
            if (string.IsNullOrEmpty(cue.Id)) return "empty id";
            if (dupes.Contains(cue.Id)) return "duplicate id";
            if (cue.Event.IsNull) return "no event";
            if (EventManager.EventFromGUID(cue.Event.Guid) == null) return "event not found";
            return null;
        }

        private void DrawBulkBar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Bulk edit {selected.Count} selected cue(s)", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    List<string> channels = EditorCues.ChannelNames();
                    int ci = EditorGUILayout.Popup(Mathf.Max(0, channels.IndexOf(bulkChannel)), channels.ToArray(), GUILayout.Width(90));
                    bulkChannel = channels.Count > 0 ? channels[ci] : "SFX";
                    if (GUILayout.Button("Set channel", GUILayout.Width(90))) Bulk("Set channel", c => c.Channel = new SoundChannel(bulkChannel));
                    bulkCooldown = EditorGUILayout.FloatField(bulkCooldown, GUILayout.Width(40));
                    if (GUILayout.Button("Set cooldown", GUILayout.Width(95))) Bulk("Set cooldown", c => c.Cooldown = Mathf.Max(0, bulkCooldown));
                    bulkMax = EditorGUILayout.IntField(bulkMax, GUILayout.Width(30));
                    if (GUILayout.Button("Set max inst.", GUILayout.Width(90))) Bulk("Set max instances", c => c.MaxInstances = Mathf.Max(0, bulkMax));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    bulkTag = EditorGUILayout.TextField(bulkTag, GUILayout.Width(90));
                    if (GUILayout.Button("Add tag", GUILayout.Width(70)) && bulkTag.Length > 0)
                    {
                        Bulk("Add tag", c =>
                        {
                            if (!c.Tags.Contains(bulkTag)) c.Tags.Add(bulkTag);
                        });
                    }
                    if (GUILayout.Button("Remove tag", GUILayout.Width(80))) Bulk("Remove tag", c => c.Tags.Remove(bulkTag));
                    if (GUILayout.Button("Auto release on", GUILayout.Width(105))) Bulk("Auto release", c => c.AutoRelease = true);
                    if (GUILayout.Button("off", GUILayout.Width(35))) Bulk("Auto release", c => c.AutoRelease = false);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Duplicate", GUILayout.Width(75))) DuplicateSelected();
                    if (GUILayout.Button("Delete", GUILayout.Width(60))) DeleteSelected();
                }
            }
        }

        private void Bulk(string undo, Action<SoundCue> apply)
        {
            serialized.ApplyModifiedProperties();
            Undo.RecordObject(library, undo);
            foreach (SoundCue cue in selected) apply(cue);
            EditorUtility.SetDirty(library);
            serialized.Update();
        }

        private void DrawList()
        {
            HashSet<string> dupes = EditorCues.DuplicateIds(EditorCues.FindAllLibraries().Union(new[] { library }));
            SerializedProperty cues = serialized.FindProperty(nameof(SoundCueLibrary.Cues));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool all = library.Cues.Count > 0 && selected.Count == library.Cues.Count;
                bool toggled = GUILayout.Toggle(all, GUIContent.none, GUILayout.Width(16));
                if (toggled != all)
                {
                    selected.Clear();
                    if (toggled) selected.UnionWith(library.Cues.Where(c => c != null));
                }
                GUILayout.Label("Id", GUILayout.Width(170));
                GUILayout.Label("Event", GUILayout.MinWidth(180));
                GUILayout.Label("Channel", GUILayout.Width(70));
                GUILayout.Label("Status", GUILayout.Width(110));
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < cues.arraySize && i < library.Cues.Count; i++)
            {
                SoundCue cue = library.Cues[i];
                if (cue == null || !Matches(cue)) continue;
                SerializedProperty prop = cues.GetArrayElementAtIndex(i);
                string problem = Problem(cue, dupes);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isSelected = selected.Contains(cue);
                    if (GUILayout.Toggle(isSelected, GUIContent.none, GUILayout.Width(16)) != isSelected)
                    {
                        if (isSelected) selected.Remove(cue);
                        else selected.Add(cue);
                    }
                    EditorGUILayout.PropertyField(prop.FindPropertyRelative(nameof(SoundCue.Id)), GUIContent.none, GUILayout.Width(170));
                    string path = cue.Event.IsNull ? "(none)" : EventManager.EventFromGUID(cue.Event.Guid)?.Path ?? cue.Event.Path;
                    GUILayout.Label(new GUIContent(path, path), GUILayout.MinWidth(180));
                    GUILayout.Label(cue.Channel.Name, GUILayout.Width(70));
                    Color old = GUI.color;
                    if (problem != null) GUI.color = new Color(1f, 0.5f, 0.45f);
                    GUILayout.Label(problem ?? (cue.AutoRelease ? "ok" : "ok (held)"), GUILayout.Width(110));
                    GUI.color = old;
                    bool playing = previewCue == cue && preview.isValid();
                    using (new EditorGUI.DisabledScope(cue.Event.IsNull))
                    {
                        if (GUILayout.Button(playing ? "■" : "▶", EditorStyles.miniButton, GUILayout.Width(24)))
                        {
                            if (playing) StopPreview();
                            else StartPreview(cue);
                        }
                    }
                    if (GUILayout.Button(expanded == cue ? "▴" : "▾", EditorStyles.miniButton, GUILayout.Width(24))) expanded = expanded == cue ? null : cue;
                }
                if (expanded == cue)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        SerializedProperty end = prop.GetEndProperty();
                        SerializedProperty child = prop.Copy();
                        bool enter = true;
                        while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                        {
                            enter = false;
                            if (child.name == nameof(SoundCue.Id)) continue;
                            EditorGUILayout.PropertyField(child, true);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool Matches(SoundCue cue)
        {
            if (search.Length > 0)
            {
                string path = cue.Event.Path ?? string.Empty;
                if ((cue.Id ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            if (channelFilter.Length > 0 && cue.Channel.Name != channelFilter) return false;
            if (tagFilter.Length > 0 && (cue.Tags == null || !cue.Tags.Contains(tagFilter))) return false;
            return true;
        }

        private string UniqueId(string baseId)
        {
            var existing = new HashSet<string>(EditorCues.FindAllLibraries().Union(new[] { library }).SelectMany(l => l.Cues).Where(c => c != null).Select(c => c.Id));
            if (!existing.Contains(baseId)) return baseId;
            for (int n = 2; ; n++)
            {
                string id = $"{baseId}_{n}";
                if (!existing.Contains(id)) return id;
            }
        }

        private void AddCue(SoundCue cue)
        {
            serialized.ApplyModifiedProperties();
            Undo.RecordObject(library, "Add cue");
            library.Cues.Add(cue);
            EditorUtility.SetDirty(library);
            serialized.Update();
            EditorCues.Invalidate();
        }

        /// <summary>Creates one cue per event: id = snake_case event name (folder-prefixed / numbered on collision).</summary>
        internal void AddFromEvents(IEnumerable<EditorEventRef> events)
        {
            serialized.ApplyModifiedProperties();
            Undo.RecordObject(library, "Add cues from FMOD events");
            List<string> channels = EditorCues.ChannelNames();
            foreach (EditorEventRef e in events)
            {
                if (library.Cues.Any(c => c != null && c.Event.Guid == e.Guid)) continue;
                library.Cues.Add(CreateCue(e, channels, id => UniqueIdIncluding(id)));
            }
            EditorUtility.SetDirty(library);
            serialized.Update();
            EditorCues.Invalidate();
        }

        private string UniqueIdIncluding(string candidate)
        {
            var existing = new HashSet<string>(EditorCues.FindAllLibraries().Union(new[] { library }).SelectMany(l => l.Cues).Where(c => c != null).Select(c => c.Id));
            return existing.Contains(candidate) ? null : candidate;
        }

        /// <summary>Builds a cue from an FMOD event. <paramref name="accept"/> returns the id if free, null otherwise.</summary>
        internal static SoundCue CreateCue(EditorEventRef e, List<string> channels, Func<string, string> accept)
        {
            string[] segments = e.Path.Substring("event:/".Length).Split('/');
            string name = EditorCues.ToSnakeCase(segments[segments.Length - 1]);
            string folder = segments.Length > 1 ? EditorCues.ToSnakeCase(segments[segments.Length - 2]) : string.Empty;
            string id = accept(name) ?? (folder.Length > 0 ? accept(folder + "_" + name) : null);
            for (int n = 2; id == null; n++) id = accept($"{name}_{n}");
            return new SoundCue
            {
                Id = id,
                Event = new EventReference { Guid = e.Guid, Path = e.Path },
                Channel = new SoundChannel(EditorCues.GuessChannel(e.Path, channels)),
                AutoRelease = e.IsOneShot,
            };
        }

        private void DuplicateSelected()
        {
            serialized.ApplyModifiedProperties();
            Undo.RecordObject(library, "Duplicate cues");
            foreach (SoundCue cue in selected.ToList())
            {
                SoundCue copy = JsonUtility.FromJson<SoundCue>(JsonUtility.ToJson(cue));
                copy.Event = cue.Event;
                copy.Id = UniqueId(cue.Id + "_copy");
                library.Cues.Add(copy);
            }
            EditorUtility.SetDirty(library);
            serialized.Update();
            EditorCues.Invalidate();
        }

        private void DeleteSelected()
        {
            if (!EditorUtility.DisplayDialog("Delete cues", $"Delete {selected.Count} cue(s)? (Undo is available)", "Delete", "Cancel")) return;
            serialized.ApplyModifiedProperties();
            Undo.RecordObject(library, "Delete cues");
            library.Cues.RemoveAll(c => selected.Contains(c));
            selected.Clear();
            EditorUtility.SetDirty(library);
            serialized.Update();
            EditorCues.Invalidate();
        }

        private void StartPreview(SoundCue cue)
        {
            StopPreview();
            EditorEventRef eventRef = EventManager.EventFromGUID(cue.Event.Guid);
            if (eventRef == null)
            {
                ShowNotification(new GUIContent("Event not in FMOD's cache"));
                return;
            }
            try
            {
                EditorUtils.LoadPreviewBanks();
                var parameters = new Dictionary<string, float>();
                foreach (SoundParameterValue p in cue.DefaultParameters)
                {
                    if (!string.IsNullOrEmpty(p.Name)) parameters[p.Name] = p.Value;
                }
                preview = EditorUtils.PreviewEvent(eventRef, parameters);
                previewCue = cue;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[kinatraa.fmodaudio] Preview of '{cue.Id}' failed: {ex.Message}");
            }
        }

        private void StopPreview()
        {
            if (preview.isValid()) EditorUtils.PreviewStop(preview);
            preview.clearHandle();
            previewCue = null;
        }

        private void OnInspectorUpdate()
        {
            if (previewCue != null) Repaint();
        }
    }
}
