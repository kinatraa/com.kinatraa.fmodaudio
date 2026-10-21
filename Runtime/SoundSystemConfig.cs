using System;
using System.Collections.Generic;
using UnityEngine;

namespace kinatraa.FmodAudio
{
    /// <summary>Maps a channel name to an FMOD bus.</summary>
    [Serializable]
    public class SoundChannelDefinition
    {
        /// <summary>Channel name used from code and as the settings key (e.g. "Music").</summary>
        public string Name;
        /// <summary>FMOD bus path. Defaults to <c>bus:/&lt;Name&gt;</c>; the master bus is <c>bus:/</c>.</summary>
        public string BusPath;
        /// <summary>Linear volume (0..1) used when the settings store has no saved value.</summary>
        [Range(0f, 1f)] public float DefaultVolume = 1f;

        /// <summary>Creates an empty channel definition (for serialization).</summary>
        public SoundChannelDefinition() { }

        /// <summary>Creates a channel definition.</summary>
        public SoundChannelDefinition(string name, string busPath, float defaultVolume = 1f)
        {
            Name = name;
            BusPath = busPath;
            DefaultVolume = defaultVolume;
        }
    }

    /// <summary>Banks loaded while a scene is loaded and unloaded with it.</summary>
    [Serializable]
    public class SceneBankSet
    {
        /// <summary>Scene name (not path).</summary>
        public string Scene;
        /// <summary>Bank names, without extension (e.g. "Level1").</summary>
        public List<string> Banks = new List<string>();
    }

    /// <summary>Maps a short snapshot key (e.g. "Pause") to an FMOD snapshot path.</summary>
    [Serializable]
    public class SnapshotMapping
    {
        /// <summary>Key used from code, e.g. <c>Sound.PushSnapshot("Pause")</c>.</summary>
        public string Key;
        /// <summary>FMOD snapshot path, e.g. <c>snapshot:/Pause</c>.</summary>
        public string Path;

        /// <summary>Creates an empty mapping (for serialization).</summary>
        public SnapshotMapping() { }

        /// <summary>Creates a mapping.</summary>
        public SnapshotMapping(string key, string path) { Key = key; Path = path; }
    }

    /// <summary>How <see cref="Sound.PauseAll"/> pauses audio.</summary>
    public enum SoundPauseMode
    {
        /// <summary>Pauses the FMOD master bus (everything stops advancing, including UI and music).</summary>
        MasterBus,
        /// <summary>Starts the snapshot mapped to the "Pause" key; nothing is paused (the mix changes).</summary>
        Snapshot,
        /// <summary>Both: pauses the master bus and starts the "Pause" snapshot.</summary>
        MasterBusAndSnapshot,
    }

    /// <summary>What happens when the application loses focus.</summary>
    public enum SoundFocusLossBehavior
    {
        /// <summary>Nothing (FMOD for Unity still suspends the mixer when the app is paused on mobile).</summary>
        None,
        /// <summary>Calls <see cref="Sound.PauseAll"/> on focus loss and <see cref="Sound.ResumeAll"/> on focus gain.</summary>
        Pause,
        /// <summary>Mutes the master bus while unfocused.</summary>
        Mute,
    }

    /// <summary>Log verbosity of the sound system.</summary>
    public enum SoundLogLevel
    {
        /// <summary>No logs.</summary>
        None,
        /// <summary>Errors only.</summary>
        Error,
        /// <summary>Errors and warnings (default).</summary>
        Warning,
        /// <summary>Everything, including informational messages.</summary>
        Info,
    }

    /// <summary>
    /// Project-wide configuration. Put it at <c>Resources/kinatraaFmodAudioConfig</c> (the Setup Wizard creates it)
    /// to have it picked up automatically, or pass it to <see cref="Sound.Initialize"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "kinatraa/FMOD Audio/Sound System Config", fileName = ResourcesName, order = 0)]
    public class SoundSystemConfig : ScriptableObject
    {
        /// <summary>Resources path the auto-bootstrap loads the config from.</summary>
        public const string ResourcesName = "kinatraaFmodAudioConfig";

