namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Pure display-text hygiene for UI Toolkit labels. Editor fonts
    /// have no glyphs for variation selectors, zero-width characters or
    /// supplementary-plane (non-BMP) characters: each occurrence renders
    /// a placeholder square and logs a "not found in [...]" console
    /// warning on every draw (verified on 2022.3). These helpers remove
    /// or replace exactly those problem classes.
    ///
    /// All methods are pure, never throw, map null to string.Empty and
    /// return the SAME instance when nothing needs changing (no
    /// allocation on the clean fast path -- including text that contains
    /// perfectly valid supplementary-plane characters).
    ///
    /// Strip guarantees: a strip never removes a character that draws
    /// its own glyph, never removes an unpaired surrogate and never
    /// removes half of a valid surrogate pair. Note that removing
    /// zero-width joiners/non-joiners can change joining, ligation and
    /// line-break behavior in shaping-capable renderers; UI Toolkit
    /// 2022.3 TextCore performs no such shaping, so this is irrelevant
    /// for editor UIs but stated for completeness.
    /// <seealso cref="StripVariationSelectors"/>
    /// <seealso cref="StripInvisibleCharacters"/>
    /// <seealso cref="ReplaceNonBmpCharacters"/>
    /// </summary>
    public static class TextSanitizer
    {
        // -- Codepoint sets ----------------------------------------------------

        /// <summary>
        /// True for every Unicode variation selector: U+FE00..U+FE0F,
        /// the Mongolian free variation selectors U+180B..U+180D and
        /// U+180F, and the ideographic variation selectors
        /// U+E0100..U+E01EF. U+180E (MONGOLIAN VOWEL SEPARATOR) is
        /// deliberately NOT included: it sits among the FVS codepoints
        /// but is not a variation selector.
        /// </summary>
        private static bool IsVariationSelector(int codepoint)
        {
            return (codepoint >= 0xFE00 && codepoint <= 0xFE0F)
                || (codepoint >= 0x180B && codepoint <= 0x180D)
                || codepoint == 0x180F
                || (codepoint >= 0xE0100 && codepoint <= 0xE01EF);
        }

        /// <summary>
        /// True for every codepoint <see cref="StripInvisibleCharacters"/>
        /// removes: the variation selectors plus zero-width
        /// space/non-joiner/joiner U+200B..U+200D, word joiner and the
        /// invisible math operators U+2060..U+2064, BOM/ZWNBSP U+FEFF,
        /// and the emoji tag characters U+E0000..U+E007F (used by tag
        /// sequences such as subdivision flags; each tag character
        /// would otherwise draw its own placeholder square).
        /// Deliberate exclusions: U+00AD SOFT HYPHEN (conditionally
        /// visible and covered by editor fonts), the bidi controls
        /// (removing them can visibly reorder mixed-direction text) and
        /// U+034F COMBINING GRAPHEME JOINER (rare; not a verified
        /// warning source).
        /// </summary>
        private static bool IsInvisibleCharacter(int codepoint)
        {
            return IsVariationSelector(codepoint)
                || (codepoint >= 0x200B && codepoint <= 0x200D)
                || (codepoint >= 0x2060 && codepoint <= 0x2064)
                || codepoint == 0xFEFF
                || (codepoint >= 0xE0000 && codepoint <= 0xE007F);
        }

        // Cached delegates: no per-call allocation.
        private static readonly System.Func<int, bool> VariationSelectorPredicate =
            IsVariationSelector;
        private static readonly System.Func<int, bool> InvisibleCharacterPredicate =
            IsInvisibleCharacter;

        // -- Public API --------------------------------------------------------

        /// <summary>
        /// Removes every Unicode variation selector -- U+FE00..U+FE0F,
        /// Mongolian FVS U+180B..U+180D and U+180F, and ideographic
        /// variation selectors U+E0100..U+E01EF (matched only as valid
        /// surrogate pairs). Model/user text routinely carries U+FE0F
        /// after symbols, and real Japanese text carries ideographic
        /// selectors after kanji; editor fonts have a glyph for neither.
        /// This is the shaping-safe granular op (it never touches
        /// ZWJ/ZWNJ), safe on any script.
        /// </summary>
        public static string StripVariationSelectors(string text)
        {
            return StripWhere(text, VariationSelectorPredicate);
        }

        /// <summary>
        /// Superset of <see cref="StripVariationSelectors"/> that also
        /// removes zero-width space/non-joiner/joiner (U+200B..U+200D),
        /// word joiner and the invisible math operators
        /// (U+2060..U+2064), BOM/ZWNBSP (U+FEFF) and emoji tag
        /// characters (U+E0000..U+E007F). This is what
        /// <c>FontFix.SanitizeDisplayText</c> forwards to: the
        /// recommended default cleanup for free-form text headed for an
        /// editor UI label. Never removes a character that draws its
        /// own glyph.
        /// </summary>
        public static string StripInvisibleCharacters(string text)
        {
            return StripWhere(text, InvisibleCharacterPredicate);
        }

        /// <summary>
        /// LOSSY: replaces every supplementary-plane codepoint (one
        /// replacement per surrogate pair) and every unpaired surrogate
        /// code unit with <paramref name="replacement"/> (null is
        /// treated as empty). Supplementary characters have no glyph in
        /// the editor fonts and draw placeholder squares with per-draw
        /// console warnings -- but they include legitimate content
        /// (emoji, and rare-but-real supplementary kanji such as
        /// U+20B9F), so this is warning-spam triage, not routine text
        /// processing: opt in deliberately. Run
        /// <see cref="StripInvisibleCharacters"/> first (or use the
        /// <c>FontFix.SanitizeDisplayText(text, replacement)</c>
        /// composite) so ideographic variation selectors are stripped
        /// rather than turned into visible replacements. When the
        /// replacement itself contains no surrogates, the result is
        /// guaranteed to contain no surrogate code units at all.
        /// </summary>
        public static string ReplaceNonBmpCharacters(string text, string replacement)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }
            int first = IndexOfSurrogate(text);
            if (first < 0)
            {
                return text; // Pure BMP: same instance.
            }
            string safeReplacement = replacement ?? string.Empty;
            var sb = new System.Text.StringBuilder(text.Length);
            sb.Append(text, 0, first);
            int i = first;
            while (i < text.Length)
            {
                char c = text[i];
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    if (char.IsHighSurrogate(c) && i + 1 < text.Length
                        && char.IsLowSurrogate(text[i + 1]))
                    {
                        i += 2; // Valid pair: ONE replacement per codepoint.
                    }
                    else
                    {
                        i += 1; // Unpaired surrogate: also unrenderable.
                    }
                    sb.Append(safeReplacement);
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // -- Internals ---------------------------------------------------------

        /// <summary>
        /// Single pair-aware scanner shared by both strip ops so their
        /// codepoint sets can never drift apart. A lone surrogate maps
        /// to its code-unit value (0xD800..0xDFFF), which no predicate
        /// set contains, so strips never eat malformed halves -- that
        /// cleanup belongs to <see cref="ReplaceNonBmpCharacters"/>.
        /// Pair matching matters: the low units of supplementary kanji
        /// overlap the IVS low-surrogate range (U+20500 is 0xD841
        /// 0xDD00), so unit-based matching would corrupt valid text.
        /// </summary>
        private static string StripWhere(string text, System.Func<int, bool> shouldStrip)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }
            int first = IndexOfMatch(text, shouldStrip);
            if (first < 0)
            {
                return text; // Clean: same instance, zero allocation.
            }
            var sb = new System.Text.StringBuilder(text.Length - 1);
            sb.Append(text, 0, first);
            int i = first;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length
                    && char.IsLowSurrogate(text[i + 1]))
                {
                    if (!shouldStrip(char.ConvertToUtf32(c, text[i + 1])))
                    {
                        sb.Append(c);
                        sb.Append(text[i + 1]);
                    }
                    i += 2;
                    continue;
                }
                if (!shouldStrip(c))
                {
                    sb.Append(c);
                }
                i++;
            }
            return sb.ToString();
        }

        private static int IndexOfMatch(string text, System.Func<int, bool> shouldStrip)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length
                    && char.IsLowSurrogate(text[i + 1]))
                {
                    if (shouldStrip(char.ConvertToUtf32(c, text[i + 1])))
                    {
                        return i;
                    }
                    i += 2;
                    continue;
                }
                if (shouldStrip(c))
                {
                    return i;
                }
                i++;
            }
            return -1;
        }

        private static int IndexOfSurrogate(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 0xD800 && c <= 0xDFFF)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
