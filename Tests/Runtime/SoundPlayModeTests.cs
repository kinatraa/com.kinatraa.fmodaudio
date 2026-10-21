using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FMODUnity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace kinatraa.FmodAudio.Tests
{
    /// <summary>
    /// Play Mode tests of the runtime API. They need the banks and cues of the "Minimal Studio Project" sample
    /// (cues ui_click, explosion, engine_loop, battle_theme, menu_theme; banks Master, Master.strings, Common, Music, Level1)
    /// and are ignored in projects without them. Add the package to "testables" in Packages/manifest.json to run them.
    /// </summary>
    public class SoundPlayModeTests
    {
        private const string EnginePath = "event:/SFX/EngineLoop";

        private static IEnumerator Frames(int n)
        {
            // FMOD processes commands on its own update; wait a little real time as well (batch-mode frames are very short).
            float end = Time.realtimeSinceStartup + 0.05f * n;
            for (int i = 0; i < n || Time.realtimeSinceStartup < end; i++) yield return null;
        }

        private static FMOD.Studio.EventInstance FirstInstance(string path)
        {
            RuntimeManager.GetEventDescription(path).getInstanceList(out FMOD.Studio.EventInstance[] list);
            return list != null && list.Length > 0 ? list[0] : default;
        }

        private static SoundSystemConfig ResourcesConfig => Resources.Load<SoundSystemConfig>(SoundSystemConfig.ResourcesName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Sound.IsInitialized) Sound.Initialize(ResourcesConfig);
            if (!Sound.HasCue("engine_loop") || !Sound.HasCue("battle_theme") || !Sound.HasCue("menu_theme"))
            {
                Assert.Ignore("Needs the cues of the Minimal Studio Project sample (engine_loop, battle_theme, menu_theme).");
            }
            Sound.StopAll(false);
            yield return Frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Sound.SetPolicy(null);
            if (!Sound.IsInitialized) Sound.Initialize(ResourcesConfig);
            Sound.StopAll(false);
            Sound.ResumeAll();
            yield return Frames(3);
        }

        [UnityTest]
        public IEnumerator Music_StoppingTheSlotAnyWayRaisesOnMusicChangedOnce()
        {
            var changes = new List<string>();
            Action<string> record = id => changes.Add(id);
            Sound.OnMusicChanged += record;
            try
            {
                Sound.PlayMusic("battle_theme");
                yield return Frames(2);
                Sound.StopChannel(SoundChannel.Music, false);
                Assert.IsNull(Sound.CurrentMusic);
                CollectionAssert.AreEqual(new[] { "battle_theme", null }, changes, "StopChannel(Music) ends the music slot");

                changes.Clear();
                Sound.PlayMusic("menu_theme");
                Sound.PlayMusic("battle_theme");
                yield return Frames(2);
                Sound.StopMusic(false);
                CollectionAssert.AreEqual(new[] { "menu_theme", "battle_theme", null }, changes, "switching raises no null in between");

                changes.Clear();
                Sound.PlayMusic("menu_theme");
                Sound.MusicHandle.Stop(false);
                CollectionAssert.AreEqual(new[] { "menu_theme", null }, changes, "stopping the music handle directly");
            }
            finally
            {
                Sound.OnMusicChanged -= record;
            }
        }

        [UnityTest]
        public IEnumerator Handle_SetPositionAttachToTimelineAndParameters()
        {
            SoundHandle handle = Sound.Play("engine_loop", new Vector3(1, 2, 3));
            Assert.IsTrue(handle.IsValid);
            yield return Frames(2);

            handle.SetPosition(new Vector3(7, 0, 0));
            yield return Frames(2);
            FirstInstance(EnginePath).get3DAttributes(out FMOD.ATTRIBUTES_3D a);
            Assert.AreEqual(7f, a.position.x, 1e-3f);

            var go = new GameObject("follow-target");
            go.transform.position = new Vector3(0, 0, 12);
            handle.AttachTo(go.transform);
            go.transform.position = new Vector3(0, 0, 15);
            yield return Frames(3);
            FirstInstance(EnginePath).get3DAttributes(out a);
            Assert.AreEqual(15f, a.position.z, 1e-3f);
            UnityEngine.Object.Destroy(go);

            handle.SetParameter("RPM", 0.7f);
            Assert.IsTrue(handle.TryGetParameter("RPM", out float rpm));
            Assert.AreEqual(0.7f, rpm, 1e-3f);
            Assert.IsFalse(handle.TryGetParameter("DoesNotExist", out _));

            handle.TimelinePosition = 400;
            yield return Frames(3);
            Assert.IsTrue(handle.TimelinePosition >= 300, "timeline position was set: " + handle.TimelinePosition);

            handle.Stop(false);
            yield return Frames(2);
            Assert.IsFalse(handle.IsValid);
            Assert.DoesNotThrow(() =>
            {
                handle.SetPosition(Vector3.zero);
                handle.AttachTo(null);
                handle.TimelinePosition = 10;
            }, "stale handles stay safe no-ops");
            Assert.AreEqual(0, handle.TimelinePosition);
        }

        private sealed class ThrowingPolicy : ISoundPolicy
        {
            public bool OnBeforePlay(ref SoundPlayRequest request) => throw new InvalidOperationException("policy bug");
            public void OnFocusChanged(bool hasFocus) => throw new InvalidOperationException("policy bug");
        }

        [UnityTest]
        public IEnumerator ThrowingGameCode_DoesNotBreakPlayback()
        {
            Sound.SetPolicy(new ThrowingPolicy());
            LogAssert.Expect(LogType.Error, new Regex("ISoundPolicy.OnBeforePlay threw InvalidOperationException"));
            SoundHandle handle = Sound.Play("engine_loop");
            Assert.IsTrue(handle.IsValid, "a throwing policy is logged and treated as allow");
            Sound.SetPolicy(null);

            Action<string, SoundHandle> bad = (id, h) => throw new InvalidOperationException("handler bug");
            Sound.OnCuePlayed += bad;
            LogAssert.Expect(LogType.Error, new Regex("OnCuePlayed handler threw"));
            SoundHandle second = Sound.Play("engine_loop");
            Sound.OnCuePlayed -= bad;
            Assert.IsTrue(second.IsValid);
            handle.Stop(false);
            second.Stop(false);
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator Volumes_ResetToDefaultsAndPreInitValuesAreKept()
        {
            SoundChannelDefinition music = ResourcesConfig != null ? ResourcesConfig.Channels.Find(c => c != null && c.Name == "Music") : null;
            float defaultVolume = music != null ? Mathf.Clamp01(music.DefaultVolume) : 1f;
            Dictionary<string, float> originalVolumes = Sound.GetAllVolumes();
            bool originalMuted = Sound.IsBusMuted(SoundChannel.Music);
            try
            {
                Sound.SetBusVolume(SoundChannel.Music, 0.3f);
                Sound.SetBusMuted(SoundChannel.Music, true);
                Sound.ResetVolumes(save: false);
                Assert.AreEqual(defaultVolume, Sound.GetBusVolume(SoundChannel.Music), 1e-4f);
                Assert.IsFalse(Sound.IsBusMuted(SoundChannel.Music));
                Assert.DoesNotThrow(Sound.SaveSettings);

                Sound.Shutdown();
                yield return null;
                Sound.SetBusVolume(SoundChannel.Music, 0.25f);   // before initialization (AutoInitialize off scenario)
                Assert.IsTrue(Sound.Initialize(ResourcesConfig));
                Assert.AreEqual(0.25f, Sound.GetBusVolume(SoundChannel.Music), 1e-4f, "value set before Initialize wins");
            }
            finally
            {
                if (!Sound.IsInitialized) Sound.Initialize(ResourcesConfig);
                Sound.ApplyVolumes(originalVolumes, save: true);
                Sound.SetBusMuted(SoundChannel.Music, originalMuted);
            }
        }

        [UnityTest]
        public IEnumerator Banks_AsyncCallbacksAlwaysComplete()
        {
            LogAssert.ignoreFailingMessages = true; // FMOD itself may log the missing file
            try
            {
                bool? missing = null;
                Sound.LoadBankAsync("DoesNotExist_kinatraa", ok => missing = ok);
                for (int i = 0; i < 10 && missing == null; i++) yield return null;
                Assert.AreEqual(false, missing, "a missing bank completes with false");

                bool? pending = null;
                Sound.LoadBankAsync("Level1", ok => pending = ok, loadSampleData: true);
                Sound.Shutdown();
                Assert.AreEqual(false, pending, "a load still pending at Shutdown completes with false");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                if (!Sound.IsInitialized) Sound.Initialize(ResourcesConfig);
            }
            Assert.IsFalse(Sound.IsBankLoaded("DoesNotExist_kinatraa"));
        }

        [UnityTest]
        public IEnumerator SoundBankLoader_ReloadsAfterDisableEnable()
        {
            var go = new GameObject("bank-loader");
            go.SetActive(false);
            var loader = go.AddComponent<SoundBankLoader>();
            loader.Banks.Add("Level1");
            loader.LoadOn = SoundBankLoadTiming.Awake;
            loader.UnloadOn = SoundBankUnloadTiming.OnDisable;
            go.SetActive(true);
            Assert.IsTrue(loader.IsLoaded);
            CollectionAssert.AreEqual(new[] { "Level1" }, loader.LoadedBanks);

            go.SetActive(false);
            Assert.IsFalse(loader.IsLoaded);
            go.SetActive(true);
            Assert.IsTrue(loader.IsLoaded, "banks are loaded again when the object is re-enabled");

            UnityEngine.Object.Destroy(go);
            yield return null;
        }
    }
}
