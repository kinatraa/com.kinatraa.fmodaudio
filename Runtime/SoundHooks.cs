using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace kinatraa.fmodaudio
{
    /// <summary>
    /// Where channel volumes and mutes are persisted. Plug your game's save system in with
    /// <see cref="Sound.SetSettingsStore"/> or <see cref="Sound.BindSettings"/>. Keys are channel names ("Music", "SFX"...).
    /// </summary>
    public interface ISoundSettingsStore
    {
        /// <summary>Returns true and the saved linear volume (0..1) for <paramref name="key"/> if one exists.</summary>
        bool TryLoad(string key, out float value);
        /// <summary>Saves a linear volume (0..1).</summary>
        void Save(string key, float value);
        /// <summary>Returns true and the saved mute state for <paramref name="key"/> if one exists.</summary>
        bool TryLoadMuted(string key, out bool muted);
        /// <summary>Saves a mute state.</summary>
        void SaveMuted(string key, bool muted);
    }

    /// <summary>Default store: PlayerPrefs with a configurable key prefix.</summary>
    public sealed class PlayerPrefsSoundSettingsStore : ISoundSettingsStore
    {
        /// <summary>Default key prefix.</summary>
        public const string DefaultPrefix = "kinatraa.fmodaudio.";

        private readonly string prefix;
        private readonly Dictionary<string, string> volumeKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> muteKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Creates a store whose keys are <c>prefix + key</c> (volume) and <c>prefix + key + ".muted"</c>.</summary>
        public PlayerPrefsSoundSettingsStore(string prefix = DefaultPrefix)
        {
            this.prefix = prefix ?? string.Empty;
        }

        // Keys are cached so saving every frame (e.g. while dragging a slider) does not allocate.
        private string VolumeKey(string key)
        {
            if (!volumeKeys.TryGetValue(key, out string k)) volumeKeys[key] = k = prefix + key;
            return k;
        }

        private string MuteKey(string key)
        {
            if (!muteKeys.TryGetValue(key, out string k)) muteKeys[key] = k = prefix + key + ".muted";
            return k;
        }

        /// <inheritdoc/>
        public bool TryLoad(string key, out float value)
        {
            string k = VolumeKey(key);
            value = PlayerPrefs.GetFloat(k, 1f);
            return PlayerPrefs.HasKey(k);
        }

        /// <inheritdoc/>
        public void Save(string key, float value) => PlayerPrefs.SetFloat(VolumeKey(key), value);

        /// <inheritdoc/>
        public bool TryLoadMuted(string key, out bool muted)
        {
            string k = MuteKey(key);
            muted = PlayerPrefs.GetInt(k, 0) != 0;
            return PlayerPrefs.HasKey(k);
        }

        /// <inheritdoc/>
        public void SaveMuted(string key, bool muted) => PlayerPrefs.SetInt(MuteKey(key), muted ? 1 : 0);
    }

    /// <summary>Adapter that turns delegates into an <see cref="ISoundSettingsStore"/> (see <see cref="Sound.BindSettings"/>).</summary>
    public sealed class DelegateSoundSettingsStore : ISoundSettingsStore
    {
        private readonly Func<string, float?> load;
        private readonly Action<string, float> save;
        private readonly Func<string, bool?> loadMuted;
        private readonly Action<string, bool> saveMuted;

        /// <summary>Creates the adapter. Return null from a load delegate when no value is saved. Any delegate may be null.</summary>
        public DelegateSoundSettingsStore(Func<string, float?> load, Action<string, float> save,
            Func<string, bool?> loadMuted = null, Action<string, bool> saveMuted = null)
        {
            this.load = load;
            this.save = save;
            this.loadMuted = loadMuted;
            this.saveMuted = saveMuted;
        }

        /// <inheritdoc/>
        public bool TryLoad(string key, out float value)
        {
            float? v = load?.Invoke(key);
            value = v ?? 1f;
            return v.HasValue;
        }

        /// <inheritdoc/>
        public void Save(string key, float value) => save?.Invoke(key, value);

        /// <inheritdoc/>
        public bool TryLoadMuted(string key, out bool muted)
        {
            bool? v = loadMuted?.Invoke(key);
            muted = v ?? false;
            return v.HasValue;
        }

        /// <inheritdoc/>
        public void SaveMuted(string key, bool muted) => saveMuted?.Invoke(key, muted);
    }

    /// <summary>A play request passed to <see cref="ISoundPolicy.OnBeforePlay"/>; the policy may modify it.</summary>
    public struct SoundPlayRequest
    {
        /// <summary>Cue id, or null for direct EventReference plays.</summary>
        public string CueId;
        /// <summary>Channel of the cue.</summary>
        public SoundChannel Channel;
        /// <summary>Instance volume (linear, 1 = unchanged). Modifiable.</summary>
        public float Volume;
        /// <summary>Instance pitch multiplier. Modifiable.</summary>
        public float Pitch;
    }

    /// <summary>
    /// Optional game hook that can veto or modify playback (e.g. no SFX during a cutscene) and react to focus changes.
    /// Set with <see cref="Sound.SetPolicy"/>.
    /// </summary>
    public interface ISoundPolicy
    {
        /// <summary>Called before every play. Return false to veto. Volume/pitch in <paramref name="request"/> can be changed.</summary>
        bool OnBeforePlay(ref SoundPlayRequest request);
        /// <summary>Called when the application gains or loses focus (after the configured focus behaviour runs).</summary>
        void OnFocusChanged(bool hasFocus);
    }

    /// <summary>
    /// Optional game hook that decides which banks load with which scene. Set with <see cref="Sound.SetBankProvider"/>.
    /// The default provider uses <see cref="SoundSystemConfig.SceneBanks"/>.
    /// </summary>
    public interface ISoundBankProvider
    {
        /// <summary>Adds the names of the banks needed while <paramref name="scene"/> is loaded to <paramref name="banks"/>.</summary>
        void GetBanksForScene(Scene scene, List<string> banks);
    }

    /// <summary>Marks a string field as a cue id: the inspector shows a dropdown of registered cue ids.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SoundCueIdAttribute : PropertyAttribute
    {
    }
}
