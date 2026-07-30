using NUnit.Framework;
using UnityEngine;

namespace Colloid.UitkFontKit.Tests
{
    /// <summary>
    /// Guards the settings override surface: candidate replacement,
    /// tier disabling, cache invalidation only on real changes.
    /// </summary>
    public class FontKitSettingsTests
    {
        [TearDown]
        public void TearDown()
        {
            FontKitSettings.ResetToDefaults();
        }

        [Test]
        public void CjkUiFontNames_EmptyList_DisablesResolution()
        {
            FontKitSettings.CjkUiFontNames = new string[0];
            Assert.IsNull(FontKit.CjkUiFontAsset,
                "an empty candidate list must disable CJK resolution");
            Assert.IsEmpty(FontKit.CjkUiFontSource);
        }

        [Test]
        public void MonoCandidates_AllEmpty_FallBackToLabelFont()
        {
            FontKitSettings.EditorMonoFontPaths = new string[0];
            FontKitSettings.OsMonoFontNames = new string[0];
            Font font = FontKit.EditorMonoFont;
            Assert.IsNotNull(font,
                "the label-font tier has no candidate list and must"
                + " still resolve");
            Assert.AreEqual("label", FontKit.EditorMonoFontSource);
        }

        [Test]
        public void ResetToDefaults_RestoresResolution()
        {
            FontKitSettings.EditorMonoFontPaths = new string[0];
            FontKitSettings.OsMonoFontNames = new string[0];
            Assert.AreEqual("label", FontKit.EditorMonoFontSource);
            FontKitSettings.ResetToDefaults();
            string source = FontKit.EditorMonoFontSource;
            Assert.IsNotEmpty(source);
            Debug.Log("[FontKitSettingsTests] post-reset mono source = "
                + source);
        }

        [Test]
        public void AssigningEqualValues_DoesNotInvalidateCache()
        {
            Font before = FontKit.EditorMonoFont;
            // Same contents, different array instance: must be treated
            // as unchanged so teardown-style resets keep caches warm.
            FontKitSettings.OsMonoFontNames =
                (string[])FontKitDefaults.OsMonoFontNames.Clone();
            FontKitSettings.ResetToDefaults();
            Assert.AreSame(before, FontKit.EditorMonoFont,
                "value-equal assignment must not drop the cache");
        }

        [Test]
        public void AssigningNull_RestoresDefaults()
        {
            FontKitSettings.CjkUiFontNames = new[] { "Nonexistent Font" };
            FontKitSettings.CjkUiFontNames = null;
            Assert.AreSame(FontKitDefaults.CjkUiFontNames,
                FontKitSettings.CjkUiFontNames);
        }

        [Test]
        public void ResetCaches_ForcesReprobe_WithoutThrowing()
        {
            // Capture the SOURCE, not the Font reference: when the OS-font
            // tier wins, ResetCaches destroys the kit-owned Font and a
            // held reference would compare as null afterwards (Unity's
            // overloaded == on destroyed objects), turning this into a
            // false failure on machines where the bundled TTF is absent.
            string sourceBefore = FontKit.EditorMonoFontSource;
            Assert.IsNotEmpty(sourceBefore);
            Assert.DoesNotThrow(FontKit.ResetCaches);
            Font after = FontKit.EditorMonoFont;
            Assert.IsNotNull(after, "re-probing after ResetCaches must"
                + " resolve again");
            Assert.AreEqual(sourceBefore, FontKit.EditorMonoFontSource,
                "with unchanged settings the re-probe must land on the"
                + " same candidate");
        }
    }
}
