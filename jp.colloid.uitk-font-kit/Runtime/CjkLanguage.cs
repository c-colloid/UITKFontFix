using UnityEngine;

namespace Colloid.UitkFontKit
{
    /// <summary>
    /// Pure language policy helpers. Deliberately takes the language as a
    /// PARAMETER instead of reading Application.systemLanguage itself:
    /// calling Application.systemLanguage from a ScriptableObject
    /// constructor or field initializer throws (UnityException: get_
    /// systemLanguage is not allowed to be called during serialization),
    /// which in a real incident broke a settings singleton load and took
    /// the whole UI down with it. Callers should query the system language
    /// once, from OnEnable or later, and pass it in.
    /// </summary>
    public static class CjkLanguage
    {
        /// <summary>
        /// True when UI text in this language needs an explicit Latin+CJK
        /// font assignment. The editor's default UI font (Inter) has no
        /// CJK coverage, so CJK glyphs otherwise fall back to per-glyph OS
        /// font substitution -- and when several candidate fonts/weights
        /// can supply a glyph, the dynamic fallback atlas mixes them,
        /// producing patchy pseudo-bold runs next to normal-weight Latin
        /// text. Assigning ONE font that covers both scripts removes the
        /// fallback path entirely.
        /// </summary>
        public static bool ShouldPreferCjkUi(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Japanese:
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                case SystemLanguage.Korean:
                    return true;
                default:
                    return false;
            }
        }
    }
}
