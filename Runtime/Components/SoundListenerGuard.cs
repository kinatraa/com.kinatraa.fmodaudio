using UnityEngine;

namespace kinatraa.fmodaudio
{
    /// <summary>
    /// Ensures an FMOD StudioListener exists (adds one to this object if none is active), warns when more than one
    /// is active, and warns about (or disables) Unity AudioListeners. Put it on the main camera.
    /// </summary>
    [AddComponentMenu("kinatraa/FMOD Audio/Sound Listener Guard")]
    public class SoundListenerGuard : MonoBehaviour
    {
        /// <summary>Add a StudioListener to this object when no listener is active.</summary>
        public bool AddListenerIfMissing = true;
        /// <summary>Disable enabled Unity AudioListeners instead of only warning.</summary>
        public bool DisableUnityAudioListeners;

        private void Start() => Check();

        /// <summary>Runs the checks now. Called automatically in Start.</summary>
        public void Check()
        {
            int count = FmodBackend.ListenerCount;
            if (count == 0)
            {
                if (AddListenerIfMissing)
                {
                    FmodBackend.AddListener(gameObject);
                    SoundLog.Info($"Added an FMOD StudioListener to '{name}'.");
                }
                else
                {
                    SoundLog.Warning("No FMOD StudioListener is active; 3D sounds will not be positioned correctly.");
                }
            }
            else if (count > 1)
            {
                SoundLog.Warning($"{count} FMOD StudioListeners are active. Keep exactly one unless you need multiple listeners (split-screen).");
            }

#if kinatraa_fmodaudio_unity_audio
#if UNITY_2023_1_OR_NEWER
            AudioListener[] unityListeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
            AudioListener[] unityListeners = FindObjectsOfType<AudioListener>();
#endif
            foreach (AudioListener listener in unityListeners)
            {
                if (!listener.isActiveAndEnabled) continue;
                if (DisableUnityAudioListeners)
                {
                    listener.enabled = false;
                    SoundLog.Info($"Disabled Unity AudioListener on '{listener.name}'.");
                }
                else
                {
                    SoundLog.Warning($"Unity AudioListener active on '{listener.name}'. With FMOD it is not needed; remove it or enable DisableUnityAudioListeners.");
                }
            }
#endif
        }
    }
}
