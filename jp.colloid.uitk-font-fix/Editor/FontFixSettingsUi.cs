using UnityEngine;
using UnityEngine.UIElements;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// The shared no-code configuration form used by BOTH the Project
    /// Settings pane and the diagnostics window's edit section -- one
    /// implementation so the two entry points can never drift apart.
    /// Candidate lists are edited as one name per line (robust, needs
    /// no explanation); Apply pushes into FontFixSettings for this
    /// session, Save also writes the project settings file, and Use
    /// package defaults resets everything and deletes the file.
    /// </summary>
    internal static class FontFixSettingsUi
    {
        internal static VisualElement CreateForm(System.Action onSettingsChanged)
        {
            var form = new VisualElement();

            TextField cjkNames = AddMultiline(form, "CJK UI font candidates",
                "One family name per line, most preferred first."
                + " English family names (the OS reports English names"
                + " even on localized Windows).");
            TextField cjkStyle = AddSingle(form, "CJK base face style");
            TextField cjkBold = AddSingle(form,
                "CJK bold face style (empty disables real-bold wiring)");
            TextField monoPaths = AddMultiline(form, "Editor mono font paths",
                "EditorGUIUtility.Load paths for the bundled mono TTF,"
                + " one per line.");
            TextField osMono = AddMultiline(form, "OS mono font candidates",
                "One family name per line.");

            var status = new Label();
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.opacity = 0.8f;
            status.style.marginTop = 4f;

            System.Action refresh = delegate
            {
                cjkNames.SetValueWithoutNotify(
                    string.Join("\n", FontFixSettings.CjkUiFontNames));
                cjkStyle.SetValueWithoutNotify(FontFixSettings.CjkUiStyleName);
                cjkBold.SetValueWithoutNotify(FontFixSettings.CjkUiBoldStyleName);
                monoPaths.SetValueWithoutNotify(
                    string.Join("\n", FontFixSettings.EditorMonoFontPaths));
                osMono.SetValueWithoutNotify(
                    string.Join("\n", FontFixSettings.OsMonoFontNames));
                status.text = BuildStatusText();
            };

            System.Action applyFields = delegate
            {
                FontFixSettings.CjkUiFontNames = SplitLines(cjkNames.value);
                FontFixSettings.CjkUiStyleName = cjkStyle.value;
                FontFixSettings.CjkUiBoldStyleName = cjkBold.value;
                FontFixSettings.EditorMonoFontPaths = SplitLines(monoPaths.value);
                FontFixSettings.OsMonoFontNames = SplitLines(osMono.value);
            };

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 4f;

            buttons.Add(new Button(delegate
            {
                applyFields();
                refresh();
                if (onSettingsChanged != null)
                {
                    onSettingsChanged();
                }
            }) { text = "Apply (this session)" });

            buttons.Add(new Button(delegate
            {
                applyFields();
                FontFixProjectSettings.Save();
                refresh();
                if (onSettingsChanged != null)
                {
                    onSettingsChanged();
                }
            }) { text = "Save to project" });

            buttons.Add(new Button(delegate
            {
                FontFixSettings.ResetToDefaults();
                FontFixProjectSettings.Delete();
                refresh();
                if (onSettingsChanged != null)
                {
                    onSettingsChanged();
                }
            }) { text = "Use package defaults" });

            form.Add(buttons);
            form.Add(status);
            refresh();
            return form;
        }

        private static string BuildStatusText()
        {
            string fileState;
            if (!FontFixProjectSettings.Exists)
            {
                fileState = "not saved to project (session only)";
            }
            else if (FontFixProjectSettings.MatchesCurrentSettings())
            {
                fileState = "saved to project (in sync)";
            }
            else
            {
                fileState = "project file exists but DIFFERS from the"
                    + " current values (edited here, or overridden by"
                    + " code after load)";
            }
            return "Resolution: mono = " + Describe(FontFix.EditorMonoFontSource)
                + ", cjk-ui = " + Describe(FontFix.CjkUiFontSource)
                + "\nProject file: " + fileState
                + "\nNote: open windows keep their current font; newly"
                + " built UI picks up changes.";
        }

        private static string Describe(string source)
        {
            return string.IsNullOrEmpty(source) ? "(none)" : source;
        }

        private static TextField AddSingle(VisualElement parent, string label)
        {
            var field = new TextField(label);
            parent.Add(field);
            return field;
        }

        private static TextField AddMultiline(
            VisualElement parent, string label, string hint)
        {
            var heading = new Label(label);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 6f;
            parent.Add(heading);
            var hintLabel = new Label(hint);
            hintLabel.style.whiteSpace = WhiteSpace.Normal;
            hintLabel.style.opacity = 0.7f;
            parent.Add(hintLabel);
            var field = new TextField { multiline = true };
            field.style.minHeight = 40f;
            parent.Add(field);
            return field;
        }

        internal static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new string[0];
            }
            string[] raw = text.Split('\n');
            var result = new System.Collections.Generic.List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string trimmed = raw[i].Trim().TrimEnd('\r');
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }
            return result.ToArray();
        }
    }
}
