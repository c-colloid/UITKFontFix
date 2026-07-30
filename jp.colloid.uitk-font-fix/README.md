# UITK Font Fix

Font utilities for Unity UI Toolkit **editor** UIs: resolves a monospace
font and a Latin+CJK UI font that are guaranteed to actually load,
applies them with the one composition pattern that survives UI Toolkit
style inheritance, and keeps free-form text from tripping known TextCore
rendering traps.

Zero dependencies. Editor-first (Unity 2022.3 LTS or newer; the runtime
assembly is structurally prepared for future runtime support). MIT.

## The traps this package fixes

UI Toolkit editor UIs on Unity 2022.3 have a set of font failure modes
that are easy to hit and hard to diagnose:

- **CJK text renders "patchy bold".** The editor default font (Inter)
  has no CJK coverage, so CJK glyphs fall back per glyph to whatever OS
  fonts can supply them -- mixing families and weights mid-string.
- **OS fonts can silently render empty text.**
  `Font.CreateDynamicFontFromOSFont` can produce a face UI Toolkit
  cannot load ("Unable to load font face"), and `FontEngine` cannot
  validate such a `Font` first.
- **USS cannot select OS fonts by name.** Fonts must be assigned from
  C# via `style.unityFontDefinition`.
- **Model/user text spams warnings and draws placeholder squares.**
  Variation selectors (for example U+FE0F after a warning-sign
  character) and emoji-plane characters have no glyph in the editor
  fonts.
- **`Application.systemLanguage` throws during serialization**, so naive
  language gating can take a whole settings asset down with it.

`FontFix` packages the verified workarounds behind a small facade:
resolvers that cache, never throw, and report which candidate won;
apply helpers with a documented inheritance contract; display-text
sanitizers; a source-level glyph audit; and a diagnostics window.

