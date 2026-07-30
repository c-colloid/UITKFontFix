using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Colloid.UitkFontFix.Tests
{
    /// <summary>
    /// Guards the GlyphAudit helper AND self-audits this package's own
    /// shipped sources (Editor + Runtime) with it -- the same dogfood
    /// guard consumer projects are meant to copy. During development a
    /// literal U+FE0F actually slipped into a doc comment of
    /// GlyphAudit.cs itself; the self-audit below is what catches that
    /// class of accident from now on.
    /// </summary>
    public class GlyphAuditTests
    {
        private static readonly string Fe0F = ((char)0xFE0F).ToString();

        // -- Self-audit ------------------------------------------------------

        [Test]
        public void PackageShippedSources_PassGlyphAudit()
        {
            string root = GlyphAudit.PackageRootPath();
            Assert.IsNotNull(root,
                "PackageInfo.FindForAssembly must resolve this package");
            var offenders = new List<string>();
            offenders.AddRange(GlyphAudit.AuditSourceDirectory(
                Path.Combine(root, "Editor"), null));
            offenders.AddRange(GlyphAudit.AuditSourceDirectory(
                Path.Combine(root, "Runtime"), null));
            Assert.IsEmpty(offenders,
                "shipped sources must pass the glyph audit:\n"
                + string.Join("\n", offenders.ToArray()));
        }

        // -- Constructed-codepoint audit -------------------------------------

        [Test]
        public void AuditConstructedCodepoints_FlagsEmojiPlane()
        {
            List<string> offenders = GlyphAudit.AuditConstructedCodepoints(
                "test.cs", "var s = char.ConvertFromUtf32(0x1F4CE);", null);
            Assert.AreEqual(1, offenders.Count);
            StringAssert.Contains("U+1F4CE", offenders[0]);
        }

        [Test]
        public void AuditConstructedCodepoints_AcceptsWhitelisted()
        {
            List<string> offenders = GlyphAudit.AuditConstructedCodepoints(
                "test.cs", "var s = char.ConvertFromUtf32(0x2713);", null);
            Assert.IsEmpty(offenders);
        }

        [Test]
        public void AuditConstructedCodepoints_ExemptsSelectorComparisons()
        {
            // Sanitizers legitimately COMPARE against the selector values
            // in order to remove them; a comparison never renders.
            List<string> offenders = GlyphAudit.AuditConstructedCodepoints(
                "test.cs",
                "if (c == (char)0xFE0F || c == (char)0xFE0E) { }", null);
            Assert.IsEmpty(offenders);
        }

        [Test]
        public void AuditConstructedCodepoints_FlagsUnicodeEscapes()
        {
            List<string> offenders = GlyphAudit.AuditConstructedCodepoints(
                "test.cs", "string s = \"\\u4E00\";", null);
            Assert.AreEqual(1, offenders.Count);
            StringAssert.Contains("U+4E00", offenders[0]);
        }

        [Test]
        public void AuditConstructedCodepoints_ExtraWhitelistExtends()
        {
            List<string> offenders = GlyphAudit.AuditConstructedCodepoints(
                "test.cs", "var s = char.ConvertFromUtf32(0x3042);",
                new[] { 0x3042 });
            Assert.IsEmpty(offenders);
        }

        // -- Variation-selector audit ----------------------------------------

        [Test]
        public void AuditVariationSelectors_FlagsLiteralSelector()
        {
            List<string> offenders = GlyphAudit.AuditVariationSelectors(
                "test.cs", "string s = \"warn" + Fe0F + "\";");
            Assert.AreEqual(1, offenders.Count);
            StringAssert.Contains("U+FE0F", offenders[0]);
        }

        [Test]
        public void AuditVariationSelectors_FlagsEscape()
        {
            List<string> offenders = GlyphAudit.AuditVariationSelectors(
                "test.cs", "string s = \"warn\\uFE0F\";");
            Assert.AreEqual(1, offenders.Count);
            StringAssert.Contains("escape", offenders[0]);
        }

        [Test]
        public void AuditVariationSelectors_CleanTextPasses()
        {
            List<string> offenders = GlyphAudit.AuditVariationSelectors(
                "test.cs", "string s = \"plain\";");
            Assert.IsEmpty(offenders);
        }

        // -- ASCII audit ------------------------------------------------------

        [Test]
        public void AuditAsciiOnly_FlagsNonAsciiByte()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("plain " + Fe0F);
            List<string> offenders = GlyphAudit.AuditAsciiOnly(
                "test.cs", bytes);
            Assert.AreEqual(1, offenders.Count);
            StringAssert.Contains("non-ASCII byte", offenders[0]);
        }

        [Test]
        public void AuditAsciiOnly_CleanBytesPass()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("plain ascii only");
            Assert.IsEmpty(GlyphAudit.AuditAsciiOnly("test.cs", bytes));
        }

        // -- Directory handling ----------------------------------------------

        [Test]
        public void AuditSourceDirectory_MissingDirectory_ReportsOffender()
        {
            List<string> offenders = GlyphAudit.AuditSourceDirectory(
                "Nonexistent/Path/That/Does/Not/Exist", null);
            Assert.AreEqual(1, offenders.Count);
            StringAssert.Contains("not found", offenders[0]);
        }
    }
}
