# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] - 2026-07-30

### Added

- `FontKit` static facade (namespace `Colloid.UitkFontKit`):
  - `EditorMonoFont` / `EditorMonoFontSource`: editor-bundled RobotoMono
    first, face-probed single-name OS fonts second, default label font last.
  - `CjkUiFontAsset` / `CjkUiFontSource`: DynamicOS `FontAsset` resolution
    for Latin+CJK UI text (Yu Gothic UI priority chain).
  - `ApplyMono(VisualElement)` (inline, wins over inheritance) and
    `ApplyCjkUi(VisualElement)` (assign on a container root to inherit).
  - `ShouldPreferCjkUi(SystemLanguage)` pure language policy.
  - `StripVariationSelectors(string)` display-text hygiene (U+FE0F/U+FE0E).
  - `ResetCaches()` for tests and candidate-list changes.
- `FontKitSettings` static configuration (candidate list overrides with
  cache invalidation only on real changes).
- Runtime assembly (`Colloid.UitkFontKit`) with pure utilities:
  `TextSanitizer`, `CjkLanguage`, `SafeGlyphs`, `FontKitDefaults` --
  the structural seed for the v2 runtime support.
- Version seam `FontShims` with the reserved
  `UITK_FONT_KIT_FROMSDFFONTASSET` define (see design note
  `docs/design-notes/2026-07-30-uitk-font-kit-architecture.md`).
- EditMode test suite covering resolution, application, settings overrides,
  language policy, text hygiene and the safe-glyph whitelist, including
  batch-mode skip handling for probes that cannot run headless.
