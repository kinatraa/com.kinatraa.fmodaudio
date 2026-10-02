using System;
using System.Collections.Generic;
using FMODUnity;

namespace kinatraa.fmodaudio
{
    /// <summary>What happens when a cue is played while it already has <see cref="SoundCue.MaxInstances"/> live instances.</summary>
    public enum SoundInstanceLimitPolicy
    {
        /// <summary>The new play request is ignored.</summary>
        Reject,
        /// <summary>The oldest live instance is stopped (with fade-out) and the new one plays.</summary>
        StopOldest,
    }

    /// <summary>A named parameter value applied to an event instance.</summary>
    [Serializable]
    public struct SoundParameterValue
    {
        /// <summary>FMOD parameter name, as authored in FMOD Studio.</summary>
        public string Name;
        /// <summary>Value to apply.</summary>
        public float Value;

        /// <summary>Creates a parameter value.</summary>
        public SoundParameterValue(string name, float value) { Name = name; Value = value; }
    }

    /// <summary>
    /// A playable sound: a stable string id mapped to an FMOD event (by GUID, never by path) plus playback rules.
    /// Cues live inside a <see cref="SoundCueLibrary"/>.
    /// </summary>
    [Serializable]
    public class SoundCue
    {
        /// <summary>Unique id used from code, e.g. <c>Sound.Play("ui_click")</c>.</summary>
        public string Id;

        /// <summary>The FMOD event. Stored as a GUID so renaming/moving the event in FMOD Studio does not break it.</summary>
        public EventReference Event;

        /// <summary>Channel used for <see cref="Sound.StopChannel"/>, filtering and the debugger.</summary>
        public SoundChannel Channel = SoundChannel.SFX;

        /// <summary>Parameter values applied to every new instance before it starts.</summary>
        public List<SoundParameterValue> DefaultParameters = new List<SoundParameterValue>();

        /// <summary>Minimum seconds (unscaled) between two plays of this cue. 0 disables the cooldown.</summary>
        public float Cooldown;

        /// <summary>Maximum simultaneous live instances. 0 means unlimited.</summary>
        public int MaxInstances;

        /// <summary>Behaviour when <see cref="MaxInstances"/> is reached.</summary>
        public SoundInstanceLimitPolicy LimitPolicy = SoundInstanceLimitPolicy.Reject;

        /// <summary>
        /// When true (default, intended for non-looping events) the FMOD instance is released right after it starts,
        /// so FMOD destroys it as soon as it finishes. When false the instance is held and released on stop/finish.
        /// </summary>
        public bool AutoRelease = true;

        /// <summary>Free-form tags used for filtering in the editor.</summary>
        public List<string> Tags = new List<string>();
    }
}
