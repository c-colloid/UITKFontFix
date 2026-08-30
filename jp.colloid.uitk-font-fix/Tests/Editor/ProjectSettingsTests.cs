using System.IO;
using NUnit.Framework;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the project-file persistence for the no-code
    /// configuration path: save/load round-trip, corrupt-file safety,
    /// sync detection and the line-splitting used by the settings
    /// form. The tests write the real settings file inside the test
    /// project, so every one of them restores defaults AND deletes the
    /// file on teardown.
    /// </summary>
    public class ProjectSettingsTests
    {
        [SetUp]
        public void SetUp()
        {
            FontFixSettings.ResetToDefaults();
            FontFixProjectSettings.Delete();
        }

        [TearDown]
        public void TearDown()
        {
            FontFixProjectSettings.Delete();
            FontFixSettings.ResetToDefaults();
        }

        [Test]
        public void FilePath_FollowsProjectSettingsPackagesConvention()
        {
            Assert.AreEqual(
                "ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json",
                FontFixProjectSettings.FilePath);
        }

        [Test]
        public void Save_ThenMutate_ThenLoad_RestoresSavedValues()
        {
            FontFixSettings.CjkUiFontNames = new[] { "Family A", "Family B" };
            FontFixSettings.CjkUiBoldStyleName = "";
            Assert.IsTrue(FontFixProjectSettings.Save());
            Assert.IsTrue(FontFixProjectSettings.Exists);

            FontFixSettings.ResetToDefaults();
            Assert.AreNotEqual("", FontFixSettings.CjkUiBoldStyleName);

            Assert.IsTrue(FontFixProjectSettings.TryLoadAndApply());
            CollectionAssert.AreEqual(new[] { "Family A", "Family B" },
                FontFixSettings.CjkUiFontNames);
            Assert.AreEqual("", FontFixSettings.CjkUiBoldStyleName,
                "the meaningful empty bold style (wiring disabled) must"
                + " survive the round-trip");
        }

        [Test]
        public void TryLoadAndApply_NoFile_ReturnsFalse_ChangesNothing()
        {
            string[] before = FontFixSettings.CjkUiFontNames;
            Assert.IsFalse(FontFixProjectSettings.TryLoadAndApply());
            Assert.AreSame(before, FontFixSettings.CjkUiFontNames);
        }

        [Test]
        public void TryLoadAndApply_CorruptFile_ReturnsFalse_NeverThrows()
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(FontFixProjectSettings.FilePath));
            File.WriteAllText(FontFixProjectSettings.FilePath,
                "this is not json {{{");
            string[] before = FontFixSettings.CjkUiFontNames;
            Assert.DoesNotThrow(delegate
            {
                FontFixProjectSettings.TryLoadAndApply();
            });
            Assert.AreSame(before, FontFixSettings.CjkUiFontNames,
                "a corrupt file must not disturb the settings");
        }

        [Test]
        public void Delete_RemovesFile()
        {
            Assert.IsTrue(FontFixProjectSettings.Save());
            FontFixProjectSettings.Delete();
            Assert.IsFalse(FontFixProjectSettings.Exists);
            Assert.DoesNotThrow(FontFixProjectSettings.Delete);
        }

        [Test]
        public void MatchesCurrentSettings_TracksDrift()
        {
            Assert.IsFalse(FontFixProjectSettings.MatchesCurrentSettings(),
                "no file means nothing to match");
            Assert.IsTrue(FontFixProjectSettings.Save());
            Assert.IsTrue(FontFixProjectSettings.MatchesCurrentSettings());
            FontFixSettings.CjkUiFontNames = new[] { "Drifted Family" };
            Assert.IsFalse(FontFixProjectSettings.MatchesCurrentSettings(),
                "changing values after saving must register as drift");
        }

        [Test]
        public void Report_ListsProjectFileSection()
        {
            string report = FontFixDiagnostics.BuildReport();
            StringAssert.Contains("-- Project settings file --", report);
            StringAssert.Contains(FontFixProjectSettings.FilePath, report);
            StringAssert.Contains("state : absent", report);
            Assert.IsTrue(FontFixProjectSettings.Save());
            StringAssert.Contains("state : present, in sync",
                FontFixDiagnostics.BuildReport());
        }

        [Test]
        public void SettingsProvider_Creates()
        {
            Assert.IsNotNull(FontFixSettingsProviderRegistration.Create());
        }

        [Test]
        public void SplitLines_TrimsAndDropsEmpties()
        {
            CollectionAssert.AreEqual(
                new[] { "Yu Gothic UI", "Meiryo" },
                FontFixSettingsUi.SplitLines(
                    "  Yu Gothic UI  \r\n\n\tMeiryo\r\n   \n"));
            CollectionAssert.AreEqual(new string[0],
                FontFixSettingsUi.SplitLines(null));
            CollectionAssert.AreEqual(new string[0],
                FontFixSettingsUi.SplitLines("  \n \r\n"));
        }
    }
}
