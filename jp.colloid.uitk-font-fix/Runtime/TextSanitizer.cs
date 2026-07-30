namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Display-text hygiene helpers for UI Toolkit labels. All methods are
    /// pure, never throw and never allocate on the clean fast path.
    /// </summary>
    public static class TextSanitizer
    {
        /// <summary>
        /// Removes emoji/text variation selectors (U+FE0F / U+FE0E) from
        /// display text. Model- and user-authored text routinely carries
        /// U+FE0F after symbols ("warning sign" + selector); editor fonts
        /// have no glyph for the selector itself, so every draw logged
        /// "U+FE0F not found in [Inter-Regular SDF]" and rendered a
        /// placeholder square (verified on 2022.3). Stripping is
        /// display-only and never changes visible content. Returns the
        /// SAME instance when nothing needs removing (fast path), and
        /// string.Empty for null input.
        /// </summary>
        public static string StripVariationSelectors(string text)
        {
            const char emojiSelector = (char)0xFE0F; // VARIATION SELECTOR-16
            const char textSelector = (char)0xFE0E;  // VARIATION SELECTOR-15
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }
            int first = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == emojiSelector || c == textSelector)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
            {
                return text;
            }
            var sb = new System.Text.StringBuilder(text.Length - 1);
            sb.Append(text, 0, first);
            for (int i = first + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c != emojiSelector && c != textSelector)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
