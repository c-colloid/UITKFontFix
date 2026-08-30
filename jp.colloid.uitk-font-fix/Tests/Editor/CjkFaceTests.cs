using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the per-face resolution cache, the created-object naming
    /// contract, and the real-Bold weight-table wiring. Machines differ
    /// in installed families, so each test configures the first family
    /// on this machine that provides BOTH a base face and a Bold face
    /// (probed through the public API), and Ignores when none does.
    /// Every test restores default settings in teardown.
    /// </summary>
    public class CjkFaceTests
    {
        private struct FaceFamily
        {
            public string Family;
            public string BaseStyle;
            public string BoldStyle;
        }

        // Linux containers ship DejaVu/Liberation; Windows ships the
        // rest. Style names are face-exact (DejaVu's regular is "Book").
        private static readonly FaceFamily[] Candidates =
        {
            new FaceFamily { Family = "DejaVu Sans", BaseStyle = "Book", BoldStyle = "Bold" },
            new FaceFamily { Family = "Liberation Sans", BaseStyle = "Regular", BoldStyle = "Bold" },
            new FaceFamily { Family = "Yu Gothic UI", BaseStyle = "Regular", BoldStyle = "Bold" },
            new FaceFamily { Family = "Segoe UI", BaseStyle = "Regular", BoldStyle = "Bold" },
            new FaceFamily { Family = "Arial", BaseStyle = "Regular", BoldStyle = "Bold" }
        };

        private FaceFamily _family;

        [SetUp]
        public void SetUp()
        {
            FontFixSettings.ResetToDefaults();
            FontFix.ResetCaches();
            foreach (FaceFamily candidate in Candidates)
            {
                FontFixSettings.CjkUiFontNames = new[] { candidate.Family };
                FontFixSettings.CjkUiStyleName = candidate.BaseStyle;
                FontFixSettings.CjkUiBoldStyleName = candidate.BoldStyle;
                if (FontFix.CjkUiFontAsset != null
                    && FontFix.GetCjkUiFontAsset(candidate.BoldStyle) != null)
                {
                    _family = candidate;
                    return;
                }
            }
            FontFixSettings.ResetToDefaults();
            Assert.Ignore("no installed family provides both a base and a"
                + " Bold face on this machine");
        }

        [TearDown]
        public void TearDown()
        {
            FontFixSettings.ResetToDefaults();
        }

        // -- Naming ------------------------------------------------------------

        [Test]
        public void CreatedCjkAsset_NameCarriesTagAndExactFormat()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            string expected = asset.faceInfo.familyName + " - "
                + asset.faceInfo.styleName + " " + FontFix.CreatedObjectNameTag;
            Assert.AreEqual(expected, asset.name);
            if (asset.material != null)
            {
                Assert.AreEqual(asset.name + " Material", asset.material.name,
                    "TextCore's default material name must be overridden");
            }
            if (asset.atlasTextures != null && asset.atlasTextures.Length > 0
                && asset.atlasTextures[0] != null)
            {
                Assert.AreEqual(asset.name + " Atlas",
                    asset.atlasTextures[0].name,
                    "TextCore's default atlas name must be overridden");
            }
        }

        [Test]
        public void SharedEditorAssets_NeverTagged()
        {
            // Mono settings stay default in this fixture: the bundled
            // TTF / label-font tiers return SHARED editor objects that
            // the kit must never rename.
            Font mono = FontFix.EditorMonoFont;
            if (mono == null || FontFix.EditorMonoFontSource.StartsWith(
                    "os:", System.StringComparison.Ordinal))
            {
                Assert.Ignore("no shared-tier mono font resolved here");
            }
            StringAssert.DoesNotContain(FontFix.CreatedObjectNameTag, mono.name);
        }

        // -- Per-style cache ---------------------------------------------------

        [Test]
        public void GetCjkUiFontAsset_NullEmptyOrBaseStyle_ReturnsSameInstanceAsBase()
        {
            UnityEngine.TextCore.Text.FontAsset baseAsset = FontFix.CjkUiFontAsset;
            Assert.AreSame(baseAsset, FontFix.GetCjkUiFontAsset(null));
            Assert.AreSame(baseAsset, FontFix.GetCjkUiFontAsset(""));
            Assert.AreSame(baseAsset, FontFix.GetCjkUiFontAsset(_family.BaseStyle));
            Assert.AreSame(baseAsset,
                FontFix.GetCjkUiFontAsset(_family.BaseStyle.ToUpperInvariant()),
                "base-style aliasing must be case-insensitive -- otherwise"
                + " the base atlas gets silently duplicated");
        }

        [Test]
        public void GetCjkUiFontAsset_CacheKeyIsOrdinalCaseInsensitive()
        {
            Assert.AreSame(
                FontFix.GetCjkUiFontAsset(_family.BoldStyle),
                FontFix.GetCjkUiFontAsset(_family.BoldStyle.ToLowerInvariant()),
                "one asset per face regardless of caller casing");
        }

        [Test]
        public void GetCjkUiFontAsset_UnknownStyle_ReturnsNullTwice_NeverThrows()
        {
            Assert.DoesNotThrow(delegate
            {
                Assert.IsNull(FontFix.GetCjkUiFontAsset("NoSuchFaceXyz"));
                Assert.IsNull(FontFix.GetCjkUiFontAsset("NoSuchFaceXyz"));
            });
        }

        [Test]
        public void GetCjkUiFontAsset_CalledFirst_TriggersBaseResolution()
        {
            FontFix.ResetCaches();
            UnityEngine.TextCore.Text.FontAsset bold =
                FontFix.GetCjkUiFontAsset(_family.BoldStyle);
            Assert.IsNotNull(bold,
                "styled lookup before any base access must trigger the"
                + " base probe (family is unknown otherwise)");
            Assert.IsNotEmpty(FontFix.CjkUiFontSource);
            Assert.AreEqual(FontFix.CjkUiFontAsset.faceInfo.familyName,
                bold.faceInfo.familyName,
                "styled face must come from the family that won base"
                + " resolution");
        }

        [Test]
        public void StyledAsset_FaceInfoStyleName_Record()
        {
            UnityEngine.TextCore.Text.FontAsset bold =
                FontFix.GetCjkUiFontAsset(_family.BoldStyle);
            // Empirical record: requested vs actual face, the mismatch
            // detector the diagnostics report relies on.
            Debug.Log("[CjkFaceTests] requested '" + _family.BoldStyle
                + "' -> face '" + bold.faceInfo.styleName + "' family '"
                + bold.faceInfo.familyName + "'");
            Assert.IsNotNull(bold);
        }

        // -- Bold wiring -------------------------------------------------------

        [Test]
        public void BoldWiring_Slot7_IsSameInstanceAsGetBold()
        {
            UnityEngine.TextCore.Text.FontAsset baseAsset = FontFix.CjkUiFontAsset;
            UnityEngine.TextCore.Text.FontWeightPair[] table =
                baseAsset.fontWeightTable;
            Assert.IsNotNull(table);
            Assert.Greater(table.Length, 7);
            UnityEngine.TextCore.Text.FontAsset wired = table[7].regularTypeface;
            Assert.IsNotNull(wired,
                "bold face must be wired into fontWeightTable[7] --"
                + " read-back also pins that the getter returns the live"
                + " array on this editor version");
            Assert.AreSame(wired, FontFix.GetCjkUiFontAsset(_family.BoldStyle),
                "the wired instance must BE the per-style cache entry"
                + " (one Bold atlas total)");
            StringAssert.Contains(FontFix.CreatedObjectNameTag, wired.name);
        }

        [Test]
        public void BoldWiring_EmptyStyleName_Disables()
        {
            FontFixSettings.CjkUiBoldStyleName = "";
            UnityEngine.TextCore.Text.FontAsset baseAsset = FontFix.CjkUiFontAsset;
            Assert.IsNotNull(baseAsset, "base must still resolve");
            Assert.IsNull(baseAsset.fontWeightTable[7].regularTypeface,
                "empty bold style must restore the faux-bold behavior");
        }

        [Test]
        public void CjkUiBoldStyleName_NullRestoresDefault()
        {
            FontFixSettings.CjkUiBoldStyleName = null;
            Assert.AreEqual(FontFixDefaults.CjkUiBoldStyleName,
                FontFixSettings.CjkUiBoldStyleName);
        }

        [Test]
        public void CjkUiBoldStyleName_EqualValueKeepsCacheWarm()
        {
            UnityEngine.TextCore.Text.FontAsset before = FontFix.CjkUiFontAsset;
            FontFixSettings.CjkUiBoldStyleName = _family.BoldStyle;
            Assert.AreSame(before, FontFix.CjkUiFontAsset,
                "assigning the equal value must not drop caches");
        }

        [Test]
        public void BoldWiring_SkippedWhenBoldStyleEqualsBaseStyle()
        {
            FontFixSettings.CjkUiBoldStyleName = _family.BaseStyle;
            UnityEngine.TextCore.Text.FontAsset baseAsset = FontFix.CjkUiFontAsset;
            Assert.IsNotNull(baseAsset);
            Assert.IsNull(baseAsset.fontWeightTable[7].regularTypeface,
                "self-wiring guard: the base asset must not be wired into"
                + " its own weight table");
        }

        // -- Lifecycle ---------------------------------------------------------

        [Test]
        public void ResetCaches_DestroysStyleAssets_AndReprobeRewiresFresh()
        {
            UnityEngine.TextCore.Text.FontAsset oldBold =
                FontFix.GetCjkUiFontAsset(_family.BoldStyle);
            Assert.IsNotNull(oldBold);
            FontFix.ResetCaches();
            Assert.IsTrue(oldBold == null,
                "kit-owned style assets must be destroyed on reset"
                + " (fake-null after DestroyImmediate)");
            UnityEngine.TextCore.Text.FontAsset newWired =
                FontFix.CjkUiFontAsset.fontWeightTable[7].regularTypeface;
            Assert.IsNotNull(newWired, "re-probe must rewire a fresh bold");
            Assert.IsFalse(ReferenceEquals(oldBold, newWired));
        }

        [Test]
        public void ChangingCjkUiFontNames_ClearsStyleCache()
        {
            Assert.IsNotNull(FontFix.GetCjkUiFontAsset(_family.BoldStyle));
            FontFixSettings.CjkUiFontNames = new string[0];
            Assert.IsNull(FontFix.CjkUiFontAsset);
            Assert.IsNull(FontFix.GetCjkUiFontAsset(_family.BoldStyle),
                "family binding must reset with the candidate list --"
                + " stale styled assets from the old family must not"
                + " survive");
        }

        // -- Application -------------------------------------------------------

        [Test]
        public void ApplyCjkUiFace_NoOpsOnMiss_AssignsOnHit()
        {
            var missLabel = new Label("header");
            FontFix.ApplyCjkUiFace(missLabel, "NoSuchFaceXyz");
            Assert.AreEqual(StyleKeyword.Null,
                missLabel.style.unityFontDefinition.keyword,
                "a face miss must keep the inherited font");

            var hitLabel = new Label("header");
            FontFix.ApplyCjkUiFace(hitLabel, _family.BoldStyle);
            Assert.AreEqual(StyleKeyword.Undefined,
                hitLabel.style.unityFontDefinition.keyword);
            Assert.AreSame(FontFix.GetCjkUiFontAsset(_family.BoldStyle),
                hitLabel.style.unityFontDefinition.value.fontAsset);
            Assert.DoesNotThrow(delegate
            {
                FontFix.ApplyCjkUiFace(null, _family.BoldStyle);
            });
        }

        // -- Diagnostics -------------------------------------------------------

        [Test]
        public void Diagnostics_ReportListsFacesSection()
        {
            UnityEngine.TextCore.Text.FontAsset unused = FontFix.CjkUiFontAsset;
            string report = FontFixDiagnostics.BuildReport();
            StringAssert.Contains("-- CJK faces --", report);
            StringAssert.Contains("bold wiring    : wired -> ", report);
            StringAssert.Contains("style cache    : ", report);
            StringAssert.Contains("  page 0: ", report);
            Debug.Log("[CjkFaceTests]\n" + report);
        }
    }
}
