using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Editor-side facade of UITK Font Fix: resolves a safe monospace
    /// font and a Latin+CJK UI FontAsset, and applies them to UI Toolkit
    /// elements. Every resolver is cached, never throws, and reports a
    /// diagnostic source string. Built from behavior verified empirically
    /// on Unity 2022.3 (see the package README for the full list):
    ///
    ///  - OS dynamic Fonts (Font.CreateDynamicFontFromOSFont) can carry
    ///    faces UI Toolkit cannot load ("Unable to load font face" +
    ///    empty text), and FontEngine.LoadFontFace(Font) rejects ALL of
    ///    them with Invalid_File, so OS fonts are only ever handed to
    ///    UI Toolkit as DynamicOS FontAssets
    ///    (FontAsset.CreateFontAsset(family, style)).
    ///  - USS cannot select OS fonts by name; assignment happens from C#
    ///    via style.unityFontDefinition. Because an INLINE style always
    ///    beats an inherited value, the intended composition is:
    ///    ApplyCjkUi on a container root (descendants inherit it) plus
    ///    ApplyMono on code leaves (inline, wins locally).
    ///  - The editor default font (Inter) has no CJK coverage; per-glyph
    ///    OS fallback mixes fonts/weights and renders CJK text patchy
    ///    bold. One explicit Latin+CJK font on the root removes that.
    /// </summary>
    public static class FontFix
    {
        private const int ProbePointSize = 12;

        private static Font _monoFont;
        private static bool _monoProbed;
        private static bool _monoFontOwned;
        private static string _monoFontSource = string.Empty;

        private static UnityEngine.TextCore.Text.FontAsset _cjkUiAsset;
        private static bool _cjkUiProbed;
        private static string _cjkUiSource = string.Empty;

        private static bool _cleanupHooked;

        // -- Resolution --------------------------------------------------------

        /// <summary>
        /// The resolved monospace font: editor-bundled RobotoMono first
        /// (a real TTF asset TextCore always accepts), face-probed
        /// single-name OS fonts second, the default editor label font
        /// last. Never null in a functioning editor (batch mode
        /// included); may be null only when every candidate including the
        /// built-in fallbacks is unavailable, in which case ApplyMono
        /// leaves the inherited font in place.
        /// </summary>
        public static Font EditorMonoFont
        {
            get
            {
                if (!_monoProbed)
                {
                    _monoProbed = true;
                    _monoFont = ResolveMono(out _monoFontSource, out _monoFontOwned);
                    if (_monoFontOwned)
                    {
                        HookCleanup();
                    }
                }
                return _monoFont;
            }
        }

        /// <summary>
        /// Diagnostic: which monospace candidate won ("editor:&lt;path&gt;",
        /// "os:&lt;name&gt;", "label"), or empty when nothing resolved.
        /// </summary>
        public static string EditorMonoFontSource
        {
            get
            {
                Font unused = EditorMonoFont;
                return _monoFontSource;
            }
        }

        /// <summary>
        /// The resolved Latin+CJK UI font as a TextCore FontAsset in
        /// DynamicOS mode, or null when no candidate in
        /// FontFixSettings.CjkUiFontNames is installed/creatable (callers
        /// then keep the inherited/default font). Glyphs are fetched
        /// lazily at render time: HasCharacter returning false right
        /// after creation is normal. Cached after the first probe; the
        /// transient asset is destroyed before every domain reload so
        /// DynamicOS atlas textures cannot pile up across recompiles.
        /// </summary>
        public static UnityEngine.TextCore.Text.FontAsset CjkUiFontAsset
        {
            get
            {
                if (!_cjkUiProbed)
                {
                    _cjkUiProbed = true;
                    _cjkUiAsset = ResolveCjkUi(out _cjkUiSource);
                }
                return _cjkUiAsset;
            }
        }

        /// <summary>
        /// Diagnostic: which CJK UI candidate won ("osasset:&lt;name&gt;"),
        /// or empty when none resolved.
        /// </summary>
        public static string CjkUiFontSource
        {
            get
            {
                UnityEngine.TextCore.Text.FontAsset unused = CjkUiFontAsset;
                return _cjkUiSource;
            }
        }

        // -- Application -------------------------------------------------------

        /// <summary>
        /// Applies the monospace font to one LEAF element via an inline
        /// style.unityFontDefinition assignment (inline always beats an
        /// inherited value, so this survives an ApplyCjkUi on any
        /// ancestor). No-ops, keeping the inherited font, when nothing
        /// resolved. Do not call ApplyMono and ApplyCjkUi on the SAME
        /// element: both are inline writes and the last one wins.
        /// </summary>
        public static void ApplyMono(VisualElement leaf)
        {
            if (leaf == null)
            {
                return;
            }
            Font font = EditorMonoFont;
            if (font != null)
            {
                leaf.style.unityFontDefinition =
                    new StyleFontDefinition(FontDefinition.FromFont(font));
            }
        }

        /// <summary>
        /// Applies the Latin+CJK UI font to a CONTAINER ROOT via
        /// style.unityFontDefinition; descendants inherit it unless they
        /// carry their own inline definition (e.g. an ApplyMono leaf,
        /// which keeps winning regardless of call order). No-ops when
        /// nothing resolved -- callers must not assume the font changed.
        /// </summary>
        public static void ApplyCjkUi(VisualElement containerRoot)
        {
            if (containerRoot == null)
            {
                return;
            }
            UnityEngine.TextCore.Text.FontAsset asset = CjkUiFontAsset;
            if (asset != null)
            {
                containerRoot.style.unityFontDefinition =
                    new StyleFontDefinition(FontShims.DefinitionFromFontAsset(asset));
            }
        }

        // -- Pure helpers (forwarders to the runtime assembly) ----------------

        /// <summary>
        /// True when UI text in this language needs the explicit
        /// Latin+CJK assignment (ja/zh/ko). Pure; see CjkLanguage for the
        /// rationale and for the Application.systemLanguage timing trap.
        /// </summary>
        public static bool ShouldPreferCjkUi(SystemLanguage language)
        {
            return CjkLanguage.ShouldPreferCjkUi(language);
        }

        /// <summary>
        /// Lossless display-text hygiene for model/user text before it
        /// reaches a UI Toolkit label: strips every variation selector
        /// (including ideographic ones), zero-width characters and the
        /// BOM -- codepoints with no glyph in the editor fonts that
        /// otherwise draw placeholder squares and spam per-draw console
        /// warnings. Never removes a character that draws its own
        /// glyph; same-instance fast path when clean; null maps to
        /// string.Empty. Forwards to
        /// TextSanitizer.StripInvisibleCharacters.
        /// </summary>
        public static string SanitizeDisplayText(string text)
        {
            return TextSanitizer.StripInvisibleCharacters(text);
        }

        /// <summary>
        /// LOSSY overload for surfaces that must stay strictly BMP:
        /// strips invisible characters FIRST, then replaces every
        /// supplementary-plane codepoint (emoji and other characters
        /// the editor fonts cannot draw) and every unpaired surrogate
        /// with <paramref name="nonBmpReplacement"/>. The order is a
        /// behavioral guarantee: ideographic variation selectors are
        /// supplementary-plane, so replace-before-strip would emit a
        /// visible replacement after every selector-bearing kanji.
        /// Passing the replacement IS the lossy opt-in; string.Empty
        /// deletes non-BMP content, any other string substitutes it.
        /// </summary>
        public static string SanitizeDisplayText(string text, string nonBmpReplacement)
        {
            return TextSanitizer.ReplaceNonBmpCharacters(
                TextSanitizer.StripInvisibleCharacters(text), nonBmpReplacement);
        }

        // -- Cache control -----------------------------------------------------

        /// <summary>
        /// Drops every cached resolution (destroying kit-owned transient
        /// objects) so the next access re-probes. Called automatically
        /// when FontFixSettings values actually change; also useful from
        /// tests.
        /// </summary>
        public static void ResetCaches()
        {
            InvalidateCaches();
        }

        internal static void InvalidateCaches()
        {
            DestroyOwned();
            _monoProbed = false;
            _monoFontSource = string.Empty;
            _cjkUiProbed = false;
            _cjkUiSource = string.Empty;
        }

        // -- Resolution internals ---------------------------------------------

        private static Font ResolveMono(out string source, out bool owned)
        {
            owned = false;

            // 1. Editor-bundled mono TTF (real font asset, always accepted
            //    by TextCore; resolves on 2022.3).
            string[] paths = FontFixSettings.EditorMonoFontPaths;
            for (int i = 0; i < paths.Length; i++)
            {
                Font bundled = LoadEditorFont(paths[i]);
                if (bundled != null && FaceLoads(bundled))
                {
                    source = "editor:" + paths[i];
                    return bundled;
                }
            }

            // 2. Single-name OS fonts, gated by the installed-name list
            //    AND the TextCore face probe. On 2022.3 the probe rejects
            //    every OS dynamic font (see FaceLoads), making this a
            //    defense-in-depth tier for editor versions where either
            //    the bundle path moves or the probe starts passing;
            //    without the gate a broken face would render EMPTY text.
            string[] installed = InstalledOsFontNames();
            string[] names = FontFixSettings.OsMonoFontNames;
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (!ContainsIgnoreCase(installed, name))
                {
                    continue;
                }
                Font osFont = CreateOsFont(name);
                if (osFont == null)
                {
                    continue;
                }
                if (FaceLoads(osFont))
                {
                    osFont.hideFlags = HideFlags.HideAndDontSave;
                    owned = true;
                    source = "os:" + name;
                    return osFont;
                }
                Object.DestroyImmediate(osFont);
            }

            // 3. Default editor label font (never a broken face).
            Font label = DefaultLabelFont();
            source = label != null ? "label" : string.Empty;
            return label;
        }

        private static UnityEngine.TextCore.Text.FontAsset ResolveCjkUi(out string source)
        {
            string[] installed = InstalledOsFontNames();
            string[] names = FontFixSettings.CjkUiFontNames;
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (!ContainsIgnoreCase(installed, name))
                {
                    continue;
                }
                UnityEngine.TextCore.Text.FontAsset asset =
                    FontShims.TryCreateOsFontAsset(name, FontFixSettings.CjkUiStyleName);
                if (asset != null)
                {
                    // Transient object: never saved, destroyed before each
                    // domain reload (and on cache reset).
                    asset.hideFlags = HideFlags.HideAndDontSave;
                    HookCleanup();
                    source = "osasset:" + name;
                    return asset;
                }
            }
            source = string.Empty;
            return null;
        }

        // -- Cleanup -----------------------------------------------------------

        private static void HookCleanup()
        {
            if (_cleanupHooked)
            {
                return;
            }
            _cleanupHooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyOwned;
        }

        private static void DestroyOwned()
        {
            if (_cjkUiAsset != null)
            {
                Object.DestroyImmediate(_cjkUiAsset);
            }
            _cjkUiAsset = null;
            if (_monoFontOwned && _monoFont != null)
            {
                Object.DestroyImmediate(_monoFont);
            }
            _monoFont = null;
            _monoFontOwned = false;
        }

        // -- Probe internals ---------------------------------------------------

        private static Font LoadEditorFont(string path)
        {
            try
            {
                return EditorGUIUtility.Load(path) as Font;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static Font CreateOsFont(string name)
        {
            // Single name on purpose: a NAME ARRAY here produced a face
            // UI Toolkit could not load, rendering text empty (2022.3).
            try
            {
                return Font.CreateDynamicFontFromOSFont(name, ProbePointSize);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static string[] InstalledOsFontNames()
        {
            try
            {
                return Font.GetOSInstalledFontNames() ?? new string[0];
            }
            catch (System.Exception)
            {
                return new string[0];
            }
        }

        private static Font DefaultLabelFont()
        {
            try
            {
                if (EditorStyles.label != null && EditorStyles.label.font != null)
                {
                    return EditorStyles.label.font;
                }
            }
            catch (System.Exception)
            {
                // EditorStyles may be unavailable extremely early; fall
                // through to the built-in runtime font.
            }
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// TextCore face probe: the same load UI Toolkit performs when a
        /// Font-based FontDefinition renders, so a font failing here is
        /// the one that would log "Unable to load font face" and draw
        /// nothing. Note this is only valid as a gate for REAL font
        /// assets: on 2022.3 it returns Invalid_File for every OS dynamic
        /// Font (which is exactly why CJK resolution uses the FontAsset
        /// route instead). If the probe machinery itself is unavailable
        /// the candidate is accepted (cannot disprove usability).
        /// </summary>
        private static bool FaceLoads(Font font)
        {
            try
            {
                FontEngine.InitializeFontEngine(); // Idempotent.
                return FontEngine.LoadFontFace(font, ProbePointSize)
                    == FontEngineError.Success;
            }
            catch (System.Exception)
            {
                return true;
            }
        }

        private static bool ContainsIgnoreCase(string[] values, string name)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], name,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
