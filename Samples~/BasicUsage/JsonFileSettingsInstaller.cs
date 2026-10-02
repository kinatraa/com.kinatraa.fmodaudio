using UnityEngine;

namespace kinatraa.fmodaudio.Samples
{
    /// <summary>Add to a scene to use the JSON store instead of PlayerPrefs. Volumes reload immediately.</summary>
    public class JsonFileSettingsInstaller : MonoBehaviour
    {
        public string FileName = "kinatraa-fmodaudio-sample-settings.json";

        private void Awake() => Sound.SetSettingsStore(new JsonFileSettingsStore(FileName));
    }
}
