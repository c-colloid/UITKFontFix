# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.4.1] - 2026-09-08

### Fixed

- Fonts resolved from an OS family no longer break when a scene is
  loaded (File > New Scene, opening a scene) or on other editor events
  that destroy unflagged objects, and no longer need a re-probe to
  recover. Three gaps in the 0.4.0 lifecycle policy: (1) atlas pages
  TextCore adds lazily during rendering were re-flagged only before a
  Play Mode transition or on a cached access, so a consumer that
  applied the font once left every later page at `HideFlags.None`
  until the next scene load destroyed it -- and Japanese UI text grows
  several pages, since `CreateFontAsset` samples at 90 pt into
  1024x1024 pages. Pages are now flagged within one editor update
  (a per-tick page-count comparison, no native call). (2) Only Play
  Mode had a push sweep; the package now verifies after each scene
  load and at a low rate from the editor update. (3) An in-place
  repair kept a surviving material, but UI Toolkit regenerates an
  element's cached text mesh only when its generation-settings hash
  changes, and that hash covers the font asset and its material. The
  repaired asset therefore kept drawing stale mesh data that pointed
  at the destroyed page, throwing `NullReferenceException` from
  `UIRStylePainter.DrawTextInfo` (`Material.mainTexture` reads back as
  null for a destroyed texture) until a re-probe replaced the instance.
  The repair now always installs a new material and marks every
  editor-window text element drawn with the asset for repaint.

## [0.4.0] - 2026-08-30

### Fixed

- Fonts resolved from an OS family no longer break across a Play Mode
  transition. `FontAsset.CreateFontAsset` leaves the atlas material and
  atlas textures it creates at `HideFlags.None`, so the editor destroys
  them while the `HideAndDontSave` `FontAsset` survives holding
  destroyed references, and every later draw throws
  `MissingReferenceException` from inside TextCore. The package now
  stamps the protective flags on those children at creation -- exactly
  what Unity's own runtime-font-asset cache does -- re-stamps them
  before each Play Mode transition (TextCore adds atlas pages lazily,
  unflagged), verifies liveness on every cached access and on
  entering/leaving Play Mode, and repairs a damaged asset **in place**.
  In-place matters: UI Toolkit elements hold the `FontAsset` in their
  inline style, so a replacement instance would not heal them. The gap
  applied to every face the package creates, including the real-Bold
  face wired into the weight table by default.
- Changing `FontFixSettings` (from code, the Project Settings pane, the
  diagnostics window, or the project settings file) destroys the
  resolved objects that open windows are still painting with. The new
  `CachesInvalidated` event makes that recoverable, and the two UIs
  this package ships now subscribe to it and re-apply their own fonts;
  the settings UIs no longer claim that open windows keep working.

### Added

- `FontFix.CachesInvalidated`: a `static event Action` raised
  synchronously when a resolved object the package handed out was
  destroyed or replaced (`ResetCaches()`, a settings change, or a
  rebuild after unrepairable damage). Subscribers re-apply their fonts;
  it does not fire for an in-place repair, since the instance is
  unchanged. Multi-property operations raise it once, and a scope in
  which nothing actually changed raises nothing. Subscriptions are lost
  on every domain reload.
- Diagnostics report: the CJK atlas section now shows whether the asset
  is usable, the atlas material with its `hideFlags`, how many pages are
  in use, and per-page flags (distinguishing a destroyed page from an
  unused array slot).

## [0.3.0] - 2026-08-30

### Added

- No-code configuration: a Project Settings pane (Edit > Project
  Settings > UITK Font Fix) and the same form inside the diagnostics
  window's "Edit configuration" foldout. Apply pushes values into the
  session; Save to project persists them to
  `ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json`
  (versioned, team-shared) via the new `FontFixProjectSettings` API;
  Use package defaults resets and deletes the file.
- The saved file is applied once per domain load, before consumer
  code in Unity's customary assembly-initialization order (a
  convention, not a documented guarantee); later `FontFixSettings`
  assignments from code win by design. The diagnostics report gains a
  "Project settings file" section that shows presence and drift, and
  the diagnostics Re-probe now applies pending foldout edits and
  refreshes the report in place (half-edited fields survive).

## [0.2.0] - 2026-08-30

### Added

