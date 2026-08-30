using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Project Settings pane (Edit > Project Settings > UITK Font Fix):
    /// the team-shared, no-code way to pick fonts. Edits apply to the
    /// running session and "Save to project" persists them to
    /// FontFixProjectSettings.FilePath so the whole team gets them via
    /// version control. Application to UI still happens through the
    /// ApplyCjkUi/ApplyMono calls in each window.
    /// </summary>
    internal static class FontFixSettingsProviderRegistration
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            var provider = new SettingsProvider(
                "Project/UITK Font Fix", SettingsScope.Project);
            provider.label = "UITK Font Fix";
            provider.keywords = new System.Collections.Generic.HashSet<string>(
                new[] { "font", "uitk", "cjk", "japanese", "monospace" });
            provider.activateHandler = delegate (
                string searchContext, VisualElement rootElement)
            {
                if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
                {
                    FontFix.ApplyCjkUi(rootElement);
                }
                var container = new VisualElement();
                container.style.paddingLeft = 10f;
                container.style.paddingRight = 10f;
                container.style.paddingTop = 6f;

                var title = new Label("UITK Font Fix");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.fontSize = 19;
                title.style.marginBottom = 6f;
                container.Add(title);

                var intro = new Label(
                    "Font candidates for editor UIs built with this"
                    + " package. Changes affect every window that calls"
                    + " FontFix.ApplyCjkUi / ApplyMono; windows already"
                    + " open pick them up when rebuilt or reopened.");
                intro.style.whiteSpace = WhiteSpace.Normal;
                intro.style.marginBottom = 4f;
                container.Add(intro);

                container.Add(FontFixSettingsUi.CreateForm(null));

                var diagButton = new Button(FontFixDiagnosticsWindow.Open)
                {
                    text = "Open diagnostics window"
                };
                diagButton.style.alignSelf = Align.FlexStart;
                diagButton.style.marginTop = 6f;
                container.Add(diagButton);

                rootElement.Add(container);
            };
            return provider;
        }
    }
}
