using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace kinatraa.fmodaudio
{
    public static partial class Sound
    {
        private const int MaxQueuedPlays = 128;
        private const float MaxQueueSeconds = 10f;
        private const float SweepInterval = 0.1f;

        private enum SlotState : byte { Free, Queued, Active }

        private enum SpaceMode : byte { TwoD, Position, Follow }

        private sealed class Slot
        {
            public int Generation;
            public SlotState State;
            public EventInstance Instance;
            public CueState Cue;               // null for direct EventReference plays
            public EventReference Reference;   // direct plays only
            public SoundChannel Channel;
            public SpaceMode Space;
            public Vector3 Position;
            public Transform Follow;
            public bool Attached;
            public bool Held;                  // not auto-released: we release on stop/finish
            public float Volume = 1f;
            public float Pitch = 1f;
            public bool Paused;
            public List<SoundParameterValue> QueuedParameters;
            public int ActiveIndex = -1;
            public float QueuedAt;
        }

        private sealed class CueState
        {
            public SoundCue Cue;
            public SoundCueLibrary Library;
            public EventDescription Description;
            public bool HasDescription;
            public float LastPlayTime = float.NegativeInfinity;
            public int FailedLookupEpoch = -1; // bankEpoch of the last failed event lookup (no retry until banks change)
            public readonly List<SoundHandle> Live = new List<SoundHandle>();
        }

        private sealed class ChannelState
        {
            public SoundChannel Channel;
            public string BusPath;
            public float DefaultVolume = 1f;
            public float Volume = 1f;
            public bool Muted;
            public Bus Bus;
            public bool HasBus;
        }

        private sealed class SnapshotSlot
        {
            public int Generation;
            public bool Active;
            public EventInstance Instance;
            public string Path;
        }

        private sealed class BankOp
        {
            public string Name;
            public bool LoadSamples;
            public bool RaiseLoaded;
            public Action<bool> Callback;
        }

        private sealed class ConfigSceneBankProvider : ISoundBankProvider
        {
            public void GetBanksForScene(Scene scene, List<string> banks)
            {
                if (config == null) return;
                foreach (SceneBankSet set in config.SceneBanks)
                {
                    if (set != null && set.Banks != null && string.Equals(set.Scene, scene.name, StringComparison.Ordinal)) banks.AddRange(set.Banks);
                }
            }
        }

        private static readonly ISoundBankProvider DefaultBankProvider = new ConfigSceneBankProvider();

        private static bool initialized;
        private static bool failed;
        private static bool startingUp; // buses are applied once after all startup banks loaded (strings bank alone cannot resolve them)
        private static SoundSystemConfig config;
        private static GameObject root;
        private static SoundUpdater updater;
        private static ISoundSettingsStore store;
        private static ISoundPolicy policy;
        private static ISoundBankProvider bankProvider = DefaultBankProvider;

        private static readonly List<Slot> slots = new List<Slot>();
        private static readonly Stack<int> freeSlots = new Stack<int>();
        private static readonly List<int> active = new List<int>();
        private static readonly List<int> queued = new List<int>();
        private static readonly List<int> flushBuffer = new List<int>();
        private static int attachedCount;
        private static float nextSweepTime;

        private static readonly Dictionary<string, CueState> cues = new Dictionary<string, CueState>(StringComparer.Ordinal);
        private static readonly List<SoundCueLibrary> libraries = new List<SoundCueLibrary>();
        private static readonly Dictionary<string, ChannelState> channels = new Dictionary<string, ChannelState>(StringComparer.Ordinal);
        private static readonly List<SnapshotSlot> snapshots = new List<SnapshotSlot>();
        private static readonly Dictionary<string, int> bankRefs = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<BankOp> bankOps = new List<BankOp>();
        private static readonly Dictionary<int, List<string>> sceneBanks = new Dictionary<int, List<string>>();

        private static int bankEpoch; // bumped on every bank load/unload
        private static SoundHandle music;
        private static string musicCueId;
        private static bool paused;
        private static bool pausedByFocus;
        private static bool mutedByFocus;
        private static bool reapplyPause;
        private static SnapshotHandle pauseSnapshot;

        // ------------------------------------------------------------------ bootstrap

        /// <summary>Resets every static so Enter Play Mode Options with domain reload disabled works.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            initialized = false;
            failed = false;
            startingUp = false;
            config = null;
            root = null;
            updater = null;
            store = null;
            policy = null;
            bankProvider = DefaultBankProvider;
            slots.Clear();
            freeSlots.Clear();
            active.Clear();
            queued.Clear();
            flushBuffer.Clear();
            attachedCount = 0;
            nextSweepTime = 0f;
            cues.Clear();
            libraries.Clear();
            channels.Clear();
            snapshots.Clear();
            bankRefs.Clear();
            bankOps.Clear();
            sceneBanks.Clear();
            music = default;
            musicCueId = null;
            paused = false;
            pausedByFocus = false;
            mutedByFocus = false;
            reapplyPause = false;
            pauseSnapshot = default;
            OnBusVolumeChanged = null;
            OnBusMuteChanged = null;
            OnCuePlayed = null;
            OnMusicChanged = null;
            OnBankLoaded = null;
            OnBankUnloaded = null;
            FmodBackend.Reset();
            SoundLog.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            if (initialized) return;
            SoundSystemConfig resourcesConfig = Resources.Load<SoundSystemConfig>(SoundSystemConfig.ResourcesName);
            if (resourcesConfig == null)
            {
                SoundLog.Warning($"No config found at Resources/{SoundSystemConfig.ResourcesName}; using defaults. " +
                                 "Run Tools > kinatraa > FMOD Audio > Setup Wizard to create one.");
            }
            else if (!resourcesConfig.AutoInitialize)
            {
                return;
            }
            InitializeInternal(resourcesConfig);
        }

        private static bool InitializeInternal(SoundSystemConfig cfg)
        {
            if (initialized)
            {
                if (cfg != null && cfg != config) SoundLog.Warning("Initialize called while already initialized. Call Sound.Shutdown() first to switch configs.");
                return true;
            }
            if (!Application.isPlaying)
            {
                SoundLog.Warning("Sound.Initialize only works in Play Mode and in players.");
                return false;
            }

            config = cfg != null ? cfg : SoundSystemConfig.CreateDefault();
            SoundLog.Level = config.LogLevel;
            if (!FmodBackend.Initialize(out string error))
            {
                failed = true;
                SoundLog.Error($"FMOD could not be initialized ({error}). Sound is disabled for this session.");
                return false;
            }
            failed = false;
            initialized = true;

            root = new GameObject("[kinatraa.fmodaudio]");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<SoundRuntime>();
            updater = root.AddComponent<SoundUpdater>();
            updater.enabled = false;

            startingUp = true;
            if (store == null && config.PersistSettings) store = new PlayerPrefsSoundSettingsStore(config.SettingsKeyPrefix);
            foreach (SoundChannelDefinition def in config.Channels) AddChannel(def);
            foreach (SoundCueLibrary library in config.Libraries) RegisterLibraryInternal(library);

            if (config.LoadAllBanksOnStart)
            {
                var all = new List<string>();
                FmodBackend.GetBanksFromFmodSettings(all);
                foreach (string bank in all) LoadBankInternal(bank, false);
            }
            foreach (string bank in config.Banks)
            {
                if (!string.IsNullOrEmpty(bank)) LoadBankInternal(bank, false);
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) HandleSceneLoaded(scene, LoadSceneMode.Additive);
            }

            startingUp = false;
            ApplyAllChannels();
            SoundLog.Info($"Initialized with FMOD {FmodBackend.VersionString}.");
            return true;
        }

        private static void ShutdownInternal(bool destroyRoot)
        {
            if (!initialized) return;
            initialized = false;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                Slot s = slots[active[i]];
                FmodBackend.Stop(s.Instance, false);
                if (s.Held) FmodBackend.Release(s.Instance);
                FreeSlot(active[i]);
            }
            for (int i = queued.Count - 1; i >= 0; i--) FreeSlot(queued[i]);
            foreach (SnapshotSlot snap in snapshots)
            {
                if (!snap.Active) continue;
                FmodBackend.Stop(snap.Instance, false);
                FmodBackend.Release(snap.Instance);
                snap.Active = false;
                snap.Generation++;
            }
            if (paused && config.PauseMode != SoundPauseMode.Snapshot) FmodBackend.SetMasterPaused(false);
            if (mutedByFocus)
            {
                mutedByFocus = false;
                ApplyAllChannels();
            }
            foreach (KeyValuePair<string, int> kv in bankRefs)
            {
                for (int n = 0; n < kv.Value; n++) FmodBackend.UnloadBank(kv.Key);
            }

            bankRefs.Clear();
            bankOps.Clear();
            sceneBanks.Clear();
            cues.Clear();
            libraries.Clear();
            channels.Clear();
            music = default;
            musicCueId = null;
            paused = false;
            pausedByFocus = false;
            reapplyPause = false;
            pauseSnapshot = default;
            if (store is PlayerPrefsSoundSettingsStore) store = null;
            if (config != null && (config.hideFlags & HideFlags.DontSave) != 0) UnityEngine.Object.Destroy(config); // in-memory defaults
            config = null;

            GameObject go = root;
            root = null;
            updater = null;
            if (destroyRoot && go != null) UnityEngine.Object.Destroy(go);
            SoundLog.Info("Shut down.");
        }

        internal static void HandleRuntimeDestroyed(GameObject go)
        {
            if (go == root) ShutdownInternal(false);
        }

        private static bool EnsureRunning()
        {
            if (initialized) return true;
            if (!failed) SoundLog.WarningOnce("not-initialized", "Sound is not initialized. Enable AutoInitialize in the config or call Sound.Initialize().");
            return false;
        }

        // ------------------------------------------------------------------ playing

        private static SoundHandle PlayCue(string cueId, SpaceMode space, Vector3 position, Transform follow)
        {
            if (!EnsureRunning()) return default;
            if (string.IsNullOrEmpty(cueId))
            {
                SoundLog.WarningOnce("empty-id", "Play called with an empty cue id.");
                return default;
            }
            if (!cues.TryGetValue(cueId, out CueState cue))
            {
                SoundLog.WarningOnce("cue:" + cueId, $"Unknown cue id '{cueId}'. Add it to a registered SoundCueLibrary.");
                return default;
            }

            var request = new SoundPlayRequest { CueId = cueId, Channel = cue.Cue.Channel, Volume = 1f, Pitch = 1f };
            if (policy != null && !policy.OnBeforePlay(ref request)) return default;

            float now = Time.unscaledTime;
            if (cue.Cue.Cooldown > 0f && now - cue.LastPlayTime < cue.Cue.Cooldown) return default;

            if (cue.Cue.MaxInstances > 0)
            {
                CompactLive(cue);
                while (cue.Live.Count >= cue.Cue.MaxInstances)
                {
                    if (cue.Cue.LimitPolicy == SoundInstanceLimitPolicy.Reject) return default;
                    SoundHandle oldest = cue.Live[0];
                    cue.Live.RemoveAt(0);
                    HandleStop(oldest, true);
                }
            }

            SoundHandle handle = Spawn(cue, default, request, space, position, follow);
            if (handle.Index == 0) return default;
            cue.LastPlayTime = now;
            if (cue.Cue.MaxInstances > 0) cue.Live.Add(handle);
            OnCuePlayed?.Invoke(cueId, handle);
            return handle;
        }

        private static SoundHandle PlayEvent(EventReference reference, SpaceMode space, Vector3 position, Transform follow)
        {
            if (!EnsureRunning()) return default;
            if (FmodBackend.IsNull(reference))
            {
                SoundLog.WarningOnce("null-event", "Play called with an empty EventReference.");
                return default;
            }
            var request = new SoundPlayRequest { CueId = null, Channel = default, Volume = 1f, Pitch = 1f };
            if (policy != null && !policy.OnBeforePlay(ref request)) return default;
            return Spawn(null, reference, request, space, position, follow);
        }

        private static SoundHandle Spawn(CueState cue, EventReference reference, in SoundPlayRequest request, SpaceMode space, Vector3 position, Transform follow)
        {
            int index = AllocateSlot();
            Slot s = slots[index];
            s.Cue = cue;
            s.Reference = reference;
            s.Channel = request.Channel;
            s.Space = space;
            s.Position = position;
            s.Follow = follow;
            s.Volume = request.Volume;
            s.Pitch = request.Pitch;
            s.Paused = false;
            s.Held = cue != null && !cue.Cue.AutoRelease;
            s.QueuedParameters?.Clear();
            s.State = SlotState.Queued; // not in the queue yet; TryStart makes it Active, FreeSlot recycles it
            var handle = new SoundHandle(index + 1, s.Generation);

            if (FmodBackend.AllBanksLoaded && TryStart(s, index, bankOps.Count == 0)) return handle;
            if (FmodBackend.AllBanksLoaded && bankOps.Count == 0)
            {
                FreeSlot(index);
                return default;
            }
            if (queued.Count >= MaxQueuedPlays)
            {
                SoundLog.WarningOnce("queue-full", $"More than {MaxQueuedPlays} plays queued while banks load; extra plays are dropped.");
                FreeSlot(index);
                return default;
            }
            s.QueuedAt = Time.unscaledTime;
            queued.Add(index);
            RefreshUpdater();
            return handle;
        }

        /// <summary>Creates and starts the FMOD instance of a slot. Returns false when the event is not available.</summary>
        private static bool TryStart(Slot s, int index, bool logFailure)
        {
            EventDescription description;
            if (s.Cue != null)
            {
                if (!s.Cue.HasDescription || !FmodBackend.IsValid(s.Cue.Description))
                {
                    // A failed lookup is only retried after a bank was loaded or unloaded (avoids FMOD log spam).
                    s.Cue.HasDescription = s.Cue.FailedLookupEpoch != bankEpoch && FmodBackend.TryGetEvent(s.Cue.Cue.Event, out s.Cue.Description);
                    s.Cue.FailedLookupEpoch = s.Cue.HasDescription ? -1 : bankEpoch;
                }
                if (!s.Cue.HasDescription)
                {
                    if (logFailure)
                    {
                        SoundLog.WarningOnce("event:" + s.Cue.Cue.Id, $"Cue '{s.Cue.Cue.Id}': event {FmodBackend.Describe(s.Cue.Cue.Event)} not found. " +
                                                                     "Is it assigned, built, and is its bank loaded?");
                    }
                    return false;
                }
                description = s.Cue.Description;
            }
            else if (!FmodBackend.TryGetEvent(s.Reference, out description))
            {
                if (logFailure) SoundLog.WarningOnce("event:" + FmodBackend.Describe(s.Reference), $"Event {FmodBackend.Describe(s.Reference)} not found. Is its bank loaded?");
                return false;
            }

            if (!FmodBackend.CreateInstance(description, out EventInstance instance))
            {
                if (logFailure) SoundLog.WarningOnce("create:" + FmodBackend.GetPath(description), $"Could not create an instance of {FmodBackend.GetPath(description)}.");
                return false;
            }

            if (s.Cue != null)
            {
                List<SoundParameterValue> defaults = s.Cue.Cue.DefaultParameters;
                for (int i = 0; i < defaults.Count; i++) ApplyParameter(instance, defaults[i].Name, defaults[i].Value);
            }
            if (s.QueuedParameters != null)
            {
                for (int i = 0; i < s.QueuedParameters.Count; i++) ApplyParameter(instance, s.QueuedParameters[i].Name, s.QueuedParameters[i].Value);
                s.QueuedParameters.Clear();
            }
            if (s.Volume != 1f) FmodBackend.SetVolume(instance, s.Volume);
            if (s.Pitch != 1f) FmodBackend.SetPitch(instance, s.Pitch);

            if (s.Space == SpaceMode.Follow && s.Follow != null)
            {
                FmodBackend.Set3D(instance, s.Follow);
                s.Attached = true;
                attachedCount++;
            }
            else if (s.Space == SpaceMode.Position)
            {
                FmodBackend.Set3D(instance, s.Position);
            }
            else if (FmodBackend.Is3D(description))
            {
                FmodBackend.Set3DAtListener(instance);
            }

            if (s.Paused) FmodBackend.SetPaused(instance, true);
            FmodBackend.Start(instance);
            if (!s.Held) FmodBackend.Release(instance);

            s.Instance = instance;
            s.State = SlotState.Active;
            s.ActiveIndex = active.Count;
            active.Add(index);
            RefreshUpdater();
            return true;
        }

        private static void ApplyParameter(EventInstance instance, string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!FmodBackend.SetParameter(instance, name, value))
            {
                SoundLog.WarningOnce("param:" + name, $"Parameter '{name}' not found on {FmodBackend.GetPath(GetDescriptionOf(instance))}.");
            }
        }

        private static EventDescription GetDescriptionOf(EventInstance instance)
        {
            FmodBackend.TryGetDescription(instance, out EventDescription d);
            return d;
        }

        private static int AllocateSlot()
        {
            if (freeSlots.Count > 0) return freeSlots.Pop();
            slots.Add(new Slot());
            return slots.Count - 1;
        }

        private static void FreeSlot(int index)
        {
            Slot s = slots[index];
            if (s.State == SlotState.Free) return;
            if (s.State == SlotState.Active && s.ActiveIndex >= 0)
            {
                int lastIndex = active.Count - 1;
                int moved = active[lastIndex];
                active[s.ActiveIndex] = moved;
                slots[moved].ActiveIndex = s.ActiveIndex;
                active.RemoveAt(lastIndex);
            }
            else if (s.State == SlotState.Queued)
            {
                queued.Remove(index);
            }
            if (s.Attached)
            {
                s.Attached = false;
                attachedCount--;
            }
            s.State = SlotState.Free;
            s.Generation++;
            s.Instance = default;
            s.Cue = null;
            s.Reference = default;
            s.Follow = null;
            s.ActiveIndex = -1;
            freeSlots.Push(index);
        }

        private static bool TryGetSlot(SoundHandle handle, out Slot slot, out int index)
        {
            index = handle.Index - 1;
            if (index < 0 || index >= slots.Count)
            {
                slot = null;
                return false;
            }
            slot = slots[index];
            return slot.State != SlotState.Free && slot.Generation == handle.Generation;
        }

        /// <summary>Like <see cref="TryGetSlot"/> but also frees slots whose instance already finished.</summary>
        private static bool TryGetLiveSlot(SoundHandle handle, out Slot slot)
        {
            if (!TryGetSlot(handle, out slot, out int index)) return false;
            if (slot.State == SlotState.Queued) return true;
            if (!FmodBackend.IsValid(slot.Instance))
            {
                FreeSlot(index);
                return false;
            }
            if (slot.Held && FmodBackend.GetState(slot.Instance) == SoundPlaybackState.Stopped)
            {
                FmodBackend.Release(slot.Instance);
                FreeSlot(index);
                return false;
            }
            return true;
        }

        private static void CompactLive(CueState cue)
        {
            for (int i = cue.Live.Count - 1; i >= 0; i--)
            {
                if (!TryGetLiveSlot(cue.Live[i], out _)) cue.Live.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ handle operations

        internal static bool HandleIsValid(SoundHandle h) => TryGetLiveSlot(h, out _);

        internal static bool HandleIsPlaying(SoundHandle h)
        {
            if (!TryGetLiveSlot(h, out Slot s) || s.State != SlotState.Active || FmodBackend.GetPaused(s.Instance)) return false;
            SoundPlaybackState state = FmodBackend.GetState(s.Instance);
            return state == SoundPlaybackState.Playing || state == SoundPlaybackState.Starting || state == SoundPlaybackState.Sustaining;
        }

        internal static bool HandleIsPaused(SoundHandle h)
        {
            if (!TryGetLiveSlot(h, out Slot s)) return false;
            return s.State == SlotState.Queued ? s.Paused : FmodBackend.GetPaused(s.Instance);
        }

        internal static SoundPlaybackState HandleGetState(SoundHandle h)
        {
            if (!TryGetLiveSlot(h, out Slot s)) return SoundPlaybackState.Stopped;
            return s.State == SlotState.Queued ? SoundPlaybackState.Queued : FmodBackend.GetState(s.Instance);
        }

        internal static string HandleGetCueId(SoundHandle h)
        {
            return TryGetSlot(h, out Slot s, out _) && s.Cue != null ? s.Cue.Cue.Id : null;
        }

        internal static void HandleStop(SoundHandle h, bool allowFadeOut)
        {
            if (!TryGetSlot(h, out Slot s, out int index)) return;
            if (s.State == SlotState.Active)
            {
                FmodBackend.Stop(s.Instance, allowFadeOut);
                if (s.Held) FmodBackend.Release(s.Instance);
            }
            FreeSlot(index);
        }

        internal static void HandleSetPaused(SoundHandle h, bool value)
        {
            if (!TryGetLiveSlot(h, out Slot s)) return;
            s.Paused = value;
            if (s.State == SlotState.Active) FmodBackend.SetPaused(s.Instance, value);
        }

        internal static void HandleSetParameter(SoundHandle h, string name, float value)
        {
            if (string.IsNullOrEmpty(name) || !TryGetLiveSlot(h, out Slot s)) return;
            if (s.State == SlotState.Active)
            {
                ApplyParameter(s.Instance, name, value);
                return;
            }
            if (s.QueuedParameters == null) s.QueuedParameters = new List<SoundParameterValue>();
            for (int i = 0; i < s.QueuedParameters.Count; i++)
            {
                if (s.QueuedParameters[i].Name == name)
                {
                    s.QueuedParameters[i] = new SoundParameterValue(name, value);
                    return;
                }
            }
            s.QueuedParameters.Add(new SoundParameterValue(name, value));
        }

        internal static void HandleSetParameter(SoundHandle h, SoundParameterId id, float value)
        {
            if (!id.IsValid || !TryGetLiveSlot(h, out Slot s)) return;
            if (s.State != SlotState.Active)
            {
                SoundLog.WarningOnce("param-id-queued", "SetParameter(SoundParameterId) on a queued sound is ignored; use the name overload while banks load.");
                return;
            }
            if (!FmodBackend.SetParameter(s.Instance, id, value)) SoundLog.WarningOnce("param-id", "SetParameter: parameter id not valid for this event.");
        }

        internal static SoundParameterId HandleGetParameterId(SoundHandle h, string name)
        {
            if (string.IsNullOrEmpty(name) || !TryGetLiveSlot(h, out Slot s)) return default;
            if (s.State == SlotState.Active)
            {
                return FmodBackend.TryGetParameterId(s.Instance, name, out SoundParameterId id) ? id : default;
            }
            if (s.Cue != null && s.Cue.HasDescription && FmodBackend.TryGetParameterId(s.Cue.Description, name, out SoundParameterId qid)) return qid;
            return default;
        }

        internal static void HandleSetVolume(SoundHandle h, float volume)
        {
            if (!TryGetLiveSlot(h, out Slot s)) return;
            s.Volume = volume;
            if (s.State == SlotState.Active) FmodBackend.SetVolume(s.Instance, volume);
        }

        internal static void HandleSetPitch(SoundHandle h, float pitch)
        {
            if (!TryGetLiveSlot(h, out Slot s)) return;
            s.Pitch = pitch;
            if (s.State == SlotState.Active) FmodBackend.SetPitch(s.Instance, pitch);
        }

        // ------------------------------------------------------------------ update (runs only while something needs it)

        private static void RefreshUpdater()
        {
            if (updater != null) updater.enabled = active.Count > 0 || queued.Count > 0 || bankOps.Count > 0 || reapplyPause;
        }

        internal static void Tick()
        {
            if (!initialized)
            {
                if (updater != null) updater.enabled = false;
                return;
            }
            if (reapplyPause)
            {
                reapplyPause = false;
                if (paused && config.PauseMode != SoundPauseMode.Snapshot) FmodBackend.SetMasterPaused(true);
            }
            if (bankOps.Count > 0) PollBankOps();
            if (queued.Count > 0 && FmodBackend.AllBanksLoaded) FlushQueue();
            else if (queued.Count > 0) DropExpiredQueuedPlays();
            if (attachedCount > 0) UpdateAttached();

            float now = Time.unscaledTime;
            if (now >= nextSweepTime)
            {
                nextSweepTime = now + SweepInterval;
                Sweep();
            }
            RefreshUpdater();
        }

        private static void FlushQueue()
        {
            flushBuffer.Clear();
            flushBuffer.AddRange(queued);
            queued.Clear();
            bool waitingForBanks = bankOps.Count > 0;
            foreach (int index in flushBuffer)
            {
                Slot s = slots[index];
                if (s.State != SlotState.Queued) continue;
                if (TryStart(s, index, !waitingForBanks)) continue;
                if (waitingForBanks)
                {
                    queued.Add(index);
                }
                else
                {
                    FreeSlot(index);
                }
            }
            flushBuffer.Clear();
        }

        /// <summary>Plays never wait forever: a bank that never finishes loading drops them after <see cref="MaxQueueSeconds"/>.</summary>
        private static void DropExpiredQueuedPlays()
        {
            float now = Time.unscaledTime;
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                if (now - slots[queued[i]].QueuedAt < MaxQueueSeconds) continue;
                SoundLog.WarningOnce("queue-timeout", $"Queued plays were dropped after waiting {MaxQueueSeconds} s for FMOD banks to load.");
                FreeSlot(queued[i]);
            }
        }

        private static void UpdateAttached()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Slot s = slots[active[i]];
                if (!s.Attached) continue;
                if (s.Follow == null)
                {
                    // Transform destroyed: the sound keeps its last position.
                    s.Attached = false;
                    attachedCount--;
                    continue;
                }
                FmodBackend.Set3D(s.Instance, s.Follow);
            }
        }

        private static void Sweep()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                int index = active[i];
                Slot s = slots[index];
                if (!FmodBackend.IsValid(s.Instance))
                {
                    FreeSlot(index);
                }
                else if (s.Held && FmodBackend.GetState(s.Instance) == SoundPlaybackState.Stopped)
                {
                    FmodBackend.Release(s.Instance);
                    FreeSlot(index);
                }
            }
        }

        // ------------------------------------------------------------------ stopping

        private static void StopChannelInternal(SoundChannel channel, bool allowFadeOut)
        {
            if (!initialized) return;
            ChannelState cs = GetChannel(channel, false);
            if (cs != null && cs.BusPath == FmodBackend.MasterBusPath)
            {
                StopAllInternal(allowFadeOut);
                return;
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                int index = active[i];
                if (slots[index].Channel == channel) HandleStop(new SoundHandle(index + 1, slots[index].Generation), allowFadeOut);
            }
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                if (slots[queued[i]].Channel == channel) FreeSlot(queued[i]);
            }
            if (cs != null && ResolveBus(cs)) FmodBackend.StopBus(cs.Bus, allowFadeOut);
            RefreshUpdater();
        }

        private static void StopAllInternal(bool allowFadeOut)
        {
            if (!initialized) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                int index = active[i];
                HandleStop(new SoundHandle(index + 1, slots[index].Generation), allowFadeOut);
            }
            for (int i = queued.Count - 1; i >= 0; i--) FreeSlot(queued[i]);
            if (FmodBackend.TryGetBus(FmodBackend.MasterBusPath, out Bus master)) FmodBackend.StopBus(master, allowFadeOut);
            if (musicCueId != null)
            {
                music = default;
                musicCueId = null;
                OnMusicChanged?.Invoke(null);
            }
            RefreshUpdater();
        }

        // ------------------------------------------------------------------ music

        private static SoundHandle PlayMusicInternal(string cueId, bool restartIfPlaying)
        {
            if (!restartIfPlaying && musicCueId == cueId && HandleIsValid(music)) return music;
            SoundHandle next = PlayCue(cueId, SpaceMode.TwoD, default, null);
            if (next.Index == 0) return default;
            SoundHandle previous = music;
            music = next;
            musicCueId = cueId;
            HandleStop(previous, true);
            OnMusicChanged?.Invoke(cueId);
            return next;
        }

        private static void StopMusicInternal(bool allowFadeOut)
        {
            HandleStop(music, allowFadeOut);
            bool hadMusic = musicCueId != null;
            music = default;
            musicCueId = null;
            if (hadMusic) OnMusicChanged?.Invoke(null);
        }

        // ------------------------------------------------------------------ global parameters, snapshots, pause

        private static void SetGlobalParameterInternal(string name, float value)
        {
            if (!EnsureRunning() || string.IsNullOrEmpty(name)) return;
            if (!FmodBackend.SetGlobalParameter(name, value)) SoundLog.WarningOnce("global:" + name, $"Global parameter '{name}' not found.");
        }

        private static SnapshotHandle PushSnapshotInternal(string nameOrPath)
        {
            if (!EnsureRunning() || string.IsNullOrEmpty(nameOrPath)) return default;
            if (!config.TryGetSnapshotPath(nameOrPath, out string path))
            {
                path = nameOrPath.StartsWith("snapshot:/", StringComparison.Ordinal) ? nameOrPath : "snapshot:/" + nameOrPath;
            }
            if (!FmodBackend.TryGetEvent(path, out EventDescription description) || !FmodBackend.CreateInstance(description, out EventInstance instance))
            {
                SoundLog.WarningOnce("snapshot:" + path, $"Snapshot '{path}' not found. Is it built into a loaded bank (usually Master)?");
                return default;
            }
            FmodBackend.Start(instance);

            int index = -1;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!snapshots[i].Active)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                snapshots.Add(new SnapshotSlot());
                index = snapshots.Count - 1;
            }
            SnapshotSlot slot = snapshots[index];
            slot.Active = true;
            slot.Instance = instance;
            slot.Path = path;
            return new SnapshotHandle(index + 1, slot.Generation);
        }

        private static bool TryGetSnapshot(SnapshotHandle h, out SnapshotSlot slot)
        {
            int index = h.Index - 1;
            slot = index >= 0 && index < snapshots.Count ? snapshots[index] : null;
            return slot != null && slot.Active && slot.Generation == h.Generation;
        }

        internal static bool SnapshotIsValid(SnapshotHandle h) => TryGetSnapshot(h, out _);

        internal static void SnapshotRelease(SnapshotHandle h, bool allowFadeOut)
        {
            if (!TryGetSnapshot(h, out SnapshotSlot slot)) return;
            FmodBackend.Stop(slot.Instance, allowFadeOut);
            FmodBackend.Release(slot.Instance);
            slot.Active = false;
            slot.Instance = default;
            slot.Generation++;
        }

        private static void PauseAllInternal()
        {
            if (!EnsureRunning()) return;
            if (paused)
            {
                pausedByFocus = false; // an explicit pause now owns the paused state
                return;
            }
            paused = true;
            if (config.PauseMode != SoundPauseMode.Snapshot) FmodBackend.SetMasterPaused(true);
            if (config.PauseMode != SoundPauseMode.MasterBus) pauseSnapshot = PushSnapshotInternal("Pause");
        }

        private static void ResumeAllInternal()
        {
            if (!initialized || !paused) return;
            paused = false;
            pausedByFocus = false;
            if (config.PauseMode != SoundPauseMode.Snapshot) FmodBackend.SetMasterPaused(false);
            SnapshotRelease(pauseSnapshot, true);
            pauseSnapshot = default;
        }

        internal static void HandleFocusChanged(bool hasFocus)
        {
            if (!initialized) return;
            switch (config.FocusLossBehavior)
            {
                case SoundFocusLossBehavior.Pause:
                    if (!hasFocus && !paused)
                    {
                        PauseAllInternal();
                        pausedByFocus = true;
                    }
                    else if (hasFocus && pausedByFocus)
                    {
                        ResumeAllInternal();
                    }
                    break;
                case SoundFocusLossBehavior.Mute:
                    mutedByFocus = !hasFocus;
                    ApplyAllChannels();
                    break;
            }
            policy?.OnFocusChanged(hasFocus);
        }

        /// <summary>FMOD for Unity unpauses the master bus when a mobile app resumes; re-apply our pause next frame.</summary>
        internal static void HandleApplicationResumed()
        {
            if (!initialized || !paused) return;
            reapplyPause = true;
            RefreshUpdater();
        }

        // ------------------------------------------------------------------ channels & settings

        private static string DefaultBusPath(string channelName) => channelName == "Master" ? FmodBackend.MasterBusPath : "bus:/" + channelName;

        private static void AddChannel(SoundChannelDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Name)) return;
            var cs = new ChannelState
            {
                Channel = new SoundChannel(def.Name),
                BusPath = string.IsNullOrEmpty(def.BusPath) ? DefaultBusPath(def.Name) : def.BusPath,
                DefaultVolume = Mathf.Clamp01(def.DefaultVolume),
            };
            LoadChannelSettings(cs);
            channels[def.Name] = cs;
        }

        private static ChannelState GetChannel(SoundChannel channel, bool create)
        {
            if (!channel.IsValid) return null;
            if (channels.TryGetValue(channel.Name, out ChannelState cs)) return cs;
            if (!create) return null;
            cs = new ChannelState { Channel = channel, BusPath = DefaultBusPath(channel.Name) };
            LoadChannelSettings(cs);
            channels[channel.Name] = cs;
            SoundLog.Info($"Channel '{channel.Name}' is not in the config; using bus '{cs.BusPath}'.");
            return cs;
        }

        private static void LoadChannelSettings(ChannelState cs)
        {
            cs.Volume = store != null && store.TryLoad(cs.Channel.Name, out float v) ? Mathf.Clamp01(v) : cs.DefaultVolume;
            cs.Muted = store != null && store.TryLoadMuted(cs.Channel.Name, out bool m) && m;
        }

        private static bool ResolveBus(ChannelState cs)
        {
            if (cs.HasBus && FmodBackend.IsValid(cs.Bus)) return true;
            cs.HasBus = FmodBackend.TryGetBus(cs.BusPath, out cs.Bus);
            if (!cs.HasBus && FmodBackend.AllBanksLoaded)
            {
                SoundLog.WarningOnce("bus:" + cs.BusPath, $"Bus '{cs.BusPath}' (channel '{cs.Channel.Name}') not found. " +
                                                          "Create it in FMOD Studio, rebuild banks, or fix the channel's bus path in the config.");
            }
            return cs.HasBus;
        }

        private static void ApplyChannel(ChannelState cs)
        {
            if (!initialized || !ResolveBus(cs)) return;
            FmodBackend.SetBusVolume(cs.Bus, cs.Volume);
            bool isMaster = cs.BusPath == FmodBackend.MasterBusPath;
            FmodBackend.SetBusMuted(cs.Bus, cs.BusPath, cs.Muted || (isMaster && mutedByFocus));
        }

        private static void ApplyAllChannels()
        {
            foreach (KeyValuePair<string, ChannelState> kv in channels) ApplyChannel(kv.Value);
        }

        private static void SetBusVolumeInternal(SoundChannel channel, float volume, bool save)
        {
            ChannelState cs = GetChannel(channel, true);
            if (cs == null) return;
            volume = Mathf.Clamp01(volume);
            bool changed = cs.Volume != volume;
            cs.Volume = volume;
            ApplyChannel(cs);
            if (save) store?.Save(cs.Channel.Name, volume);
            if (changed) OnBusVolumeChanged?.Invoke(cs.Channel, volume);
        }

        private static void SetBusMutedInternal(SoundChannel channel, bool muted)
        {
            ChannelState cs = GetChannel(channel, true);
            if (cs == null) return;
            bool changed = cs.Muted != muted;
            cs.Muted = muted;
            ApplyChannel(cs);
            store?.SaveMuted(cs.Channel.Name, muted);
            if (changed) OnBusMuteChanged?.Invoke(cs.Channel, muted);
        }

        private static void SetSettingsStoreInternal(ISoundSettingsStore settingsStore)
        {
            store = settingsStore;
            foreach (KeyValuePair<string, ChannelState> kv in channels)
            {
                ChannelState cs = kv.Value;
                float oldVolume = cs.Volume;
                bool oldMuted = cs.Muted;
                LoadChannelSettings(cs);
                ApplyChannel(cs);
                if (oldVolume != cs.Volume) OnBusVolumeChanged?.Invoke(cs.Channel, cs.Volume);
                if (oldMuted != cs.Muted) OnBusMuteChanged?.Invoke(cs.Channel, cs.Muted);
            }
        }

        // ------------------------------------------------------------------ libraries

        private static void RegisterLibraryInternal(SoundCueLibrary library)
        {
            if (library == null || libraries.Contains(library)) return;
            libraries.Add(library);
            AddCues(library, null);
        }

        private static void AddCues(SoundCueLibrary library, HashSet<string> onlyIds)
        {
            foreach (SoundCue cue in library.Cues)
            {
                if (cue == null || string.IsNullOrEmpty(cue.Id)) continue;
                if (onlyIds != null && (!onlyIds.Contains(cue.Id) || cues.ContainsKey(cue.Id))) continue;
                if (cues.TryGetValue(cue.Id, out CueState existing) && existing.Library != library)
                {
                    SoundLog.WarningOnce("dup:" + cue.Id, $"Cue id '{cue.Id}' exists in '{existing.Library.name}' and '{library.name}'; '{library.name}' wins.");
                }
                cues[cue.Id] = new CueState { Cue = cue, Library = library };
            }
        }

        private static void UnregisterLibraryInternal(SoundCueLibrary library)
        {
            if (library == null || !libraries.Remove(library)) return;
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, CueState> kv in cues)
            {
                if (kv.Value.Library == library) removed.Add(kv.Key);
            }
            foreach (string id in removed) cues.Remove(id);
            for (int i = libraries.Count - 1; i >= 0; i--) AddCues(libraries[i], removed);
        }

        // ------------------------------------------------------------------ banks

        private static bool LoadBankInternal(string bankName, bool loadSamples)
        {
            if (!EnsureRunning() || string.IsNullOrEmpty(bankName)) return false;
            if (!FmodBackend.LoadBank(bankName, loadSamples, out string error))
            {
                SoundLog.ErrorOnce("bank:" + bankName, $"Could not load bank '{bankName}': {error}");
                return false;
            }
            bool first = AddBankRef(bankName);
            bankEpoch++;
            if (FmodBackend.HasBankLoaded(bankName))
            {
                if (first) OnBankLoaded?.Invoke(bankName);
                if (!startingUp) ApplyAllChannels();
            }
            else
            {
                bankOps.Add(new BankOp { Name = bankName, RaiseLoaded = first });
                RefreshUpdater();
            }
            return true;
        }

        private static void LoadBankAsyncInternal(string bankName, Action<bool> onComplete, bool loadSamples)
        {
            if (!EnsureRunning() || string.IsNullOrEmpty(bankName))
            {
                onComplete?.Invoke(false);
                return;
            }
            if (!FmodBackend.LoadBank(bankName, loadSamples, out string error))
            {
                SoundLog.ErrorOnce("bank:" + bankName, $"Could not load bank '{bankName}': {error}");
                onComplete?.Invoke(false);
                return;
            }
            bool first = AddBankRef(bankName);
            bankOps.Add(new BankOp { Name = bankName, LoadSamples = loadSamples, RaiseLoaded = first, Callback = onComplete });
            RefreshUpdater();
        }

        private static bool AddBankRef(string bankName)
        {
            bankRefs.TryGetValue(bankName, out int count);
            bankRefs[bankName] = count + 1;
            return count == 0;
        }

        private static void PollBankOps()
        {
            for (int i = bankOps.Count - 1; i >= 0; i--)
            {
                BankOp op = bankOps[i];
                if (!FmodBackend.HasBankLoaded(op.Name))
                {
                    if (!FmodBackend.AllBanksLoaded) continue; // still loading (WebGL / Android OBB)
                    bankOps.RemoveAt(i);
                    ReleaseBankRef(op.Name);
                    SoundLog.ErrorOnce("bank:" + op.Name, $"Bank '{op.Name}' failed to load.");
                    op.Callback?.Invoke(false);
                    continue;
                }
                if (op.LoadSamples && FmodBackend.IsSampleDataLoading(op.Name)) continue;
                bankOps.RemoveAt(i);
                bankEpoch++;
                ApplyAllChannels();
                if (op.RaiseLoaded) OnBankLoaded?.Invoke(op.Name);
                op.Callback?.Invoke(true);
            }
        }

        private static int ReleaseBankRef(string bankName)
        {
            if (!bankRefs.TryGetValue(bankName, out int count)) return -1;
            count--;
            if (count <= 0) bankRefs.Remove(bankName);
            else bankRefs[bankName] = count;
            return count;
        }

        private static void UnloadBankInternal(string bankName)
        {
            if (!initialized || string.IsNullOrEmpty(bankName)) return;
            int remaining = ReleaseBankRef(bankName);
            if (remaining < 0)
            {
                SoundLog.WarningOnce("unload:" + bankName, $"UnloadBank('{bankName}') called without a matching LoadBank.");
                return;
            }
            FmodBackend.UnloadBank(bankName);
            bankEpoch++;
            if (remaining == 0) OnBankUnloaded?.Invoke(bankName);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!initialized || bankProvider == null || sceneBanks.ContainsKey(scene.handle)) return;
            var banks = new List<string>();
            try
            {
                bankProvider.GetBanksForScene(scene, banks);
            }
            catch (Exception e)
            {
                SoundLog.Error($"Bank provider failed for scene '{scene.name}': {e.Message}");
            }
            for (int i = banks.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(banks[i]) || !LoadBankInternal(banks[i], false)) banks.RemoveAt(i);
            }
            sceneBanks[scene.handle] = banks;
        }

        private static void HandleSceneUnloaded(Scene scene)
        {
            if (!sceneBanks.TryGetValue(scene.handle, out List<string> banks)) return;
            sceneBanks.Remove(scene.handle);
            foreach (string bank in banks) UnloadBankInternal(bank);
        }

        // ------------------------------------------------------------------ diagnostics (editor debugger)

        internal struct DebugSound
        {
            public SoundHandle Handle;
            public string CueId;
            public string EventPath;
            public SoundChannel Channel;
            public SoundPlaybackState State;
            public bool Paused;
            public bool Attached;
            public bool Held;
        }

        internal static void DebugGetSounds(List<DebugSound> into)
        {
            into.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                int index = active[i];
                Slot s = slots[index];
                into.Add(new DebugSound
                {
                    Handle = new SoundHandle(index + 1, s.Generation),
                    CueId = s.Cue?.Cue.Id,
                    EventPath = FmodBackend.GetPath(GetDescriptionOf(s.Instance)),
                    Channel = s.Channel,
                    State = FmodBackend.GetState(s.Instance),
                    Paused = FmodBackend.GetPaused(s.Instance),
                    Attached = s.Attached,
                    Held = s.Held,
                });
            }
            foreach (int index in queued)
            {
                Slot s = slots[index];
                into.Add(new DebugSound
                {
                    Handle = new SoundHandle(index + 1, s.Generation),
                    CueId = s.Cue?.Cue.Id,
                    EventPath = s.Cue != null ? FmodBackend.Describe(s.Cue.Cue.Event) : FmodBackend.Describe(s.Reference),
                    Channel = s.Channel,
                    State = SoundPlaybackState.Queued,
                    Paused = s.Paused,
                });
            }
        }

        internal static void DebugGetParameters(SoundHandle h, List<KeyValuePair<string, float>> into)
        {
            into.Clear();
            if (TryGetSlot(h, out Slot s, out _) && s.State == SlotState.Active) FmodBackend.GetParameters(s.Instance, into);
        }

        internal static void DebugGetBankRefs(List<KeyValuePair<string, int>> into)
        {
            into.Clear();
            into.AddRange(bankRefs);
        }

        internal static void DebugGetLoadedBanks(List<string> into) => FmodBackend.GetLoadedBankPaths(into);

        internal static void DebugGetSnapshots(List<string> into)
        {
            into.Clear();
            foreach (SnapshotSlot s in snapshots)
            {
                if (s.Active) into.Add(s.Path);
            }
        }

        internal static void DebugGetGlobalParameters(List<FmodBackend.GlobalParameterInfo> into) => FmodBackend.GetGlobalParameters(into);

        internal static void DebugGetCueIds(List<string> into)
        {
            into.Clear();
            into.AddRange(cues.Keys);
            into.Sort(StringComparer.Ordinal);
        }

        internal static string FmodVersion => FmodBackend.VersionString;
    }
}
