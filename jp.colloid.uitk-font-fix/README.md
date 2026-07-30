# UITK Font Fix

Font utilities for Unity UI Toolkit editor UIs. Resolves a safe monospace
font and a Latin+CJK UI font, applies them with the correct
inline-vs-inherited composition, and keeps model/user text from tripping
known TextCore rendering traps. Zero dependencies, MIT, Editor-first
(Unity 2022.3 LTS+; runtime support is structurally prepared for v2).

Everything in this package exists because of behavior verified on real
machines (Unity 2022.3.22f1, Windows 11, Japanese locale) -- see
"Verified ground truth" below. If a design choice looks odd, that section
is why.

## Install

Via Git URL (Package Manager > Add package from git URL):

```
https://github.com/c-colloid/UITKFontFix.git?path=jp.colloid.uitk-font-fix
```

Or drop the `jp.colloid.uitk-font-fix` folder into your project's
`Packages/` directory.

## Quick start

```csharp
using Colloid.UitkFontFix;

// In your EditorWindow.CreateGUI (or later -- never from a
// ScriptableObject constructor/field initializer):
if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
{
    FontFix.ApplyCjkUi(rootVisualElement);   // container root: inherited
}
FontFix.ApplyMono(codeLabel);                // leaf: inline, wins locally

// Before displaying model/user text:
label.text = FontFix.StripVariationSelectors(rawText);
```

The composition rule: `ApplyCjkUi` goes on a **container root** (all
descendants inherit it), `ApplyMono` goes on **leaf** elements (an inline
style always beats an inherited value, so code stays monospaced inside a
CJK container). Never call both on the same element -- inline writes are
last-one-wins.

## API

All members cache their result, never throw, and are safe in batch mode.

| Member | Purpose |
| --- | --- |
| `FontFix.EditorMonoFont` | Bundled RobotoMono, then face-probed OS fonts, then the default label font. Never null in a functioning editor. |
| `FontFix.CjkUiFontAsset` | DynamicOS `FontAsset` for Latin+CJK UI text (Yu Gothic UI chain by default); null when no candidate exists. |
| `FontFix.EditorMonoFontSource` / `CjkUiFontSource` | Diagnostics: which candidate won (`"editor:..."`, `"os:..."`, `"label"`, `"osasset:..."`). |
| `FontFix.ApplyMono(leaf)` | Inline mono assignment on a leaf element. |
| `FontFix.ApplyCjkUi(containerRoot)` | Font assignment descendants inherit. |
| `FontFix.ShouldPreferCjkUi(lang)` | Pure ja/zh/ko policy. |
| `FontFix.StripVariationSelectors(text)` | Removes U+FE0F/U+FE0E before display. |
| `FontFix.ResetCaches()` | Drops caches (tests, candidate changes). |
| `FontFixSettings` | Candidate-list overrides (`EditorMonoFontPaths`, `OsMonoFontNames`, `CjkUiFontNames`, `CjkUiStyleName`). Null restores defaults; equal values keep caches warm. |
| `TextSanitizer`, `CjkLanguage`, `SafeGlyphs`, `FontFixDefaults` | Runtime-assembly pure utilities behind the facade. |

## Verified ground truth (why this design)

Measured on Unity 2022.3.22f1 / Windows 11 / Japanese locale
(2026-07-30..31), in both interactive and batch editors:

1. `FontEngine.LoadFontFace(Font)` returns `Invalid_File` for **every**
   OS dynamic font (`Font.CreateDynamicFontFromOSFont`). It cannot
   validate OS fonts -- which is why this package never hands an OS
   `Font` to UI Toolkit.
2. The working OS-font route is
   `TextCore.Text.FontAsset.CreateFontAsset(family, style)` (DynamicOS
   mode) assigned via `FontDefinition`. Glyphs are fetched lazily at
   render time (`HasCharacter` false right after creation is normal),
   and creation works headless.
