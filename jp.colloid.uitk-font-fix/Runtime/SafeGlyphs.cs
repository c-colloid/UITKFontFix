namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Whitelist of non-ASCII codepoints that are safe to bake into FIXED
    /// UI strings rendered with the editor's default font. Emoji-plane
    /// characters (e.g. the paperclip U+1F4CE) and variation selectors
    /// (U+FE0F/U+FE0E) have no glyph in the editor's Inter SDF font: they
    /// render as placeholder squares and spam "not found in
    /// [Inter-Regular SDF]" console warnings on every draw (verified in a
    /// live 2022.3 session). Fixed UI text should therefore stick to
    /// printable ASCII plus this proven-safe BMP set; free-form text from
    /// models/users is handled by TextSanitizer instead.
    ///
    /// All glyph strings should be built from codepoints at runtime
    /// (char.ConvertFromUtf32) so source files stay strictly ASCII.
    /// </summary>
    public static class SafeGlyphs
    {
        /// <summary>
        /// Non-ASCII codepoints confirmed rendering in the 2022.3 editor
        /// Inter SDF font (a live user session showed warnings ONLY for
        /// U+1F4CE and U+FE0F while all of these drew fine). Everything is
        /// BMP and text-presentation-default. U+00A0 supports no-wrap
        /// assembly of inline runs. Treat as read-only; pass project
        /// additions through the extraCodepoints parameter of
        /// <see cref="IsSafeCodepoint(int, int[])"/> after verifying
        /// editor font coverage.
        /// </summary>
        public static readonly int[] DefaultCodepoints =
        {
            0x00A0, // NO-BREAK SPACE (no-wrap assembly)
            0x00BB, // RIGHT-POINTING DOUBLE ANGLE QUOTATION MARK
            0x00D7, // MULTIPLICATION SIGN
            0x2022, // BULLET
            0x2193, // DOWNWARDS ARROW
            0x2197, // NORTH EAST ARROW
            0x2261, // IDENTICAL TO
            0x25B8, // BLACK RIGHT-POINTING SMALL TRIANGLE
            0x25BE, // BLACK DOWN-POINTING SMALL TRIANGLE
            0x25CE, // BULLSEYE
            0x25E6, // WHITE BULLET
            0x258D, // LEFT SEVEN EIGHTHS BLOCK (caret/spinner)
            0x2699, // GEAR (text presentation)
            0x26A0, // WARNING SIGN (text presentation)
            0x2713, // CHECK MARK
            0x2715, // MULTIPLICATION X
            0x2731  // HEAVY ASTERISK
        };

        /// <summary>
        /// True when a codepoint may appear in a fixed UI glyph string:
        /// printable ASCII or a member of <see cref="DefaultCodepoints"/>.
        /// </summary>
        public static bool IsSafeCodepoint(int codepoint)
        {
            return IsSafeCodepoint(codepoint, null);
        }

        /// <summary>
        /// True when a codepoint is printable ASCII, a member of
        /// <see cref="DefaultCodepoints"/>, or listed in
        /// <paramref name="extraCodepoints"/> (a project-specific
        /// whitelist extension -- add codepoints there ONLY after
        /// verifying they render in the target editor font).
        /// </summary>
        public static bool IsSafeCodepoint(int codepoint, int[] extraCodepoints)
        {
            if (codepoint >= 0x20 && codepoint < 0x7F)
            {
                return true; // Printable ASCII.
            }
            for (int i = 0; i < DefaultCodepoints.Length; i++)
            {
                if (DefaultCodepoints[i] == codepoint)
                {
                    return true;
                }
            }
            if (extraCodepoints != null)
            {
                for (int i = 0; i < extraCodepoints.Length; i++)
                {
                    if (extraCodepoints[i] == codepoint)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
