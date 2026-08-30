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
        /// <summary>
        /// Suffix tag stamped on the name of every transient Object this
        /// package creates (FontAssets, their materials and first atlas
        /// pages, owned OS Fonts), so kit-created objects are
        /// identifiable in the UITK Debugger and the Memory Profiler
        /// (search for the tag). Never applied to shared editor assets
        /// such as the bundled mono TTF or the default label font.
        /// </summary>
        public const string CreatedObjectNameTag = "[UITK Font Fix]";

        private const int ProbePointSize = 12;

        private static Font _monoFont;
        private static bool _monoProbed;
        private static bool _monoFontOwned;
        private static string _monoFontSource = string.Empty;

        private static UnityEngine.TextCore.Text.FontAsset _cjkUiAsset;
        private static bool _cjkUiProbed;
        private static string _cjkUiSource = string.Empty;

        // Family name that won CJK resolution (never parsed back out of
        // the source string), and the per-style asset cache keyed by
        // face style name. A PRESENT null value is a cached miss, so
        // repeated lookups of an unavailable face neither re-run
        // CreateFontAsset nor repeat its one-line console log.
        private static string _cjkUiFamilyName;
        private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.TextCore.Text.FontAsset>
            _cjkStyleAssets = new System.Collections.Generic.Dictionary<string, UnityEngine.TextCore.Text.FontAsset>(
                System.StringComparer.OrdinalIgnoreCase);

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
                    // Wiring runs AFTER the probed flag and field are set:
                    // it calls GetCjkUiFontAsset, which reads this getter
                    // again and must see the resolved base.
                    WireBoldFace();
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

        /// <summary>
        /// The resolved CJK family in the given face style, e.g.
        /// GetCjkUiFontAsset("Semibold") for headers. Null/empty or the
        /// configured base style name (FontFixSettings.CjkUiStyleName)
        /// returns the SAME instance as CjkUiFontAsset. Other styles are
        /// created once per style from the SAME family that won base
        /// resolution and cached (misses included), kit-owned and
        /// destroyed on domain reload / ResetCaches. Returns null when
        /// the base did not resolve or the family has no face with that
        /// EXACT style name (face names are exact: some families call
        /// their regular face "Book"; a miss also logs one console line
        /// from TextCore). Never throws.
        /// </summary>
        public static UnityEngine.TextCore.Text.FontAsset GetCjkUiFontAsset(string styleName)
        {
            UnityEngine.TextCore.Text.FontAsset baseAsset = CjkUiFontAsset;
            if (string.IsNullOrEmpty(styleName)
                || string.Equals(styleName, FontFixSettings.CjkUiStyleName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return baseAsset;
            }
            if (baseAsset == null || string.IsNullOrEmpty(_cjkUiFamilyName))
            {
                // Base miss is already cached by the probed flag; do not
                // insert per-style negative entries for it.
                return null;
            }
            UnityEngine.TextCore.Text.FontAsset cached;
            if (_cjkStyleAssets.TryGetValue(styleName, out cached))
            {
                return cached;
            }
            UnityEngine.TextCore.Text.FontAsset created =
                CreateOwnedCjkAsset(_cjkUiFamilyName, styleName);
            _cjkStyleAssets[styleName] = created;
            return created;
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

        /// <summary>
        /// Applies a specific face of the resolved CJK family to one
        /// LEAF element via an inline style.unityFontDefinition write --
        /// e.g. ApplyCjkUiFace(header, "Semibold"). Distinct from
        /// ApplyCjkUi on purpose: ApplyCjkUi belongs on container roots,
        /// face selection belongs on leaves (like ApplyMono). No-ops,
        /// keeping the inherited font, when the element is null or the
        /// face did not resolve -- inside a composed CJK root that means
        /// visible degradation to the inherited base face.
        /// </summary>
        public static void ApplyCjkUiFace(VisualElement leaf, string styleName)
        {
            if (leaf == null)
            {
                return;
            }
            UnityEngine.TextCore.Text.FontAsset asset = GetCjkUiFontAsset(styleName);
            if (asset != null)
            {
                leaf.style.unityFontDefinition =
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

        // -- Diagnostics accessors (internal) ----------------------------------

        /// <summary>Family bound by the last successful CJK resolution, or null.</summary>
        internal static string BoundCjkFamilyName
        {
            get { return _cjkUiFamilyName; }
        }

        /// <summary>Snapshot of the per-style cache (misses included as null values).</summary>
        internal static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset>>
            GetCjkStyleCacheSnapshot()
        {
            return new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset>>(
                _cjkStyleAssets);
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
                    // Kit-owned object: tag it. Dynamic-font glyph
                    // resolution goes through fontNames, never
                    // Object.name, so renaming cannot break the face.
                    osFont.name = name + " " + CreatedObjectNameTag;
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
                    CreateOwnedCjkAsset(name, FontFixSettings.CjkUiStyleName);
                if (asset != null)
                {
                    _cjkUiFamilyName = name;
                    source = "osasset:" + name;
                    return asset;
                }
            }
            source = string.Empty;
            return null;
        }

        /// <summary>
        /// Creates a kit-OWNED DynamicOS FontAsset for one (family,
        /// style) pair: transient (never saved, destroyed before each
        /// domain reload and on cache reset), tagged with
        /// <see cref="CreatedObjectNameTag"/> on the asset, its material
        /// and its first atlas page. Later atlas pages are added lazily
        /// by TextCore and keep engine default names -- the diagnostics
        /// report lists every page name so they stay attributable.
        /// Returns null when the family has no face with that exact
        /// style name.
        /// </summary>
        private static UnityEngine.TextCore.Text.FontAsset CreateOwnedCjkAsset(
            string familyName, string styleName)
        {
            UnityEngine.TextCore.Text.FontAsset asset = null;
            try
            {
                asset = FontShims.TryCreateOsFontAsset(familyName, styleName);
                if (asset == null)
                {
                    return null;
                }
                asset.hideFlags = HideFlags.HideAndDontSave;
                NameCreatedAsset(asset);
                HookCleanup();
                return asset;
            }
            catch (System.Exception)
            {
                // Never-throw contract: a partially initialized asset
                // must not escape both the caches and destruction.
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
                return null;
            }
        }

        private static void NameCreatedAsset(UnityEngine.TextCore.Text.FontAsset asset)
        {
            try
            {
                // Distinguishing info first (narrow panels truncate from
                // the right), provenance tag last; the actual face names
                // from faceInfo, so a loose OS match stays visible.
                asset.name = asset.faceInfo.familyName + " - "
                    + asset.faceInfo.styleName + " " + CreatedObjectNameTag;
                if (asset.material != null)
                {
                    asset.material.name = asset.name + " Material";
                }
                if (asset.atlasTextures != null && asset.atlasTextures.Length > 0
                    && asset.atlasTextures[0] != null)
                {
                    asset.atlasTextures[0].name = asset.name + " Atlas";
                }
            }
            catch (System.Exception)
            {
                // Naming is diagnostics-only; never let it fail resolution.
            }
        }

        /// <summary>
        /// Wires the real Bold face into the base asset's
        /// fontWeightTable[7] so '-unity-font-style: bold' renders it
        /// instead of faux (SDF-dilated) bold. Runs once per base
        /// resolution; the wired instance IS the per-style cache entry
        /// (one Bold atlas total). Skipped when disabled
        /// (FontFixSettings.CjkUiBoldStyleName is empty), when the bold
        /// style equals the base style (self-wiring guard), or when the
        /// family has no such face -- rendering then keeps the faux-bold
        /// behavior. Note bold-and-italic keeps consulting
        /// italicTypeface, which stays empty (default CJK families ship
        /// no italic faces), so it remains faux.
        /// </summary>
        private static void WireBoldFace()
        {
            if (_cjkUiAsset == null)
            {
                return;
            }
            string boldStyle = FontFixSettings.CjkUiBoldStyleName;
            if (string.IsNullOrEmpty(boldStyle)
                || string.Equals(boldStyle, FontFixSettings.CjkUiStyleName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                UnityEngine.TextCore.Text.FontAsset boldAsset =
                    GetCjkUiFontAsset(boldStyle);
                if (boldAsset == null)
                {
                    return;
                }
                // The getter returns the live array (verified on 2022.3),
                // so element assignment sticks; a regression test reads
                // the slot back in case a future version returns a copy.
                UnityEngine.TextCore.Text.FontWeightPair[] table =
                    _cjkUiAsset.fontWeightTable;
                if (table != null && table.Length > 7)
                {
                    table[7].regularTypeface = boldAsset;
                }
            }
            catch (System.Exception)
            {
                // Wiring is an enhancement; never let it fail resolution.
            }
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
            // Deterministic order: the BASE asset (whose weight table may
            // reference a style asset) dies first, then the style assets.
            // A destroyed slot reference fake-nulls and TextCore's null
            // check falls back to faux rendering, so even the reverse
            // order is benign -- the fixed order just makes that argument
            // unnecessary.
            if (_cjkUiAsset != null)
            {
                Object.DestroyImmediate(_cjkUiAsset);
            }
            _cjkUiAsset = null;
            foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset> entry
                in _cjkStyleAssets)
            {
                if (entry.Value != null)
                {
                    Object.DestroyImmediate(entry.Value);
                }
            }
            _cjkStyleAssets.Clear();
            _cjkUiFamilyName = null;
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
