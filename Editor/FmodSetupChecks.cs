using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FMODUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>Registers the FMOD-dependent setup checks with the bootstrap wizard.</summary>
    [InitializeOnLoad]
    internal static class FmodSetupChecks
    {
        static FmodSetupChecks()
        {
            SetupCheckRegistry.StudioProjectFolderProvider = () =>
            {
                string folder = FmodProject.StudioProjectFolder;
                return folder == null ? null : FmodProject.ToProjectRelative(folder);
            };
            SetupCheckRegistry.RegisterFmodChecks(
                new FmodSettingsCheck(),
                new StudioConventionCheck(),
                new ListenerCheck(),
                new BanksCheck(),
                new ConfigCheck(),
                new PlatformSanityCheck(),
                new SmokeTestCheck());
        }
    }

    /// <summary>3. FMOD settings asset exists and points at the FMOD Studio project.</summary>
    internal sealed class FmodSettingsCheck : SetupCheck
    {
        private string chosen;

        public override int Order => 30;
        public override string Title => "FMOD settings linked to the Studio project";

        public override SetupCheckResult Evaluate()
        {
            Settings settings = FmodProject.LoadSettings();
            List<string> found = FmodProject.FindStudioProjects();
            string candidates = found.Count == 0
                ? " No .fspro was found next to Assets/. Create the project in FMOD Studio 2.03 (e.g. in FMODProject/) or use Browse."
                : found.Count == 1 ? $" Detected: {FmodProject.ToProjectRelative(found[0])}." : $" {found.Count} .fspro files detected; pick one under Details.";
            if (settings == null) return SetupCheckResult.Missing("FMOD settings asset (Resources/FMODStudioSettings) does not exist." + candidates);
            if (!settings.HasSourceProject || string.IsNullOrEmpty(settings.SourceProjectPath))
            {
                return SetupCheckResult.Missing("FMOD settings are not linked to an FMOD Studio project (Source: Studio project)." + candidates);
            }
            string fspro = FmodProject.StudioProjectPath;
            if (!File.Exists(fspro)) return SetupCheckResult.Missing($"Configured Studio project '{settings.SourceProjectPath}' does not exist." + candidates);
            return SetupCheckResult.Ok($"Linked to {settings.SourceProjectPath} (import type: {settings.ImportType}).");
        }

        private string Target()
        {
            if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen)) return chosen;
            List<string> found = FmodProject.FindStudioProjects();
            return found.Count == 1 ? found[0] : null;
        }

        public override bool CanFix(SetupCheckResult result) => result.Status != SetupStatus.OK && (FmodProject.LoadSettings() == null || Target() != null);
        public override string FixLabel => FmodProject.LoadSettings() == null && Target() == null ? "Create settings" : "Link project";

        public override void Fix()
        {
            Settings settings = FmodProject.LoadSettings();
            if (settings == null)
            {
                settings = Settings.Instance; // FMOD creates the asset (with its platforms) when it is missing
                LogChange("Created FMOD settings asset via FMOD for Unity (Resources/FMODStudioSettings).");
            }
            string target = Target();
            if (target == null || settings == null) return;
            if (settings.HasSourceProject && FmodProject.StudioProjectPath == Path.GetFullPath(target)) return;
            FmodProject.SetStudioProject(settings, target);
            LogChange($"FMOD settings: Source = FMOD Studio project, path = {settings.SourceProjectPath}, bank folder = {settings.SourceBankPath}.");
        }

        public override void DrawGUI()
        {
            List<string> found = FmodProject.FindStudioProjects();
            foreach (string path in found)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isChosen = Path.GetFullPath(path) == Path.GetFullPath(Target() ?? string.Empty);
                    if (GUILayout.Toggle(isChosen, FmodProject.ToProjectRelative(path)) && !isChosen) chosen = path;
                }
            }
            if (GUILayout.Button("Browse for .fspro...", GUILayout.Width(160)))
            {
                string picked = EditorUtility.OpenFilePanel("Locate FMOD Studio project", FmodProject.UnityProjectRoot, "fspro");
                if (!string.IsNullOrEmpty(picked)) chosen = picked;
            }
            if (!string.IsNullOrEmpty(chosen)) EditorGUILayout.LabelField("Will link: " + FmodProject.ToProjectRelative(chosen), EditorStyles.miniLabel);
        }
    }

    /// <summary>4. Studio project follows the bus / snapshot convention (read-only).</summary>
    internal sealed class StudioConventionCheck : SetupCheck
    {
        public override int Order => 40;
        public override string Title => "Studio project conventions (buses, snapshots)";

        internal static List<string> RequiredBuses(SoundSystemConfig config)
        {
            List<SoundChannelDefinition> defs = config != null ? config.Channels : SoundSystemConfig.CreateDefaultChannels();
            return defs.Where(d => d != null && !string.IsNullOrEmpty(d.Name))
                .Select(d => string.IsNullOrEmpty(d.BusPath) ? "bus:/" + d.Name : d.BusPath)
                .Where(p => p != "bus:/").Distinct().ToList();
        }

        internal static List<string> OptionalSnapshots(SoundSystemConfig config)
        {
            List<SnapshotMapping> maps = config != null ? config.Snapshots : SoundSystemConfig.CreateDefault().Snapshots;
            return maps.Where(m => m != null && !string.IsNullOrEmpty(m.Path)).Select(m => m.Path).Distinct().ToList();
        }

        public override SetupCheckResult Evaluate()
        {
            string fspro = FmodProject.StudioProjectPath;
            if (fspro == null || !File.Exists(fspro)) return SetupCheckResult.Warn("Link the Studio project first (item 3).");
            StudioProjectInfo info = StudioProjectInfo.Scan(fspro);
            if (info.Error != null) return SetupCheckResult.Warn($"Could not read the Studio project metadata: {info.Error}");

            SoundSystemConfig config = EditorCues.FindResourcesConfig();
            List<string> missingBuses = RequiredBuses(config).Where(b => !info.BusPaths.Contains(b)).ToList();
            List<string> missingSnapshots = OptionalSnapshots(config)
                .Where(s => !info.SnapshotPaths.Contains(s) && !info.SnapshotNames.Contains(s.Substring(s.LastIndexOf('/') + 1))).ToList();

            var sb = new StringBuilder();
            if (!info.IsStudio203)
            {
                sb.Append($"The Studio project was saved as '{info.SerializationModel ?? "unknown"}', not by an FMOD Studio 2.03.x. Banks must be built with FMOD Studio 2.03 to match FMOD for Unity 2.03.15. ");
            }
            foreach (string bus in missingBuses)
            {
                sb.Append($"Missing bus '{bus}': in FMOD Studio open Window > Mixer, right-click 'Master Bus' > New Group, name it '{bus.Substring(5)}', " +
                          "route the matching events to it (event's master track output), then File > Save and File > Build. ");
            }
            foreach (string snap in missingSnapshots)
            {
                sb.Append($"Optional snapshot '{snap}' not found: in the Mixer's Snapshots tab right-click > New Overriding Snapshot, name it " +
                          $"'{snap.Substring(snap.LastIndexOf('/') + 1)}', assign it to the Master bank, then save and build. ");
            }
            if (sb.Length == 0) return SetupCheckResult.Ok($"Studio {info.SerializationModel}; all {RequiredBuses(config).Count} buses and {OptionalSnapshots(config).Count} snapshots found.");
            sb.Append("The bus list comes from the config's channels (editable under Details).");
            return missingBuses.Count > 0 ? SetupCheckResult.Missing(sb.ToString()) : SetupCheckResult.Warn(sb.ToString());
        }

        public override void DrawGUI()
        {
            SoundSystemConfig config = EditorCues.FindResourcesConfig();
            if (config == null)
            {
                EditorGUILayout.LabelField("Default buses are checked. Create the config (item 8) to define your own channels/buses.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            var so = new SerializedObject(config);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SoundSystemConfig.Channels)), new GUIContent("Channels / buses (config)"), true);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(SoundSystemConfig.Snapshots)), new GUIContent("Snapshots (config)"), true);
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>6. Exactly one StudioListener on the main camera of the open scenes; no Unity AudioListener.</summary>
    internal sealed class ListenerCheck : SetupCheck
    {
        private const string RemoveKey = "kinatraa.fmodaudio.wizard.removeAudioListener";

        public override int Order => 60;
        public override string Title => "Listener on the main camera";

        private static bool RemoveAudioListener
        {
            get => SessionState.GetBool(RemoveKey, true);
            set => SessionState.SetBool(RemoveKey, value);
        }

        private static List<T> InOpenScenes<T>() where T : Component
        {
            var list = new List<T>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<T>(true));
            }
            return list;
        }

        private static Camera MainCamera() => InOpenScenes<Camera>().FirstOrDefault(c => c.CompareTag("MainCamera"));

        public override SetupCheckResult Evaluate()
        {
            Camera main = MainCamera();
            List<StudioListener> fmod = InOpenScenes<StudioListener>();
#if kinatraa_fmodaudio_unity_audio
            List<AudioListener> unity = InOpenScenes<AudioListener>().Where(l => l.enabled).ToList();
#else
            var unity = new List<Component>();
#endif
            if (SceneManager.sceneCount == 0 || (main == null && fmod.Count == 0))
            {
                return SetupCheckResult.Warn("No camera tagged MainCamera in the open scene(s). Open your main scene and re-scan, or add a SoundListenerGuard to your camera prefab.");
            }
            if (fmod.Count == 0) return SetupCheckResult.Missing($"No FMOD StudioListener in the open scene(s). Fix adds one to '{main.name}'.");
            if (fmod.Count > 1) return SetupCheckResult.Warn($"{fmod.Count} StudioListeners in the open scenes. Keep one unless you need split-screen listeners.");
            if (unity.Count > 0) return SetupCheckResult.Warn($"Unity AudioListener enabled on '{unity[0].name}'. With FMOD it is not needed (Fix removes it when the option under Details is on).");
            return SetupCheckResult.Ok($"One StudioListener on '{fmod[0].name}'.");
        }

        public override bool CanFix(SetupCheckResult result)
        {
            if (result.Status == SetupStatus.Missing) return MainCamera() != null;
            return result.Status == SetupStatus.Warning && RemoveAudioListener && InOpenScenes<StudioListener>().Count == 1 && HasUnityListener();
        }

        private static bool HasUnityListener()
        {
#if kinatraa_fmodaudio_unity_audio
            return InOpenScenes<AudioListener>().Any(l => l.enabled);
#else
            return false;
#endif
        }

        public override void Fix()
        {
            Camera main = MainCamera();
            if (InOpenScenes<StudioListener>().Count == 0 && main != null)
            {
                Undo.AddComponent<StudioListener>(main.gameObject);
                EditorSceneManager.MarkSceneDirty(main.gameObject.scene);
                LogChange($"Added StudioListener to '{main.name}' in scene '{main.gameObject.scene.name}' (Undo available; save the scene).");
            }
#if kinatraa_fmodaudio_unity_audio
            if (!RemoveAudioListener) return;
            foreach (AudioListener listener in InOpenScenes<AudioListener>())
            {
                Scene scene = listener.gameObject.scene;
                string owner = listener.name;
                Undo.DestroyObjectImmediate(listener);
                EditorSceneManager.MarkSceneDirty(scene);
                LogChange($"Removed Unity AudioListener from '{owner}' in scene '{scene.name}' (Undo available; save the scene).");
            }
#endif
        }

        public override void DrawGUI()
        {
            RemoveAudioListener = EditorGUILayout.ToggleLeft("Fix also removes Unity AudioListeners (with Undo)", RemoveAudioListener);
        }
    }

    /// <summary>7. Banks built, Master + Master.strings present, not stale, FMOD bank cache fresh.</summary>
    internal sealed class BanksCheck : SetupCheck
    {
        public override int Order => 70;
        public override string Title => "Banks built and up to date";

        private static List<string> MasterNames(Settings settings) => settings.MasterBanks != null && settings.MasterBanks.Count > 0 ? settings.MasterBanks : new List<string> { "Master" };

        public override SetupCheckResult Evaluate()
        {
            Settings settings = FmodProject.LoadSettings();
            if (settings == null) return SetupCheckResult.Missing("FMOD settings missing (item 3).");
            EditorUtils.ValidateSource(out bool valid, out string reason);
            if (!valid) return SetupCheckResult.Missing(reason + " Build the project in FMOD Studio 2.03 (File > Build).");

            string folder = FmodProject.EditorPlatformBuildFolder;
            if (folder == null || !Directory.Exists(folder)) return SetupCheckResult.Missing($"Bank folder '{folder}' does not exist. Build the project in FMOD Studio (File > Build).");
            string[] files = Directory.GetFiles(folder, "*.bank");
            var names = new HashSet<string>(files.Select(Path.GetFileNameWithoutExtension), StringComparer.Ordinal);

            var missing = new List<string>();
            foreach (string master in MasterNames(settings))
            {
                if (!names.Contains(master)) missing.Add(master);
                if (!names.Contains(master + ".strings")) missing.Add(master + ".strings");
            }
            if (missing.Count > 0) return SetupCheckResult.Missing($"Missing {string.Join(", ", missing)} in {FmodProject.ToProjectRelative(folder)}. Build in FMOD Studio.");

            var warnings = new List<string>();
            SoundSystemConfig config = EditorCues.FindResourcesConfig();
            if (config != null)
            {
                IEnumerable<string> wanted = config.Banks.Concat(config.SceneBanks.Where(s => s != null).SelectMany(s => s.Banks));
                List<string> unknown = wanted.Where(b => !string.IsNullOrEmpty(b) && !names.Contains(b)).Distinct().ToList();
                if (unknown.Count > 0) warnings.Add($"Config references banks that were not built: {string.Join(", ", unknown)}.");
                if (config.SceneBanks.Any(s => s != null && s.Banks.Count > 0) && settings.BankLoadType == BankLoadType.All)
                {
                    warnings.Add("FMOD Settings > Initialization > Load Banks is 'All', so per-scene banks are always loaded and never unload. " +
                                 "Set it to 'None' (the config loads Master/Master.strings) or 'Specified'.");
                }
            }

            DateTime newestBank = files.Max(File.GetLastWriteTimeUtc);
            string fspro = FmodProject.StudioProjectPath;
            if (fspro != null && File.Exists(fspro))
            {
                StudioProjectInfo info = StudioProjectInfo.Scan(fspro);
                if (info.NewestMetadataUtc > newestBank.AddSeconds(2)) warnings.Add("Banks are older than the last Studio project save; rebuild in FMOD Studio (File > Build).");
            }
            if (StagingSystem.SourceLibsExist)
            {
                warnings.Add("FMOD for Unity has not finished its post-import native library update (Assets/Plugins/FMOD/staging). " +
                             "Let the editor run (or restart Unity) and follow FMOD > Setup Wizard until it completes.");
            }
            else if (CacheIsStale(files))
            {
                warnings.Add("FMOD for Unity's event cache is older than the banks; Fix refreshes it.");
            }

            string target = settings.ImportType == ImportType.StreamingAssets
                ? $"copied to StreamingAssets/{settings.TargetBankFolder} at build time"
                : $"imported as assets into Assets/{settings.TargetAssetPath}";
            string summary = $"{files.Length} bank(s) in {FmodProject.ToProjectRelative(folder)}; {target}.";
            return warnings.Count == 0 ? SetupCheckResult.Ok(summary) : SetupCheckResult.Warn(string.Join(" ", warnings) + " " + summary);
        }

        public override bool CanFix(SetupCheckResult result)
        {
            string folder = FmodProject.EditorPlatformBuildFolder;
            if (result.Status != SetupStatus.Warning || folder == null || !Directory.Exists(folder)) return false;
            return !StagingSystem.SourceLibsExist && CacheIsStale(Directory.GetFiles(folder, "*.bank"));
        }

        // FMOD stores the cache time as the newest bank's local write time (File.GetLastWriteTime), so compare in local time.
        // The cache asset is read directly: EventManager only loads it lazily (and never at startup in batch mode).
        private static bool CacheIsStale(string[] bankFiles)
        {
            if (bankFiles.Length == 0) return false;
            var cache = AssetDatabase.LoadAssetAtPath<EventCache>(EventManager.CacheAssetFullName);
            DateTime cacheTime = cache != null && cache.cacheVersion == FMOD.VERSION.number ? cache.CacheTime : DateTime.MinValue;
            return cacheTime < bankFiles.Max(File.GetLastWriteTime);
        }

        public override string FixLabel => "Refresh FMOD cache";

        public override void Fix()
        {
            EventManager.RefreshBanks();
            LogChange("Refreshed FMOD for Unity's bank/event cache (EventManager.RefreshBanks).");
        }
    }

    /// <summary>8. Resources/kinatraaFmodAudioConfig.asset with default channels, snapshots and an empty cue library.</summary>
    internal sealed class ConfigCheck : SetupCheck
    {
        internal const string LibraryPath = EditorCues.AudioFolder + "/SoundCues.asset";

        public override int Order => 80;
        public override string Title => "kinatraa config asset";

        public override SetupCheckResult Evaluate()
        {
            SoundSystemConfig config = EditorCues.FindResourcesConfig();
            if (config == null) return SetupCheckResult.Missing($"No {SoundSystemConfig.ResourcesName} in a Resources folder. The runtime falls back to defaults and warns. Fix creates {EditorCues.ConfigAssetPath}.");
            var issues = new List<string>();
            if (config.Channels.Count == 0) issues.Add("no channels defined");
            if (config.Libraries.Count == 0 || config.Libraries.Any(l => l == null)) issues.Add("no (or empty) cue library entries");
            if (!config.Banks.Contains("Master") || !config.Banks.Contains("Master.strings")) issues.Add("always-loaded banks do not include Master and Master.strings");
            string where = AssetDatabase.GetAssetPath(config);
            return issues.Count == 0 ? SetupCheckResult.Ok($"{where}: {config.Channels.Count} channels, {config.Libraries.Count} library(ies).")
                : SetupCheckResult.Warn($"{where}: {string.Join("; ", issues)}.");
        }

        public override bool CanFix(SetupCheckResult result) => result.Status == SetupStatus.Missing;
        public override string FixLabel => "Create config";

        public override void Fix()
        {
            if (EditorCues.FindResourcesConfig() != null) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(EditorCues.AudioFolder)) AssetDatabase.CreateFolder("Assets", "Audio");

            SoundCueLibrary library = AssetDatabase.LoadAssetAtPath<SoundCueLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SoundCueLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
                LogChange($"Created empty cue library {LibraryPath}.");
            }
            SoundSystemConfig config = ScriptableObject.CreateInstance<SoundSystemConfig>();
            config.Libraries.Add(library);
            AssetDatabase.CreateAsset(config, EditorCues.ConfigAssetPath);
            AssetDatabase.SaveAssets();
            EditorCues.Invalidate();
            LogChange($"Created {EditorCues.ConfigAssetPath} (channels Master/Music/SFX/UI/Voice/Ambient mapped to bus:/<Name>, snapshots Pause/Ducking).");
        }
    }

    /// <summary>11. Read-only platform sanity: speaker mode, sample rate, voices, live update.</summary>
    internal sealed class PlatformSanityCheck : SetupCheck
    {
        private static readonly int[] CommonRates = { 0, 22050, 24000, 32000, 44100, 48000 };

        public override int Order => 110;
        public override string Title => "Platform sanity (read-only)";

        internal static string NameOf(Platform platform)
        {
            PropertyInfo display = typeof(Platform).GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return display?.GetValue(platform) as string ?? platform.GetType().Name;
        }

        public override SetupCheckResult Evaluate()
        {
            Settings settings = FmodProject.LoadSettings();
            if (settings == null) return SetupCheckResult.Warn("FMOD settings missing (item 3).");
            var warnings = new List<string>();
            var lines = new List<string>();
            IEnumerable<Platform> platforms = settings.Platforms.Where(p => p != null && !(p is PlatformGroup));
            foreach (Platform p in platforms.Distinct())
            {
                lines.Add($"{NameOf(p)}: {p.SpeakerMode}, {(p.SampleRate == 0 ? "default rate" : p.SampleRate + " Hz")}, {p.RealChannelCount} real / {p.VirtualChannelCount} virtual voices, live update {p.LiveUpdate}");
                if (p.RealChannelCount > p.VirtualChannelCount) warnings.Add($"{NameOf(p)}: more real than virtual voices.");
                if (p.RealChannelCount > 256) warnings.Add($"{NameOf(p)}: real voices above 256 are clamped by FMOD for Unity.");
                if (!CommonRates.Contains(p.SampleRate)) warnings.Add($"{NameOf(p)}: unusual sample rate {p.SampleRate} Hz.");
            }
            Platform editor = settings.PlayInEditorPlatform;
            if (editor != null && editor.LiveUpdate == TriStateBool.Disabled) warnings.Add("Live Update is disabled in the editor; enable it to connect FMOD Studio while in Play Mode.");
            if (settings.DefaultPlatform != null && settings.DefaultPlatform.LiveUpdate == TriStateBool.Enabled)
            {
                warnings.Add("Live Update is enabled for all builds (including release); 'Development' is safer.");
            }
            string detail = string.Join(" | ", lines);
            return warnings.Count == 0 ? SetupCheckResult.Ok(detail) : SetupCheckResult.Warn(string.Join(" ", warnings) + " Details: " + detail);
        }
    }

    /// <summary>12. Play Mode smoke test: initialize, play a cue, report.</summary>
    internal sealed class SmokeTestCheck : SetupCheck
    {
        private const string CueKey = "kinatraa.fmodaudio.wizard.smokeCue";
        private string lastResult;

        public override int Order => 120;
        public override string Title => "Runtime smoke test (Play Mode)";
        public override bool IncludedInFixAll => false;

        public override SetupCheckResult Evaluate()
        {
            if (lastResult != null) return SetupCheckResult.Info(lastResult);
            return SetupCheckResult.Info(EditorApplication.isPlaying ? "Pick a cue under Details and run the test." : "Enter Play Mode, then run the test from Details.");
        }

        /// <summary>Plays <paramref name="cueId"/> through the runtime API and reports success or the reason of failure. Play Mode only.</summary>
        public static bool Run(string cueId, out string report)
        {
            if (!EditorApplication.isPlaying)
            {
                report = "FAILED: enter Play Mode first.";
                return false;
            }
            if (!Sound.IsInitialized && !Sound.Initialize(EditorCues.FindResourcesConfig()))
            {
                report = "FAILED: FMOD could not be initialized (see Console).";
                return false;
            }
            if (!Sound.TryGetCue(cueId, out SoundCue cue))
            {
                report = $"FAILED: cue '{cueId}' is not registered. Add its library to the config's Libraries.";
                return false;
            }
            SoundHandle handle = Sound.Play(cueId);
            if (handle.IsValid)
            {
                report = $"OK: '{cueId}' -> {cue.Event.Path} state {handle.State}, FMOD instances {Sound.GetEventInstanceCount(cueId)}, ready={Sound.IsReady}.";
                return true;
            }
            if (cue.Event.IsNull) report = $"FAILED: cue '{cueId}' has no event assigned.";
            else if (EventManager.EventFromGUID(cue.Event.Guid) == null) report = $"FAILED: event {cue.Event} is not in FMOD's cache (deleted, or banks not built/refreshed).";
            else report = $"FAILED: event {cue.Event.Path} not found at runtime. Is its bank loaded (config Banks / scene banks)?";
            return false;
        }

        public override void DrawGUI()
        {
            List<string> ids = EditorCues.AllCueIds();
            if (ids.Count == 0)
            {
                EditorGUILayout.LabelField("No cues yet. Add cues with Tools > kinatraa > FMOD Audio > Cue Library.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            int index = Mathf.Max(0, ids.IndexOf(SessionState.GetString(CueKey, ids[0])));
            index = EditorGUILayout.Popup("Cue", index, ids.ToArray());
            SessionState.SetString(CueKey, ids[index]);
            if (!EditorApplication.isPlaying)
            {
                if (GUILayout.Button("Enter Play Mode", GUILayout.Width(160))) EditorApplication.isPlaying = true;
                return;
            }
            if (GUILayout.Button("Run smoke test", GUILayout.Width(160)))
            {
                bool ok = Run(ids[index], out lastResult);
                if (ok) Debug.Log("[kinatraa.fmodaudio] Smoke test " + lastResult);
                else Debug.LogWarning("[kinatraa.fmodaudio] Smoke test " + lastResult);
            }
        }
    }
}
