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
            for (int i = 0; i < report.Length; i++)
            {
                // Font names and OS candidate lists are our own ASCII
                // configuration; the report as a whole must stay ASCII so
                // it can be pasted anywhere (except when an OS reports a
                // localized font name, which we surface as-is -- hence
                // this guard runs with default settings only).
                Assert.LessOrEqual((int)report[i], 0x7F,
                    "non-ASCII char at index " + i + ": U+"
                    + ((int)report[i]).ToString("X4"));
            }
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
