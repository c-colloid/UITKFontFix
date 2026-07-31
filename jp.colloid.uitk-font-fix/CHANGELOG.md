# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
