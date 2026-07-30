using NUnit.Framework;
using UnityEngine;

namespace Colloid.UitkFontKit.Tests
{
    /// <summary>
    /// Guards the diagnostics report builder (batch-safe: the report
    /// must never throw even when nothing resolves) and smoke-tests the
    /// window type without showing it (headless safe).
    /// </summary>
    public class DiagnosticsTests
    {
        [TearDown]
        public void TearDown()
        {
            FontKitSettings.ResetToDefaults();
        }

        [Test]
        public void BuildReport_NeverThrows_AndContainsSections()
        {
            string report = null;
            Assert.DoesNotThrow(delegate
            {
                report = FontKitDiagnostics.BuildReport();
            });
            Assert.IsNotNull(report);
            StringAssert.Contains("-- Resolution --", report);
            StringAssert.Contains("-- CJK atlas --", report);
            StringAssert.Contains("-- Environment --", report);
            StringAssert.Contains("-- Known traps", report);
            StringAssert.Contains("mono   : ", report);
            StringAssert.Contains("cjk-ui : ", report);
            Debug.Log("[DiagnosticsTests]\n" + report);
        }

        [Test]
        public void BuildReport_IsStrictAscii()
        {
            string report = FontKitDiagnostics.BuildReport();
            // Kit-authored report content must stay ASCII so it can be
            // pasted anywhere. Resolved OBJECT names (Font.name /
            // FontAsset.name) are assigned by the engine/OS and are
            // deliberately surfaced as-is, so they are excluded from the
            // guard rather than constrained by it.
            report = RemoveEngineAssignedName(report,
                FontKit.EditorMonoFont != null ? FontKit.EditorMonoFont.name : null);
            report = RemoveEngineAssignedName(report,
                FontKit.CjkUiFontAsset != null ? FontKit.CjkUiFontAsset.name : null);
            for (int i = 0; i < report.Length; i++)
            {
                Assert.LessOrEqual((int)report[i], 0x7F,
                    "non-ASCII char at index " + i + ": U+"
                    + ((int)report[i]).ToString("X4"));
            }
        }

        private static string RemoveEngineAssignedName(string report, string name)
        {
            // string.Replace throws on an empty search value, and a
            // nameless object has nothing to remove anyway.
            return string.IsNullOrEmpty(name) ? report : report.Replace(name, "");
        }

        [Test]
        public void BuildReport_WithNoCandidates_ReportsNone()
        {
            FontKitSettings.CjkUiFontNames = new string[0];
            string report = FontKitDiagnostics.BuildReport();
            StringAssert.Contains("cjk-ui : (none)", report);
            StringAssert.Contains("(no CJK UI FontAsset resolved)", report);
            StringAssert.Contains("(none configured)", report);
        }

        [Test]
        public void DiagnosticsWindow_CreateAndDestroy_NeverThrows()
        {
            // Smoke test only: instantiating the window type must be safe
            // headless. Showing/docking it is interactive-editor behavior
            // covered by the on-device probe checklist.
            FontKitDiagnosticsWindow window = null;
            Assert.DoesNotThrow(delegate
            {
                window = ScriptableObject.CreateInstance<FontKitDiagnosticsWindow>();
            });
            Assert.IsNotNull(window);
            Object.DestroyImmediate(window);
        }
    }
}
