using NUnit.Framework;
using UnityEngine;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the settings override surface: candidate replacement,
    /// tier disabling, cache invalidation only on real changes.
    /// </summary>
    public class FontFixSettingsTests
    {
        [TearDown]
        public void TearDown()
        {
            FontFixSettings.ResetToDefaults();
        }

        [Test]
        public void CjkUiFontNames_EmptyList_DisablesResolution()
        {
            FontFixSettings.CjkUiFontNames = new string[0];
            Assert.IsNull(FontFix.CjkUiFontAsset,
                "an empty candidate list must disable CJK resolution");
            Assert.IsEmpty(FontFix.CjkUiFontSource);
        }

        [Test]
        public void MonoCandidates_AllEmpty_FallBackToLabelFont()
        {
            FontFixSettings.EditorMonoFontPaths = new string[0];
            FontFixSettings.OsMonoFontNames = new string[0];
            Font font = FontFix.EditorMonoFont;
            Assert.IsNotNull(font,
                "the label-font tier has no candidate list and must"
                + " still resolve");
            Assert.AreEqual("label", FontFix.EditorMonoFontSource);
        }

        [Test]
        public void ResetToDefaults_RestoresResolution()
        {
            FontFixSettings.EditorMonoFontPaths = new string[0];
            FontFixSettings.OsMonoFontNames = new string[0];
            Assert.AreEqual("label", FontFix.EditorMonoFontSource);
            FontFixSettings.ResetToDefaults();
            string source = FontFix.EditorMonoFontSource;
            Assert.IsNotEmpty(source);
            Debug.Log("[FontFixSettingsTests] post-reset mono source = "
                + source);
        }

        [Test]
        public void AssigningEqualValues_DoesNotInvalidateCache()
        {
            Font before = FontFix.EditorMonoFont;
            // Same contents, different array instance: must be treated
            // as unchanged so teardown-style resets keep caches warm.
            FontFixSettings.OsMonoFontNames =
                (string[])FontFixDefaults.OsMonoFontNames.Clone();
            FontFixSettings.ResetToDefaults();
            Assert.AreSame(before, FontFix.EditorMonoFont,
                "value-equal assignment must not drop the cache");
        }

        [Test]
        public void AssigningNull_RestoresDefaults()
        {
            FontFixSettings.CjkUiFontNames = new[] { "Nonexistent Font" };
            FontFixSettings.CjkUiFontNames = null;
            Assert.AreSame(FontFixDefaults.CjkUiFontNames,
                FontFixSettings.CjkUiFontNames);
        }

        [Test]
        public void ResetCaches_ForcesReprobe_WithoutThrowing()
        {
            // Capture the SOURCE, not the Font reference: when the OS-font
            // tier wins, ResetCaches destroys the kit-owned Font and a
            // held reference would compare as null afterwards (Unity's
            // overloaded == on destroyed objects), turning this into a
            // false failure on machines where the bundled TTF is absent.
            string sourceBefore = FontFix.EditorMonoFontSource;
            Assert.IsNotEmpty(sourceBefore);
            Assert.DoesNotThrow(FontFix.ResetCaches);
            Font after = FontFix.EditorMonoFont;
            Assert.IsNotNull(after, "re-probing after ResetCaches must"
                + " resolve again");
            Assert.AreEqual(sourceBefore, FontFix.EditorMonoFontSource,
                "with unchanged settings the re-probe must land on the"
                + " same candidate");
        }
    }
}
