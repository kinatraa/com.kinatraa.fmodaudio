using System;
using System.Collections.Generic;
using UnityEngine;

namespace kinatraa.FmodAudio
{
    /// <summary>
    /// A list of <see cref="SoundCue"/>s. Several libraries can be registered at once (for example a core library
    /// plus one per level) through <see cref="SoundSystemConfig.Libraries"/> or <see cref="Sound.RegisterLibrary"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "kinatraa/FMOD Audio/Sound Cue Library", fileName = "SoundCueLibrary", order = 1)]
    public class SoundCueLibrary : ScriptableObject
    {
        /// <summary>The cues in this library. Ids must be unique across all registered libraries.</summary>
        public List<SoundCue> Cues = new List<SoundCue>();

        /// <summary>Finds a cue by id (ordinal comparison). Linear search; intended for editor/tools use.</summary>
        public SoundCue Find(string id)
        {
            for (int i = 0; i < Cues.Count; i++)
            {
                if (Cues[i] != null && string.Equals(Cues[i].Id, id, StringComparison.Ordinal)) return Cues[i];
            }
            return null;
        }
    }
}
