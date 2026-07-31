using UnityEngine.UIElements;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Single seam for the TextCore/UI Toolkit calls that could differ
    /// between Unity majors: keeping every such call in this file makes
    /// any future API change a one-file fix. Verified against the
    /// UnityCsReference sources (2022.3, 2023.2 and 6000.0 branches):
    /// FontDefinition.FromSDFFont(FontAsset) and
    /// FontAsset.CreateFontAsset(family, style) are identical across
    /// all of them, so no version branching is needed today.
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
            return FontDefinition.FromSDFFont(asset);
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
