using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace kinatraa.FmodAudio.Editor
{
    /// <summary>
    /// Tools > kinatraa > FMOD Audio > Runtime Debugger (Play Mode): active handles with parameters, FMOD instance
    /// counts, bus volumes and mutes, banks, music, snapshots and global parameters with live editing.
    /// </summary>
    public sealed class RuntimeDebuggerWindow : EditorWindow
    {
        private readonly List<Sound.DebugSound> sounds = new List<Sound.DebugSound>();
        private readonly List<KeyValuePair<string, float>> parameters = new List<KeyValuePair<string, float>>();
        private readonly List<SoundChannel> channels = new List<SoundChannel>();
        private readonly List<string> banks = new List<string>();
        private readonly List<KeyValuePair<string, int>> bankRefs = new List<KeyValuePair<string, int>>();
        private readonly List<string> snapshots = new List<string>();
        private readonly List<FmodBackend.GlobalParameterInfo> globals = new List<FmodBackend.GlobalParameterInfo>();
        private readonly List<string> cueIds = new List<string>();
        private readonly HashSet<SoundHandle> expanded = new HashSet<SoundHandle>();
        private readonly List<SnapshotHandle> pushedSnapshots = new List<SnapshotHandle>();
        private Vector2 scroll;
        private string playCue = string.Empty;
        private string filter = string.Empty;
        private int snapshotKey;

        /// <summary>Opens the window.</summary>
        [MenuItem(EditorUi.MenuRoot + "Runtime Debugger", priority = 21)]
        public static void Open()
        {
            RuntimeDebuggerWindow w = GetWindow<RuntimeDebuggerWindow>();
            w.titleContent = new GUIContent(EditorUi.DebuggerTitle);
            w.minSize = new Vector2(520, 300);
            w.Show();
        }

        private void OnEnable() => titleContent = new GUIContent(EditorUi.DebuggerTitle);

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                pushedSnapshots.Clear();
                EditorUi.Header("Runtime Debugger", "Live view of the sound system: sounds, music, buses, parameters, snapshots and banks.");
                EditorGUILayout.HelpBox("Enter Play Mode to inspect the sound system.", MessageType.Info);
                return;
            }
            if (!Sound.IsInitialized)
            {
                EditorGUILayout.HelpBox("Sound is not initialized (AutoInitialize off, or FMOD failed; see Console).", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(Sound.IsPaused ? "Resume all" : "Pause all", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    if (Sound.IsPaused) Sound.ResumeAll();
                    else Sound.PauseAll();
                }
                if (GUILayout.Button("Stop all", EditorStyles.toolbarButton, GUILayout.Width(60))) Sound.StopAll();
                GUILayout.FlexibleSpace();
                Sound.DebugGetCueIds(cueIds);
                if (cueIds.Count > 0)
                {
                    int i = Mathf.Max(0, cueIds.IndexOf(playCue));
                    i = EditorGUILayout.Popup(i, cueIds.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(160));
                    playCue = cueIds[i];
                    if (GUILayout.Button("Play cue", EditorStyles.toolbarButton, GUILayout.Width(60))) Sound.Play(playCue);
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField($"FMOD {Sound.FmodVersion}   ready: {Sound.IsReady}   paused: {Sound.IsPaused}   tracked: {Sound.ActiveSoundCount} (queued {Sound.QueuedSoundCount})",
                EditorStyles.miniBoldLabel);

            Section("Music");
            string music = Sound.CurrentMusic;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(music == null ? "(none)" : $"{music} - {Sound.MusicHandle.State}{(Sound.MusicHandle.IsPaused ? " (paused)" : string.Empty)}");
                using (new EditorGUI.DisabledScope(music == null))
                {
                    if (GUILayout.Button("Pause", GUILayout.Width(60))) Sound.PauseMusic();
                    if (GUILayout.Button("Resume", GUILayout.Width(60))) Sound.ResumeMusic();
                    if (GUILayout.Button("Stop", GUILayout.Width(60))) Sound.StopMusic();
                }
            }

            Section("Active sounds");
            Sound.DebugGetSounds(sounds);
            using (new EditorGUILayout.HorizontalScope())
            {
                filter = EditorGUILayout.TextField(filter, EditorStyles.toolbarSearchField);
                GUILayout.Label($"{sounds.Count} sound(s)", EditorStyles.miniLabel, GUILayout.Width(80));
            }
            if (sounds.Count == 0) EditorGUILayout.LabelField("(none)");
            foreach (Sound.DebugSound s in sounds)
            {
                if (filter.Length > 0 && (s.CueId ?? string.Empty).IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    (s.EventPath ?? string.Empty).IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    s.Channel.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool open = expanded.Contains(s.Handle);
                    bool now = EditorGUILayout.Foldout(open, $"{s.CueId ?? "(event)"}  {s.EventPath}", true);
                    if (now != open)
                    {
                        if (now) expanded.Add(s.Handle);
                        else expanded.Remove(s.Handle);
                    }
                    GUILayout.Label($"{s.Channel.Name} | {s.State}{(s.Paused ? " paused" : string.Empty)}{(s.Attached ? " attached" : string.Empty)}{(s.Held ? " held" : string.Empty)}",
                        GUILayout.Width(260));
                    if (s.CueId != null) GUILayout.Label($"inst {Sound.GetEventInstanceCount(s.CueId)}", GUILayout.Width(50));
                    if (GUILayout.Button(s.Paused ? "▶" : "❚❚", EditorStyles.miniButton, GUILayout.Width(26)))
                    {
                        if (s.Paused) s.Handle.Resume();
                        else s.Handle.Pause();
                    }
                    if (GUILayout.Button("■", EditorStyles.miniButton, GUILayout.Width(22))) s.Handle.Stop();
                }
                if (!expanded.Contains(s.Handle)) continue;
                Sound.DebugGetParameters(s.Handle, parameters);
                EditorGUI.indentLevel++;
                if (parameters.Count == 0) EditorGUILayout.LabelField("(no local parameters)");
                foreach (KeyValuePair<string, float> p in parameters)
                {
                    float v = EditorGUILayout.FloatField(p.Key, p.Value);
                    if (!Mathf.Approximately(v, p.Value)) s.Handle.SetParameter(p.Key, v);
                }
                EditorGUI.indentLevel--;
            }

            Section("Channels (buses / VCAs)");
            Sound.GetChannels(channels);
            foreach (SoundChannel c in channels)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string path = Sound.DebugGetChannelPath(c) ?? string.Empty;
                    float v = EditorGUILayout.Slider(new GUIContent(c.Name, path), Sound.GetBusVolume(c), 0f, 1f);
                    if (!Mathf.Approximately(v, Sound.GetBusVolume(c))) Sound.SetBusVolume(c, v);
                    bool muted = GUILayout.Toggle(Sound.IsBusMuted(c), "mute", GUILayout.Width(50));
                    if (muted != Sound.IsBusMuted(c)) Sound.SetBusMuted(c, muted);
                    if (GUILayout.Button(new GUIContent("Stop", "Sound.StopChannel"), EditorStyles.miniButton, GUILayout.Width(40))) Sound.StopChannel(c);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Reset volumes", "Sound.ResetVolumes(): configured defaults, unmuted, saved"), EditorStyles.miniButton, GUILayout.Width(100))) Sound.ResetVolumes();
                if (GUILayout.Button(new GUIContent("Save settings", "Sound.SaveSettings(): flush the settings store to disk"), EditorStyles.miniButton, GUILayout.Width(100))) Sound.SaveSettings();
            }

            Section("Global parameters");
            Sound.DebugGetGlobalParameters(globals);
            if (globals.Count == 0) EditorGUILayout.LabelField("(none)");
            foreach (FmodBackend.GlobalParameterInfo g in globals)
            {
                float v = EditorGUILayout.Slider(g.Name, g.Value, g.Min, g.Max);
                if (!Mathf.Approximately(v, g.Value)) Sound.SetGlobalParameter(g.Name, v);
            }

            Section("Snapshots");
            Sound.DebugGetSnapshots(snapshots);
            EditorGUILayout.LabelField("Active: " + (snapshots.Count == 0 ? "(none)" : string.Join(", ", snapshots)), EditorStyles.wordWrappedMiniLabel);
            if (Sound.Config != null && Sound.Config.Snapshots.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string[] keys = new string[Sound.Config.Snapshots.Count];
                    for (int k = 0; k < keys.Length; k++) keys[k] = Sound.Config.Snapshots[k]?.Key ?? "(empty)";
                    snapshotKey = Mathf.Clamp(snapshotKey, 0, keys.Length - 1);
                    snapshotKey = EditorGUILayout.Popup(snapshotKey, keys, GUILayout.Width(140));
                    if (GUILayout.Button("Push", EditorStyles.miniButton, GUILayout.Width(50)))
                    {
                        SnapshotHandle h = Sound.PushSnapshot(keys[snapshotKey]);
                        if (h.IsValid) pushedSnapshots.Add(h);
                    }
                    pushedSnapshots.RemoveAll(h => !h.IsValid);
                    using (new EditorGUI.DisabledScope(pushedSnapshots.Count == 0))
                    {
                        if (GUILayout.Button($"Release pushed ({pushedSnapshots.Count})", EditorStyles.miniButton, GUILayout.Width(130)))
                        {
                            foreach (SnapshotHandle h in pushedSnapshots) h.Release();
                            pushedSnapshots.Clear();
                        }
                    }
                }
            }

            Section("Banks");
            Sound.DebugGetBankRefs(bankRefs);
            foreach (KeyValuePair<string, int> b in bankRefs) EditorGUILayout.LabelField($"{b.Key}  (refs via Sound: {b.Value})");
            Sound.DebugGetLoadedBanks(banks);
            EditorGUILayout.LabelField("Loaded in FMOD: " + (banks.Count == 0 ? "(none)" : string.Join(", ", banks)), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
