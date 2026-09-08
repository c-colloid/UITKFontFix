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

        // Editor-update guard: liveness is re-checked at most this often
        // (seconds); the page-count stamp runs every tick and is free.
        private const double LivenessCheckInterval = 0.25;
        private static double _nextLivenessCheck;

        // Guards against re-entering the verify/rebuild path from a
        // getter reached inside it (a CachesInvalidated handler that
        // applies fonts does exactly that).
        private static bool _verifying;

        // CachesInvalidated bookkeeping: batch scope depth, a pending
        // raise inside an open scope, and the drain state that keeps a
        // handler-triggered invalidation from recursing forever.
        private static int _batchDepth;
        private static bool _batchPending;
        private static bool _raising;
        private static bool _raiseAgain;

        /// <summary>
        /// Raised when a cached object this package HANDED OUT was
        /// destroyed or replaced by a different instance: ResetCaches, a
        /// FontFixSettings change (including the settings UIs) and the
        /// rare rebuild after unrepairable damage. UI Toolkit elements
        /// keep the FontAsset in their inline style, so a subscriber
        /// re-applies its fonts -- typically one ApplyFonts() method
        /// calling ApplyCjkUi/ApplyMono again.
        ///
        /// It does NOT fire when damage was repaired in place (the
        /// instance is unchanged, so subscribers have nothing to do) nor
        /// before a domain reload (subscriptions die with it anyway).
        /// Raised synchronously, after the caches are cleared, so a
        /// handler that immediately re-applies re-probes cleanly and no
        /// repaint can observe the destroyed objects. Each subscriber is
        /// invoked separately and a throwing one is logged, never
        /// allowed to starve the others.
        ///
        /// Subscriptions are lost on every domain reload: subscribe from
        /// CreateGUI/OnEnable (or an InitializeOnLoadMethod), and
        /// unsubscribe in OnDisable.
        /// </summary>
        public static event System.Action CachesInvalidated;

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
                EnsureMonoResolved();
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
        /// Never returns an asset whose atlas material or in-use atlas
        /// pages have been destroyed: such damage (a Play Mode
        /// transition is the usual cause) is repaired in place, keeping
        /// the instance -- and therefore every element already using it
        /// -- valid. Read this property (or call ApplyCjkUi) each time
        /// rather than caching the FontAsset in your own field: only
        /// this path runs that check.
        /// </summary>
        public static UnityEngine.TextCore.Text.FontAsset CjkUiFontAsset
        {
            get
            {
                EnsureCjkResolved();
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
        /// tests. Elements that already carry a destroyed instance in
        /// their inline style are NOT re-applied automatically: this
        /// raises <see cref="CachesInvalidated"/> synchronously so
        /// subscribers can do it.
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
            // The raise is the LAST step on purpose: a handler that
            // re-applies fonts calls back into the getters, which must
            // see "not probed" and resolve freshly. Raising next to the
            // destruction would make them see "probed, result null" and
            // silently no-op.
            if (_batchDepth > 0)
            {
                _batchPending = true;
                return;
            }
            RaiseCachesInvalidated();
        }

        /// <summary>
        /// Opens a scope in which repeated invalidations raise
        /// CachesInvalidated only once, at the end. Used by the
        /// multi-property operations (settings reset, the settings form,
        /// the project settings file), where each property assignment
        /// would otherwise make every subscriber re-probe against a
        /// half-applied configuration. Nestable; a scope in which
        /// nothing actually changed raises nothing.
        /// </summary>
        internal static void BeginSettingsBatch()
        {
            _batchDepth++;
        }

        /// <summary>Closes a BeginSettingsBatch scope. Must be called from a finally.</summary>
        internal static void EndSettingsBatch()
        {
            if (_batchDepth > 0)
            {
                _batchDepth--;
            }
            if (_batchDepth > 0 || !_batchPending)
            {
                return;
            }
            _batchPending = false;
            RaiseCachesInvalidated();
        }

        private static void RaiseCachesInvalidated()
        {
            if (_raising)
            {
                // A handler invalidated the caches again. Drain it after
                // the current pass rather than recursing.
                _raiseAgain = true;
                return;
            }
            _raising = true;
            try
            {
                // At most one extra pass: a handler that invalidates
                // unconditionally must terminate, not spin.
                for (int pass = 0; pass < 2; pass++)
                {
                    _raiseAgain = false;
                    System.Action handlers = CachesInvalidated;
                    if (handlers == null)
                    {
                        return;
                    }
                    System.Delegate[] list = handlers.GetInvocationList();
                    for (int i = 0; i < list.Length; i++)
                    {
                        try
                        {
                            ((System.Action)list[i])();
                        }
                        catch (System.Exception e)
                        {
                            // Deliberate departure from this package's
                            // silent-swallow style: a consumer's broken
                            // handler must stay attributable, and must
                            // not starve the other subscribers.
                            Debug.LogException(e);
                        }
                    }
                    if (!_raiseAgain)
                    {
                        return;
                    }
                }
            }
            finally
            {
                _raising = false;
                _raiseAgain = false;
            }
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

        private static void EnsureMonoResolved()
        {
            if (_monoProbed)
            {
                VerifyOwned();
            }
            if (_monoProbed)
            {
                return;
            }
            _monoProbed = true;
            _monoFont = ResolveMono(out _monoFontSource, out _monoFontOwned);
            if (_monoFontOwned)
            {
                HookEditorEvents();
            }
        }

        private static void EnsureCjkResolved()
        {
            if (_cjkUiProbed)
            {
                // May clear the probed flag when the damage could not be
                // repaired; the resolution below then runs immediately.
                VerifyOwned();
            }
            if (_cjkUiProbed)
            {
                return;
            }
            _cjkUiProbed = true;
            _cjkUiAsset = ResolveCjkUi(out _cjkUiSource);
            // Wiring runs AFTER the probed flag and field are set: it
            // calls GetCjkUiFontAsset, which reads the getter again and
            // must see the resolved base.
            WireBoldFace();
        }

        /// <summary>
        /// Repairs damaged owned objects, or drops the caches when the
        /// damage cannot be repaired in place (which raises
        /// CachesInvalidated so consumers can re-apply). Re-entrant
        /// calls -- a handler applying fonts lands back in a getter --
        /// return immediately.
        /// </summary>
        private static void VerifyOwned()
        {
            if (_verifying)
            {
                return;
            }
            _verifying = true;
            try
            {
                bool rebuild = false;

                // An owned OS Font has no repairable sub-state: a
                // destroyed one can only be re-probed. Shared editor
                // fonts (the bundled TTF, the label font) are persistent
                // assets and never reach this branch.
                if (_monoProbed && !ReferenceEquals(_monoFont, null)
                    && _monoFont == null)
                {
                    rebuild = true;
                }

                System.Collections.Generic.List<UnityEngine.TextCore.Text.FontAsset> repaired = null;
                if (!rebuild)
                {
                    foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset> entry
                        in _cjkStyleAssets)
                    {
                        if (!FontAssetLifecycle.NeedsAttention(entry.Value))
                        {
                            continue;
                        }
                        if (!FontAssetLifecycle.TryRepair(entry.Value))
                        {
                            rebuild = true;
                            break;
                        }
                        if (repaired == null)
                        {
                            repaired = new System.Collections.Generic.List<UnityEngine.TextCore.Text.FontAsset>();
                        }
                        repaired.Add(entry.Value);
                    }
                }
                if (!rebuild && FontAssetLifecycle.NeedsAttention(_cjkUiAsset))
                {
                    if (!FontAssetLifecycle.TryRepair(_cjkUiAsset))
                    {
                        rebuild = true;
                    }
                    else
                    {
                        if (repaired == null)
                        {
                            repaired = new System.Collections.Generic.List<UnityEngine.TextCore.Text.FontAsset>();
                        }
                        repaired.Add(_cjkUiAsset);
                    }
                }

                if (rebuild)
                {
                    InvalidateCaches();
                }
                else
                {
                    // Cheap, and the only pass that sees atlas pages
                    // TextCore added lazily since the last transition.
                    StampOwned();
                    if (repaired != null)
                    {
                        for (int i = 0; i < repaired.Count; i++)
                        {
                            RepaintElementsUsing(repaired[i]);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Never-throw contract: a failed verification must not
                // take the caller's resolution down with it.
            }
            finally
            {
                _verifying = false;
            }
        }

        /// <summary>
        /// Re-applies the protective flags to every owned asset,
        /// covering atlas pages TextCore added lazily since the last
        /// pass. Run before each Play Mode transition, which is what the
        /// unflagged objects would not survive.
        /// </summary>
        private static void StampOwned()
        {
            FontAssetLifecycle.Stamp(_cjkUiAsset);
            foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset> entry
                in _cjkStyleAssets)
            {
                FontAssetLifecycle.Stamp(entry.Value);
            }
        }

        /// <summary>
        /// Editor-update guard. Two jobs: (1) every tick, stamp atlas
        /// pages TextCore added since the last stamp -- they are born
        /// unflagged during rendering, and the next scene load would
        /// destroy them; a consumer that applied the font once never
        /// calls back to trigger the access-time stamp, and no editor
        /// event fires between the page add and the load. (2) At a low
        /// rate, verify liveness, so damage from a path without a
        /// dedicated hook (the scene events and Play Mode have theirs)
        /// is still repaired within a fraction of a second.
        /// </summary>
        internal static void GuardTick()
        {
            if (_verifying)
            {
                return;
            }
            try
            {
                FontAssetLifecycle.StampNewPages(_cjkUiAsset);
                foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset> entry
                    in _cjkStyleAssets)
                {
                    FontAssetLifecycle.StampNewPages(entry.Value);
                }

                double now = EditorApplication.timeSinceStartup;
                if (now < _nextLivenessCheck)
                {
                    return;
                }
                _nextLivenessCheck = now + LivenessCheckInterval;
                if (AnyOwnedNeedsAttention())
                {
                    VerifyOwned();
                }
            }
            catch (System.Exception)
            {
                // An editor callback must never surface an exception
                // from this package.
            }
        }

        private static bool AnyOwnedNeedsAttention()
        {
            if (_monoProbed && _monoFontOwned && !ReferenceEquals(_monoFont, null)
                && _monoFont == null)
            {
                return true;
            }
            if (FontAssetLifecycle.NeedsAttention(_cjkUiAsset))
            {
                return true;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset> entry
                in _cjkStyleAssets)
            {
                if (FontAssetLifecycle.NeedsAttention(entry.Value))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Asks every editor-window text element drawn with the asset to
        /// regenerate. An in-place repair swaps the asset's material,
        /// which is what invalidates UI Toolkit's cached text mesh (its
        /// generation-settings hash covers the material), but the cache
        /// is only consulted on a repaint -- and an element whose last
        /// draw threw is no longer dirty, so nothing would repaint it.
        /// resolvedStyle carries the computed value, so elements that
        /// inherit the definition from an ApplyCjkUi root are covered.
        /// Repairs are rare; walking every window is affordable.
        /// </summary>
        private static void RepaintElementsUsing(UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (asset == null)
            {
                return;
            }
            EditorWindow[] windows;
            try
            {
                windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            }
            catch (System.Exception)
            {
                return;
            }
            for (int i = 0; i < windows.Length; i++)
            {
                try
                {
                    VisualElement root = windows[i] != null
                        ? windows[i].rootVisualElement
                        : null;
                    if (root == null || root.panel == null)
                    {
                        continue;
                    }
                    root.Query<TextElement>().ForEach(element =>
                    {
                        if (ReferenceEquals(
                            element.resolvedStyle.unityFontDefinition.fontAsset, asset))
                        {
                            element.MarkDirtyRepaint();
                        }
                    });
                }
                catch (System.Exception)
                {
                    // One window's tree must not stop the sweep.
                }
            }
        }

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
                // Adopt stamps HideAndDontSave on the asset AND the
                // protective flags on its material and atlas page, which
                // TextCore leaves unflagged (and which a Play Mode
                // transition would otherwise destroy).
                FontAssetLifecycle.Adopt(asset);
                HookEditorEvents();
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

        private static void HookEditorEvents()
        {
            if (_cleanupHooked)
            {
                return;
            }
            _cleanupHooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyOwned;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += GuardTick;
            UnityEditor.SceneManagement.EditorSceneManager.newSceneCreated += OnNewSceneCreated;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        /// <summary>
        /// A scene load is the other event (besides a Play Mode
        /// transition) that destroys unflagged objects, and it has no
        /// "before" hook the package could stamp from. These fire after
        /// the load and before the next repaint, which is where a PUSH
        /// sweep repairs whatever the guard tick did not get to stamp.
        /// </summary>
        private static void OnNewSceneCreated(
            UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.NewSceneSetup setup,
            UnityEditor.SceneManagement.NewSceneMode mode)
        {
            SweepAfterLoad();
        }

        private static void OnSceneOpened(
            UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            SweepAfterLoad();
        }

        private static void SweepAfterLoad()
        {
            try
            {
                VerifyOwned();
            }
            catch (System.Exception)
            {
                // An editor callback must never surface an exception
                // from this package.
            }
        }

        /// <summary>
        /// Play Mode transitions are where TextCore's unflagged atlas
        /// material and pages get destroyed. The Exiting* values fire
        /// before the transition, which is when the protective flags
        /// must be on every page (including ones TextCore added lazily
        /// since the last pass). The Entered* values fire on a later
        /// editor tick and drive a PUSH verification sweep: a consumer
        /// that applied a font once and never calls the package again
        /// would otherwise never trigger the access-time check.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            try
            {
                if (change == PlayModeStateChange.ExitingEditMode
                    || change == PlayModeStateChange.ExitingPlayMode)
                {
                    StampOwned();
                }
                else
                {
                    VerifyOwned();
                }
            }
            catch (System.Exception)
            {
                // An editor callback must never surface an exception
                // from this package.
            }
        }

        private static void DestroyOwned()
        {
            // Deterministic order: the BASE asset (whose weight table may
            // reference a style asset) dies first, then the style assets.
            // A destroyed slot reference fake-nulls and TextCore's null
            // check falls back to faux rendering, so even the reverse
            // order is benign -- the fixed order just makes that argument
            // unnecessary.
            FontAssetLifecycle.Forget(_cjkUiAsset);
            SafeDestroy(_cjkUiAsset);
            _cjkUiAsset = null;
            foreach (System.Collections.Generic.KeyValuePair<string, UnityEngine.TextCore.Text.FontAsset> entry
                in _cjkStyleAssets)
            {
                FontAssetLifecycle.Forget(entry.Value);
                SafeDestroy(entry.Value);
            }
            _cjkStyleAssets.Clear();
            _cjkUiFamilyName = null;
            if (_monoFontOwned)
            {
                SafeDestroy(_monoFont);
            }
            _monoFont = null;
            _monoFontOwned = false;
        }

        /// <summary>
        /// Destroys a live owned object, absorbing anything TextCore's
        /// own teardown might throw. FontAsset.OnDestroy unconditionally
        /// destroys its material, so disposing an asset whose material
        /// already died reaches engine code this package does not
        /// control -- and this runs from settings setters, which must
        /// not surface an exception.
        /// </summary>
        private static void SafeDestroy(Object obj)
        {
            try
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            catch (System.Exception)
            {
                // Losing one transient object is strictly better than
                // breaking the caller.
            }
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
