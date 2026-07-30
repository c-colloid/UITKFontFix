using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the resolution pipelines. Batch-mode design (verified on
    /// 2022.3): OS dynamic Font FACE operations are dead headless, but
    /// DynamicOS FontAsset creation works -- tests that depend on the
    /// latter still Ignore instead of failing when a particular headless
    /// machine cannot create one, because the interactive editor is the
    /// environment that matters for rendering.
    /// </summary>
    public class FontFixResolveTests
    {
        [SetUp]
        public void SetUp()
        {
            // No-op when settings are already default (cache-preserving).
            FontFixSettings.ResetToDefaults();
        }

        // -- Monospace ---------------------------------------------------------

        [Test]
        public void EditorMonoFont_ResolvesNonNull_InBatchMode()
        {
            Font font = FontFix.EditorMonoFont;
            Assert.IsNotNull(font,
                "FontFix must always resolve a mono font (bundled TTF"
                + " -> face-probed OS font -> default label font)");
        }

        [Test]
        public void EditorMonoFont_IsCached_SameInstance()
        {
            Assert.AreSame(FontFix.EditorMonoFont, FontFix.EditorMonoFont);
        }

        [Test]
        public void EditorMonoFontSource_ReportsWinningCandidate()
        {
            string source = FontFix.EditorMonoFontSource;
            Assert.IsNotEmpty(source, "a candidate must have resolved");
            // Empirical record for the log: which candidate won on this
            // editor version (expected "editor:Fonts/RobotoMono/..." on
            // 2022.3).
            Debug.Log("[FontFixResolveTests] mono source = " + source
                + ", font = " + FontFix.EditorMonoFont.name);
        }

        [Test]
        public void EditorBundledMonoFontPath_Resolves()
        {
            // Empirical probe of the documented editor-bundle paths. At
            // least one candidate must load (verified on 2022.3.22f1); a
            // failure here means the editor moved its bundled mono font
            // and FontFixDefaults.EditorMonoFontPaths needs a new entry.
            bool any = false;
            string[] paths = FontFixDefaults.EditorMonoFontPaths;
            for (int i = 0; i < paths.Length; i++)
            {
                var font = EditorGUIUtility.Load(paths[i]) as Font;
                Debug.Log("[FontFixResolveTests] EditorGUIUtility.Load(\""
                    + paths[i] + "\") -> " + (font != null ? font.name : "null"));
                any |= font != null;
            }
            Assert.IsTrue(any,
                "no editor-bundled mono font path resolved; update"
                + " FontFixDefaults.EditorMonoFontPaths for this editor"
                + " version");
        }

        // -- CJK UI ------------------------------------------------------------

        [Test]
        public void CjkUiFontAsset_NeverThrows_InBatchMode()
        {
            // Unlike the mono pipeline there is no bundled candidate and
            // no label-font last resort: null is a legitimate result on a
            // machine with none of the candidate fonts installed, so this
            // only guards against exceptions.
            Assert.DoesNotThrow(delegate
            {
                UnityEngine.TextCore.Text.FontAsset unused = FontFix.CjkUiFontAsset;
            });
        }

        [Test]
        public void CjkUiFontAsset_IsCached_SameInstance()
        {
            Assert.AreSame(FontFix.CjkUiFontAsset, FontFix.CjkUiFontAsset);
        }

        [Test]
        public void CjkUi_InstalledCandidateNames_ContainYuGothicUi_OnWindows()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Windows-only expectation: Yu Gothic UI"
                    + " ships with Windows 10/11.");
            }
            // Batch-safe half of the "resolves on this machine" claim:
            // the installed-name gate is the FIRST of the two gates
            // ResolveCjkUi applies, and must find Yu Gothic UI on any
            // Windows 10/11 install. Note GetOSInstalledFontNames()
            // reports ENGLISH names even on Japanese Windows.
            string[] installed = Font.GetOSInstalledFontNames() ?? new string[0];
            bool found = false;
            for (int i = 0; i < installed.Length; i++)
            {
                if (string.Equals(installed[i], "Yu Gothic UI",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found,
                "expected \"Yu Gothic UI\" in Font.GetOSInstalledFontNames()"
                + " on this Windows machine");
        }

        [Test]
        public void CjkUiFontAsset_ResolvesOnWindows_AndReportsWinner()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("CJK UI candidates are Windows-first; on"
                    + " other platforms only Noto Sans CJK JP may exist.");
            }
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            if (asset == null && Application.isBatchMode)
            {
                Assert.Ignore("DynamicOS FontAsset creation unavailable in"
                    + " headless batch mode on this machine; exercised in"
                    + " the interactive editor instead.");
            }
            Assert.IsNotNull(asset,
                "expected a CJK UI FontAsset (e.g. Yu Gothic UI) to"
                + " resolve on this Windows machine");
            string source = FontFix.CjkUiFontSource;
            Assert.IsNotEmpty(source, "a candidate must have resolved");
            StringAssert.StartsWith("osasset:", source);
            Debug.Log("[FontFixResolveTests] CJK UI source = " + source
                + ", asset = " + asset.name);
        }
    }
}
