using UnityEngine;

namespace kinatraa.fmodaudio.Samples
{
    /// <summary>Single music slot: switching tracks, an intensity parameter, pause and a pause snapshot.</summary>
    public class MusicExample : MonoBehaviour
    {
        [SoundCueId] public string BattleCue = "battle_theme";
        [SoundCueId] public string MenuCue = "menu_theme";

        private float intensity;
        private SnapshotHandle pauseSnapshot;

        private void OnEnable() => Sound.OnMusicChanged += LogMusic;

        private void OnDisable() => Sound.OnMusicChanged -= LogMusic;

        private static void LogMusic(string cueId) => Debug.Log("Music is now: " + (cueId ?? "(none)"));

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(280, 10, 260, 400), GUI.skin.box);
            GUILayout.Label("Music: " + (Sound.CurrentMusic ?? "(none)"));
            if (GUILayout.Button("Play battle theme")) Sound.PlayMusic(BattleCue);
            if (GUILayout.Button("Play menu theme")) Sound.PlayMusic(MenuCue);
            if (GUILayout.Button("Stop music")) Sound.StopMusic();
            GUILayout.Label($"MusicIntensity {intensity:0.00}");
            float next = GUILayout.HorizontalSlider(intensity, 0f, 1f);
            if (!Mathf.Approximately(next, intensity)) Sound.SetMusicParameter("MusicIntensity", intensity = next);

            GUILayout.Space(10);
            if (GUILayout.Button(Sound.IsPaused ? "Resume all" : "Pause all"))
            {
                if (Sound.IsPaused) Sound.ResumeAll();
                else Sound.PauseAll();
            }
            if (!pauseSnapshot.IsValid && GUILayout.Button("Push 'Pause' snapshot")) pauseSnapshot = Sound.PushSnapshot("Pause");
            if (pauseSnapshot.IsValid && GUILayout.Button("Release snapshot")) pauseSnapshot.Release();
            GUILayout.EndArea();
        }
    }
}
