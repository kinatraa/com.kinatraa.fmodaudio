using UnityEngine;

namespace kinatraa.fmodaudio
{
    /// <summary>Lives on the persistent root object; forwards application focus/pause and destruction to <see cref="Sound"/>.</summary>
    [AddComponentMenu("")]
    internal sealed class SoundRuntime : MonoBehaviour
    {
        private void OnApplicationFocus(bool hasFocus) => Sound.HandleFocusChanged(hasFocus);

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) Sound.HandleApplicationSuspended();
            else Sound.HandleApplicationResumed();
        }

        private void OnApplicationQuit() => Sound.HandleApplicationSuspended();

        private void OnDestroy() => Sound.HandleRuntimeDestroyed(gameObject);
    }
}
