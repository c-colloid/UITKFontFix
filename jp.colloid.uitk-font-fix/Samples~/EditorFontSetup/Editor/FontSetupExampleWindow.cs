using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Colloid.UitkFontFix;

namespace UitkFontFixSamples.EditorFontSetup
{
    /// <summary>
    /// Sample EditorWindow demonstrating the canonical UITK Font Fix
    /// composition:
    ///
    ///   1. Language-gated FontFix.ApplyCjkUi on the WINDOW ROOT, so
    ///      every descendant inherits one Latin+CJK font (no patchy
    ///      per-glyph OS fallback).
    ///   2. FontFix.ApplyMono on a code-block LEAF inside that root.
    ///      ApplyMono writes an INLINE style, and an inline style always
    ///      beats an inherited value, so the code block stays monospaced
    ///      no matter which Apply* call ran first.
    ///   3. FontFix.SanitizeDisplayText on free-form (model/user) text
    ///      before it reaches a Label, so codepoints the editor font
    ///      cannot draw (e.g. the variation selector U+FE0F) never hit
    ///      the renderer.
    ///
    /// Open it via Window > UITK Font Fix > Samples > Font Setup Example.
    ///
    /// This file lives in an Editor/ folder and deliberately has NO
    /// asmdef: the imported copy compiles into Assembly-CSharp-Editor,
    /// which automatically references the package assemblies because
    /// they are autoReferenced (details in the sample README).
    /// </summary>
    public class FontSetupExampleWindow : EditorWindow
    {
        // NEVER read Application.systemLanguage from a field initializer
        // here. EditorWindow derives from ScriptableObject, and
        // ScriptableObject field initializers / constructors run during
        // serialization, where get_systemLanguage throws:
        //
        //   UnityException: get_systemLanguage is not allowed to be
        //   called during serialization
        //
        // In a real incident that exact pattern broke a settings
        // singleton load and took the whole UI down with it. Query the
        // language in CreateGUI (or OnEnable) instead -- see below.
        //
        //   // WRONG -- throws during serialization:
        //   // private bool _preferCjk =
        //   //     FontFix.ShouldPreferCjkUi(Application.systemLanguage);

        [MenuItem("Window/UITK Font Fix/Samples/Font Setup Example")]
        public static void Open()
        {
            FontSetupExampleWindow window =
                GetWindow<FontSetupExampleWindow>();
            window.titleContent = new GUIContent("Font Setup Example");
            window.minSize = new Vector2(480f, 380f);
            window.Show();
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 6f;
            root.style.paddingBottom = 6f;

            // -- Customizing resolution (usually NOT needed) --------------
            // FontFixSettings overrides the candidate lists code-first.
            // A value that actually CHANGES invalidates the FontFix
            // caches so the next access re-probes; re-assigning an equal
            // value keeps the caches warm; assigning null restores that
            // property's default. No FontFix.ResetCaches() call is
            // needed after an override -- the assignment itself
            // invalidates. In a real project put overrides in an
            // [InitializeOnLoad] bootstrap so every window agrees on the
            // same candidates. Example for a Simplified-Chinese-first
            // product:
            //
            // FontFixSettings.CjkUiFontNames = new string[]
            // {
            //     "Microsoft YaHei UI",
            //     "Microsoft YaHei",
            //     "Noto Sans CJK SC",
            //     "Yu Gothic UI"
            // };

            // -- 1. Language gate + container-root CJK font ---------------
            // Safe timing: CreateGUI runs well after serialization, so
            // reading Application.systemLanguage HERE is allowed (see
            // the field-initializer warning above).
            SystemLanguage language = Application.systemLanguage;
            if (FontFix.ShouldPreferCjkUi(language))
            {
                // Container root: descendants INHERIT the Latin+CJK
                // font. The editor default font (Inter) has no CJK
                // coverage; without this call CJK glyphs come from
                // per-glyph OS fallback, which mixes fonts and weights
                // and renders CJK text patchy bold next to Latin text.
                FontFix.ApplyCjkUi(root);
            }

            root.Add(BuildHeader(language));
            root.Add(BuildCodeBlock());
            root.Add(BuildSanitizedTextSection());
            root.Add(BuildFooter());
        }

        // -- 1b. Inherited CJK text ---------------------------------------

        private static VisualElement BuildHeader(SystemLanguage language)
        {
            var section = new VisualElement();

            var title = new Label("UITK Font Fix -- composition sample");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4f;
            section.Add(title);

            // Demo text is BUILT FROM CODEPOINTS at runtime so this
            // source file stays strictly ASCII -- the same policy the
            // "Glyph Audit Tests" sample enforces for your own sources.
            // It reads "nihongo hyouji tesuto" (Japanese display test).
            // The label carries no inline font, so it inherits the root
            // font: on a ja/zh/ko machine it renders in one consistent
            // CJK font instead of patchy per-glyph fallback.
            var cjkDemo = new Label(FromCodepoints(new int[]
            {
                0x65E5, 0x672C, 0x8A9E, // nihongo (Japanese language)
                0x8868, 0x793A,         // hyouji  (display)
                0x30C6, 0x30B9, 0x30C8  // tesuto  (test, in katakana)
            }));
            section.Add(cjkDemo);

            var gate = new Label(
                "system language: " + language
                + "  |  prefer CJK UI: "
                + (FontFix.ShouldPreferCjkUi(language) ? "yes" : "no"));
            gate.style.marginBottom = 4f;
            section.Add(gate);
            return section;
        }