        /// <summary>When false, the auto-bootstrap skips this config; call <see cref="Sound.Initialize"/> yourself.</summary>
        public bool AutoInitialize = true;

        /// <summary>Channels mapped to FMOD buses. Add entries to define custom channels without code changes.</summary>
        public List<SoundChannelDefinition> Channels = CreateDefaultChannels();

        /// <summary>Cue libraries registered at startup.</summary>
        public List<SoundCueLibrary> Libraries = new List<SoundCueLibrary>();

        /// <summary>Banks loaded at startup and kept loaded (bank names without extension).</summary>
        public List<string> Banks = new List<string> { "Master.strings", "Master" };

        /// <summary>Banks loaded and unloaded together with a scene (used by the default bank provider).</summary>
        public List<SceneBankSet> SceneBanks = new List<SceneBankSet>();

        /// <summary>Loads every bank listed in the FMOD settings at startup. Convenient for small games.</summary>
        public bool LoadAllBanksOnStart;

        /// <summary>Snapshot keys usable with <see cref="Sound.PushSnapshot"/>. "Pause" is used by <see cref="Sound.PauseAll"/>.</summary>
        public List<SnapshotMapping> Snapshots = new List<SnapshotMapping>
        {
            new SnapshotMapping("Pause", "snapshot:/Pause"),
            new SnapshotMapping("Ducking", "snapshot:/Ducking"),
        };

        /// <summary>How <see cref="Sound.PauseAll"/> works.</summary>
        public SoundPauseMode PauseMode = SoundPauseMode.MasterBus;

        /// <summary>What happens when the application loses focus.</summary>
        public SoundFocusLossBehavior FocusLossBehavior = SoundFocusLossBehavior.None;

        /// <summary>When true and no custom store is set, volumes/mutes persist with <see cref="PlayerPrefsSoundSettingsStore"/>.</summary>
        public bool PersistSettings = true;

        /// <summary>Key prefix used by the default PlayerPrefs settings store.</summary>
        public string SettingsKeyPrefix = PlayerPrefsSoundSettingsStore.DefaultPrefix;

        /// <summary>Log verbosity.</summary>
        public SoundLogLevel LogLevel = SoundLogLevel.Warning;

        /// <summary>Looks up the snapshot path mapped to <paramref name="key"/>.</summary>
        public bool TryGetSnapshotPath(string key, out string path)
        {
            for (int i = 0; i < Snapshots.Count; i++)
            {
                SnapshotMapping m = Snapshots[i];
                if (m != null && string.Equals(m.Key, key, StringComparison.Ordinal) && !string.IsNullOrEmpty(m.Path))
                {
                    path = m.Path;
                    return true;
                }
            }
            path = null;
            return false;
        }

        /// <summary>The built-in channels: Master, Music, SFX, UI, Voice, Ambient.</summary>
        public static List<SoundChannelDefinition> CreateDefaultChannels()
        {
            return new List<SoundChannelDefinition>
            {
                new SoundChannelDefinition("Master", "bus:/"),
                new SoundChannelDefinition("Music", "bus:/Music"),
                new SoundChannelDefinition("SFX", "bus:/SFX"),
                new SoundChannelDefinition("UI", "bus:/UI"),
                new SoundChannelDefinition("Voice", "bus:/Voice"),
                new SoundChannelDefinition("Ambient", "bus:/Ambient"),
            };
        }

        /// <summary>Creates an in-memory config with default values (not saved as an asset).</summary>
        public static SoundSystemConfig CreateDefault()
        {
            SoundSystemConfig config = CreateInstance<SoundSystemConfig>();
            config.name = "kinatraaFmodAudioConfig (defaults)";
            config.hideFlags = HideFlags.DontSave;
            return config;
        }
    }
}
