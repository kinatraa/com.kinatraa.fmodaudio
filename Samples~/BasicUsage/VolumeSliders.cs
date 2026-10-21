using System.Collections.Generic;
using UnityEngine;

namespace kinatraa.FmodAudio.Samples
{
    /// <summary>Settings-menu style volume sliders and mute toggles bound to the configured channels/buses.</summary>
    public class VolumeSliders : MonoBehaviour
    {
        private readonly List<SoundChannel> channels = new List<SoundChannel>();

        private void OnEnable() => Sound.OnBusVolumeChanged += OnVolumeChanged;

        private void OnDisable() => Sound.OnBusVolumeChanged -= OnVolumeChanged;

        private static void OnVolumeChanged(SoundChannel channel, float volume) => Debug.Log($"{channel} volume -> {volume:0.00}");

        private void OnGUI()
        {
            Sound.GetChannels(channels);
            GUILayout.BeginArea(new Rect(550, 10, 280, 400), GUI.skin.box);
            GUILayout.Label("Volumes (saved through the settings store)");
            foreach (SoundChannel channel in channels)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(channel.Name, GUILayout.Width(60));
                float volume = GUILayout.HorizontalSlider(Sound.GetBusVolume(channel), 0f, 1f, GUILayout.Width(130));
                if (!Mathf.Approximately(volume, Sound.GetBusVolume(channel))) Sound.SetBusVolume(channel, volume);
                bool muted = GUILayout.Toggle(Sound.IsBusMuted(channel), "mute");
                if (muted != Sound.IsBusMuted(channel)) Sound.SetBusMuted(channel, muted);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to defaults")) Sound.ResetVolumes();
            if (GUILayout.Button("Save")) Sound.SaveSettings();   // e.g. when the settings menu closes
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