        // -- 2. Monospace leaf INSIDE the CJK container -------------------

        private static VisualElement BuildCodeBlock()
        {
            var box = new VisualElement();
            box.style.marginTop = 6f;
            box.style.paddingLeft = 6f;
            box.style.paddingRight = 6f;
            box.style.paddingTop = 4f;
            box.style.paddingBottom = 4f;
            box.style.backgroundColor = new Color(0f, 0f, 0f, 0.2f);

            // ApplyMono targets exactly ONE leaf element. It writes an
            // inline style.unityFontDefinition, and inline ALWAYS beats
            // the value inherited from the ApplyCjkUi root above -- the
            // call order does not matter. That asymmetry is the whole
            // composition rule: ApplyCjkUi on container roots, ApplyMono
            // on code leaves. Never call both on the SAME element; both
            // are inline writes and the last one would win.
            var code = new Label(
                "// monospace leaf inside the CJK container\n"
                + "mono   source: "
                + Describe(FontFix.EditorMonoFontSource) + "\n"
                + "cjk-ui source: "
                + Describe(FontFix.CjkUiFontSource));
            FontFix.ApplyMono(code);
            box.Add(code);
            return box;
        }

        // -- 3. Sanitized dynamic text ------------------------------------

        private static VisualElement BuildSanitizedTextSection()
        {
            var section = new VisualElement();
            section.style.marginTop = 8f;

            var heading = new Label("Sanitized dynamic text");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            section.Add(heading);

            // Free-form text (model output, user input, log lines)
            // routinely carries the emoji variation selector U+FE0F
            // after symbols. Editor fonts have no glyph for the selector
            // itself: every draw logs a "not found in [...]" warning and
            // renders a placeholder square. Route EVERY dynamic string
            // through FontFix.SanitizeDisplayText before display; it is
            // pure, never throws, and returns the SAME instance when the
            // text is already clean (fast path).
            //
            // The demo value is constructed at runtime: WARNING SIGN
            // (U+26A0, in the SafeGlyphs whitelist) followed by the
            // selector. This keeps the file ASCII and still passes the
            // glyph audit -- (char) casts of the selector values are
            // exempt there because comparing/removing never renders.
            string raw = "Build finished "
                + char.ConvertFromUtf32(0x26A0) + (char)0xFE0F;

            var input = new TextField("Raw text (paste anything)")
            {
                value = raw
            };
            section.Add(input);

            var display = new Label();
            var status = new Label();
            status.style.opacity = 0.7f;
            UpdateSanitized(display, status, raw);
            input.RegisterValueChangedCallback(evt =>
            {
                UpdateSanitized(display, status, evt.newValue);
            });
            section.Add(display);
            section.Add(status);
            return section;
        }

        private static void UpdateSanitized(
            Label display, Label status, string raw)
        {
            string clean = FontFix.SanitizeDisplayText(raw);
            display.text = "display: " + clean;
            int removed = (raw != null ? raw.Length : 0) - clean.Length;
            status.text = removed > 0
                ? "sanitizer removed " + removed + " char(s)"
                : "already display-safe (same instance returned)";
        }

        // -- 4. Diagnostics pointer ---------------------------------------

        private static VisualElement BuildFooter()
        {
            var section = new VisualElement();
            section.style.marginTop = 10f;

            var hint = new Label(
                "Wondering which candidates resolved on this machine? "
                + "Window > UITK Font Fix > Diagnostics shows the "
                + "winning candidates, the CJK atlas state and the "
                + "known-trap checklist.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 4f;
            section.Add(hint);

            var openDiagnostics = new Button(FontFixDiagnosticsWindow.Open)
            {
                text = "Open diagnostics window"
            };
            openDiagnostics.style.alignSelf = Align.FlexStart;
            section.Add(openDiagnostics);
            return section;
        }

        // -- Helpers ------------------------------------------------------

        private static string FromCodepoints(int[] codepoints)
        {
            var sb = new System.Text.StringBuilder(codepoints.Length);
            for (int i = 0; i < codepoints.Length; i++)
            {
                sb.Append(char.ConvertFromUtf32(codepoints[i]));
            }
            return sb.ToString();
        }

        private static string Describe(string source)
        {
            return string.IsNullOrEmpty(source)
                ? "(none resolved)"
                : source;
        }
    }
}
