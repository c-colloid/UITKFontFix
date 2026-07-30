using System.Collections.Generic;
using NUnit.Framework;
using Colloid.UitkFontFix;

namespace UitkFontFixSamples.GlyphAuditTests
{
    /// <summary>
    /// Template EditMode test: audits YOUR project's editor sources
    /// with GlyphAudit.AuditSourceDirectory so a fixed UI string can
    /// never bake in a glyph the editor font cannot draw (placeholder
    /// square plus a console warning on every draw). Adapt
    /// AuditedDirectories and ExtraCodepoints below, then keep the test
    /// running in CI.
    ///
    /// What the audit flags in every *.cs file (recursive):
    ///   - codepoints constructed in source (char.ConvertFromUtf32
    ///     literals, (char) casts, unicode escapes) that are outside
    ///     the SafeGlyphs whitelist plus ExtraCodepoints;
    ///   - literal or escaped variation selectors (U+FE0F / U+FE0E);
    ///   - any non-ASCII byte (strict-ASCII source policy keeps glyph
    ///     content reviewable and diff-safe).
    ///
    /// (char) casts of the selector values are exempt: sanitizers
    /// legitimately COMPARE against them in order to remove them, and a
    /// comparison never renders.
    /// </summary>
    public class ProjectGlyphAuditTests
    {
        /// <summary>
        /// Directories to audit, relative to the project root (the
        /// editor's working directory; package paths work too). Point
        /// this at the folders holding YOUR editor UI sources. A
        /// missing directory is reported as an offender instead of
        /// being silently skipped, so a moved folder fails this test
        /// visibly -- fix the list rather than swallowing it.
        /// </summary>
        private static readonly string[] AuditedDirectories =
        {
            "Assets/Editor"
            // , "Assets/MyTool/Editor"
            // , "Packages/com.mycompany.mytool/Editor"
        };

        /// <summary>
        /// Project-specific extension of the SafeGlyphs whitelist,
        /// passed to the audit as extraCodepoints (the same array shape
        /// SafeGlyphs.IsSafeCodepoint(codepoint, extra) accepts). Add a
        /// codepoint ONLY after verifying it actually renders in the
        /// target editor font: drop it into a Label and confirm the
        /// console stays free of "not found in [...]" warnings. Leave
        /// empty (or null) to use the package defaults alone.
        /// </summary>
        private static readonly int[] ExtraCodepoints =
        {
            // Example (add only after verifying rendering):
            // 0x2192, // RIGHTWARDS ARROW
        };

        [Test]
        public void EditorSources_PassGlyphAudit()
        {
            var offenders = new List<string>();
            for (int i = 0; i < AuditedDirectories.Length; i++)
            {
                offenders.AddRange(GlyphAudit.AuditSourceDirectory(
                    AuditedDirectories[i], ExtraCodepoints));
            }
            Assert.IsEmpty(offenders,
                "glyph audit offenders (file: reason):\n"
                + string.Join("\n", offenders.ToArray()));
        }
    }
}
