using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the transient-object lifecycle: the protective hideFlags
    /// TextCore does not set, the liveness checks that keep a damaged
    /// FontAsset from reaching UI Toolkit, the in-place repair that
    /// preserves instance identity, and the CachesInvalidated contract.
    ///
    /// The Play Mode transition that destroys the atlas material and
    /// pages cannot be driven from an EditMode test, so the damage is
    /// simulated the way the editor produces it: DestroyImmediate on the
    /// children while the asset itself stays alive. Like CjkFaceTests,
    /// the fixture binds the first family available on this machine
    /// (Linux CI containers have DejaVu/Liberation), so these run for
    /// real headless.
    /// </summary>
    public class FontAssetLifecycleTests
    {
        private struct FaceFamily
        {
            public string Family;
            public string BaseStyle;
            public string BoldStyle;
        }

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
                if (FontFix.CjkUiFontAsset != null)
                {
                    _family = candidate;
                    return;
                }
            }
            FontFixSettings.ResetToDefaults();
            Assert.Ignore("no installed family resolves a DynamicOS"
                + " FontAsset on this machine");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            FontFixSettings.ResetToDefaults();
            FontFix.ResetCaches();
        }

        // -- Prevention --------------------------------------------------------

        [Test]
        public void CreatedAsset_StampsProtectiveFlagsOnMaterialAndAtlasPages()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            Assert.AreEqual(HideFlags.HideAndDontSave,
                asset.hideFlags & HideFlags.HideAndDontSave,
                "the asset itself must stay hidden and unsaved");

            Assert.IsNotNull(asset.material, "a created asset has a material");
            Assert.AreEqual(HideFlags.DontSave,
                asset.material.hideFlags & HideFlags.DontSave,
                "TextCore creates the atlas material with HideFlags.None;"
                + " without the DontSave bits a Play Mode transition"
                + " destroys it while the asset survives");

            Texture2D[] pages = asset.atlasTextures;
            Assert.IsNotNull(pages);
            Assert.Greater(pages.Length, 0);
            int used = asset.atlasTextureCount;
            for (int i = 0; i < used && i < pages.Length; i++)
            {
                Assert.IsNotNull(pages[i], "in-use page " + i + " must exist");
                Assert.AreEqual(HideFlags.DontSave,
                    pages[i].hideFlags & HideFlags.DontSave,
                    "atlas page " + i + " must carry the DontSave bits");
            }
        }

        [Test]
        public void AtlasTextures_ReturnsLiveArray_SoPageAssignmentSticks()
        {
            // The in-place repair replaces page 0 through this array. If
            // a future Unity returns a copy instead of the backing field,
            // the repair would silently do nothing -- fail here instead.
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            Texture2D[] pages = asset.atlasTextures;
            Texture2D original = pages[0];
            var probe = new Texture2D(1, 1, TextureFormat.Alpha8, false);
            try
            {
                pages[0] = probe;
                Assert.AreSame(probe, asset.atlasTextures[0],
                    "atlasTextures must expose the live backing array");
            }
            finally
            {
                pages[0] = original;
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void Stamp_WithDestroyedChildren_DoesNotThrow()
        {
            // hideFlags is a native property: stamping a destroyed object
            // throws, so the stamp pass must skip dead children.
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            DestroyChildren(asset);
            Assert.DoesNotThrow(delegate { FontAssetLifecycle.Stamp(asset); });
        }

        // -- Detection ---------------------------------------------------------

        [Test]
        public void DestroyedChildren_AreDetected()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            Assert.IsTrue(FontAssetLifecycle.IsUsable(asset));
            Assert.IsFalse(FontAssetLifecycle.NeedsAttention(asset));

            DestroyChildren(asset);

            Assert.IsFalse(FontAssetLifecycle.IsUsable(asset),
                "an asset whose material and atlas page are destroyed is"
                + " not usable, even though the asset itself is alive");
            Assert.IsTrue(FontAssetLifecycle.NeedsAttention(asset));
        }

        [Test]
        public void CachedMiss_IsNotTreatedAsDamage()
        {
            Assert.IsTrue(FontAssetLifecycle.IsCachedMiss(null));
            Assert.IsFalse(FontAssetLifecycle.NeedsAttention(null),
                "a present-null cache entry is a resolution MISS, not"
                + " damage: repairing it would re-probe on every access");
        }

        [Test]
        public void NegativeStyleCache_SurvivesRepeatedLookups()
        {
            const string missingFace = "NoSuchFaceXyz";
            Assert.IsNull(FontFix.GetCjkUiFontAsset(missingFace));
            for (int i = 0; i < 5; i++)
            {
                Assert.IsNull(FontFix.GetCjkUiFontAsset(missingFace));
            }
            int misses = 0;
            var snapshot = FontFix.GetCjkStyleCacheSnapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (string.Equals(snapshot[i].Key, missingFace,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    misses++;
                    Assert.IsNull(snapshot[i].Value);
                }
            }
            Assert.AreEqual(1, misses,
                "the cached miss must stay exactly one entry: dropping it"
                + " would re-run CreateFontAsset on every lookup");
        }

        // -- Recovery ----------------------------------------------------------

        [Test]
        public void TryRepair_RestoresMaterialAndAtlas_OnTheSameInstance()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            Material deadMaterial = asset.material;
            DestroyChildren(asset);

            Assert.IsTrue(FontAssetLifecycle.TryRepair(asset),
                "a live asset with destroyed children must be repairable");
            Assert.IsTrue(FontAssetLifecycle.IsUsable(asset));
            Assert.IsNotNull(asset.material);
            Assert.AreNotSame(deadMaterial, asset.material);
            Assert.IsNotNull(asset.atlasTextures[0]);
            Assert.IsTrue(asset.atlasTextures[0].isReadable,
                "TextCore rasterizes glyphs into the page: it must be"
                + " readable or text renders blank");
            Assert.AreEqual(HideFlags.DontSave,
                asset.material.hideFlags & HideFlags.DontSave,
                "the replacement material must be protected too");
            StringAssert.Contains(FontFix.CreatedObjectNameTag, asset.material.name);
            Assert.IsNotNull(asset.characterLookupTable,
                "the reset must leave the lookup tables rebuilt");
        }

        [Test]
        public void Getter_SelfHeals_KeepingTheSameInstance()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            DestroyChildren(asset);

            UnityEngine.TextCore.Text.FontAsset again = FontFix.CjkUiFontAsset;

            Assert.AreSame(asset, again,
                "identity is the point: elements already holding this"
                + " FontAsset in their inline style must keep working");
            Assert.IsTrue(FontAssetLifecycle.IsUsable(again));
        }

        [Test]
        public void Getter_RepairsCachedStyleFaces_Too()
        {
            UnityEngine.TextCore.Text.FontAsset bold =
                FontFix.GetCjkUiFontAsset(_family.BoldStyle);
            if (bold == null)
            {
                Assert.Ignore("this family has no separate Bold face");
            }
            DestroyChildren(bold);

            UnityEngine.TextCore.Text.FontAsset unused = FontFix.CjkUiFontAsset;

            Assert.IsTrue(FontAssetLifecycle.IsUsable(bold),
                "the sweep must cover every cached face, not just the"
                + " base one -- bold text draws through this asset");
            Assert.AreSame(bold, FontFix.GetCjkUiFontAsset(_family.BoldStyle));
        }

        [Test]
        public void Repair_KeepsTheBoldWeightTableSlot()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            UnityEngine.TextCore.Text.FontWeightPair[] table = asset.fontWeightTable;
            if (table == null || table.Length <= 7 || table[7].regularTypeface == null)
            {
                Assert.Ignore("no real Bold face is wired on this machine");
            }
            UnityEngine.TextCore.Text.FontAsset wired = table[7].regularTypeface;

            DestroyChildren(asset);
            Assert.IsTrue(FontAssetLifecycle.TryRepair(asset));

            UnityEngine.TextCore.Text.FontWeightPair[] after = asset.fontWeightTable;
            Assert.IsNotNull(after);
            Assert.AreSame(wired, after[7].regularTypeface,
                "the reset must not drop the real-bold wiring");
        }

        // -- CachesInvalidated -------------------------------------------------

        [Test]
        public void ResetCaches_RaisesOnce()
        {
            int raises = 0;
            System.Action handler = delegate { raises++; };
            FontFix.CachesInvalidated += handler;
            try
            {
                FontFix.ResetCaches();
                Assert.AreEqual(1, raises);
            }
            finally
            {
                FontFix.CachesInvalidated -= handler;
            }
        }

        [Test]
        public void InPlaceRepair_DoesNotRaise()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            int raises = 0;
            System.Action handler = delegate { raises++; };
            FontFix.CachesInvalidated += handler;
            try
            {
                DestroyChildren(asset);
                UnityEngine.TextCore.Text.FontAsset healed = FontFix.CjkUiFontAsset;
                Assert.AreSame(asset, healed);
                Assert.AreEqual(0, raises,
                    "the instance did not change, so subscribers have"
                    + " nothing to re-apply");
            }
            finally
            {
                FontFix.CachesInvalidated -= handler;
            }
        }

        [Test]
        public void SettingsBatch_RaisesOncePerOperation_AndNotAtAllWhenNothingChanges()
        {
            int raises = 0;
            System.Action handler = delegate { raises++; };
            FontFix.CachesInvalidated += handler;
            try
            {
                // SetUp left five non-default values: restoring them is
                // one user operation and must be one notification.
                FontFixSettings.ResetToDefaults();
                Assert.AreEqual(1, raises,
                    "five property assignments must coalesce into one raise");

                FontFixSettings.ResetToDefaults();
                Assert.AreEqual(1, raises,
                    "a batch in which nothing actually changed must not raise");
            }
            finally
            {
                FontFix.CachesInvalidated -= handler;
            }
        }

        [Test]
        public void Handler_ResolvesFreshUsableInstance_DuringRaise()
        {
            UnityEngine.TextCore.Text.FontAsset fromHandler = null;
            System.Action handler = delegate
            {
                fromHandler = FontFix.CjkUiFontAsset;
            };
            FontFix.CachesInvalidated += handler;
            try
            {
                FontFix.ResetCaches();
            }
            finally
            {
                FontFix.CachesInvalidated -= handler;
            }
            Assert.IsNotNull(fromHandler,
                "the raise happens after the probed flags are cleared, so"
                + " a handler that re-applies re-resolves instead of"
                + " seeing a cached null");
            Assert.IsTrue(FontAssetLifecycle.IsUsable(fromHandler));
            Assert.AreSame(fromHandler, FontFix.CjkUiFontAsset);
        }

        [Test]
        public void ThrowingHandler_IsLogged_AndDoesNotStarveOthers()
        {
            bool secondRan = false;
            System.Action bad = delegate
            {
                throw new System.InvalidOperationException("handler under test");
            };
            System.Action good = delegate { secondRan = true; };
            LogAssert.ignoreFailingMessages = true;
            FontFix.CachesInvalidated += bad;
            FontFix.CachesInvalidated += good;
            try
            {
                Assert.DoesNotThrow(delegate { FontFix.ResetCaches(); });
                Assert.IsTrue(secondRan,
                    "one broken subscriber must not starve the others");
            }
            finally
            {
                FontFix.CachesInvalidated -= bad;
                FontFix.CachesInvalidated -= good;
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.IsTrue(FontAssetLifecycle.IsUsable(FontFix.CjkUiFontAsset),
                "invalidation must complete despite the exception");
        }

        [Test]
        public void HandlerThatInvalidatesAgain_Terminates()
        {
            int calls = 0;
            System.Action handler = null;
            handler = delegate
            {
                calls++;
                if (calls < 10)
                {
                    FontFix.ResetCaches();
                }
            };
            FontFix.CachesInvalidated += handler;
            try
            {
                Assert.DoesNotThrow(delegate { FontFix.ResetCaches(); });
            }
            finally
            {
                FontFix.CachesInvalidated -= handler;
            }
            Assert.LessOrEqual(calls, 2,
                "a nested invalidation drains at most once instead of"
                + " recursing without end");
            Assert.GreaterOrEqual(calls, 1);
        }

        // -- Helpers -----------------------------------------------------------

        // -- Scene loads and lazily added pages --------------------------------

        [Test]
        public void TryRepair_ReplacesTheMaterial_EvenWhenOnlyAPageDied()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            Material survivor = asset.material;
            Object.DestroyImmediate(asset.atlasTextures[0]);
            Assert.IsTrue(FontAssetLifecycle.NeedsAttention(asset),
                "a dead page with a live material is damage");

            Assert.IsTrue(FontAssetLifecycle.TryRepair(asset));

            Assert.IsTrue(FontAssetLifecycle.IsUsable(asset));
            Assert.AreNotSame(survivor, asset.material,
                "UI Toolkit regenerates an element's cached text mesh only"
                + " when its generation-settings hash changes, and that hash"
                + " covers the material: keeping the survivor would leave"
                + " already-drawn elements pointing at the dead page");
            Assert.IsTrue(survivor == null,
                "the replaced material must be destroyed, not leaked");
            Assert.AreSame(asset.atlasTextures[0], asset.material.mainTexture,
                "the new material must point at the live page 0");
        }

        [Test]
        public void LazyPages_AreBornUnflagged_AndTheGuardTickStampsThem()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                asset.TryAddCharacters(ManyDistinctCharacters());
            }
            catch (System.Exception e)
            {
                Assert.Ignore("TryAddCharacters is unavailable here: " + e.GetType().Name);
            }
            int used = asset.atlasTextureCount;
            if (used < 2)
            {
                Assert.Ignore("the atlas did not grow past one page on this machine");
            }

            Texture2D[] pages = asset.atlasTextures;
            bool sawUnflagged = false;
            for (int i = 1; i < used; i++)
            {
                Assert.IsNotNull(pages[i], "page " + i + " must be alive");
                if (pages[i].hideFlags == HideFlags.None)
                {
                    sawUnflagged = true;
                }
            }
            Assert.IsTrue(sawUnflagged,
                "TextCore creates later pages unflagged; if this ever stops"
                + " holding, the guard tick is no longer needed");

            FontFix.GuardTick();

            for (int i = 0; i < used; i++)
            {
                Assert.AreEqual(HideFlags.DontSave,
                    pages[i].hideFlags & HideFlags.DontSave,
                    "page " + i + " must be protected after one guard tick");
            }
        }

        [Test]
        public void NewScene_KeepsStampedChildren_AndKillsUnflaggedObjects()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            Material material = asset.material;
            Texture2D page = asset.atlasTextures[0];
            // Control: what TextCore's lazily added pages look like
            // before the guard stamps them.
            var control = new Texture2D(1, 1, TextureFormat.Alpha8, false);

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            Assert.IsTrue(control == null,
                "a scene load destroys HideFlags.None objects -- the"
                + " mechanism the whole guard exists for");
            Assert.IsTrue(material != null, "the stamped material survives");
            Assert.IsTrue(page != null, "the stamped page survives");
            Assert.IsTrue(FontAssetLifecycle.IsUsable(asset));
        }

        [Test]
        public void NewScene_SweepsAndRepairsWithoutAnyGetterAccess()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            DestroyChildren(asset);
            Assert.IsFalse(FontAssetLifecycle.IsUsable(asset));

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            Assert.IsTrue(FontAssetLifecycle.IsUsable(asset),
                "the scene hook must sweep and repair on its own: a"
                + " consumer that applied the font once never reads the"
                + " getter again");
            Assert.AreSame(asset, FontFix.CjkUiFontAsset);
        }

        [Test]
        public void GuardTick_RepairsDamage_WithoutAnyGetterAccess()
        {
            UnityEngine.TextCore.Text.FontAsset asset = FontFix.CjkUiFontAsset;
            DestroyChildren(asset);

            // The liveness check is rate limited; a fresh interval has
            // elapsed for sure only after a second tick past the window.
            FontFix.GuardTick();
            System.Threading.Thread.Sleep(300);
            FontFix.GuardTick();

            Assert.IsTrue(FontAssetLifecycle.IsUsable(asset));
            Assert.AreSame(asset, FontFix.CjkUiFontAsset);
        }

        private static string ManyDistinctCharacters()
        {
            // Built from code points (sources stay ASCII): printable
            // Basic Latin, Latin-1 letters, Greek and Cyrillic -- around
            // 280 glyphs, several pages at the 90 pt sampling size
            // CreateFontAsset uses. Ranges a family lacks are skipped by
            // TextCore, which only shrinks the page count.
            var sb = new System.Text.StringBuilder();
            AppendRange(sb, 0x21, 0x7E);
            AppendRange(sb, 0xC0, 0xFF);
            AppendRange(sb, 0x391, 0x3C9);
            AppendRange(sb, 0x410, 0x44F);
            return sb.ToString();
        }

        private static void AppendRange(System.Text.StringBuilder sb, int first, int last)
        {
            for (int cp = first; cp <= last; cp++)
            {
                sb.Append((char)cp);
            }
        }

        private static void DestroyChildren(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            // Reproduces what the editor does to objects left at
            // HideFlags.None across a Play Mode transition: the children
            // die, the asset does not.
            Assert.IsNotNull(asset);
            if (asset.material != null)
            {
                Object.DestroyImmediate(asset.material);
            }
            Texture2D[] pages = asset.atlasTextures;
            int used = asset.atlasTextureCount;
            for (int i = 0; i < used && i < pages.Length; i++)
            {
                if (pages[i] != null)
                {
                    Object.DestroyImmediate(pages[i]);
                }
            }
        }
    }
}