3. `Font.CreateDynamicFontFromOSFont` with a **name array** produces an
   unloadable face ("Unable to load font face" / "Can't Generate Mesh")
   and text renders empty. Single names do not pass face validation
   either; the route is avoided entirely.
4. `EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf")`
   resolves on 2022.3 -- a real TTF asset TextCore always accepts, hence
   the preferred mono candidate.
5. USS cannot select OS fonts by name; font application happens from C#
   via `style.unityFontDefinition`. Inline styles beat inherited values,
   which is what makes the root/leaf composition reliable.
6. The editor default font (Inter) has no CJK coverage. Per-glyph OS
   fallback mixes fonts and weights, rendering CJK text "patchy bold".
   One explicit Latin+CJK font on the container root removes the
   fallback path. Proven chain: Yu Gothic UI > Yu Gothic > Meiryo UI >
   Meiryo > Noto Sans CJK JP > MS UI Gothic.
7. `Font.GetOSInstalledFontNames()` reports **English** family names
   even on Japanese Windows.
8. The `<mark>` rich-text tag is incompatible with DynamicOS FontAssets:
   mark quads draw above or below glyphs depending on their atlas PAGE,
   producing patchy bold / faded runs (or fully hidden text with opaque
   marks). Atlas prefill cannot fix it (ASCII alone spans 4 pages; CJK
   can never be single-page). Do not combine `<mark>` with these fonts.
9. Emoji-plane characters (e.g. U+1F4CE) and variation selectors
   (U+FE0F, which models and users routinely emit) have no glyph in the
   editor fonts: placeholder squares plus console warning spam. Use
   `StripVariationSelectors` for free-form text and `SafeGlyphs` for
   fixed UI strings.
10. `Application.systemLanguage` throws when read from ScriptableObject
    constructors/field initializers. Query it in `OnEnable` or later and
    pass the value to `ShouldPreferCjkUi`.
11. Batch mode: OS dynamic font FACE operations all fail, DynamicOS
    FontAsset creation works. The test suite is built around exactly
    this split.

## Unity version compatibility

Primary target is 2022.3 LTS (every ground-truth item above was measured
there). All version-sensitive TextCore/UI Toolkit calls are concentrated
in the internal `FontShims` seam. Research as of 2026-07-30 found
`FontDefinition.FromSDFFont` documented unchanged from 2022.1 through
6000.x (a rename to `FromSDFFontAsset` could NOT be confirmed anywhere);
should a rename ever ship, define `UITK_FONT_FIX_FROMSDFFONTASSET`
(script define or asmdef versionDefines) or edit `FontShims` -- callers
never change.

## Migrating from UnityAgentPanel's internal FontLoader

This package is the generalized extraction of
`Colloid.AgentPanel.UI.FontLoader` / `IconLoader`. Mapping:

| UnityAgentPanel | UITK Font Fix |
| --- | --- |
| `FontLoader.MonoFont` / `MonoFontSource` | `FontFix.EditorMonoFont` / `EditorMonoFontSource` |
| `FontLoader.JapaneseUiFontAsset` / `JapaneseUiFontSource` | `FontFix.CjkUiFontAsset` / `CjkUiFontSource` (source prefix `osasset:` unchanged) |
| `FontLoader.ApplyMono` | `FontFix.ApplyMono` |
| `FontLoader.ApplyJapaneseUi` | `FontFix.ApplyCjkUi` |
| `IconLoader.StripVariationSelectors` | `FontFix.StripVariationSelectors` (or `TextSanitizer` directly) |
| `IconLoader.SafeGlyphCodepoints` / `IsSafeGlyphCodepoint` | `SafeGlyphs.DefaultCodepoints` / `IsSafeCodepoint` |
| `GlyphAuditTests` source-scan internals | `GlyphAudit.AuditSourceDirectory` (public helper) |
| (hardcoded candidate lists) | `FontFixSettings` overrides |

## License

MIT (c) 2026 colloid
