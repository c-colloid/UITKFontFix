# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
