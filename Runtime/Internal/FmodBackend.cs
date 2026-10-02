using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace kinatraa.fmodaudio
{
    /// <summary>
    /// The only code in the package that calls FMOD APIs. Written against FMOD for Unity 2.03.15.
    /// If FMOD changes its API, this is the file to fix. Everything here is main-thread only and never throws.
    /// </summary>
    internal static class FmodBackend
    {
        internal const string MasterBusPath = "bus:/";

        private static FMOD.Studio.System studio;
        private static readonly Dictionary<FMOD.GUID, EventDescription> eventsByGuid = new Dictionary<FMOD.GUID, EventDescription>(new GuidComparer());
        private static readonly Dictionary<string, EventDescription> eventsByPath = new Dictionary<string, EventDescription>(StringComparer.Ordinal);

        internal static void Reset()
        {
            studio.clearHandle();
            eventsByGuid.Clear();
            eventsByPath.Clear();
        }

        // ---------------------------------------------------------------- system

        /// <summary>Initializes FMOD through RuntimeManager (which loads the banks configured in FMOD settings).</summary>
        internal static bool Initialize(out string error)
        {
            error = null;
            try
            {
                studio = RuntimeManager.StudioSystem;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            if (!studio.isValid())
            {
                error = "FMOD Studio system handle is invalid.";
                return false;
            }
            return true;
        }

        /// <summary>True while FMOD's RuntimeManager exists. Never creates it.</summary>
        internal static bool IsAlive => RuntimeManager.IsInitialized && studio.isValid();

        /// <summary>
        /// True when FMOD has no bank load in flight. RuntimeManager loads bank files synchronously except on WebGL and
        /// Android with OBB, so its counter is only consulted there. (In 2.03.15 that counter also stays incremented after
        /// a failed LoadBank, which would otherwise block readiness forever.)
        /// </summary>
        internal static bool AllBanksLoaded
        {
            get
            {
                if (!IsAlive) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
                return RuntimeManager.HaveAllBanksLoaded;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return !Settings.Instance.AndroidUseOBB || RuntimeManager.HaveAllBanksLoaded;
#else
                return true;
#endif
            }
        }

        internal static string VersionString
        {
            get
            {
                uint v = FMOD.VERSION.number;
                return $"{(v >> 16) & 0xFFFF:X}.{(v >> 8) & 0xFF:X2}.{v & 0xFF:X2}";
            }
        }

        // ---------------------------------------------------------------- events

        internal static bool IsNull(EventReference reference) => reference.IsNull;

        internal static string Describe(EventReference reference) => reference.ToString();

        internal static bool TryGetEvent(EventReference reference, out EventDescription description)
        {
            if (reference.IsNull)
            {
                description = default;
                return false;
            }
            return TryGetEvent(reference.Guid, out description);
        }

        internal static bool TryGetEvent(FMOD.GUID guid, out EventDescription description)
        {
            if (eventsByGuid.TryGetValue(guid, out description) && description.isValid()) return true;
            if (!IsAlive || studio.getEventByID(guid, out description) != FMOD.RESULT.OK || !description.isValid())
            {
                description = default;
                return false;
            }
            eventsByGuid[guid] = description;
            return true;
        }

        internal static bool TryGetEvent(string path, out EventDescription description)
        {
            if (eventsByPath.TryGetValue(path, out description) && description.isValid()) return true;
            if (!IsAlive || studio.getEvent(path, out description) != FMOD.RESULT.OK || !description.isValid())
            {
                description = default;
                return false;
            }
            eventsByPath[path] = description;
            return true;
        }

        internal static bool IsValid(EventDescription description) => description.isValid();

        internal static bool Is3D(EventDescription description)
        {
            description.is3D(out bool is3D);
            return is3D;
        }

        internal static bool IsOneShot(EventDescription description)
        {
            description.isOneshot(out bool oneShot);
            return oneShot;
        }

        internal static string GetPath(EventDescription description)
        {
            if (description.getPath(out string path) == FMOD.RESULT.OK && !string.IsNullOrEmpty(path)) return path;
            return description.getID(out FMOD.GUID guid) == FMOD.RESULT.OK ? guid.ToString() : "(unknown)";
        }

        internal static int GetInstanceCount(EventDescription description)
        {
            return description.getInstanceCount(out int count) == FMOD.RESULT.OK ? count : 0;
        }

        internal static bool TryGetParameterId(EventDescription description, string name, out SoundParameterId id)
        {
            if (description.isValid() && description.getParameterDescriptionByName(name, out PARAMETER_DESCRIPTION p) == FMOD.RESULT.OK)
            {
                id = new SoundParameterId(p.id.data1, p.id.data2);
                return true;
            }
            id = default;
            return false;
        }

        // ---------------------------------------------------------------- instances

        internal static bool CreateInstance(EventDescription description, out EventInstance instance)
        {
            return description.createInstance(out instance) == FMOD.RESULT.OK && instance.isValid();
        }

        internal static void Start(EventInstance instance) => instance.start();

        internal static void Release(EventInstance instance) => instance.release();

        internal static void Stop(EventInstance instance, bool allowFadeOut)
        {
            instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        internal static bool IsValid(EventInstance instance) => instance.isValid();

        internal static SoundPlaybackState GetState(EventInstance instance)
        {
            if (!instance.isValid() || instance.getPlaybackState(out PLAYBACK_STATE state) != FMOD.RESULT.OK) return SoundPlaybackState.Stopped;
            switch (state)
            {
                case PLAYBACK_STATE.PLAYING: return SoundPlaybackState.Playing;
                case PLAYBACK_STATE.SUSTAINING: return SoundPlaybackState.Sustaining;
                case PLAYBACK_STATE.STARTING: return SoundPlaybackState.Starting;
                case PLAYBACK_STATE.STOPPING: return SoundPlaybackState.Stopping;
                default: return SoundPlaybackState.Stopped;
            }
        }

        internal static bool GetPaused(EventInstance instance)
        {
            instance.getPaused(out bool paused);
            return paused;
        }

        internal static void SetPaused(EventInstance instance, bool paused) => instance.setPaused(paused);

        internal static void SetVolume(EventInstance instance, float volume) => instance.setVolume(volume);

        internal static void SetPitch(EventInstance instance, float pitch) => instance.setPitch(pitch);

        internal static bool SetParameter(EventInstance instance, string name, float value)
        {
            return instance.setParameterByName(name, value) == FMOD.RESULT.OK;
        }

        internal static bool SetParameter(EventInstance instance, SoundParameterId id, float value)
        {
            PARAMETER_ID pid = new PARAMETER_ID { data1 = id.Data1, data2 = id.Data2 };
            return instance.setParameterByID(pid, value) == FMOD.RESULT.OK;
        }

        internal static bool TryGetParameterId(EventInstance instance, string name, out SoundParameterId id)
        {
            if (instance.isValid() && instance.getDescription(out EventDescription d) == FMOD.RESULT.OK) return TryGetParameterId(d, name, out id);
            id = default;
            return false;
        }

        internal static void Set3D(EventInstance instance, Vector3 position) => instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));

        internal static void Set3D(EventInstance instance, Transform transform) => instance.set3DAttributes(RuntimeUtils.To3DAttributes(transform));

        /// <summary>Places a 3D event on listener 0 so a "2D" play of a 3D event is heard without attenuation.</summary>
        internal static void Set3DAtListener(EventInstance instance)
        {
            if (IsAlive && studio.getListenerAttributes(0, out FMOD.ATTRIBUTES_3D attributes) == FMOD.RESULT.OK)
            {
                instance.set3DAttributes(attributes);
            }
        }

        /// <summary>Debugger helper (allocates): fills local parameter names/values of a live instance.</summary>
        internal static void GetParameters(EventInstance instance, List<KeyValuePair<string, float>> into)
        {
            into.Clear();
            if (!instance.isValid() || instance.getDescription(out EventDescription d) != FMOD.RESULT.OK) return;
            d.getParameterDescriptionCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (d.getParameterDescriptionByIndex(i, out PARAMETER_DESCRIPTION p) != FMOD.RESULT.OK) continue;
                if ((p.flags & (PARAMETER_FLAGS.READONLY | PARAMETER_FLAGS.AUTOMATIC | PARAMETER_FLAGS.GLOBAL)) != 0) continue;
                instance.getParameterByID(p.id, out float value);
                into.Add(new KeyValuePair<string, float>(p.name, value));
            }
        }

        internal static bool TryGetDescription(EventInstance instance, out EventDescription description)
        {
            description = default;
            return instance.isValid() && instance.getDescription(out description) == FMOD.RESULT.OK;
        }

        // ---------------------------------------------------------------- buses

        internal static bool TryGetBus(string path, out Bus bus)
        {
            bus = default;
            return IsAlive && studio.getBus(path, out bus) == FMOD.RESULT.OK && bus.isValid();
        }

        internal static bool IsValid(Bus bus) => bus.isValid();

        internal static void SetBusVolume(Bus bus, float volume) => bus.setVolume(volume);

        internal static void SetBusMuted(Bus bus, string path, bool muted)
        {
            // In the editor RuntimeManager re-applies the master-bus mute every frame from its own flag,
            // so the master bus is muted through RuntimeManager to avoid fighting it.
            if (path == MasterBusPath)
            {
                if (IsAlive) RuntimeManager.MuteAllEvents(muted);
                return;
            }
            bus.setMute(muted);
        }

        internal static void SetBusPaused(Bus bus, bool paused) => bus.setPaused(paused);

        internal static void StopBus(Bus bus, bool allowFadeOut)
        {
            bus.stopAllEvents(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        internal static void SetMasterPaused(bool paused)
        {
            if (TryGetBus(MasterBusPath, out Bus master)) master.setPaused(paused);
        }

        // ---------------------------------------------------------------- global parameters

        internal static bool SetGlobalParameter(string name, float value)
        {
            return IsAlive && studio.setParameterByName(name, value) == FMOD.RESULT.OK;
        }

        internal static bool TryGetGlobalParameter(string name, out float value)
        {
            value = 0f;
            return IsAlive && studio.getParameterByName(name, out value) == FMOD.RESULT.OK;
        }

        internal struct GlobalParameterInfo
        {
            public string Name;
            public float Value;
            public float Min;
            public float Max;
        }

        /// <summary>Debugger helper (allocates).</summary>
        internal static void GetGlobalParameters(List<GlobalParameterInfo> into)
        {
            into.Clear();
            if (!IsAlive || studio.getParameterDescriptionList(out PARAMETER_DESCRIPTION[] list) != FMOD.RESULT.OK) return;
            foreach (PARAMETER_DESCRIPTION p in list)
            {
                if ((p.flags & PARAMETER_FLAGS.READONLY) != 0) continue;
                studio.getParameterByID(p.id, out float value);
                into.Add(new GlobalParameterInfo { Name = p.name, Value = value, Min = p.minimum, Max = p.maximum });
            }
        }

        // ---------------------------------------------------------------- banks

        /// <summary>Loads a bank through RuntimeManager (reference counted, honours FMOD's bank folder settings).</summary>
        internal static bool LoadBank(string name, bool loadSamples, out string error)
        {
            error = null;
            if (!IsAlive)
            {
                error = "FMOD is not initialized.";
                return false;
            }
            try
            {
                RuntimeManager.LoadBank(name, loadSamples);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        internal static void UnloadBank(string name)
        {
            if (!IsAlive) return;
            try
            {
                RuntimeManager.UnloadBank(name);
            }
            catch (Exception e)
            {
                SoundLog.Warning($"Unloading bank '{name}' failed: {e.Message}");
            }
        }

        internal static bool HasBankLoaded(string name) => IsAlive && RuntimeManager.HasBankLoaded(name);

        /// <summary>True while sample data of a loaded bank is still loading. Needs the strings bank for the lookup.</summary>
        internal static bool IsSampleDataLoading(string name)
        {
            if (!IsAlive || studio.getBank("bank:/" + name, out Bank bank) != FMOD.RESULT.OK) return false;
            return bank.getSampleLoadingState(out LOADING_STATE state) == FMOD.RESULT.OK && state == LOADING_STATE.LOADING;
        }

        /// <summary>Bank names listed in the FMOD for Unity settings (master banks first, strings before master).</summary>
        internal static void GetBanksFromFmodSettings(List<string> into)
        {
            Settings settings = Settings.Instance;
            if (settings == null) return;
            foreach (string master in settings.MasterBanks)
            {
                into.Add(master + ".strings");
                into.Add(master);
            }
            foreach (string bank in settings.Banks) into.Add(bank);
        }

        /// <summary>Debugger helper (allocates): paths of all banks loaded in FMOD.</summary>
        internal static void GetLoadedBankPaths(List<string> into)
        {
            into.Clear();
            if (!IsAlive || studio.getBankList(out Bank[] banks) != FMOD.RESULT.OK) return;
            foreach (Bank b in banks)
            {
                into.Add(b.getPath(out string path) == FMOD.RESULT.OK && !string.IsNullOrEmpty(path) ? path : "(unnamed bank)");
            }
        }

        // ---------------------------------------------------------------- listeners

        internal static int ListenerCount => StudioListener.ListenerCount;

        internal static void AddListener(GameObject target)
        {
            if (target.GetComponent<StudioListener>() == null) target.AddComponent<StudioListener>();
        }

        internal static bool HasListener(GameObject target) => target.GetComponent<StudioListener>() != null;

        private sealed class GuidComparer : IEqualityComparer<FMOD.GUID>
        {
            public bool Equals(FMOD.GUID x, FMOD.GUID y) => x.Equals(y);
            public int GetHashCode(FMOD.GUID obj) => obj.GetHashCode();
        }
    }
}
