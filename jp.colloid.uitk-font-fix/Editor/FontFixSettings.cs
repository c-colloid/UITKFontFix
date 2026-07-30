using System;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Code-first configuration for the FontFix resolvers. Assign new
    /// candidate arrays (most preferred first) BEFORE the first
    /// resolution, or at any time -- a value that actually changes
    /// invalidates the FontFix caches so the next access re-probes;
    /// re-assigning an equal value keeps the caches warm (safe to call
    /// ResetToDefaults from test teardown).
    ///
    /// Deliberately a static class rather than a ScriptableObject: a
    /// settings asset invites reading Application.systemLanguage from
    /// constructors/field initializers, which throws during
    /// serialization and in a real incident broke a singleton load and
    /// the whole UI with it. Code configuration keeps the package
    /// zero-dependency and the failure surface minimal.
    ///
    /// Assigning null to any property restores that property's default.
    /// </summary>
    public static class FontFixSettings
    {
        private static string[] _editorMonoFontPaths = FontFixDefaults.EditorMonoFontPaths;
        private static string[] _osMonoFontNames = FontFixDefaults.OsMonoFontNames;
        private static string[] _cjkUiFontNames = FontFixDefaults.CjkUiFontNames;
        private static string _cjkUiStyleName = FontFixDefaults.CjkUiStyleName;

        /// <summary>
        /// EditorGUIUtility.Load paths tried for the bundled mono TTF.
        /// Default: FontFixDefaults.EditorMonoFontPaths (RobotoMono).
        /// An empty array disables the tier.
        /// </summary>
        public static string[] EditorMonoFontPaths
        {
            get { return _editorMonoFontPaths; }
            set { SetList(ref _editorMonoFontPaths, value, FontFixDefaults.EditorMonoFontPaths); }
        }

        /// <summary>
        /// OS monospace family names probed one at a time. Default:
        /// FontFixDefaults.OsMonoFontNames. An empty array disables the
        /// tier (the resolver then falls straight to the label font).
        /// </summary>
        public static string[] OsMonoFontNames
        {
            get { return _osMonoFontNames; }
            set { SetList(ref _osMonoFontNames, value, FontFixDefaults.OsMonoFontNames); }
        }

        /// <summary>
        /// Latin+CJK UI family names probed one at a time, most preferred
        /// first. Default: FontFixDefaults.CjkUiFontNames (Japanese
        /// priority chain). Replace for zh/ko-first products. An empty
        /// array disables CJK resolution entirely (CjkUiFontAsset stays
        /// null and ApplyCjkUi no-ops).
        /// </summary>
        public static string[] CjkUiFontNames
        {
            get { return _cjkUiFontNames; }
            set { SetList(ref _cjkUiFontNames, value, FontFixDefaults.CjkUiFontNames); }
        }

        /// <summary>
        /// Style name passed to FontAsset.CreateFontAsset for the CJK UI
        /// font. Default: "Regular". Null/empty restores the default.
        /// </summary>
        public static string CjkUiStyleName
        {
            get { return _cjkUiStyleName; }
            set
            {
                string next = string.IsNullOrEmpty(value)
                    ? FontFixDefaults.CjkUiStyleName
                    : value;
                if (string.Equals(_cjkUiStyleName, next, StringComparison.Ordinal))
                {
                    return;
                }
                _cjkUiStyleName = next;
                FontFix.InvalidateCaches();
            }
        }

        /// <summary>
        /// Restores every property to its default. Only invalidates the
        /// FontFix caches when something actually changes.
        /// </summary>
        public static void ResetToDefaults()
        {
            EditorMonoFontPaths = null;
            OsMonoFontNames = null;
            CjkUiFontNames = null;
            CjkUiStyleName = null;
        }

        private static void SetList(ref string[] field, string[] value, string[] fallback)
        {
            string[] next = value ?? fallback;
            if (SameSequence(field, next))
            {
                field = next;
                return;
            }
            field = next;
            FontFix.InvalidateCaches();
        }

        private static bool SameSequence(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