- Created-object naming: every FontAsset the package creates (plus its
  material, first atlas page, and any kit-owned OS Font) carries the
  `FontFix.CreatedObjectNameTag` suffix `[UITK Font Fix]`, making
  package-created objects identifiable in the UITK Debugger and
  searchable in the Memory Profiler. Shared editor assets are never
  renamed.
- `FontFix.GetCjkUiFontAsset(string styleName)`: cached per-face
  assets of the resolved CJK family (Semibold, Light, ...); face names
  are exact, misses are cached, base-style lookups alias to
  `CjkUiFontAsset`.
- `FontFix.ApplyCjkUiFace(VisualElement, string)`: leaf-element face
  assignment (no-op on a face miss).
- `FontFixSettings.CjkUiBoldStyleName` (default `"Bold"`; `""`
  disables bold wiring; null restores the default).
- Diagnostics report: new "CJK faces" section (bold wiring state,
  per-face cache with actual face names) and per-page atlas texture
  names.

### Changed

- `-unity-font-style: bold` on CJK-styled text now renders the
  family's REAL Bold face when one exists: the package wires it into
  the base asset's `fontWeightTable` at resolution time. Families
  without a Bold face keep the previous synthetic (SDF-dilated) bold,
  and `FontFixSettings.CjkUiBoldStyleName = ""` restores the previous
  behavior wholesale. Real Bold has different advances, so bold labels
  can wrap differently after upgrading. Bold-and-italic stays
  synthetic (no italic wiring; default CJK families ship no italic
  faces).

## [0.1.0] - 2026-07-31

### Added

- `FontFix` static facade (namespace `Colloid.UitkFontFix`):
  - `EditorMonoFont` / `EditorMonoFontSource`: editor-bundled RobotoMono
    first, face-probed single-name OS fonts second, default label font last.
  - `CjkUiFontAsset` / `CjkUiFontSource`: DynamicOS `FontAsset` resolution
    for Latin+CJK UI text (Yu Gothic UI priority chain).
  - `ApplyMono(VisualElement)` (inline, wins over inheritance) and
    `ApplyCjkUi(VisualElement)` (assign on a container root to inherit).
  - `ShouldPreferCjkUi(SystemLanguage)` pure language policy.
  - `SanitizeDisplayText(string)` lossless display-text hygiene
    (variation selectors incl. ideographic ones, zero-width characters,
    BOM, emoji tag characters) and the lossy
    `SanitizeDisplayText(string, string)` overload (strip-then-replace
    of non-BMP codepoints and unpaired surrogates).
  - `ResetCaches()` for tests and candidate-list changes.
- `FontFixSettings` static configuration (candidate list overrides with
  cache invalidation only on real changes).
- Runtime assembly (`Colloid.UitkFontFix`) with pure utilities:
  `TextSanitizer`, `CjkLanguage`, `SafeGlyphs`, `FontFixDefaults` --
  the structural seed for the v2 runtime support.
- Internal version seam `FontShims` concentrating every
  version-sensitive TextCore/UI Toolkit call (APIs verified identical
  across the 2022.3/2023.2/6000.0 UnityCsReference branches; no
  version branching needed).
- `GlyphAudit` source-audit helper usable from consumer test assemblies
  (constructed-codepoint whitelist scan, variation-selector scan,
  strict-ASCII scan) plus a self-audit test over this package's own
  shipped sources.
- Diagnostics window (Window > UITK Font Fix > Diagnostics) with a
  batch-safe plain-text report builder (`FontFixDiagnostics`): resolution
  results, CJK atlas page state, candidate availability and the
  known-trap checklist.
- Runtime `TextSanitizer` with granular ops: `StripVariationSelectors`
  (all Unicode variation selectors, surrogate-pair-aware),
  `StripInvisibleCharacters` (superset; the `SanitizeDisplayText`
  default) and the lossy `ReplaceNonBmpCharacters`.
- Two importable samples ("Editor Font Setup", "Glyph Audit Tests")
  listed in the Package Manager Samples tab.
- EditMode test suite covering resolution, application, settings overrides,
  language policy, text hygiene (including surrogate edge cases), the
  safe-glyph whitelist, the glyph audit and the diagnostics report,
  including batch-mode skip handling for probes that cannot run headless.
