# UITK Font Kit

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
https://github.com/c-colloid/UITKFontFix.git?path=jp.colloid.uitk-font-kit
```

Or drop the `jp.colloid.uitk-font-kit` folder into your project's
`Packages/` directory.

## Quick start

```csharp
using Colloid.UitkFontKit;

// In your EditorWindow.CreateGUI (or later -- never from a
// ScriptableObject constructor/field initializer):
if (FontKit.ShouldPreferCjkUi(Application.systemLanguage))
{
    FontKit.ApplyCjkUi(rootVisualElement);   // container root: inherited
}
FontKit.ApplyMono(codeLabel);                // leaf: inline, wins locally

// Before displaying model/user text:
label.text = FontKit.StripVariationSelectors(rawText);
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
| `FontKit.EditorMonoFont` | Bundled RobotoMono, then face-probed OS fonts, then the default label font. Never null in a functioning editor. |
| `FontKit.CjkUiFontAsset` | DynamicOS `FontAsset` for Latin+CJK UI text (Yu Gothic UI chain by default); null when no candidate exists. |
| `FontKit.EditorMonoFontSource` / `CjkUiFontSource` | Diagnostics: which candidate won (`"editor:..."`, `"os:..."`, `"label"`, `"osasset:..."`). |
| `FontKit.ApplyMono(leaf)` | Inline mono assignment on a leaf element. |
| `FontKit.ApplyCjkUi(containerRoot)` | Font assignment descendants inherit. |
| `FontKit.ShouldPreferCjkUi(lang)` | Pure ja/zh/ko policy. |
| `FontKit.StripVariationSelectors(text)` | Removes U+FE0F/U+FE0E before display. |
| `FontKit.ResetCaches()` | Drops caches (tests, candidate changes). |
| `FontKitSettings` | Candidate-list overrides (`EditorMonoFontPaths`, `OsMonoFontNames`, `CjkUiFontNames`, `CjkUiStyleName`). Null restores defaults; equal values keep caches warm. |
| `TextSanitizer`, `CjkLanguage`, `SafeGlyphs`, `FontKitDefaults` | Runtime-assembly pure utilities behind the facade. |

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

## License

MIT (c) 2026 colloid
