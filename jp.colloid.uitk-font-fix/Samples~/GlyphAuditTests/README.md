# Glyph Audit Tests

A ready-to-adapt EditMode test that runs the package's source-level
glyph audit (`GlyphAudit.AuditSourceDirectory`) over YOUR project's own
editor sources.

## Why

A fixed UI string that bakes in a glyph the editor font cannot draw
fails silently at review time and loudly at runtime: a placeholder
square plus a console warning on every draw. The audit catches the
whole defect class at test time instead. Per `*.cs` file (recursive) it
flags:

- codepoints constructed in source (`char.ConvertFromUtf32` literals,
  `(char)` casts, `\uXXXX` / `\UXXXXXXXX` escapes) outside the
  `SafeGlyphs` whitelist;
- literal or escaped variation selectors (U+FE0F / U+FE0E);
- any non-ASCII byte (strict-ASCII source policy).

`(char)` casts of the selector values are exempt: sanitizers
legitimately compare against them in order to remove them, and a
comparison never renders. The package runs this exact audit over its
own shipped sources; this sample lets your project do the same.

## Setup

1. Import the sample. It lands at
   `Assets/Samples/UITK Font Fix/<version>/Glyph Audit Tests/` with its
   own editor-only test assembly, so it never ships in builds and only
   compiles when tests are included (`UNITY_INCLUDE_TESTS`).
2. Edit `AuditedDirectories` in `ProjectGlyphAuditTests.cs` to the
   folders holding your editor UI sources. Paths are relative to the
   project root (the editor's working directory); `Assets/...` and
   `Packages/...` paths both work. The template default
   `Assets/Editor` is only a starting guess -- a missing directory is
   reported as a test failure ON PURPOSE so you notice and fix the
   list.
3. Run it: **Window > General > Test Runner**, EditMode tab,
   `EditorSources_PassGlyphAudit` -- or headless in CI via
   `-runTests -testPlatform EditMode`.

## Extending the whitelist (extraCodepoints)

`SafeGlyphs.DefaultCodepoints` is the proven-safe non-ASCII set for the
2022.3 editor font (checkmarks, arrows, bullets, warning sign, ...).
To allow additional non-ASCII codepoints in your own fixed UI strings:

1. Verify the glyph actually renders in the target editor font: put it
   in a `Label` and confirm the console stays free of
   `not found in [...]` warnings.
2. Add its codepoint to the `ExtraCodepoints` array in the test. The
   array is passed straight through to the audit and matches the
   `SafeGlyphs.IsSafeCodepoint(codepoint, extraCodepoints)` overload,
   so runtime checks and the audit share one whitelist.

Keep the extension small. Free-form text from users or models should
never be whitelisted -- route it through `FontFix.SanitizeDisplayText`
at display time instead.

## About the asmdef

`UitkFontFixSamples.GlyphAuditTests` mirrors the package's own test
assembly pattern:

- references `Colloid.UitkFontFix` (SafeGlyphs) and
  `Colloid.UitkFontFix.Editor` (GlyphAudit), plus
  `UnityEngine.TestRunner` / `UnityEditor.TestRunner`;
- `overrideReferences: true` with the `nunit.framework.dll`
  precompiled reference;
- Editor-only platform, `autoReferenced: false`, and the
  `UNITY_INCLUDE_TESTS` define constraint.

Rename the assembly and namespace to match your project's conventions
if you like; keep the references and the define constraint. If your
project already has an editor test assembly, you can instead move
`ProjectGlyphAuditTests.cs` into it and add the two
`Colloid.UitkFontFix` references there, then delete this sample's
asmdef.

Import only ONE package version's copy of this sample at a time -- two
imported copies would collide on the assembly name.
