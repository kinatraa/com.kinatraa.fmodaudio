using System.Collections.Generic;
using System.IO;
using kinatraa.FmodAudio.Editor;
using NUnit.Framework;

namespace kinatraa.FmodAudio.Tests
{
    /// <summary>Edit Mode tests of the editor tooling's pure logic (no FMOD banks or Play Mode needed).</summary>
    public class EditorToolsTests
    {
        [TestCase("BattleTheme", "battle_theme")]
        [TestCase("UI Click-2", "ui_click_2")]
        [TestCase("EngineLoop", "engine_loop")]
        [TestCase("HTTPServer", "http_server")]
        [TestCase("Click", "click")]
        [TestCase("footstep_grass", "footstep_grass")]
        [TestCase("  ", "")]
        public void ToSnakeCase(string input, string expected) => Assert.AreEqual(expected, EditorCues.ToSnakeCase(input));

        [TestCase("ui_click", "UiClick")]
        [TestCase("3d_boom", "_3dBoom")]
        [TestCase("engine-loop", "EngineLoop")]
        [TestCase("", "_")]
        public void ToPascalCase(string input, string expected) => Assert.AreEqual(expected, EditorCues.ToPascalCase(input));

        [TestCase("event:/Music/BattleTheme", "Music")]
        [TestCase("event:/ui/Click", "UI")]
        [TestCase("event:/Dialogue/Intro", "Voice")]
        [TestCase("event:/Ambience/Wind", "Ambient")]
        [TestCase("event:/Click", "SFX")]
        [TestCase("event:/Weapons/Gun", "SFX")]
        public void GuessChannel(string path, string expected)
        {
            var channels = new List<string> { "Master", "Music", "SFX", "UI", "Voice", "Ambient" };
            Assert.AreEqual(expected, EditorCues.GuessChannel(path, channels));
        }

        [Test]
        public void SoundIds_SortedUniqueAndEscaped()
        {
            string code = SoundIdsGenerator.BuildSource(new[] { "zeta", "ui_click", "ui-click", "ui_click", "say\"hi", "" }, null, "SoundIds");
            int a = code.IndexOf("UiClick =", System.StringComparison.Ordinal);
            int b = code.IndexOf("UiClick_2 =", System.StringComparison.Ordinal);
            int z = code.IndexOf("Zeta =", System.StringComparison.Ordinal);
            Assert.IsTrue(a > 0 && b > 0 && z > 0, code);
            Assert.IsTrue(code.Contains("\"ui-click\";") && code.Contains("\"ui_click\";"), code);
            Assert.IsTrue(code.Contains("SayHi = \"say\\\"hi\";"), code);
            Assert.IsFalse(code.Contains("namespace"), code);
            Assert.AreEqual(code, SoundIdsGenerator.BuildSource(new[] { "ui_click", "say\"hi", "zeta", "ui-click" }, null, "SoundIds"), "deterministic");
        }

        [Test]
        public void SoundIds_NamespaceAndClassNameCollision()
        {
            string code = SoundIdsGenerator.BuildSource(new[] { "sound_ids" }, "Game.Audio", "SoundIds");
            Assert.IsTrue(code.Contains("namespace Game.Audio\n{"), code);
            Assert.IsTrue(code.Contains("public const string SoundIds_ = \"sound_ids\";"), code);
        }

        [Test]
        public void SoundIds_SettingsValidation()
        {
            Assert.IsNull(SoundIdsGenerator.Validate("Assets/Scripts/SoundIds.cs", "", "SoundIds"));
            Assert.IsNull(SoundIdsGenerator.Validate("Assets/Gen/Ids.cs", "My.Game", "Ids"));
            Assert.IsNotNull(SoundIdsGenerator.Validate("Assets/Gen/Ids.txt", "", "Ids"));
            Assert.IsNotNull(SoundIdsGenerator.Validate("Packages/x/Ids.cs", "", "Ids"));
            Assert.IsNotNull(SoundIdsGenerator.Validate("Assets/Ids.cs", "My..Game", "Ids"));
            Assert.IsNotNull(SoundIdsGenerator.Validate("Assets/Ids.cs", "", "class"));
            Assert.IsNotNull(SoundIdsGenerator.Validate("Assets/Ids.cs", "", "1Ids"));
        }

