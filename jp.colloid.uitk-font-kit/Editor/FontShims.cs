using UnityEngine.UIElements;

namespace Colloid.UitkFontKit
{
    /// <summary>
    /// Single seam for TextCore/UI Toolkit API differences between Unity
    /// majors. Keep EVERY version-sensitive call in this file so a future
    /// rename costs a one-file change.
    ///
    /// Research record (2026-07-30): FontDefinition.FromSDFFont(
    /// TextCore.Text.FontAsset) is documented from 2022.1 through the
    /// 6000.x Scripting API; a rename to "FromSDFFontAsset" could NOT be
    /// confirmed in any official documentation. The reserved define below
    /// exists so that if such a rename ever ships, consumers (or this
    /// package's versionDefines) can flip the call site without editing
    /// callers. See docs/design-notes/2026-07-30-uitk-font-kit-
    /// architecture.md in the repository for the full evaluation.
    /// </summary>
    internal static class FontShims
    {
        /// <summary>
        /// Wraps a TextCore FontAsset into a FontDefinition for
        /// style.unityFontDefinition assignment.
        /// </summary>
        internal static FontDefinition DefinitionFromFontAsset(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
#if UITK_FONT_KIT_FROMSDFFONTASSET
            return FontDefinition.FromSDFFontAsset(asset);
#else
            return FontDefinition.FromSDFFont(asset);
#endif
        }

        /// <summary>
        /// FontAsset.CreateFontAsset(familyName, styleName) creates a
        /// DynamicOS-mode FontAsset whose glyphs are fetched lazily at
        /// render time -- HasCharacter returning false right after
        /// creation is NORMAL for this mode. This is the ONLY supported
        /// route from an OS font to UI Toolkit: FontEngine.LoadFontFace(
        /// Font) returns Invalid_File for every OS dynamic Font on 2022.3
        /// (batch and interactive alike), so Font-based OS routes cannot
        /// be validated and must not be used. Works headless (batch mode)
        /// as well. Returns null instead of throwing.
        /// </summary>
        internal static UnityEngine.TextCore.Text.FontAsset TryCreateOsFontAsset(
            string familyName, string styleName)
        {
            try
            {
                return UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(
                    familyName, styleName);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
