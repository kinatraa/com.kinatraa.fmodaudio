using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace kinatraa.fmodaudio
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

    /// <summary>Loads banks for the lifetime of this object (put it in a scene for scene lifetime). Reference counted.</summary>
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

        /// <summary>True between <see cref="Load"/> and <see cref="Unload"/>.</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>Loads the banks (no-op if already loaded by this component).</summary>
        public void Load()
        {
            if (IsLoaded) return;
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
            if (LoadOn == SoundBankLoadTiming.OnEnable) Load();
        }

        private void Start()
        {
            if (LoadOn == SoundBankLoadTiming.Start) Load();
        }

        private void OnDisable()
        {
            if (UnloadOn == SoundBankUnloadTiming.OnDisable) Unload();
        }

        private void OnDestroy()
        {
            if (UnloadOn == SoundBankUnloadTiming.OnDestroy) Unload();
        }
    }
}