        [Test]
        public void StudioProjectInfo_ParsesBusesVcasReturnsAndSnapshots()
        {
            string root = Path.Combine(Path.GetTempPath(), "kinatraa-fmodaudio-tests-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string metadata = Path.Combine(root, "Metadata");
                Directory.CreateDirectory(metadata);
                File.WriteAllText(Path.Combine(root, "Test.fspro"), "<?xml version=\"1.0\"?><objects serializationModel=\"Studio.02.03.00\" />");
                File.WriteAllText(Path.Combine(metadata, "Master.xml"), Objects(Obj("MixerMaster", "m", "Master Bus")));
                File.WriteAllText(Path.Combine(metadata, "Mixer.xml"), Objects(
                    Obj("MixerGroup", "sfx", "SFX", Rel("output", "m")),
                    Obj("MixerGroup", "weapons", "Weapons", Rel("output", "sfx")),
                    Obj("MixerReturn", "rev", "Reverb", Rel("output", "m")),
                    Obj("MixerVCA", "vca1", "Music"),
                    Obj("SnapshotList", "list", null, Rel("items", "pause")),
                    Obj("Snapshot", "pause", "Pause")));

                StudioProjectInfo info = StudioProjectInfo.Scan(Path.Combine(root, "Test.fspro"));
                Assert.IsNull(info.Error, info.Error);
                Assert.IsTrue(info.IsStudio203);
                CollectionAssert.Contains(info.BusPaths, "bus:/SFX");
                CollectionAssert.Contains(info.BusPaths, "bus:/SFX/Weapons");
                CollectionAssert.Contains(info.BusPaths, "bus:/Reverb");
                CollectionAssert.Contains(info.VcaPaths, "vca:/Music");
                CollectionAssert.Contains(info.SnapshotPaths, "snapshot:/Pause");
                CollectionAssert.DoesNotContain(info.BusPaths, "bus:/Master Bus");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void StudioConvention_SplitsBusesAndVcas()
        {
            SoundSystemConfig config = SoundSystemConfig.CreateDefault();
            try
            {
                config.Channels.Add(new SoundChannelDefinition("Dialogue", "vca:/Dialogue"));
                List<string> buses = StudioConventionCheck.RequiredBuses(config);
                List<string> vcas = StudioConventionCheck.RequiredVcas(config);
                CollectionAssert.Contains(buses, "bus:/Music");
                CollectionAssert.DoesNotContain(buses, "bus:/");
                CollectionAssert.AreEqual(new[] { "vca:/Dialogue" }, vcas);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void SetupChecks_AreOrderedAndEvaluateWithoutThrowing()
        {
            int previous = int.MinValue;
            foreach (SetupCheck check in SetupCheckRegistry.All)
            {
                Assert.IsTrue(check.Order > previous, $"'{check.Title}' is out of order");
                previous = check.Order;
            }
            SetupValidator.Result result = SetupValidator.Validate();
            Assert.IsTrue(result.Entries.Count > 0);
            foreach (SetupValidator.Entry e in result.Entries) Assert.IsFalse(e.Result.Message.StartsWith("Check failed to run"), e.Title + ": " + e.Result.Message);
        }

        private static string Objects(params string[] objects) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<objects serializationModel=\"Studio.02.03.00\">\n" + string.Join("\n", objects) + "\n</objects>";

        private static string Obj(string cls, string id, string name, params string[] relationships)
        {
            string nameXml = name == null ? string.Empty : $"<property name=\"name\"><value>{name}</value></property>";
            return $"<object class=\"{cls}\" id=\"{{{id}}}\">{nameXml}{string.Join(string.Empty, relationships)}</object>";
        }

        private static string Rel(string name, string target) => $"<relationship name=\"{name}\"><destination>{{{target}}}</destination></relationship>";
    }
}
