namespace Colloid.UitkFontKit
{
    /// <summary>
    /// Default candidate lists used by the editor-side resolvers. Kept in
    /// the runtime assembly so a future runtime resolver (v2 scope) can
    /// share the same data. Treat the arrays as read-only: to customize
    /// candidates assign new arrays through <c>FontKitSettings</c> instead
    /// of mutating these in place.
    /// </summary>
    public static class FontKitDefaults
    {
        /// <summary>
        /// Editor-bundle candidate paths for the bundled monospace TTF,
        /// resolved through <c>EditorGUIUtility.Load</c>. The first path
        /// resolves on 2022.3 (verified empirically on 2022.3.22f1); the
        /// bundled TTF is preferred over any OS font because it is a real
        /// font asset that TextCore always accepts, whereas OS dynamic
        /// fonts can produce faces UI Toolkit cannot load.
        /// </summary>
        public static readonly string[] EditorMonoFontPaths =
        {
            "Fonts/RobotoMono/RobotoMono-Regular.ttf",
            "fonts/robotomono/robotomono-regular.ttf"
        };

        /// <summary>
        /// OS monospace font names probed one at a time -- never as a name
        /// array, because Font.CreateDynamicFontFromOSFont with a name
        /// array produced an unloadable face ("Unable to load font face")
        /// that rendered text EMPTY (verified on 2022.3).
        /// </summary>
        public static readonly string[] OsMonoFontNames =
        {
            "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New"
        };

        /// <summary>
        /// OS Latin+CJK UI font names probed one at a time, most preferred
        /// first. Yu Gothic UI/Yu Gothic ship with Windows 10/11 (Meiryo
        /// predates them on older installs); Noto Sans CJK JP covers
        /// Linux/manually provisioned machines; MS UI Gothic is the
        /// last-ditch legacy Windows fallback. Note that
        /// Font.GetOSInstalledFontNames() reports ENGLISH family names
        /// even on Japanese Windows (verified empirically), which is why
        /// the defaults are English names.
        /// </summary>
        public static readonly string[] CjkUiFontNames =
        {
            "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo",
            "Noto Sans CJK JP", "MS UI Gothic"
        };

        /// <summary>Default style name passed to FontAsset.CreateFontAsset.</summary>
        public const string CjkUiStyleName = "Regular";
    }
}
