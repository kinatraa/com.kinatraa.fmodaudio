using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace kinatraa.fmodaudio.Samples
{
    /// <summary>
    /// Per-scene banks driven by code: scenes named "Level*" get a bank of the same name (e.g. Level1 -> Level1.bank).
    /// The default provider reads SoundSystemConfig.SceneBanks instead; use whichever fits your game.
    /// </summary>
    public sealed class LevelNameBankProvider : ISoundBankProvider
    {
        public void GetBanksForScene(Scene scene, List<string> banks)
        {
            if (scene.name.StartsWith("Level")) banks.Add(scene.name);
        }
    }

    /// <summary>Loads a bank asynchronously with a callback, and shows how to install a custom bank provider.</summary>
    public class SceneBankExample : MonoBehaviour
    {
        public string Bank = "Level1";
        [SoundCueId] public string CueInBank = "wind";
        public bool UseLevelNameProvider;

        private SoundHandle ambience;
        private bool holdingBank;   // true while this script owns one reference to the bank
        private bool loading;

        private void Awake()
        {
            if (UseLevelNameProvider) Sound.SetBankProvider(new LevelNameBankProvider());
        }

        private void OnDestroy()
        {
            if (holdingBank) Sound.UnloadBank(Bank);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 420, 260, 160), GUI.skin.box);
            GUILayout.Label($"Bank '{Bank}' loaded: {Sound.IsBankLoaded(Bank)}" + (loading ? " (loading...)" : string.Empty));
            if (!holdingBank && !loading && GUILayout.Button("LoadBankAsync + play"))
            {
                loading = true;
                Sound.LoadBankAsync(Bank, ok =>
                {
                    loading = false;
                    holdingBank = ok;
                    Debug.Log($"Bank {Bank} loaded: {ok}");
                    if (ok) ambience = Sound.Play(CueInBank);
                }, loadSampleData: true);
            }
            if (holdingBank && GUILayout.Button("Stop + UnloadBank"))
            {
                ambience.Stop();
                Sound.UnloadBank(Bank);
                holdingBank = false;
            }
            GUILayout.EndArea();
        }
    }
}
