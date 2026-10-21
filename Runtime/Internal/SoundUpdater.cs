using UnityEngine;

namespace kinatraa.FmodAudio
{
    /// <summary>
    /// The single central updater. It is only enabled while sounds are tracked, plays are queued or bank loads are
    /// pending, so nothing ticks while the system is idle.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class SoundUpdater : MonoBehaviour
    {
        private void Update() => Sound.Tick();
    }
}
