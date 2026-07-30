# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] - 2026-07-30

### Added

- `FontFix` static facade (namespace `Colloid.UitkFontFix`):
  - `EditorMonoFont` / `EditorMonoFontSource`: editor-bundled RobotoMono
    first, face-probed single-name OS fonts second, default label font last.
  - `CjkUiFontAsset` / `CjkUiFontSource`: DynamicOS `FontAsset` resolution
    for Latin+CJK UI text (Yu Gothic UI priority chain).
  - `ApplyMono(VisualElement)` (inline, wins over inheritance) and
    `ApplyCjkUi(VisualElement)` (assign on a container root to inherit).
  - `ShouldPreferCjkUi(SystemLanguage)` pure language policy.
  - `StripVariationSelectors(string)` display-text hygiene (U+FE0F/U+FE0E).
  - `ResetCaches()` for tests and candidate-list changes.
- `FontFixSettings` static configuration (candidate list overrides with
  cache invalidation only on real changes).
- Runtime assembly (`Colloid.UitkFontFix`) with pure utilities:
  `TextSanitizer`, `CjkLanguage`, `SafeGlyphs`, `FontFixDefaults` --
  the structural seed for the v2 runtime support.
- Version seam `FontShims` with the reserved
  `UITK_FONT_FIX_FROMSDFFONTASSET` define (see design note
  `docs/design-notes/2026-07-30-uitk-font-fix-architecture.md`).
- `GlyphAudit` source-audit helper usable from consumer test assemblies
  (constructed-codepoint whitelist scan, variation-selector scan,
  strict-ASCII scan) plus a self-audit test over this package's own
  shipped sources.
- Diagnostics window (Window > UITK Font Fix > Diagnostics) with a
  batch-safe plain-text report builder (`FontFixDiagnostics`): resolution
  results, CJK atlas page state, candidate availability and the
  known-trap checklist.
- EditMode test suite covering resolution, application, settings overrides,
  language policy, text hygiene, the safe-glyph whitelist, the glyph
  audit and the diagnostics report, including batch-mode skip handling
  for probes that cannot run headless.
