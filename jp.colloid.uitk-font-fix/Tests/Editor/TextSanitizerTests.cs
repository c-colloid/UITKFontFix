using NUnit.Framework;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the display-text sanitizers, including every surrogate
    /// pitfall the pair-aware scanner must dodge (end-of-string high
    /// surrogates, lone surrogates, and Plane-2 kanji whose LOW units
    /// overlap the ideographic-variation-selector low range). All
    /// non-ASCII test data is constructed from codepoints so this
    /// source file stays strict ASCII itself.
    /// </summary>
    public class TextSanitizerTests
    {
        // Builds a string from Unicode SCALAR values (pairs emitted for
        // supplementary codepoints).
        private static string Cp(params int[] codepoints)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < codepoints.Length; i++)
            {
                sb.Append(char.ConvertFromUtf32(codepoints[i]));
            }
            return sb.ToString();
        }

        // Builds a string from raw UTF-16 CODE UNITS (for lone-surrogate
        // fixtures that ConvertFromUtf32 refuses to produce).
        private static string Units(params int[] units)
        {
            var chars = new char[units.Length];
            for (int i = 0; i < units.Length; i++)
            {
                chars[i] = (char)units[i];
            }
            return new string(chars);
        }

        private static void AssertUnits(string expected, string actual)
        {
            Assert.AreEqual(expected, actual,
                "expected units: " + Describe(expected)
                + " actual units: " + Describe(actual));
        }

        private static string Describe(string s)
        {
            if (s == null)
            {
                return "(null)";
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append("U+").Append(((int)s[i]).ToString("X4")).Append(' ');
            }
            return sb.ToString();
        }

        // -- StripVariationSelectors ------------------------------------------

        [Test]
        public void Vs_CleanAscii_SameInstance()
        {
            string clean = "abc 1";
            Assert.AreSame(clean, TextSanitizer.StripVariationSelectors(clean));
        }

        [Test]
        public void Vs_CleanAstral_SameInstance()
        {
            // Clean supplementary characters must NOT force a rebuild.
            string clean = "A" + Cp(0x1F4CE);
            Assert.AreSame(clean, TextSanitizer.StripVariationSelectors(clean));
        }

        [Test]
        public void Vs_Fe0eFe0f_Removed()
        {
            string input = Cp(0x26A0, 0xFE0F) + " " + Cp(0x2713, 0xFE0E);
            AssertUnits(Cp(0x26A0) + " " + Cp(0x2713),
                TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_Fe00Block_Removed()
        {
            // The FULL FE00-FE0F block is stripped, not just FE0E/FE0F.
            string input = Cp(0x2205, 0xFE00) + "A" + Cp(0xFE0D);
            AssertUnits(Cp(0x2205) + "A",
                TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_MongolianFvs_Removed()
        {
            string input = Cp(0x1820, 0x180B, 0x1821, 0x180F);
            AssertUnits(Cp(0x1820, 0x1821),
                TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_MongolianVowelSeparator_Preserved()
        {
            // U+180E sits among the FVS codepoints but is NOT a
            // variation selector.
            string input = Cp(0x1820, 0x180E, 0x1821);
            Assert.AreSame(input, TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_IvsPair_Removed_KanjiSurvives()
        {
            // Real-world Japanese: kanji followed by an ideographic
            // variation selector (Adobe-Japan1 sequence).
            string input = Cp(0x8FBA, 0xE0104);
            AssertUnits(Cp(0x8FBA),
                TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_UnpairedHighDb40_Preserved()
        {
            // A lone IVS high surrogate is NOT an IVS; strips must not
            // eat half-pairs.
            string input = Units(0x0041, 0xDB40, 0x0042);
            Assert.AreSame(input, TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_LoneLowDd00_Preserved()
        {
            string input = Units(0xDD00, 0x0041);
            Assert.AreSame(input, TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_Plane2KanjiWithIvsShapedLow_Preserved()
        {
            // U+20500 is 0xD841 0xDD00: its LOW unit falls inside the
            // IVS low-surrogate range. Pair-based matching must keep it.
            string input = Units(0xD841, 0xDD00);
            Assert.AreSame(input, TextSanitizer.StripVariationSelectors(input));
        }

        [Test]
        public void Vs_NullAndEmpty_ReturnEmpty()
        {
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripVariationSelectors(null));
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripVariationSelectors(""));
        }

        [Test]
        public void Vs_SelectorsOnly_BecomesEmpty()
        {
            AssertUnits(string.Empty,
                TextSanitizer.StripVariationSelectors(
                    Cp(0xFE0F, 0xFE0E, 0xFE00)));
        }

        // -- StripInvisibleCharacters -----------------------------------------

        [Test]
        public void Inv_CleanCjk_SameInstance()
        {
            string clean = Cp(0x65E5, 0x672C, 0x8A9E);
            Assert.AreSame(clean, TextSanitizer.StripInvisibleCharacters(clean));
        }

        [Test]
        public void Inv_ZeroWidthClass_Removed()
        {
            string input = "A" + Cp(0x200B) + "B" + Cp(0x200C) + "C"
                + Cp(0x200D) + "D";
            AssertUnits("ABCD",
                TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_WordJoinerAndInvisibleOperators_Removed()
        {
            string input = "f" + Cp(0x2060, 0x2061, 0x2064) + "x";
            AssertUnits("fx", TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_BomZwnbsp_Removed()
        {
            // Leading BOM (pasted text) and interior ZWNBSP both go.
            string input = Cp(0xFEFF) + "A" + Cp(0xFEFF);
            AssertUnits("A", TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_VariationSelectorSuperset()
        {
            // Everything StripVariationSelectors removes is removed here
            // too, including supplementary IVS.
            string input = Cp(0x26A0, 0xFE0F) + Cp(0x8FBA, 0xE0104);
            AssertUnits(Cp(0x26A0, 0x8FBA),
                TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_TagCharacters_Removed_BaseEmojiKept()
        {
            // Emoji tag sequence prefix: visible base (U+1F3F4) followed
            // by invisible tag characters. Tags go, base stays.
            string input = Cp(0x1F3F4, 0xE0067, 0xE007F);
            AssertUnits(Cp(0x1F3F4),
                TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_MixedCjkWithIvs_KanjiSurvives()
        {
            string input = "A" + Cp(0x8FBA, 0xE0104, 0x306E, 0x5B57);
            AssertUnits("A" + Cp(0x8FBA, 0x306E, 0x5B57),
                TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_UnpairedHighAtStringEnd_Preserved_NoThrow()
        {
            // Guards the text[i+1] end-of-string lookahead pitfall.
            string input = Units(0x0041, 0xD83D);
            Assert.AreSame(input, TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_UnpairedLowAtStart_Preserved()
        {
            string input = Units(0xDC00, 0x0041);
            Assert.AreSame(input, TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_BidiAndSoftHyphen_Preserved()
        {
            // Documented exclusions: soft hyphen and bidi controls are
            // untouched.
            string input = "A" + Cp(0x00AD, 0x200E, 0x202A) + "B";
            Assert.AreSame(input, TextSanitizer.StripInvisibleCharacters(input));
        }

        [Test]
        public void Inv_OnlyInvisibles_BecomesEmpty()
        {
            AssertUnits(string.Empty,
                TextSanitizer.StripInvisibleCharacters(
                    Cp(0x200B, 0xFEFF, 0xFE0F, 0x2060)));
        }

        [Test]
        public void Inv_NullAndEmpty_ReturnEmpty()
        {
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripInvisibleCharacters(null));
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripInvisibleCharacters(""));
        }

        // -- ReplaceNonBmpCharacters ------------------------------------------

        [Test]
        public void Rep_PureBmp_SameInstance()
        {
            string clean = "A" + Cp(0x4E00, 0xFF11);
            Assert.AreSame(clean,
                TextSanitizer.ReplaceNonBmpCharacters(clean, "?"));
        }

        [Test]
        public void Rep_EmojiPair_OneReplacementPerCodepoint()
        {
            string input = "A" + Cp(0x1F4CE) + "B";
            AssertUnits("A?B",
                TextSanitizer.ReplaceNonBmpCharacters(input, "?"));
        }

        [Test]
        public void Rep_AdjacentEmojiRun_ReplacedPerCodepoint()
        {
            string input = Cp(0x1F600, 0x1F601);
            AssertUnits("??",
                TextSanitizer.ReplaceNonBmpCharacters(input, "?"));
        }

        [Test]
        public void Rep_EmptyReplacement_Deletes()
        {
            AssertUnits(string.Empty,
                TextSanitizer.ReplaceNonBmpCharacters(Cp(0x1F4CE), ""));
        }

        [Test]
        public void Rep_NullReplacement_TreatedAsEmpty()
        {
            AssertUnits("A",
                TextSanitizer.ReplaceNonBmpCharacters("A" + Cp(0x1F4CE), null));
        }

        [Test]
        public void Rep_MultiCharReplacement()
        {
            AssertUnits("[e]",
                TextSanitizer.ReplaceNonBmpCharacters(Cp(0x1F4CE), "[e]"));
        }

        [Test]
        public void Rep_UnpairedHighMidString_Replaced()
        {
            AssertUnits("A?B",
                TextSanitizer.ReplaceNonBmpCharacters(
                    Units(0x0041, 0xD800, 0x0042), "?"));
        }

        [Test]
        public void Rep_UnpairedHighAtStringEnd_Replaced_NoThrow()
        {
            AssertUnits("A?",
                TextSanitizer.ReplaceNonBmpCharacters(
                    Units(0x0041, 0xD83D), "?"));
        }

        [Test]
        public void Rep_UnpairedLowAtStart_Replaced()
        {
            AssertUnits("?A",
                TextSanitizer.ReplaceNonBmpCharacters(
                    Units(0xDC00, 0x0041), "?"));
        }

        [Test]
        public void Rep_LoneLowAtStringEnd_Replaced()
        {
            AssertUnits("A?",
                TextSanitizer.ReplaceNonBmpCharacters(
                    Units(0x0041, 0xDFFF), "?"));
        }

        [Test]
        public void Rep_SupplementaryKanji_ReplacedLossy()
        {
            // U+20B9F is a real jouyou kanji form: legitimate Japanese
            // Plane-2 content IS lost -- the reason the op is opt-in.
            AssertUnits(Cp(0x53F1) + "?",
                TextSanitizer.ReplaceNonBmpCharacters(
                    Cp(0x53F1, 0x20B9F), "?"));
        }

        [Test]
        public void Rep_OutputContainsNoSurrogates()
        {
            string input = Units(0xD800) + Cp(0x1F4CE) + Units(0xDC00);
            string output = TextSanitizer.ReplaceNonBmpCharacters(input, "?");
            AssertUnits("???", output);
            for (int i = 0; i < output.Length; i++)
            {
                Assert.IsFalse(output[i] >= 0xD800 && output[i] <= 0xDFFF,
                    "surrogate code unit survived at index " + i);
            }
        }

        [Test]
        public void Rep_NullInput_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty,
                TextSanitizer.ReplaceNonBmpCharacters(null, "?"));
        }

        // -- FontFix facade ----------------------------------------------------

        [Test]
        public void Facade_Sanitize_Lossless_KeepsEmoji()
        {
            string input = Cp(0x26A0, 0xFE0F, 0x200B) + Cp(0x1F4CE);
            AssertUnits(Cp(0x26A0) + Cp(0x1F4CE),
                FontFix.SanitizeDisplayText(input));
        }

        [Test]
        public void Facade_Sanitize_SameInstance_WhenClean()
        {
            string clean = "plain ascii and " + Cp(0x2713);
            Assert.AreSame(clean, FontFix.SanitizeDisplayText(clean));
        }

        [Test]
        public void Facade_Sanitize_Lossy_StripBeforeReplace()
        {
            // IVS must be STRIPPED first, not replaced: replace-first
            // would emit a visible "?" after the kanji.
            string input = Cp(0x8FBA, 0xE0104) + Cp(0x1F4CE);
            AssertUnits(Cp(0x8FBA) + "?",
                FontFix.SanitizeDisplayText(input, "?"));
        }

        [Test]
        public void Facade_Sanitize_NullAndEmpty()
        {
            Assert.AreEqual(string.Empty, FontFix.SanitizeDisplayText(null));
            Assert.AreEqual(string.Empty, FontFix.SanitizeDisplayText(""));
            Assert.AreEqual(string.Empty,
                FontFix.SanitizeDisplayText(null, "?"));
            Assert.AreEqual(string.Empty,
                FontFix.SanitizeDisplayText("", "?"));
        }
    }
}
