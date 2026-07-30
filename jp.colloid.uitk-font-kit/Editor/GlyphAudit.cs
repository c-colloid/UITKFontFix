using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Colloid.UitkFontKit
{
    /// <summary>
    /// Source-level glyph safety audit, usable from consumer test
    /// assemblies: scans C# sources for constructed codepoints outside
    /// the safe-glyph whitelist, for variation-selector literals/escapes
    /// and for non-ASCII bytes. Guards against the live-feedback defect
    /// class where a fixed UI string bakes in a glyph the editor font
    /// cannot draw (placeholder square + per-draw console warning).
    ///
    /// Intended usage from a test:
    /// <code>
    ///   var offenders = GlyphAudit.AuditSourceDirectory(myEditorDir, null);
    ///   Assert.IsEmpty(offenders, string.Join("\n", offenders));
    /// </code>
    /// </summary>
    public static class GlyphAudit
    {
        private static readonly Regex ConvertFromUtf32Pattern = new Regex(
            @"ConvertFromUtf32\(\s*0x([0-9A-Fa-f]+)\s*\)");
        private static readonly Regex CharCastPattern = new Regex(
            @"\(char\)\s*0x([0-9A-Fa-f]{2,6})");
        private static readonly Regex UnicodeEscapePattern = new Regex(
            @"\\u([0-9A-Fa-f]{4})");
        private static readonly Regex LongUnicodeEscapePattern = new Regex(
            @"\\U([0-9A-Fa-f]{8})");
        private static readonly Regex SelectorEscapePattern = new Regex(
            @"\\u[Ff][Ee]0[EFef]");

        /// <summary>
        /// Root directory of THIS package's sources (works for embedded,
        /// junction and package-cache installs), or null when the
        /// package manager cannot resolve it.
        /// </summary>
        public static string PackageRootPath()
        {
            try
            {
                UnityEditor.PackageManager.PackageInfo info =
                    UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                        typeof(GlyphAudit).Assembly);
                return info != null ? info.resolvedPath : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Runs every audit over all *.cs files under
        /// <paramref name="directory"/> (recursive). Returns offender
        /// descriptions ("file: reason"); empty list means clean. A
        /// missing directory yields a single offender entry instead of
        /// throwing, so a moved path fails a test visibly.
        /// </summary>
        public static List<string> AuditSourceDirectory(
            string directory, int[] extraCodepoints)
        {
            var offenders = new List<string>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                offenders.Add("(audit): source directory not found: "
                    + (directory ?? "(null)"));
                return offenders;
            }
            string[] files = Directory.GetFiles(
                directory, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                offenders.AddRange(AuditSourceFile(files[i], extraCodepoints));
            }
            return offenders;
        }

        /// <summary>
        /// Runs every audit over one source file: constructed codepoints
        /// against the whitelist, variation-selector literals/escapes,
        /// and strict-ASCII bytes.
        /// </summary>
        public static List<string> AuditSourceFile(
            string path, int[] extraCodepoints)
        {
            var offenders = new List<string>();
            string label = Path.GetFileName(path);
            byte[] bytes = File.ReadAllBytes(path);
            string text = File.ReadAllText(path);
            offenders.AddRange(AuditConstructedCodepoints(
                label, text, extraCodepoints));
            offenders.AddRange(AuditVariationSelectors(label, text));
            offenders.AddRange(AuditAsciiOnly(label, bytes));
            return offenders;
        }

        /// <summary>
        /// Flags codepoints constructed in source (char.ConvertFromUtf32
        /// literals, (char) casts, \uXXXX and \UXXXXXXXX escapes) that
        /// are outside the safe-glyph whitelist
        /// (SafeGlyphs.IsSafeCodepoint plus
        /// <paramref name="extraCodepoints"/>). (char) casts of
        /// U+FE0F/U+FE0E/U+FEFF are exempt: sanitizers and BOM skips
        /// legitimately COMPARE against those values in order to remove
        /// them, and a comparison never renders.
        /// </summary>
        public static List<string> AuditConstructedCodepoints(
            string fileLabel, string text, int[] extraCodepoints)
        {
            var offenders = new List<string>();
            CollectMatches(offenders, fileLabel, ConvertFromUtf32Pattern,
                text, false, extraCodepoints);
            CollectMatches(offenders, fileLabel, CharCastPattern,
                text, true, extraCodepoints);
            CollectMatches(offenders, fileLabel, UnicodeEscapePattern,
                text, false, extraCodepoints);
            CollectMatches(offenders, fileLabel, LongUnicodeEscapePattern,
                text, false, extraCodepoints);
            return offenders;
        }

        /// <summary>
        /// Flags literal U+FE0F/U+FE0E characters and their escape
        /// forms. Ordinal scans on purpose: culture-sensitive searches
        /// treat variation selectors as collation-ignorable and can
        /// "find" them in strings that do not contain them.
        /// </summary>
        public static List<string> AuditVariationSelectors(
            string fileLabel, string text)
        {
            var offenders = new List<string>();
            if (text.IndexOf((char)0xFE0F) >= 0)
            {
                offenders.Add(fileLabel + ": literal U+FE0F");
            }
            if (text.IndexOf((char)0xFE0E) >= 0)
            {
                offenders.Add(fileLabel + ": literal U+FE0E");
            }
            if (SelectorEscapePattern.IsMatch(text))
            {
                offenders.Add(fileLabel + ": variation-selector escape");
            }
            return offenders;
        }

        /// <summary>
        /// Flags non-ASCII bytes (strict-ASCII source policy keeps glyph
        /// content reviewable and diff-safe). Reports the first offense
        /// per file only.
        /// </summary>
        public static List<string> AuditAsciiOnly(
            string fileLabel, byte[] bytes)
        {
            var offenders = new List<string>();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] > 0x7F)
                {
                    offenders.Add(fileLabel + ": non-ASCII byte 0x"
                        + bytes[i].ToString("X2") + " at offset " + i);
                    break;
                }
            }
            return offenders;
        }

        private static void CollectMatches(List<string> offenders,
            string fileLabel, Regex pattern, string text,
            bool allowSelectorComparisons, int[] extraCodepoints)
        {
            foreach (Match match in pattern.Matches(text))
            {
                int codepoint = System.Convert.ToInt32(
                    match.Groups[1].Value, 16);
                if (allowSelectorComparisons
                    && (codepoint == 0xFE0F || codepoint == 0xFE0E
                        || codepoint == 0xFEFF))
                {
                    continue;
                }
                if (!SafeGlyphs.IsSafeCodepoint(codepoint, extraCodepoints))
                {
                    offenders.Add(fileLabel + ": U+"
                        + codepoint.ToString("X4") + " (" + match.Value + ")");
                }
            }
        }
    }
}
