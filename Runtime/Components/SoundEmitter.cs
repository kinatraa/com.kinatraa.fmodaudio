using System.Collections.Generic;
using UnityEngine;

namespace kinatraa.FmodAudio
{
    /// <summary>When a <see cref="SoundEmitter"/> plays automatically.</summary>
    public enum SoundEmitterTrigger
    {
        /// <summary>Only when <see cref="SoundEmitter.Play"/> is called (UnityEvent, animation event, script).</summary>
        None,
        /// <summary>In Start (after per-scene banks are loaded). Recommended default.</summary>
        Start,
        /// <summary>Every OnEnable.</summary>
        OnEnable,
        /// <summary>OnTriggerEnter (3D physics), filtered by <see cref="SoundEmitter.TriggerTag"/>.</summary>
        TriggerEnter,
        /// <summary>OnTriggerEnter2D (2D physics), filtered by <see cref="SoundEmitter.TriggerTag"/>.</summary>
        TriggerEnter2D,
    }

    /// <summary>Where a <see cref="SoundEmitter"/> plays its cue.</summary>
    public enum SoundEmitterSpace
    {
        /// <summary>Attached to this transform (position follows it).</summary>
        Attached,
        /// <summary>At this transform's position when played.</summary>
        AtPosition,
        /// <summary>2D.</summary>
        TwoD,
    }

    /// <summary>When a <see cref="SoundEmitter"/> stops its last sound automatically.</summary>
    public enum SoundEmitterStop
    {
        /// <summary>Never (one-shots finish on their own).</summary>
        None,
        /// <summary>In OnDisable.</summary>
        OnDisable,
        /// <summary>In OnDestroy.</summary>
        OnDestroy,
    }

    /// <summary>
    /// Plays a cue on Start / enable / trigger, or when <see cref="Play"/> / <see cref="PlayCue"/> is called from a
    /// UnityEvent or an animation event. Tracks the last handle it started.
    /// </summary>
    [AddComponentMenu("kinatraa/FMOD Audio/Sound Emitter")]
    public class SoundEmitter : MonoBehaviour
    {
        /// <summary>Cue to play.</summary>
        [SoundCueId] public string CueId;
        /// <summary>When to play automatically.</summary>
        public SoundEmitterTrigger PlayOn = SoundEmitterTrigger.Start;
        /// <summary>Optional tag filter for trigger modes (empty = any collider).</summary>
        public string TriggerTag;
        /// <summary>Where to play.</summary>
        public SoundEmitterSpace Space = SoundEmitterSpace.Attached;
        /// <summary>When to stop automatically.</summary>
        public SoundEmitterStop StopOn = SoundEmitterStop.OnDisable;
        /// <summary>Use FMOD fade-out (AHDSR release) when stopping.</summary>
        public bool AllowFadeOut = true;
        /// <summary>Parameters applied after each play (on top of the cue defaults).</summary>
        public List<SoundParameterValue> Parameters = new List<SoundParameterValue>();

        /// <summary>Handle of the last sound this emitter started.</summary>
        public SoundHandle Handle { get; private set; }

        /// <summary>Plays <see cref="CueId"/>. Callable from UnityEvents and animation events.</summary>
        public void Play() => PlayCue(CueId);

        /// <summary>Plays another cue from this emitter (handy for animation events with a string parameter).</summary>
        public void PlayCue(string cueId)
        {
            SoundHandle handle;
            switch (Space)
            {
                case SoundEmitterSpace.Attached: handle = Sound.Play(cueId, transform); break;
                case SoundEmitterSpace.AtPosition: handle = Sound.Play(cueId, transform.position); break;
                default: handle = Sound.Play(cueId); break;
            }
            for (int i = 0; i < Parameters.Count; i++) handle.SetParameter(Parameters[i].Name, Parameters[i].Value);
            Handle = handle;
        }

        /// <summary>Stops the last sound started by this emitter.</summary>
        public void Stop() => Handle.Stop(AllowFadeOut);

        /// <summary>Sets a parameter on the last sound started by this emitter.</summary>
        public void SetParameter(string parameterName, float value) => Handle.SetParameter(parameterName, value);

        private void OnEnable()
        {
            if (PlayOn == SoundEmitterTrigger.OnEnable) Play();
        }

        private void Start()
        {
            if (PlayOn == SoundEmitterTrigger.Start) Play();
        }

        private void OnDisable()
        {
            if (StopOn == SoundEmitterStop.OnDisable) Stop();
        }

        private void OnDestroy()
        {
            if (StopOn == SoundEmitterStop.OnDestroy) Stop();
        }

        private bool PassesTagFilter(Component other) => string.IsNullOrEmpty(TriggerTag) || other.CompareTag(TriggerTag);

#if kinatraa_fmodaudio_physics
        private void OnTriggerEnter(Collider other)
        {
            if (PlayOn == SoundEmitterTrigger.TriggerEnter && PassesTagFilter(other)) Play();
        }
#endif

#if kinatraa_fmodaudio_physics2d
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlayOn == SoundEmitterTrigger.TriggerEnter2D && PassesTagFilter(other)) Play();
        }
#endif
    }
}
