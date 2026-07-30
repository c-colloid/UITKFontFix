using System;

namespace Colloid.UitkFontKit
{
    /// <summary>
    /// Code-first configuration for the FontKit resolvers. Assign new
    /// candidate arrays (most preferred first) BEFORE the first
    /// resolution, or at any time -- a value that actually changes
    /// invalidates the FontKit caches so the next access re-probes;
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
    public static class FontKitSettings
    {
        private static string[] _editorMonoFontPaths = FontKitDefaults.EditorMonoFontPaths;
        private static string[] _osMonoFontNames = FontKitDefaults.OsMonoFontNames;
        private static string[] _cjkUiFontNames = FontKitDefaults.CjkUiFontNames;
        private static string _cjkUiStyleName = FontKitDefaults.CjkUiStyleName;

        /// <summary>
        /// EditorGUIUtility.Load paths tried for the bundled mono TTF.
        /// Default: FontKitDefaults.EditorMonoFontPaths (RobotoMono).
        /// An empty array disables the tier.
        /// </summary>
        public static string[] EditorMonoFontPaths
        {
            get { return _editorMonoFontPaths; }
            set { SetList(ref _editorMonoFontPaths, value, FontKitDefaults.EditorMonoFontPaths); }
        }

        /// <summary>
        /// OS monospace family names probed one at a time. Default:
        /// FontKitDefaults.OsMonoFontNames. An empty array disables the
        /// tier (the resolver then falls straight to the label font).
        /// </summary>
        public static string[] OsMonoFontNames
        {
            get { return _osMonoFontNames; }
            set { SetList(ref _osMonoFontNames, value, FontKitDefaults.OsMonoFontNames); }
        }

        /// <summary>
        /// Latin+CJK UI family names probed one at a time, most preferred
        /// first. Default: FontKitDefaults.CjkUiFontNames (Japanese
        /// priority chain). Replace for zh/ko-first products. An empty
        /// array disables CJK resolution entirely (CjkUiFontAsset stays
        /// null and ApplyCjkUi no-ops).
        /// </summary>
        public static string[] CjkUiFontNames
        {
            get { return _cjkUiFontNames; }
            set { SetList(ref _cjkUiFontNames, value, FontKitDefaults.CjkUiFontNames); }
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
                    ? FontKitDefaults.CjkUiStyleName
                    : value;
                if (string.Equals(_cjkUiStyleName, next, StringComparison.Ordinal))
                {
                    return;
                }
                _cjkUiStyleName = next;
                FontKit.InvalidateCaches();
            }
        }

        /// <summary>
        /// Restores every property to its default. Only invalidates the
        /// FontKit caches when something actually changes.
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
            FontKit.InvalidateCaches();
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
