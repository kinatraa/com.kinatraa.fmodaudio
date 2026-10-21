using System;

namespace kinatraa.FmodAudio
{
    /// <summary>Playback state of a sound, mirroring FMOD's playback states.</summary>
    public enum SoundPlaybackState
    {
        /// <summary>Not playing (finished, stopped, or the handle is invalid).</summary>
        Stopped,
        /// <summary>Queued until FMOD banks finish loading.</summary>
        Queued,
        /// <summary>Starting (FMOD has not processed the start yet).</summary>
        Starting,
        /// <summary>Playing.</summary>
        Playing,
        /// <summary>Held at a sustain point.</summary>
        Sustaining,
        /// <summary>Fading out after a stop request.</summary>
        Stopping,
    }

    /// <summary>
    /// A recycle-safe reference to a playing sound. Handles carry a generation id: once the sound finishes or is
    /// stopped, the handle becomes stale and every call on it is a safe no-op. <c>default(SoundHandle)</c> is invalid.
    /// Main thread only.
    /// </summary>
    public readonly struct SoundHandle : IEquatable<SoundHandle>
    {
        internal readonly int Index;      // slot index + 1, so default is invalid
        internal readonly int Generation;

        internal SoundHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <summary>An invalid handle.</summary>
        public static SoundHandle Invalid => default;

        /// <summary>True while the sound is queued, playing, paused or fading out.</summary>
        public bool IsValid => Sound.HandleIsValid(this);

        /// <summary>True while the sound is audible: started, not paused, not stopped.</summary>
        public bool IsPlaying => Sound.HandleIsPlaying(this);

        /// <summary>True when the sound is paused through <see cref="Pause"/>.</summary>
        public bool IsPaused => Sound.HandleIsPaused(this);

        /// <summary>Current playback state.</summary>
        public SoundPlaybackState State => Sound.HandleGetState(this);

        /// <summary>Cue id this sound was played from, or null for direct EventReference plays / invalid handles.</summary>
        public string CueId => Sound.HandleGetCueId(this);

        /// <summary>Stops the sound. With <paramref name="allowFadeOut"/> FMOD plays its AHDSR release / fade-out.</summary>
        public void Stop(bool allowFadeOut = true) => Sound.HandleStopPublic(this, allowFadeOut);

        /// <summary>Pauses the sound.</summary>
        public void Pause() => Sound.HandleSetPaused(this, true);

        /// <summary>Resumes a paused sound.</summary>
        public void Resume() => Sound.HandleSetPaused(this, false);

        /// <summary>Sets a local event parameter by name. Logs once if the parameter does not exist.</summary>
        public void SetParameter(string name, float value) => Sound.HandleSetParameter(this, name, value);

        /// <summary>Sets a local event parameter by id (faster than by name). See <see cref="GetParameterId"/>.</summary>
        public void SetParameter(SoundParameterId id, float value) => Sound.HandleSetParameter(this, id, value);

        /// <summary>Looks up a parameter id on this sound's event. Returns an invalid id if not found.</summary>
        public SoundParameterId GetParameterId(string name) => Sound.HandleGetParameterId(this, name);

        /// <summary>Reads a local event parameter (the value set by code, or the queued value). False when it does not exist.</summary>
        public bool TryGetParameter(string name, out float value) => Sound.HandleTryGetParameter(this, name, out value);

        /// <summary>Moves the sound to a world position. Detaches it if it was following a transform.</summary>
        public void SetPosition(UnityEngine.Vector3 position) => Sound.HandleSetPosition(this, position);

        /// <summary>Makes the sound follow <paramref name="target"/> (3D attributes updated every frame until it ends).</summary>
        public void AttachTo(UnityEngine.Transform target) => Sound.HandleAttachTo(this, target);

        /// <summary>
        /// Timeline position in milliseconds. Setting it on a queued sound applies it when the sound starts. 0 for invalid
        /// handles.
        /// </summary>
        public int TimelinePosition
        {
            get => Sound.HandleGetTimelinePosition(this);
            set => Sound.HandleSetTimelinePosition(this, value);
        }

        /// <summary>Sets the instance volume (linear gain, 1 = unchanged).</summary>
        public void SetVolume(float volume) => Sound.HandleSetVolume(this, volume);

        /// <summary>Sets the instance pitch multiplier (1 = unchanged).</summary>
        public void SetPitch(float pitch) => Sound.HandleSetPitch(this, pitch);

        /// <inheritdoc/>
        public bool Equals(SoundHandle other) => Index == other.Index && Generation == other.Generation;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SoundHandle other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => (Index * 397) ^ Generation;
        /// <summary>Equality operator.</summary>
        public static bool operator ==(SoundHandle a, SoundHandle b) => a.Equals(b);
        /// <summary>Inequality operator.</summary>
        public static bool operator !=(SoundHandle a, SoundHandle b) => !a.Equals(b);
        /// <inheritdoc/>
        public override string ToString() => Index == 0 ? "SoundHandle(invalid)" : $"SoundHandle({Index - 1}:{Generation})";
    }

    /// <summary>Identifier of an FMOD event parameter, for fast repeated <see cref="SoundHandle.SetParameter(SoundParameterId, float)"/> calls.</summary>
    public readonly struct SoundParameterId : IEquatable<SoundParameterId>
    {
        internal readonly uint Data1;
        internal readonly uint Data2;
        private readonly bool valid;

        internal SoundParameterId(uint data1, uint data2)
        {
            Data1 = data1;
            Data2 = data2;
            valid = true;
        }

        /// <summary>True when this id was resolved successfully.</summary>
        public bool IsValid => valid;

        /// <inheritdoc/>
        public bool Equals(SoundParameterId other) => valid == other.valid && Data1 == other.Data1 && Data2 == other.Data2;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SoundParameterId other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => (int)(Data1 * 397) ^ (int)Data2;
    }

    /// <summary>
    /// A pushed FMOD snapshot. Call <see cref="Release"/> to pop it. Stale handles are safe no-ops.
    /// </summary>
    public readonly struct SnapshotHandle : IEquatable<SnapshotHandle>
    {
        internal readonly int Index;      // slot index + 1
        internal readonly int Generation;

        internal SnapshotHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <summary>True while the snapshot is active.</summary>
        public bool IsValid => Sound.SnapshotIsValid(this);

        /// <summary>Stops and releases the snapshot instance (pops it).</summary>
        public void Release(bool allowFadeOut = true) => Sound.SnapshotRelease(this, allowFadeOut);

        /// <inheritdoc/>
        public bool Equals(SnapshotHandle other) => Index == other.Index && Generation == other.Generation;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SnapshotHandle other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => (Index * 397) ^ Generation;
    }
}
