using NUnit.Framework;

namespace Colloid.UitkFontKit.Tests
{
    /// <summary>
    /// Guards variation-selector stripping. All non-ASCII test data is
    /// constructed from codepoints so this source file stays strict
    /// ASCII itself.
    /// </summary>
    public class TextSanitizerTests
    {
        private static readonly string Warn = char.ConvertFromUtf32(0x26A0);
        private static readonly string Check = char.ConvertFromUtf32(0x2713);
        private static readonly string Fe0F = ((char)0xFE0F).ToString();
        private static readonly string Fe0E = ((char)0xFE0E).ToString();

        private static bool ContainsOrdinal(string haystack, char needle)
        {
            // char overload of IndexOf = ordinal. Culture-sensitive
            // searches treat variation selectors as collation-ignorable
            // and misreport on non-ASCII strings.
            return haystack.IndexOf(needle) >= 0;
        }

        [Test]
        public void StripVariationSelectors_RemovesFe0FAndFe0E()
        {
            string input = Warn + Fe0F + " done " + Check + Fe0E + "!";
            string output = TextSanitizer.StripVariationSelectors(input);
            Assert.AreEqual(Warn + " done " + Check + "!", output);
            Assert.IsFalse(ContainsOrdinal(output, (char)0xFE0F));
            Assert.IsFalse(ContainsOrdinal(output, (char)0xFE0E));
        }

        [Test]
        public void StripVariationSelectors_ReturnsSameInstance_WhenClean()
        {
            string clean = "plain ascii and " + Check;
            Assert.AreSame(clean, TextSanitizer.StripVariationSelectors(clean));
        }

        [Test]
        public void StripVariationSelectors_NullAndEmpty_AreSafe()
        {
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripVariationSelectors(null));
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripVariationSelectors(""));
        }

        [Test]
        public void StripVariationSelectors_SelectorOnly_BecomesEmpty()
        {
            Assert.AreEqual(string.Empty,
                TextSanitizer.StripVariationSelectors(Fe0F + Fe0E));
        }

        [Test]
        public void FontKitFacade_Delegates()
        {
            string input = Warn + Fe0F;
            Assert.AreEqual(Warn, FontKit.StripVariationSelectors(input));
        }
    }
}
