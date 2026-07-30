using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Read-only diagnostics window (Window > UITK Font Fix >
    /// Diagnostics): shows which font candidates resolved, the CJK
    /// atlas state and the known-trap checklist. Dogfoods the kit --
    /// the window root gets ApplyCjkUi when the system language wants
    /// it, and the report body gets ApplyMono.
    /// </summary>
    public class FontFixDiagnosticsWindow : EditorWindow
    {
        [MenuItem("Window/UITK Font Fix/Diagnostics")]
        public static void Open()
        {
            var window = GetWindow<FontFixDiagnosticsWindow>();
            window.titleContent = new GUIContent("Font Fix Diagnostics");
            window.minSize = new Vector2(440f, 320f);
            window.Show();
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            // Safe timing: CreateGUI runs well after serialization, so
            // reading the system language here is allowed (never do this
            // from a ScriptableObject constructor/field initializer).
            if (FontFix.ShouldPreferCjkUi(Application.systemLanguage))
            {
                FontFix.ApplyCjkUi(root);
            }

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginTop = 4f;
            toolbar.style.marginBottom = 4f;
            toolbar.style.marginLeft = 4f;
            toolbar.style.marginRight = 4f;

            var refreshButton = new Button(OnRefreshClicked)
            {
                text = "Re-probe"
            };
            var copyButton = new Button(OnCopyClicked)
            {
                text = "Copy report"
            };
            toolbar.Add(refreshButton);
            toolbar.Add(copyButton);
            root.Add(toolbar);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;

            var report = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = FontFixDiagnostics.BuildReport()
            };
            FontFix.ApplyMono(report);
            scroll.Add(report);
            root.Add(scroll);
        }

        private void OnRefreshClicked()
        {
            FontFix.ResetCaches();
            Rebuild();
        }

        private void OnCopyClicked()
        {
            EditorGUIUtility.systemCopyBuffer = FontFixDiagnostics.BuildReport();
        }
    }
}
