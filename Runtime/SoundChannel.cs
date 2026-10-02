using System;
using UnityEngine;

namespace kinatraa.fmodaudio
{
    /// <summary>
    /// Lightweight identifier for a mixing channel (an FMOD bus). Built-in channels are exposed as static
    /// properties; custom channels are defined in <see cref="SoundSystemConfig"/> and referenced by name.
    /// </summary>
    [Serializable]
    public struct SoundChannel : IEquatable<SoundChannel>
    {
        [SerializeField] private string name;

        /// <summary>Creates a channel identifier from its name (case-sensitive, e.g. "SFX").</summary>
        public SoundChannel(string name) { this.name = name; }

        /// <summary>The channel name. Empty for the default (unassigned) channel.</summary>
        public string Name => name ?? string.Empty;

        /// <summary>True when this identifier has a name.</summary>
        public bool IsValid => !string.IsNullOrEmpty(name);

        /// <summary>The master channel (FMOD master bus, <c>bus:/</c>). Affects every other channel.</summary>
        public static SoundChannel Master => new SoundChannel("Master");
        /// <summary>Music channel (<c>bus:/Music</c>).</summary>
        public static SoundChannel Music => new SoundChannel("Music");
        /// <summary>Sound effects channel (<c>bus:/SFX</c>).</summary>
        public static SoundChannel SFX => new SoundChannel("SFX");
        /// <summary>User interface channel (<c>bus:/UI</c>).</summary>
        public static SoundChannel UI => new SoundChannel("UI");
        /// <summary>Voice / dialogue channel (<c>bus:/Voice</c>).</summary>
        public static SoundChannel Voice => new SoundChannel("Voice");
        /// <summary>Ambience channel (<c>bus:/Ambient</c>).</summary>
        public static SoundChannel Ambient => new SoundChannel("Ambient");

        /// <summary>Implicit conversion from a channel name.</summary>
        public static implicit operator SoundChannel(string name) => new SoundChannel(name);

        /// <inheritdoc/>
        public bool Equals(SoundChannel other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SoundChannel other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => Name.GetHashCode();
        /// <inheritdoc/>
        public override string ToString() => Name;
        /// <summary>Equality operator.</summary>
        public static bool operator ==(SoundChannel a, SoundChannel b) => a.Equals(b);
        /// <summary>Inequality operator.</summary>
        public static bool operator !=(SoundChannel a, SoundChannel b) => !a.Equals(b);
    }
}
