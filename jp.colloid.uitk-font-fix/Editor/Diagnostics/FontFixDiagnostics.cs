using System.Text;
using UnityEngine;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Builds the plain-text diagnostics report shown by the
    /// diagnostics window (Window > UITK Font Fix > Diagnostics) and
    /// usable from logs/bug reports. Never throws; every risky probe is
    /// guarded so the report itself cannot take a broken environment
    /// down further.
    /// </summary>
    public static class FontFixDiagnostics
    {
        /// <summary>Builds the full report (ASCII, one finding per line).</summary>
        public static string BuildReport()
        {
            var sb = new StringBuilder(2048);
            sb.Append("UITK Font Fix diagnostics\n");
            sb.Append("package: jp.colloid.uitk-font-fix\n");
            sb.Append("unity  : ").Append(SafeUnityVersion()).Append('\n');
            sb.Append('\n');

            AppendResolutionSection(sb);
            AppendAtlasSection(sb);
            AppendEnvironmentSection(sb);
            AppendKnownTrapsSection(sb);
            return sb.ToString();
        }

        private static void AppendResolutionSection(StringBuilder sb)
        {
            sb.Append("-- Resolution --\n");
            try
            {
                Font mono = FontFix.EditorMonoFont;
                sb.Append("mono   : ")
                    .Append(EmptyAsNone(FontFix.EditorMonoFontSource))
                    .Append(" (")
                    .Append(mono != null ? mono.name : "null")
                    .Append(")\n");
            }
            catch (System.Exception e)
            {
                sb.Append("mono   : probe threw ")
                    .Append(e.GetType().Name).Append('\n');
            }
            try
            {
                UnityEngine.TextCore.Text.FontAsset cjk = FontFix.CjkUiFontAsset;
                sb.Append("cjk-ui : ")
                    .Append(EmptyAsNone(FontFix.CjkUiFontSource))
                    .Append(" (")
                    .Append(cjk != null ? cjk.name : "null")
                    .Append(")\n");
            }
            catch (System.Exception e)
            {
                sb.Append("cjk-ui : probe threw ")
                    .Append(e.GetType().Name).Append('\n');
            }
            sb.Append('\n');
        }

        private static void AppendAtlasSection(StringBuilder sb)
        {
            sb.Append("-- CJK atlas --\n");
            UnityEngine.TextCore.Text.FontAsset cjk = null;
            try
            {
                cjk = FontFix.CjkUiFontAsset;
            }
            catch (System.Exception)
            {
                // Reported in the resolution section already.
            }
            if (cjk == null)
            {
                sb.Append("(no CJK UI FontAsset resolved)\n\n");
                return;
            }
            try
            {
                sb.Append("population mode: ")
                    .Append(cjk.atlasPopulationMode.ToString()).Append('\n');
                Texture2D[] pages = cjk.atlasTextures;
                int pageCount = pages != null ? pages.Length : 0;
                sb.Append("atlas pages    : ").Append(pageCount).Append('\n');
                if (pageCount > 0 && pages[0] != null)
                {
                    sb.Append("atlas size     : ")
                        .Append(pages[0].width).Append('x')
                        .Append(pages[0].height).Append('\n');
                }
                if (pageCount > 1)
                {
                    sb.Append("note: multiple atlas pages -- <mark> quads"
                        + " will interleave with glyphs (see traps)\n");
                }
            }
            catch (System.Exception e)
            {
                sb.Append("atlas probe threw ")
                    .Append(e.GetType().Name).Append('\n');
            }
            sb.Append('\n');
        }

        private static void AppendEnvironmentSection(StringBuilder sb)
        {
            sb.Append("-- Environment --\n");
            try
            {
                sb.Append("platform   : ")
                    .Append(Application.platform.ToString()).Append('\n');
                sb.Append("batch mode : ")
                    .Append(Application.isBatchMode ? "yes" : "no").Append('\n');
                SystemLanguage lang = Application.systemLanguage;
                sb.Append("language   : ").Append(lang.ToString())
                    .Append(" (prefer CJK UI: ")
                    .Append(FontFix.ShouldPreferCjkUi(lang) ? "yes" : "no")
                    .Append(")\n");
            }
            catch (System.Exception e)
            {
                sb.Append("environment probe threw ")
                    .Append(e.GetType().Name).Append('\n');
            }
            AppendCandidateAvailability(sb, "cjk-ui candidates",
                FontFixSettings.CjkUiFontNames);
            AppendCandidateAvailability(sb, "os mono candidates",
                FontFixSettings.OsMonoFontNames);
            sb.Append('\n');
        }

        private static void AppendCandidateAvailability(
            StringBuilder sb, string title, string[] names)
        {
            string[] installed;
            try
            {
                installed = Font.GetOSInstalledFontNames() ?? new string[0];
            }
            catch (System.Exception)
            {
                installed = new string[0];
            }
            sb.Append(title).Append(":\n");
            if (names == null || names.Length == 0)
            {
                sb.Append("  (none configured)\n");
                return;
            }
            for (int i = 0; i < names.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < installed.Length; j++)
                {
                    if (string.Equals(installed[j], names[i],
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                sb.Append("  ").Append(found ? "[x] " : "[ ] ")
                    .Append(names[i]).Append('\n');
            }
        }

        private static void AppendKnownTrapsSection(StringBuilder sb)
        {
            sb.Append("-- Known traps (details: package README) --\n");
            sb.Append("* <mark> rich text is incompatible with DynamicOS"
                + " FontAssets: quads draw above/below glyphs per atlas"
                + " page (patchy bold / hidden text). Atlas prefill"
                + " cannot fix it.\n");
            sb.Append("* Never pass a NAME ARRAY to"
                + " Font.CreateDynamicFontFromOSFont: the face cannot"
                + " load and text renders empty.\n");
            sb.Append("* FontEngine.LoadFontFace(Font) rejects every OS"
                + " dynamic Font; validate OS fonts via"
                + " FontAsset.CreateFontAsset instead.\n");
            sb.Append("* USS cannot select OS fonts by name; assign"
                + " style.unityFontDefinition from C#.\n");
            sb.Append("* Sanitize model/user text before display"
                + " (FontFix.SanitizeDisplayText): variation selectors,"
                + " zero-width characters and non-BMP characters have no"
                + " editor-font glyph. Keep fixed UI strings inside the"
                + " SafeGlyphs whitelist.\n");
            sb.Append("* Do not read Application.systemLanguage from"
                + " ScriptableObject constructors/field initializers.\n");
        }

        private static string EmptyAsNone(string value)
        {
            return string.IsNullOrEmpty(value) ? "(none)" : value;
        }

        private static string SafeUnityVersion()
        {
            try
            {
                return Application.unityVersion;
            }
            catch (System.Exception)
            {
                return "(unknown)";
            }
        }
    }
}
