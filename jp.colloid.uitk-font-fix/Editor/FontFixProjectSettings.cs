using System.IO;
using UnityEditor;
using UnityEngine;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Serialized form of the configurable values. Null arrays/strings
    /// mean "field absent in the file" and are skipped on apply, so a
    /// truncated or older-schema file can never wipe settings; note the
    /// EMPTY string in cjkUiBoldStyleName is meaningful (wiring
    /// disabled) and round-trips as such.
    /// </summary>
    [System.Serializable]
    internal class FontFixSettingsData
    {
        public int schemaVersion = 1;
        public string[] editorMonoFontPaths;
        public string[] osMonoFontNames;
        public string[] cjkUiFontNames;
        public string cjkUiStyleName;
        public string cjkUiBoldStyleName;
    }

    /// <summary>
    /// Project-shared persistence for FontFixSettings: a JSON snapshot
    /// of all five configurable values at
    /// ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json
    /// (versioned with the project, the location Unity's own packages
    /// use). The file's existence means "this project pins its font
    /// configuration"; deleting it returns the project to package
    /// defaults.
    ///
    /// Load order and precedence: the file is applied once per domain
    /// load (InitializeOnLoadMethod). Consumer code that assigns
    /// FontFixSettings from its own InitializeOnLoadMethod bootstrap
    /// TYPICALLY runs later and therefore wins -- Unity customarily
    /// initializes a referenced assembly (this package) before its
    /// dependents, but that ordering is convention, not a documented
    /// guarantee. The diagnostics report shows when the effective
    /// values have drifted from the file, whichever side caused it.
    /// </summary>
    public static class FontFixProjectSettings
    {
        /// <summary>Project-relative location of the settings file.</summary>
        public const string RelativeFilePath =
            "ProjectSettings/Packages/jp.colloid.uitk-font-fix/settings.json";

        /// <summary>
        /// Absolute path of the settings file, anchored to THIS
        /// project's root via Application.dataPath rather than the
        /// process working directory (which batch invocations may
        /// leave elsewhere).
        /// </summary>
        public static string FilePath
        {
            get
            {
                try
                {
                    return Path.GetFullPath(Path.Combine(
                        Application.dataPath, "..", RelativeFilePath));
                }
                catch (System.Exception)
                {
                    return RelativeFilePath;
                }
            }
        }

        /// <summary>True when the project pins its font configuration.</summary>
        public static bool Exists
        {
            get
            {
                try
                {
                    return File.Exists(FilePath);
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        [InitializeOnLoadMethod]
        private static void LoadOnEditorStart()
        {
            TryLoadAndApply();
        }

        /// <summary>
        /// Applies the settings file to FontFixSettings when it exists
        /// and parses. Returns true only when a file was found and
        /// applied. Never throws; a corrupt file is ignored.
        /// </summary>
        public static bool TryLoadAndApply()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return false;
                }
                FontFixSettingsData data =
                    JsonUtility.FromJson<FontFixSettingsData>(
                        File.ReadAllText(FilePath));
                if (data == null)
                {
                    return false;
                }
                ApplyData(data);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes the CURRENT effective FontFixSettings values to the
        /// settings file (creating the directory as needed). Returns
        /// false instead of throwing when the write fails.
        /// </summary>
        public static bool Save()
        {
            try
            {
                string path = FilePath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                // Write-then-swap so a concurrent reader (second editor
                // instance, live diagnostics refresh) can never observe
                // a torn file.
                string temp = path + ".tmp";
                File.WriteAllText(temp,
                    JsonUtility.ToJson(CaptureData(), true) + "\n");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Deletes the settings file so the project tracks package
        /// defaults again. Does NOT change the in-memory settings;
        /// callers wanting a full reset also call
        /// FontFixSettings.ResetToDefaults(). Never throws.
        /// </summary>
        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (System.Exception)
            {
                // Best effort; the diagnostics report shows the state.
            }
        }

        /// <summary>
        /// True when the file exists and matches the current effective
        /// settings -- i.e. nothing (code or UI) changed values since
        /// the file was applied or saved. Used by the diagnostics
        /// report to surface drift.
        /// </summary>
        public static bool MatchesCurrentSettings()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return false;
                }
                FontFixSettingsData data =
                    JsonUtility.FromJson<FontFixSettingsData>(
                        File.ReadAllText(FilePath));
                if (data == null)
                {
                    return false;
                }
                // Absent (null/empty) fields are skipped by ApplyData,
                // so they must be skipped here too -- otherwise a
                // hand-edited or older-schema file that omits a field
                // would report drift forever, even right after loading.
                return (data.editorMonoFontPaths == null
                        || SameList(data.editorMonoFontPaths, FontFixSettings.EditorMonoFontPaths))
                    && (data.osMonoFontNames == null
                        || SameList(data.osMonoFontNames, FontFixSettings.OsMonoFontNames))
                    && (data.cjkUiFontNames == null
                        || SameList(data.cjkUiFontNames, FontFixSettings.CjkUiFontNames))
                    && (string.IsNullOrEmpty(data.cjkUiStyleName)
                        || data.cjkUiStyleName == FontFixSettings.CjkUiStyleName)
                    && (data.cjkUiBoldStyleName == null
                        || data.cjkUiBoldStyleName == FontFixSettings.CjkUiBoldStyleName);
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        internal static FontFixSettingsData CaptureData()
        {
            var data = new FontFixSettingsData();
            data.editorMonoFontPaths = FontFixSettings.EditorMonoFontPaths;
            data.osMonoFontNames = FontFixSettings.OsMonoFontNames;
            data.cjkUiFontNames = FontFixSettings.CjkUiFontNames;
            data.cjkUiStyleName = FontFixSettings.CjkUiStyleName;
            data.cjkUiBoldStyleName = FontFixSettings.CjkUiBoldStyleName;
            return data;
        }

        internal static void ApplyData(FontFixSettingsData data)
        {
            // One batch for the whole file: subscribers to
            // FontFix.CachesInvalidated get a single notification with
            // every field applied, not one per field.
            FontFix.BeginSettingsBatch();
            try
            {
                ApplyDataFields(data);
            }
            finally
            {
                FontFix.EndSettingsBatch();
            }
        }

        private static void ApplyDataFields(FontFixSettingsData data)
        {
            // Null fields (absent in the file) are skipped; assignment
            // through FontFixSettings keeps the value-compare cache
            // invalidation semantics.
            if (data.editorMonoFontPaths != null)
            {
                FontFixSettings.EditorMonoFontPaths = data.editorMonoFontPaths;
            }
            if (data.osMonoFontNames != null)
            {
                FontFixSettings.OsMonoFontNames = data.osMonoFontNames;
            }
            if (data.cjkUiFontNames != null)
            {
                FontFixSettings.CjkUiFontNames = data.cjkUiFontNames;
            }
            if (!string.IsNullOrEmpty(data.cjkUiStyleName))
            {
                FontFixSettings.CjkUiStyleName = data.cjkUiStyleName;
            }
            if (data.cjkUiBoldStyleName != null)
            {
                // Empty string is meaningful here: wiring disabled.
                FontFixSettings.CjkUiBoldStyleName = data.cjkUiBoldStyleName;
            }
        }

        private static bool SameList(string[] a, string[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], System.StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
