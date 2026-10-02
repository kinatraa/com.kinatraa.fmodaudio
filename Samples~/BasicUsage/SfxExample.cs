using UnityEngine;

namespace kinatraa.fmodaudio.Samples
{
    /// <summary>2D, 3D and attached SFX plus a looping sound with a parameter (IMGUI buttons, no input package needed).</summary>
    public class SfxExample : MonoBehaviour
    {
        [SoundCueId] public string ClickCue = "ui_click";
        [SoundCueId] public string ExplosionCue = "explosion";
        [SoundCueId] public string FootstepCue = "footstep";
        [SoundCueId] public string EngineCue = "engine_loop";
        public Transform Walker;

        private SoundHandle engine;
        private float rpm = 0.5f;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 260, 400), GUI.skin.box);
            GUILayout.Label("SFX");
            if (GUILayout.Button("UI click (2D)")) Sound.Play(ClickCue);
            if (GUILayout.Button("Explosion at (0, 0, 10)")) Sound.Play(ExplosionCue, new Vector3(0, 0, 10));
            if (GUILayout.Button("Footstep attached to walker")) Sound.Play(FootstepCue, Walker != null ? Walker : transform);

            GUILayout.Label("Looping engine");
            if (!engine.IsValid && GUILayout.Button("Start engine")) engine = Sound.Play(EngineCue, transform);
            if (engine.IsValid)
            {
                rpm = GUILayout.HorizontalSlider(rpm, 0f, 1f);
                engine.SetParameter("RPM", rpm);
                if (GUILayout.Button(engine.IsPaused ? "Resume" : "Pause"))
                {
                    if (engine.IsPaused) engine.Resume();
                    else engine.Pause();
                }
                if (GUILayout.Button("Stop (fade out)")) engine.Stop(allowFadeOut: true);
            }
            GUILayout.EndArea();
        }
    }
}
