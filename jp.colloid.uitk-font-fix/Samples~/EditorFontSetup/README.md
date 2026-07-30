# Editor Font Setup

A complete, commented `EditorWindow` showing the canonical UITK Font Fix
composition. After importing, open it via
**Window > UITK Font Fix > Samples > Font Setup Example**.

What the window demonstrates:

- **Language gate** -- `Application.systemLanguage` is queried inside
  `CreateGUI` and passed to `FontFix.ShouldPreferCjkUi`. It is never
  read from a ScriptableObject constructor or field initializer, which
  throws during serialization (the source comment explains the
  incident behind this rule).
- **Container root** -- `FontFix.ApplyCjkUi(rootVisualElement)`; every
  descendant inherits one Latin+CJK font, removing the patchy per-glyph
  OS fallback the editor default font produces for CJK text.
- **Monospace leaf** -- `FontFix.ApplyMono` on a code block INSIDE the
  CJK container. Inline styles always beat inherited values, so the
  code block stays monospaced regardless of call order.
- **Text hygiene** -- every dynamic string goes through
  `FontFix.SanitizeDisplayText` before reaching a `Label`. Paste text
  containing emoji or variation selectors into the field to watch it
  work.
- **Customization** -- a commented-out `FontFixSettings` block shows how
  to reorder the CJK candidate list (Simplified-Chinese-first example)
  and explains the cache-invalidation rules.
- **Diagnostics** -- a button opens
  **Window > UITK Font Fix > Diagnostics** (resolved candidates, CJK
  atlas state, known-trap checklist).

## Why there is no asmdef in this sample

The sample compiles without an assembly definition on purpose:

- The code sits under `Editor/`, so Unity compiles the imported copy
  into the predefined `Assembly-CSharp-Editor` assembly (editor-only,
  never part of a player build).
- Both package assemblies (`Colloid.UitkFontFix` and
  `Colloid.UitkFontFix.Editor`) ship with `autoReferenced: true`, and
  Unity's predefined assemblies automatically reference every
  auto-referenced assembly definition -- so
  `using Colloid.UitkFontFix;` resolves with no further setup.

If your project routes all editor code through its own asmdefs instead,
move the file into one of them and add references to
`Colloid.UitkFontFix` and `Colloid.UitkFontFix.Editor`.

## Notes

- Import only ONE package version's copy of this sample at a time.
  Imported samples are plain copies under
  `Assets/Samples/UITK Font Fix/<version>/Editor Font Setup/`; two
  versions side by side would define the same class twice inside
  `Assembly-CSharp-Editor` and fail to compile.
- The source file is strictly ASCII: the Japanese demo text and the
  U+FE0F demo value are constructed from codepoints at runtime. That is
  the same policy the "Glyph Audit Tests" sample enforces for your own
  sources, and this file itself passes that audit.
