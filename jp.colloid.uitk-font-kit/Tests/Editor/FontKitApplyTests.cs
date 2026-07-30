using NUnit.Framework;
using UnityEngine.UIElements;

namespace Colloid.UitkFontKit.Tests
{
    /// <summary>
    /// Guards the application helpers: inline assignment semantics, null
    /// safety, and the mono-leaf-inside-CJK-root composition contract.
    /// </summary>
    public class FontKitApplyTests
    {
        [SetUp]
        public void SetUp()
        {
            FontKitSettings.ResetToDefaults();
        }

        [Test]
        public void ApplyMono_NeverThrows_AndAssignsDefinition()
        {
            var label = new Label("code");
            Assert.DoesNotThrow(delegate { FontKit.ApplyMono(label); });
            Assert.DoesNotThrow(delegate { FontKit.ApplyMono(null); });
            if (FontKit.EditorMonoFont != null)
            {
                Assert.AreEqual(StyleKeyword.Undefined,
                    label.style.unityFontDefinition.keyword,
                    "a resolved mono font must be assigned inline");
                Assert.IsNotNull(label.style.unityFontDefinition.value.font,
                    "the mono assignment must carry a classic Font");
            }
        }

        [Test]
        public void ApplyCjkUi_NeverThrows_AndAssignsDefinitionWhenResolved()
        {
            var root = new VisualElement();
            Assert.DoesNotThrow(delegate { FontKit.ApplyCjkUi(root); });
            Assert.DoesNotThrow(delegate { FontKit.ApplyCjkUi(null); });
            if (FontKit.CjkUiFontAsset != null)
            {
                Assert.AreEqual(StyleKeyword.Undefined,
                    root.style.unityFontDefinition.keyword,
                    "a resolved CJK UI font must be assigned");
                Assert.IsNotNull(root.style.unityFontDefinition.value.fontAsset,
                    "the assignment must carry the SDF FontAsset");
            }
        }

        [Test]
        public void ApplyCjkUi_NoOps_WhenNothingResolved()
        {
            FontKitSettings.CjkUiFontNames = new string[0];
            try
            {
                var root = new VisualElement();
                FontKit.ApplyCjkUi(root);
                Assert.AreEqual(StyleKeyword.Null,
                    root.style.unityFontDefinition.keyword,
                    "with no resolvable candidate the element must keep"
                    + " its inherited font (no inline definition)");
            }
            finally
            {
                FontKitSettings.ResetToDefaults();
            }
        }

        [Test]
        public void BothInlineOnSameElement_LastWriteWins()
        {
            // Pins the mechanic behind the documented composition rule
            // (CJK on the container ROOT, mono on LEAVES): inline writes
            // on the SAME element are last-write-wins, which is exactly
            // why production code must never target one element with
            // both. The inherited-vs-inline contest (mono leaf keeps
            // winning inside a CJK root regardless of order) requires a
            // live panel to observe and is covered by on-device probes.
            if (FontKit.CjkUiFontAsset == null || FontKit.EditorMonoFont == null)
            {
                Assert.Ignore("both a CJK UI font and a mono font must"
                    + " resolve on this machine to exercise the ordering");
            }
            var leaf = new Label("code");
            FontKit.ApplyMono(leaf);
            FontKit.ApplyCjkUi(leaf);
            Assert.AreEqual(FontKit.CjkUiFontAsset.name,
                leaf.style.unityFontDefinition.value.fontAsset.name,
                "last inline assignment on one element wins");
        }
    }
}
