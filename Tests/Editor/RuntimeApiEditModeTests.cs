using System.Collections.Generic;
using NUnit.Framework;

namespace kinatraa.fmodaudio.Tests
{
    /// <summary>Edit Mode tests of runtime types that do not need FMOD running (data types, hooks, safety when not initialized).</summary>
    public class RuntimeApiEditModeTests
    {
        [Test]
        public void SoundChannel_EqualityAndConversion()
        {
            SoundChannel music = "Music";
            Assert.IsTrue(music == SoundChannel.Music);
            Assert.IsTrue(music != SoundChannel.SFX);
            Assert.IsTrue(music.IsValid);
            Assert.IsFalse(default(SoundChannel).IsValid);
            Assert.AreEqual(string.Empty, default(SoundChannel).Name);
            Assert.AreEqual(SoundChannel.Music.GetHashCode(), new SoundChannel("Music").GetHashCode());
            Assert.IsFalse(new SoundChannel("music") == SoundChannel.Music, "channel names are case-sensitive");
        }

        [Test]
        public void Config_DefaultsAndSnapshotLookup()
        {
            SoundSystemConfig config = SoundSystemConfig.CreateDefault();
            try
            {
                Assert.AreEqual(6, config.Channels.Count);
                CollectionAssert.Contains(config.Banks, "Master");
                CollectionAssert.Contains(config.Banks, "Master.strings");
                Assert.IsTrue(config.TryGetSnapshotPath("Pause", out string path));
                Assert.AreEqual("snapshot:/Pause", path);
                Assert.IsFalse(config.TryGetSnapshotPath("pause", out _), "keys are case-sensitive");
                Assert.IsFalse(config.TryGetSnapshotPath("Missing", out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DelegateStore_ReportsMissingValues()
        {
            var volumes = new Dictionary<string, float> { { "Music", 0.4f } };
            var muted = new HashSet<string>();
            var store = new DelegateSoundSettingsStore(
                key => volumes.TryGetValue(key, out float v) ? v : (float?)null,
                (key, v) => volumes[key] = v,
                key => muted.Contains(key) ? true : (bool?)null,
                (key, m) => { if (m) muted.Add(key); else muted.Remove(key); });

            Assert.IsTrue(store.TryLoad("Music", out float music));
            Assert.AreEqual(0.4f, music, 1e-6f);
            Assert.IsFalse(store.TryLoad("SFX", out float sfx));
            Assert.AreEqual(1f, sfx, 1e-6f, "missing volume defaults to 1");
            store.Save("SFX", 0.2f);
            Assert.AreEqual(0.2f, volumes["SFX"], 1e-6f);
            Assert.IsFalse(store.TryLoadMuted("UI", out bool uiMuted));
            Assert.IsFalse(uiMuted);
            store.SaveMuted("UI", true);
            Assert.IsTrue(store.TryLoadMuted("UI", out uiMuted) && uiMuted);

            var empty = new DelegateSoundSettingsStore(null, null);
            Assert.DoesNotThrow(() =>
            {
                empty.Save("Music", 1f);
                empty.SaveMuted("Music", true);
            }, "null delegates are allowed");
            Assert.IsFalse(empty.TryLoad("Music", out _));
        }

        [Test]
        public void NotInitialized_EverythingIsASafeNoOp()
        {
            if (Sound.IsInitialized) Assert.Ignore("Sound is initialized (run in Edit Mode).");
            SoundHandle handle = Sound.Play("anything");
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(SoundPlaybackState.Stopped, handle.State);
            Assert.DoesNotThrow(() =>
            {
                handle.Stop();
                handle.SetParameter("RPM", 1f);
                handle.SetPosition(UnityEngine.Vector3.one);
                handle.TimelinePosition = 100;
                Sound.StopAll();
                Sound.StopChannel(SoundChannel.SFX);
                Sound.StopMusic();
                Sound.PauseAll();
                Sound.ResumeAll();
                Sound.SetGlobalParameter("TimeOfDay", 0.5f);
                Sound.UnloadBank("Level1");
                Sound.SaveSettings();
                default(SnapshotHandle).Release();
            });
            Assert.IsFalse(handle.TryGetParameter("RPM", out _));
            Assert.AreEqual(0, handle.TimelinePosition);
            Assert.IsNull(Sound.CurrentMusic);
            Assert.IsFalse(Sound.LoadBank("Level1"));

            bool? result = null;
            Sound.LoadBankAsync("Level1", ok => result = ok);
            Assert.AreEqual(false, result, "rejected immediately with false when not initialized");
        }
    }
}
