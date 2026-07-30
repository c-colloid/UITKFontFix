using NUnit.Framework;

namespace Colloid.UitkFontKit.Tests
{
    /// <summary>Guards the safe-glyph whitelist semantics.</summary>
    public class SafeGlyphsTests
    {
        [Test]
        public void PrintableAscii_IsAlwaysSafe()
        {
            for (int c = 0x20; c < 0x7F; c++)
            {
                Assert.IsTrue(SafeGlyphs.IsSafeCodepoint(c),
                    "printable ASCII 0x" + c.ToString("X2"));
            }
        }

        [Test]
        public void ControlCharacters_AreNotSafe()
        {
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x00));
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x1F));
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x7F));
        }

        [Test]
        public void DefaultCodepoints_AreSafe()
        {
            for (int i = 0; i < SafeGlyphs.DefaultCodepoints.Length; i++)
            {
                Assert.IsTrue(
                    SafeGlyphs.IsSafeCodepoint(SafeGlyphs.DefaultCodepoints[i]));
            }
        }

        [Test]
        public void EmojiPlaneAndSelectors_AreExcluded()
        {
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x1F4CE),
                "the paperclip must never come back");
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x1F600));
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0xFE0F));
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0xFE0E));
        }

        [Test]
        public void ExtraCodepoints_ExtendTheWhitelist()
        {
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x3042));
            Assert.IsTrue(SafeGlyphs.IsSafeCodepoint(0x3042,
                new[] { 0x3042 }));
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x3042, new int[0]));
            Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x3042, null));
        }
    }
}
