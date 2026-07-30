using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Colloid.UitkFontKit
{
    /// <summary>
    /// Read-only diagnostics window (Window > UITK Font Kit >
    /// Diagnostics): shows which font candidates resolved, the CJK
    /// atlas state and the known-trap checklist. Dogfoods the kit --
    /// the window root gets ApplyCjkUi when the system language wants
    /// it, and the report body gets ApplyMono.
    /// </summary>
    public class FontKitDiagnosticsWindow : EditorWindow
    {
        [MenuItem("Window/UITK Font Kit/Diagnostics")]
        public static void Open()
        {
            var window = GetWindow<FontKitDiagnosticsWindow>();
            window.titleContent = new GUIContent("Font Kit Diagnostics");
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
            if (FontKit.ShouldPreferCjkUi(Application.systemLanguage))
            {
                FontKit.ApplyCjkUi(root);
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
                value = FontKitDiagnostics.BuildReport()
            };
            FontKit.ApplyMono(report);
            scroll.Add(report);
            root.Add(scroll);
        }

        private void OnRefreshClicked()
        {
            FontKit.ResetCaches();
            Rebuild();
        }

        private void OnCopyClicked()
        {
            EditorGUIUtility.systemCopyBuffer = FontKitDiagnostics.BuildReport();
        }
    }
}