- [Install](#install)
- [Quick start](#quick-start)
- [Recipes](#recipes)
- [API reference](#api-reference)
- [Verified behavior (why this design)](#verified-behavior-why-this-design)
- [Unity version compatibility](#unity-version-compatibility)
- [Samples](#samples)
- [License](#license)

## Install

Via git URL (Package Manager > `+` > *Add package from git URL...*):

```
https://github.com/c-colloid/UITKFontFix.git?path=jp.colloid.uitk-font-fix
```

Or drop the `jp.colloid.uitk-font-fix` folder into your project's
`Packages/` directory (embedded package).

Both package assemblies (`Colloid.UitkFontFix`,
`Colloid.UitkFontFix.Editor`) are auto-referenced, so loose scripts
under `Assets/` can use the API immediately. Code inside your own
asmdefs adds an explicit assembly reference as usual.

## Quick start

Save as `Assets/Editor/FontFixQuickStart.cs`, then open
*Window > Font Fix Quick Start*:

```csharp
using Colloid.UitkFontFix;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class FontFixQuickStart : EditorWindow
{
    [MenuItem("Window/Font Fix Quick Start")]
    public static void Open()
    {
        GetWindow<FontFixQuickStart>("Font Fix Quick Start");
    }

    public void CreateGUI()
    {
        // 1. Container root: one Latin+CJK font that every descendant
        //    inherits. Reading the system language is safe here --
        //    CreateGUI runs long after serialization.
        if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
        {
            FontFix.ApplyCjkUi(rootVisualElement);
        }

        // 2. Ordinary labels: nothing to do, they inherit the root font.
        rootVisualElement.Add(new Label("Ready"));

        // 3. Code leaf: inline mono. An inline style always beats an
        //    inherited one, so this stays monospaced inside the CJK
        //    container.
        var code = new Label("if (x == 0) { return; }");
        FontFix.ApplyMono(code);
        rootVisualElement.Add(code);

        // 4. Free-form text (models, users, files, clipboard): sanitize
        //    before display. Only invisible codepoints are removed --
        //    visible content never changes.
        string raw = EditorGUIUtility.systemCopyBuffer;
        rootVisualElement.Add(new Label(FontFix.SanitizeDisplayText(raw)));
    }
}
```

The composition rule: `ApplyCjkUi` goes on a **container root** (all
descendants inherit it), `ApplyMono` goes on **leaf** elements (an
inline style always beats an inherited value, so code stays monospaced
inside a CJK container). Never call both on the same element -- both
are inline writes and the last one silently wins.

## Recipes

### 1. An editor window mixing CJK UI text and monospaced code

```csharp
using Colloid.UitkFontFix;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class BuildLogWindow : EditorWindow
{
    [MenuItem("Window/Build Log")]
    public static void Open()
    {
        GetWindow<BuildLogWindow>("Build Log");
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        // One CJK-capable font on the root: every Label below inherits
        // it, so Japanese/Chinese/Korean strings render in a single
        // family instead of a patchy per-glyph OS fallback.
        if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
        {
            FontFix.ApplyCjkUi(root);
        }

        root.Add(new Label("Build result")); // inherits the root font

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        var message = new Label("Shader compilation finished ");
        row.Add(message); // inherits: proportional UI font

        var code = new Label("ShaderLab.ParseError:0x2F");
        FontFix.ApplyMono(code); // inline: monospaced, wins locally
        row.Add(code);
        root.Add(row);

        // A multiline log body works the same way: one ApplyMono on
        // the leaf, regardless of how deep it sits under the CJK root.
        var log = new TextField { multiline = true, isReadOnly = true };
        log.value = "0x0042  OK\n0x0043  RETRY";
        FontFix.ApplyMono(log);
        root.Add(log);

        // Rule: ApplyCjkUi on container roots, ApplyMono on leaves.
        // Never both on the SAME element -- both are inline writes and
        // the last one wins.
    }
}
```

### 2. Language gating done safely

`Application.systemLanguage` throws when read during serialization
(constructors, field initializers) -- and one throwing field
initializer can break an entire asset load. The policy helpers are
therefore pure: they take the language as a parameter, and *you*
control when it is read.

```csharp
using Colloid.UitkFontFix;
using UnityEngine;

public class MyToolState : ScriptableObject
{
    // WRONG -- field initializers run during serialization and this
    // throws (UnityException), potentially killing the asset load:
    //
    //   private bool _preferCjk =
    //       CjkLanguage.ShouldPreferCjkUi(Application.systemLanguage);

    private bool _preferCjk;

    private void OnEnable()
    {
        // RIGHT: query once from OnEnable (or any later callback) and
        // cache the result. CjkLanguage.ShouldPreferCjkUi is pure and
        // never reads the language itself.
        _preferCjk = CjkLanguage.ShouldPreferCjkUi(
            Application.systemLanguage);
    }

    public bool PreferCjk
    {
        get { return _preferCjk; }
    }
}
```

`CjkLanguage` lives in the runtime assembly, so this pattern works in
any script. In editor code, `FontFix.ShouldPreferCjkUi` is the same
policy behind the facade.

### 3. Sanitizing model/user text before display

```csharp
using Colloid.UitkFontFix;
using UnityEngine.UIElements;

// Editor UI code (e.g. under an Editor folder).
public static class ChatView
{
    // Lossless, always safe: strips ONLY invisible codepoints --
    // variation selectors (including ideographic ones), zero-width
    // characters, the BOM and emoji tag characters. What the user
    // sees never changes; what
    // stops happening is per-draw "not found in [Inter-Regular SDF]"
    // warning spam and placeholder squares.
    public static void ShowMessage(Label target, string rawModelText)
    {
        target.text = FontFix.SanitizeDisplayText(rawModelText);
    }

    // Optional, LOSSY second step for surfaces that must stay strictly
    // BMP (the editor fonts have no emoji-plane glyphs at all): every
    // supplementary-plane character becomes a visible substitute
    // instead of a placeholder square. Opt in deliberately.
    public static void ShowMessageBmpOnly(Label target, string rawModelText)
    {
        // The overload composes strip-then-replace in the safe order:
        // ideographic variation selectors are stripped (invisible)
        // BEFORE non-BMP replacement, so selector-bearing kanji do not
        // grow a stray "*". Granular ops live on TextSanitizer if you
        // need them individually.
        target.text = FontFix.SanitizeDisplayText(rawModelText, "*");
    }
}
```

All sanitizers are pure, never throw, return the same string instance
when nothing needs changing, and map null to `string.Empty`.

### 4. Overriding the candidate fonts (zh/ko-first products)

The defaults resolve a Japanese-priority chain. Products that ship
primarily for Chinese or Korean users replace the CJK candidate list --
code-first, no asset required:

```csharp
using Colloid.UitkFontFix;
using UnityEditor;

public static class MyProjectFontConfig
{
    [InitializeOnLoadMethod]
    private static void Configure()
    {
        // Simplified-Chinese-first product:
        FontFixSettings.CjkUiFontNames = new[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei",       // zh-Hans, Windows
            "Microsoft JhengHei UI", "Microsoft JhengHei", // zh-Hant, Windows
            "Noto Sans CJK SC",                            // Linux
            "Yu Gothic UI"                                 // ja fallback
        };

        // Korean-first product (instead of the block above):
        //
        // FontFixSettings.CjkUiFontNames = new[]
        // {
        //     "Malgun Gothic",    // Windows
        //     "Noto Sans CJK KR", // Linux
        //     "Yu Gothic UI"      // ja fallback
        // };

        // Notes:
        // - Names are probed one at a time, most preferred first, and
        //   must be the ENGLISH family names: GetOSInstalledFontNames
        //   reports English names even on localized Windows.
        // - A value that actually changes invalidates the FontFix
        //   caches automatically; re-assigning an equal value keeps
        //   them warm. Assign null to restore the defaults.
    }
}
```

The same pattern applies to `EditorMonoFontPaths` and `OsMonoFontNames`
for the monospace side, and `CjkUiStyleName` for the style passed to
`FontAsset.CreateFontAsset`.

### 5. Guarding fixed UI strings with SafeGlyphs + GlyphAudit

Fixed UI strings (icons, bullets, arrows baked into your sources)
should stick to printable ASCII plus the proven-safe `SafeGlyphs`
whitelist. `GlyphAudit` turns that policy into a test that fails with
the exact file and codepoint when someone bakes in a glyph the editor
font cannot draw:

```csharp
using System.Collections.Generic;
using Colloid.UitkFontFix;
using NUnit.Framework;

// EditMode test assembly. In your test asmdef, reference
// Colloid.UitkFontFix and Colloid.UitkFontFix.Editor.
public class UiGlyphSafetyTests
{
    // Project-specific additions to the whitelist. Add a codepoint
    // here ONLY after confirming it renders in the target editor font
    // (a quick check: put it in a label and watch the console).
    private static readonly int[] ExtraCodepoints =
    {
        0x2192 // RIGHTWARDS ARROW
    };

    [Test]
    public void EditorSources_PassGlyphAudit()
    {
        // Recursively scans *.cs for: constructed codepoints outside
        // the whitelist (ConvertFromUtf32, (char) casts, \uXXXX and
        // \UXXXXXXXX escapes), variation-selector literals/escapes,
        // and non-ASCII bytes. Empty list means clean; offenders are
        // "file: reason" strings, so the failure message says exactly
        // what slipped in and where.
        List<string> offenders = GlyphAudit.AuditSourceDirectory(
            "Assets/Editor", ExtraCodepoints);
        Assert.IsEmpty(offenders, string.Join("\n", offenders));
    }

    [Test]
    public void GlyphStrings_UseOnlySafeCodepoints()
    {
        // Fixed UI strings are best assembled from codepoints at
        // runtime (char.ConvertFromUtf32) so source files stay ASCII.
        Assert.IsTrue(SafeGlyphs.IsSafeCodepoint(0x2713));  // CHECK MARK
        Assert.IsTrue(SafeGlyphs.IsSafeCodepoint(0x2192, ExtraCodepoints));
        Assert.IsFalse(SafeGlyphs.IsSafeCodepoint(0x1F4CE)); // emoji plane
    }
}
```

This package runs the same audit over its own shipped sources as part
of its test suite.

### 6. Diagnostics: what resolved, and why

Open **Window > UITK Font Fix > Diagnostics** for a read-only report
with *Re-probe* (drops caches, resolves again) and *Copy report*
buttons. The same report is available from code -- for example as part
of a bug-report bundle:

```csharp
using Colloid.UitkFontFix;
using UnityEngine;

public static class SupportBundle
{
    public static void LogFontReport()
    {
        // Plain-text ASCII report: which candidates resolved (and from
        // which tier), CJK atlas state, candidate availability on this
        // machine, and the known-trap checklist. Never throws; safe in
        // batch mode.
        Debug.Log(FontFixDiagnostics.BuildReport());
    }
}
```

Reading the report:

```
-- Resolution --
mono   : editor:Fonts/RobotoMono/RobotoMono-Regular.ttf (RobotoMono-Regular)
cjk-ui : osasset:Yu Gothic UI (Yu Gothic UI)

-- Environment --
language   : Japanese (prefer CJK UI: yes)
cjk-ui candidates:
  [x] Yu Gothic UI
  [ ] Noto Sans CJK JP
```

Source prefixes: `editor:` = editor-bundled TTF, `os:` = OS font that
passed the face probe, `label` = default editor label font (last
resort), `osasset:` = DynamicOS `FontAsset` created from an installed
family, `(none)` = nothing resolved (apply helpers no-op).

## API reference

Everything lives in the `Colloid.UitkFontFix` namespace. All resolvers
cache their result, **never throw**, and are safe in batch mode.

### `FontFix` (static facade, editor assembly)

| Member | Behavior |
| --- | --- |
| `EditorMonoFont` | Resolved monospace `Font`: bundled RobotoMono first, face-probed single-name OS fonts second, default label font last. Cached; effectively never null in a functioning editor. |
| `EditorMonoFontSource` | Which mono candidate won: `"editor:<path>"`, `"os:<name>"`, `"label"`, or empty. |
| `CjkUiFontAsset` | Latin+CJK `FontAsset` (DynamicOS mode) from the first installed candidate, or null when none resolves. Glyphs populate lazily at render time; the transient asset is destroyed before every domain reload. |
| `CjkUiFontSource` | Which CJK candidate won: `"osasset:<name>"`, or empty. |
| `ApplyMono(VisualElement)` | Inline `unityFontDefinition` assignment on one **leaf**; survives any ancestor `ApplyCjkUi`. No-ops (keeps the inherited font) on null or when nothing resolved. |
| `ApplyCjkUi(VisualElement)` | Font assignment on a **container root** that descendants inherit. No-ops on null or when nothing resolved -- callers must not assume the font changed. |
| `ShouldPreferCjkUi(SystemLanguage)` | Pure policy: true for Japanese, Chinese (all variants) and Korean. |
| `SanitizeDisplayText(string)` | Lossless display hygiene: strips every variation selector (including ideographic ones), zero-width characters, the BOM and emoji tag characters. Returns the same instance when clean, `string.Empty` for null; never removes a character that draws its own glyph. |
| `SanitizeDisplayText(string, string)` | **Lossy** overload: the strip above, then every non-BMP codepoint and unpaired surrogate becomes the given replacement (the parameter is the opt-in). Strip-then-replace order is a documented guarantee. |
| `ResetCaches()` | Drops every cached resolution (destroying package-owned transient objects) so the next access re-probes. |

### `FontFixSettings` (static configuration, editor assembly)

Code-first candidate configuration. A value that actually changes
invalidates the `FontFix` caches; assigning an equal value keeps them
warm. Assigning null to any property restores that property's default.

| Member | Behavior |
| --- | --- |
| `EditorMonoFontPaths` | `EditorGUIUtility.Load` paths tried for the bundled mono TTF. Empty array disables the tier. |
| `OsMonoFontNames` | OS monospace family names, probed one at a time. Empty array disables the tier. |
| `CjkUiFontNames` | Latin+CJK family names, probed one at a time, most preferred first. Empty array disables CJK resolution entirely (`ApplyCjkUi` then no-ops). |
| `CjkUiStyleName` | Style name passed to `FontAsset.CreateFontAsset` (default `"Regular"`). Null/empty restores the default. |
| `ResetToDefaults()` | Restores every property; only invalidates caches when something actually changed (safe in test teardown). |

### Runtime utilities (runtime assembly)

**`TextSanitizer`** -- pure display-text hygiene; never throws, never
allocates on the clean fast path, maps null to `string.Empty`.

| Member | Behavior |
| --- | --- |
| `StripVariationSelectors(string)` | Removes every Unicode variation selector: U+FE00..U+FE0F, Mongolian FVS U+180B..U+180D and U+180F, and ideographic selectors U+E0100..U+E01EF (matched only as valid surrogate pairs). The shaping-safe granular op -- never touches ZWJ/ZWNJ. |
| `StripInvisibleCharacters(string)` | Superset of the above: also removes zero-width space/non-joiner/joiner (U+200B..U+200D), word joiner and invisible math operators (U+2060..U+2064), the BOM (U+FEFF) and emoji tag characters (U+E0000..U+E007F). This is what `FontFix.SanitizeDisplayText` forwards to. |
| `ReplaceNonBmpCharacters(string, string)` | **Lossy**: replaces each supplementary-plane codepoint (one replacement per surrogate pair) and each unpaired surrogate with the replacement string; the result contains no surrogate code units. For surfaces that must stay strictly BMP. |

**`CjkLanguage`**

| Member | Behavior |
| --- | --- |
| `ShouldPreferCjkUi(SystemLanguage)` | Pure ja/zh/ko policy. Takes the language as a parameter on purpose: reading `Application.systemLanguage` during serialization throws (see Recipe 2). |

**`SafeGlyphs`**

| Member | Behavior |
| --- | --- |
| `DefaultCodepoints` | Read-only whitelist of non-ASCII BMP codepoints verified to render in the 2022.3 editor font (bullets, arrows, check marks, text-presentation gear/warning, etc.). |
| `IsSafeCodepoint(int)` | True for printable ASCII or a whitelist member. |
| `IsSafeCodepoint(int, int[])` | Same, plus a project-specific extension list (extend only after verifying editor-font coverage). |

**`FontFixDefaults`**

| Member | Behavior |
| --- | --- |
| `EditorMonoFontPaths` | Default bundled-mono probe paths (RobotoMono). |
| `OsMonoFontNames` | Default OS mono candidates (Consolas, Menlo, DejaVu Sans Mono, Courier New). |
| `CjkUiFontNames` | Default Latin+CJK chain (Yu Gothic UI > Yu Gothic > Meiryo UI > Meiryo > Noto Sans CJK JP > MS UI Gothic). |
| `CjkUiStyleName` | `"Regular"`. |

Treat the arrays as read-only; customize through `FontFixSettings`.

### Editor helpers

**`GlyphAudit`** -- source-level glyph safety audit, usable from
consumer test assemblies (see Recipe 5).

| Member | Behavior |
| --- | --- |
| `PackageRootPath()` | Root directory of this package's own sources (embedded, junction and package-cache installs), or null. |
| `AuditSourceDirectory(string, int[])` | Runs every audit over all `*.cs` under a directory (recursive); returns `"file: reason"` offenders. A missing directory yields one offender instead of throwing, so a moved path fails a test visibly. |
| `AuditSourceFile(string, int[])` | Every audit over one file. |
| `AuditConstructedCodepoints(string, string, int[])` | Flags codepoints constructed in source outside the whitelist. `(char)` casts of U+FE0F/U+FE0E/U+FEFF are exempt: sanitizers legitimately compare against those values, and a comparison never renders. |
| `AuditVariationSelectors(string, string)` | Flags literal U+FE0F/U+FE0E and their escape forms. Ordinal scans on purpose -- culture-sensitive searches treat selectors as collation-ignorable. |
| `AuditAsciiOnly(string, byte[])` | Flags the first non-ASCII byte per file (strict-ASCII source policy keeps glyph content reviewable and diff-safe). |

**`FontFixDiagnostics`**

| Member | Behavior |
| --- | --- |
| `BuildReport()` | Plain-text ASCII report: resolution results, CJK atlas page state, candidate availability, known-trap checklist. Never throws; batch-safe. |

**`FontFixDiagnosticsWindow`**

| Member | Behavior |
| --- | --- |
| `Open()` / **Window > UITK Font Fix > Diagnostics** | Read-only diagnostics window with *Re-probe* and *Copy report*; dogfoods the package's own composition (CJK root, mono report body). |

## Verified behavior (why this design)

The following facts about Unity 2022.3 TextCore / UI Toolkit were
established empirically, in both interactive and batch editors
(primarily on Windows; the headless subset re-verified on Linux). Every
non-obvious design choice above traces back to one of them.

1. `FontEngine.LoadFontFace(Font)` returns `Invalid_File` for **every**
   OS dynamic font (`Font.CreateDynamicFontFromOSFont`). OS fonts
   cannot be validated on the `Font` route -- which is why this package
   never hands an OS `Font` to UI Toolkit.
2. The working OS-font route is
   `TextCore.Text.FontAsset.CreateFontAsset(family, style)` (DynamicOS
   mode) assigned via `FontDefinition`. Glyphs are fetched lazily at
   render time (`HasCharacter` returning false right after creation is
   normal), and creation works headless.
3. `Font.CreateDynamicFontFromOSFont` with a **name array** produces an
   unloadable face ("Unable to load font face" / "Can't Generate Mesh")
   and text renders empty. Single names do not pass face validation
   either; the route is avoided entirely.
4. `EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf")`
   resolves on 2022.3 -- a real TTF asset TextCore always accepts,
   hence the preferred mono candidate.
5. USS cannot select OS fonts by name; font application happens from C#
   via `style.unityFontDefinition`. Inline styles beat inherited
   values, which is what makes the root/leaf composition reliable.
6. The editor default font (Inter) has no CJK coverage. Per-glyph OS
   fallback mixes fonts and weights, rendering CJK text "patchy bold".
   One explicit Latin+CJK font on the container root removes the
   fallback path. Verified working chain: Yu Gothic UI > Yu Gothic >
   Meiryo UI > Meiryo > Noto Sans CJK JP > MS UI Gothic.
7. `Font.GetOSInstalledFontNames()` reports **English** family names
   even on localized (e.g. Japanese) Windows -- which is why the
   candidate defaults are English names.
8. The `<mark>` rich-text tag is incompatible with DynamicOS
   FontAssets: mark quads draw above or below glyphs depending on their
   atlas page, producing patchy bold / faded runs (or fully hidden text
   with opaque marks). Atlas prefill cannot fix it (ASCII alone spans 4
   pages; CJK can never be single-page). Do not combine `<mark>` with
   these fonts.
9. Emoji-plane characters (e.g. U+1F4CE) and variation selectors
   (U+FE0F, which models and users routinely emit) have no glyph in the
   editor fonts: placeholder squares plus per-draw console warning
   spam. Free-form text goes through `SanitizeDisplayText`; fixed UI
   strings stay inside the `SafeGlyphs` whitelist.
10. `Application.systemLanguage` throws when read from ScriptableObject
    constructors or field initializers. Query it in `OnEnable` or later
    and pass the value to `ShouldPreferCjkUi`.
11. In batch mode, OS dynamic font **face** operations all fail while
    DynamicOS FontAsset **creation** works. The test suite is built
    around exactly this split.

## Unity version compatibility

The primary target is **Unity 2022.3 LTS** -- every item above was
verified there. The package also compiles and passes its EditMode suite
headless on Linux editors (CJK resolution depends on installed OS
fonts, so it correctly reports "none resolved" on machines without a
CJK candidate).

All version-sensitive TextCore/UI Toolkit calls are concentrated in a
single internal seam (`FontShims`). Verification against the
UnityCsReference sources: `FontDefinition.FromSDFFont(FontAsset)`,
`FontAsset.CreateFontAsset(family, style)`, `atlasPopulationMode` and
`atlasTextures` are identical across the 2022.3, 2023.2 and 6000.0
branches, and no `FromSDFFontAsset` rename exists in any of them.
Should such a rename ever ship in a future Unity version, define
`UITK_FONT_FIX_FROMSDFFONTASSET` (Scripting Define Symbols, or
`versionDefines` on the package asmdef) to flip the call site --
callers never change.

## Samples

Two importable samples ship with the package (Package Manager > UITK
Font Fix > *Samples*):

- **Editor Font Setup** -- a complete `EditorWindow` demonstrating the
  container/leaf composition, safe language gating and display-text
  sanitization. Import, open its window from the *Window* menu, and use
  it as a starting point for your own tool windows.
- **Glyph Audit Tests** -- a copy-ready EditMode test file that wires
  `GlyphAudit` and `SafeGlyphs` into your own project sources. Adjust
  the scanned directory and the extra-codepoint whitelist to match your
  project.

## License

MIT -- see [LICENSE.md](LICENSE.md).
