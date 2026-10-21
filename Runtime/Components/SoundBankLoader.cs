using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace kinatraa.FmodAudio
{
    /// <summary>When a <see cref="SoundBankLoader"/> loads its banks.</summary>
    public enum SoundBankLoadTiming
    {
        /// <summary>In Awake.</summary>
        Awake,
        /// <summary>In OnEnable.</summary>
        OnEnable,
        /// <summary>In Start.</summary>
        Start,
        /// <summary>Only when <see cref="SoundBankLoader.Load"/> is called.</summary>
        Manual,
    }

    /// <summary>When a <see cref="SoundBankLoader"/> unloads its banks.</summary>
    public enum SoundBankUnloadTiming
    {
        /// <summary>In OnDestroy (object or scene lifetime).</summary>
        OnDestroy,
        /// <summary>In OnDisable.</summary>
        OnDisable,
        /// <summary>Only when <see cref="SoundBankLoader.Unload"/> is called.</summary>
        Manual,
    }

    /// <summary>
    /// Loads banks for the lifetime of this object (put it in a scene for scene lifetime). Reference counted, so several
    /// loaders (or <see cref="Sound.LoadBank"/> calls) can share a bank. With <see cref="UnloadOn"/> = OnDisable the banks
    /// are loaded again when the object is re-enabled.
    /// </summary>
    [AddComponentMenu("kinatraa/FMOD Audio/Sound Bank Loader")]
    public class SoundBankLoader : MonoBehaviour
    {
        /// <summary>Bank names (without extension).</summary>
        [BankRef] public List<string> Banks = new List<string>();
        /// <summary>When to load.</summary>
        public SoundBankLoadTiming LoadOn = SoundBankLoadTiming.Awake;
        /// <summary>When to unload.</summary>
        public SoundBankUnloadTiming UnloadOn = SoundBankUnloadTiming.OnDestroy;
        /// <summary>Also load sample data (avoids a first-play delay for non-streaming events).</summary>
        public bool LoadSampleData;

        private readonly List<string> loaded = new List<string>();
        private bool reloadOnEnable;

        /// <summary>True between a successful <see cref="Load"/> and <see cref="Unload"/>.</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>The banks this component currently holds a reference to (empty when not loaded).</summary>
        public IReadOnlyList<string> LoadedBanks => loaded;

        /// <summary>
        /// Loads the banks (no-op if already loaded by this component). Does nothing, with a warning, while the sound
        /// system is not initialized, so a later call can still load them.
        /// </summary>
        public void Load()
        {
            if (IsLoaded) return;
            if (!Sound.IsInitialized)
            {
                SoundLog.Warning($"SoundBankLoader on '{name}': the sound system is not initialized, banks were not loaded.");
                return;
            }
            IsLoaded = true;
            foreach (string bank in Banks)
            {
                if (!string.IsNullOrEmpty(bank) && Sound.LoadBank(bank, LoadSampleData)) loaded.Add(bank);
            }
        }

        /// <summary>Releases the banks this component loaded.</summary>
        public void Unload()
        {
            if (!IsLoaded) return;
            IsLoaded = false;
            foreach (string bank in loaded) Sound.UnloadBank(bank);
            loaded.Clear();
        }

        private void Awake()
        {
            if (LoadOn == SoundBankLoadTiming.Awake) Load();
        }

        private void OnEnable()
        {
            if (LoadOn == SoundBankLoadTiming.OnEnable || reloadOnEnable) Load();
            reloadOnEnable = false;
        }

        private void Start()
        {
            if (LoadOn == SoundBankLoadTiming.Start) Load();
        }

        private void OnDisable()
        {
            if (UnloadOn != SoundBankUnloadTiming.OnDisable || !IsLoaded) return;
            Unload();
            // Awake/Start only run once: reload on the next enable so disable/enable cycles keep the banks loaded.
            reloadOnEnable = LoadOn == SoundBankLoadTiming.Awake || LoadOn == SoundBankLoadTiming.Start;
        }

        private void OnDestroy()
        {
            if (UnloadOn == SoundBankUnloadTiming.OnDestroy) Unload();
        }
    }
}
