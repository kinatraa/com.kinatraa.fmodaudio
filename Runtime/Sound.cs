using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace kinatraa.FmodAudio
{
    /// <summary>
    /// Static facade of the kinatraa FMOD audio system.
    /// <para>
    /// The system initializes itself before the first scene loads using <c>Resources/kinatraaFmodAudioConfig</c>
    /// (or defaults). Plays requested while FMOD banks are still loading are queued and start once loading finishes.
    /// Nothing here throws: missing cues, events, buses or banks are logged once and return invalid handles.
    /// </para>
    /// <para>Main thread only. Do not call from jobs, threads or FMOD callbacks.</para>
    /// </summary>
    public static partial class Sound
    {
        // ------------------------------------------------------------------ events

        /// <summary>Raised when a channel volume changes. Arguments: channel, new linear volume (0..1).</summary>
        public static event Action<SoundChannel, float> OnBusVolumeChanged;

        /// <summary>Raised when a channel mute state changes.</summary>
        public static event Action<SoundChannel, bool> OnBusMuteChanged;

        /// <summary>Raised after a cue was played (or queued). Arguments: cue id, handle.</summary>
        public static event Action<string, SoundHandle> OnCuePlayed;

        /// <summary>Raised when the music slot changes. Argument: new music cue id, or null when music stopped.</summary>
        public static event Action<string> OnMusicChanged;

        /// <summary>Raised when a bank loaded through this API becomes available (first reference).</summary>
        public static event Action<string> OnBankLoaded;

        /// <summary>Raised when the last reference to a bank loaded through this API is released.</summary>
        public static event Action<string> OnBankUnloaded;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>True once <see cref="Initialize"/> succeeded (automatically or manually).</summary>
        public static bool IsInitialized => initialized;

        /// <summary>True when initialized and FMOD has no bank load in flight. Plays made before that are queued.</summary>
        public static bool IsReady => initialized && FmodBackend.AllBanksLoaded;

        /// <summary>The active configuration (null before initialization).</summary>
        public static SoundSystemConfig Config => config;

        /// <summary>
        /// Initializes the system with <paramref name="config"/> (defaults when null). Called automatically before the
        /// first scene loads unless the Resources config has <see cref="SoundSystemConfig.AutoInitialize"/> disabled.
        /// Returns false if FMOD could not be initialized; the API then stays safe and silent.
        /// </summary>
        public static bool Initialize(SoundSystemConfig config = null) => InitializeInternal(config);

        /// <summary>Stops every sound, releases every instance and snapshot, unloads banks loaded through this API.</summary>
        public static void Shutdown() => ShutdownInternal(true);

        // ------------------------------------------------------------------ playback

        /// <summary>Plays a cue in 2D (3D events are placed on the listener).</summary>
        public static SoundHandle Play(string cueId) => PlayCue(cueId, SpaceMode.TwoD, default, null);

        /// <summary>Plays a cue at a world position.</summary>
        public static SoundHandle Play(string cueId, Vector3 position) => PlayCue(cueId, SpaceMode.Position, position, null);

        /// <summary>Plays a cue attached to a transform; its 3D position follows the transform until the sound ends.</summary>
        public static SoundHandle Play(string cueId, Transform follow) => PlayCue(cueId, SpaceMode.Follow, default, follow);

        /// <summary>Plays an FMOD event directly, without a cue (2D, 3D events are placed on the listener).</summary>
        public static SoundHandle Play(EventReference eventReference) => PlayEvent(eventReference, SpaceMode.TwoD, default, null);

        /// <summary>Plays an FMOD event directly at a world position.</summary>
        public static SoundHandle Play(EventReference eventReference, Vector3 position) => PlayEvent(eventReference, SpaceMode.Position, position, null);

        /// <summary>Plays an FMOD event directly, attached to a transform.</summary>
        public static SoundHandle Play(EventReference eventReference, Transform follow) => PlayEvent(eventReference, SpaceMode.Follow, default, follow);

        /// <summary>Number of handles currently tracked (playing, paused, fading out or queued).</summary>
        public static int ActiveSoundCount => active.Count + queued.Count;

        /// <summary>Number of plays waiting for FMOD banks to finish loading.</summary>
        public static int QueuedSoundCount => queued.Count;

        /// <summary>FMOD's live instance count for the cue's event (includes instances not created by this API). For diagnostics.</summary>
        public static int GetEventInstanceCount(string cueId)
        {
            if (!initialized || string.IsNullOrEmpty(cueId) || !cues.TryGetValue(cueId, out CueState cue)) return 0;
            return FmodBackend.TryGetEvent(cue.Cue.Event, out FMOD.Studio.EventDescription d) ? FmodBackend.GetInstanceCount(d) : 0;
        }

        /// <summary>Stops every sound on a channel (tracked handles of that channel plus everything routed through its bus).</summary>
        public static void StopChannel(SoundChannel channel, bool allowFadeOut = true) => StopChannelInternal(channel, allowFadeOut);

        /// <summary>Stops every sound, including music.</summary>
        public static void StopAll(bool allowFadeOut = true) => StopAllInternal(allowFadeOut);

        // ------------------------------------------------------------------ music

        /// <summary>
        /// Plays a cue in the single music slot. The previous music is stopped with fade-out (FMOD AHDSR), so authored
        /// release times give a crossfade. Playing the cue that is already playing does nothing unless
        /// <paramref name="restartIfPlaying"/> is true. If the new cue cannot play, the current music keeps playing.
        /// </summary>
        public static SoundHandle PlayMusic(string cueId, bool restartIfPlaying = false) => PlayMusicInternal(cueId, restartIfPlaying);

        /// <summary>Stops the music slot.</summary>
        public static void StopMusic(bool allowFadeOut = true) => StopMusicInternal(allowFadeOut);

        /// <summary>Pauses the music slot.</summary>
        public static void PauseMusic() => HandleSetPaused(music, true);

        /// <summary>Resumes the music slot.</summary>
        public static void ResumeMusic() => HandleSetPaused(music, false);

        /// <summary>Sets a parameter on the current music.</summary>
        public static void SetMusicParameter(string name, float value) => HandleSetParameter(music, name, value);

        /// <summary>Handle of the current music (invalid when none).</summary>
        public static SoundHandle MusicHandle => music;

        /// <summary>Cue id of the current music, or null.</summary>
        public static string CurrentMusic => HandleIsValid(music) ? musicCueId : null;

        // ------------------------------------------------------------------ global parameters & snapshots

        /// <summary>Sets an FMOD global parameter. Logs once if it does not exist.</summary>
        public static void SetGlobalParameter(string name, float value) => SetGlobalParameterInternal(name, value);

        /// <summary>Reads an FMOD global parameter (0 when missing).</summary>
        public static float GetGlobalParameter(string name)
        {
            return initialized && !string.IsNullOrEmpty(name) && FmodBackend.TryGetGlobalParameter(name, out float v) ? v : 0f;
        }

        /// <summary>
        /// Starts a snapshot. <paramref name="nameOrPath"/> is a key from <see cref="SoundSystemConfig.Snapshots"/>
        /// (e.g. "Pause"), a snapshot name ("Pause" -> <c>snapshot:/Pause</c>) or a full path. Release the handle to pop it.
        /// </summary>
        public static SnapshotHandle PushSnapshot(string nameOrPath) => PushSnapshotInternal(nameOrPath);

        /// <summary>Pauses all audio according to <see cref="SoundSystemConfig.PauseMode"/>.</summary>
        public static void PauseAll() => PauseAllInternal();

        /// <summary>Undoes <see cref="PauseAll"/>.</summary>
        public static void ResumeAll() => ResumeAllInternal();

        /// <summary>True between <see cref="PauseAll"/> and <see cref="ResumeAll"/>.</summary>
        public static bool IsPaused => paused;

        // ------------------------------------------------------------------ volumes

        /// <summary>
        /// Sets a channel volume (linear 0..1) on its FMOD bus or VCA, saves it through the settings store and raises
        /// <see cref="OnBusVolumeChanged"/>. Before initialization the value is kept and applied when the system starts.
        /// </summary>
        public static void SetBusVolume(SoundChannel channel, float volume) => SetBusVolumeInternal(channel, volume, true);

        /// <summary>Current linear volume of a channel (1 for unknown channels).</summary>
        public static float GetBusVolume(SoundChannel channel)
        {
            ChannelState cs = GetChannel(channel, false);
            return cs != null ? cs.Volume : 1f;
        }

        /// <summary>Mutes or unmutes a channel (a VCA channel is muted by setting its volume to 0), saves it and raises <see cref="OnBusMuteChanged"/>.</summary>
        public static void SetBusMuted(SoundChannel channel, bool muted) => SetBusMutedInternal(channel, muted);

        /// <summary>True when the channel is muted.</summary>
        public static bool IsBusMuted(SoundChannel channel)
        {
            ChannelState cs = GetChannel(channel, false);
            return cs != null && cs.Muted;
        }

        /// <summary>Applies several volumes at once (keys are channel names). Use it when your game serializes its own settings object.</summary>
        public static void ApplyVolumes(IDictionary<string, float> volumes, bool save = false)
        {
            if (volumes == null) return;
            foreach (KeyValuePair<string, float> kv in volumes) SetBusVolumeInternal(new SoundChannel(kv.Key), kv.Value, save);
        }

        /// <summary>Returns a new dictionary of every channel's current volume (keys are channel names).</summary>
        public static Dictionary<string, float> GetAllVolumes()
        {
            var result = new Dictionary<string, float>(channels.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, ChannelState> kv in channels) result[kv.Key] = kv.Value.Volume;
            return result;
        }

        /// <summary>
        /// Restores every channel to its configured default volume and unmutes it, raising the change events.
        /// With <paramref name="save"/> the defaults are written to the settings store (a "Reset to defaults" button).
        /// </summary>
        public static void ResetVolumes(bool save = true) => ResetVolumesInternal(save);

        /// <summary>
        /// Writes pending settings to disk when the store supports it (<see cref="IFlushableSoundSettingsStore"/>, e.g. the
        /// default PlayerPrefs store). Called automatically on focus loss, app suspension, quit and shutdown; call it when
        /// your settings menu closes to be safe against crashes.
        /// </summary>
        public static void SaveSettings() => FlushSettingsInternal();

        /// <summary>Names of every known channel (configured or used).</summary>
        public static void GetChannels(List<SoundChannel> into)
        {
            into.Clear();
            foreach (KeyValuePair<string, ChannelState> kv in channels) into.Add(kv.Value.Channel);
        }

        // ------------------------------------------------------------------ settings & hooks

        /// <summary>The settings store in use (null when persistence is disabled and none was set).</summary>
        public static ISoundSettingsStore SettingsStore => store;

        /// <summary>Plugs in the game's save system. Volumes and mutes are reloaded from it immediately.</summary>
        public static void SetSettingsStore(ISoundSettingsStore settingsStore) => SetSettingsStoreInternal(settingsStore);

        /// <summary>Delegate shortcut for <see cref="SetSettingsStore"/>. Return null from <paramref name="load"/> when nothing is saved.</summary>
        public static void BindSettings(Func<string, float?> load, Action<string, float> save,
            Func<string, bool?> loadMuted = null, Action<string, bool> saveMuted = null)
        {
            SetSettingsStoreInternal(new DelegateSoundSettingsStore(load, save, loadMuted, saveMuted));
        }

        /// <summary>Sets (or clears with null) the playback policy hook.</summary>
        public static void SetPolicy(ISoundPolicy soundPolicy) => policy = soundPolicy;

        /// <summary>Sets the bank provider used for per-scene banks (null restores the config-based default).</summary>
        public static void SetBankProvider(ISoundBankProvider provider) => bankProvider = provider ?? DefaultBankProvider;

        // ------------------------------------------------------------------ libraries

        /// <summary>Registers a cue library (e.g. per level). If an id already exists, the last registered library wins (logged once).</summary>
        public static void RegisterLibrary(SoundCueLibrary library) => RegisterLibraryInternal(library);

        /// <summary>Unregisters a library; ids it shadowed fall back to other registered libraries.</summary>
        public static void UnregisterLibrary(SoundCueLibrary library) => UnregisterLibraryInternal(library);

        /// <summary>True when a cue with this id is registered.</summary>
        public static bool HasCue(string cueId) => !string.IsNullOrEmpty(cueId) && cues.ContainsKey(cueId);

        /// <summary>Gets a registered cue by id.</summary>
        public static bool TryGetCue(string cueId, out SoundCue cue)
        {
            cue = null;
            if (string.IsNullOrEmpty(cueId) || !cues.TryGetValue(cueId, out CueState state)) return false;
            cue = state.Cue;
            return true;
        }

        // ------------------------------------------------------------------ banks

        /// <summary>
        /// Loads a bank (name without extension, e.g. "Level1"), reference counted. Bank metadata loads synchronously
        /// through FMOD's RuntimeManager (asynchronously on WebGL / Android OBB). Returns false on failure (logged once).
        /// </summary>
        public static bool LoadBank(string bankName, bool loadSampleData = false) => LoadBankInternal(bankName, loadSampleData);

        /// <summary>
        /// Loads a bank and invokes <paramref name="onComplete"/> (true on success) once it is available and, if
        /// requested, its sample data finished loading. The callback runs on the main thread, at the earliest next frame;
        /// it runs immediately with false when the request is rejected up front (not initialized, empty name, bank file
        /// missing). A load that has not finished after 30 s, or is still pending at <see cref="Shutdown"/>, completes with false.
        /// </summary>
        public static void LoadBankAsync(string bankName, Action<bool> onComplete = null, bool loadSampleData = false)
        {
            LoadBankAsyncInternal(bankName, onComplete, loadSampleData);
        }

        /// <summary>Releases one reference to a bank loaded with <see cref="LoadBank"/> / <see cref="LoadBankAsync"/>.</summary>
        public static void UnloadBank(string bankName) => UnloadBankInternal(bankName);

        /// <summary>True when FMOD has the bank loaded (through this API or not).</summary>
        public static bool IsBankLoaded(string bankName) => initialized && !string.IsNullOrEmpty(bankName) && FmodBackend.HasBankLoaded(bankName);
    }
}
