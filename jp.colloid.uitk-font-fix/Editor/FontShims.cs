using UnityEngine;
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
        // Atlas material property ids. TextCore's own TextShaderUtilities
        // initializes the identical values from these exact names
        // (ID_MainTex = Shader.PropertyToID("_MainTex") and friends);
        // resolving them here keeps the package independent of that
        // type's visibility, which is not part of the documented API.
        internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        internal static readonly int GradientScaleId = Shader.PropertyToID("_GradientScale");
        internal static readonly int TextureWidthId = Shader.PropertyToID("_TextureWidth");
        internal static readonly int TextureHeightId = Shader.PropertyToID("_TextureHeight");
        internal static readonly int WeightNormalId = Shader.PropertyToID("_WeightNormal");
        internal static readonly int WeightBoldId = Shader.PropertyToID("_WeightBold");
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

        // -- Transient-asset plumbing -----------------------------------------
        //
        // The accessors below exist so the lifecycle policy
        // (FontAssetLifecycle) never touches TextCore directly. They are
        // deliberately thin: the seam's value is that a future Unity
        // renaming or demoting one of them is a one-file fix.

        /// <summary>The asset's atlas material (public get/set on TextAsset).</summary>
        internal static Material GetMaterial(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            return asset.material;
        }

        /// <summary>Installs a replacement atlas material.</summary>
        internal static void SetMaterial(
            UnityEngine.TextCore.Text.FontAsset asset, Material material)
        {
            asset.material = material;
        }

        /// <summary>
        /// The LIVE atlas page array (the getter returns the backing
        /// field, so element assignment sticks -- verified on 2022.3 and
        /// pinned by a regression test). Trailing entries beyond
        /// GetUsedAtlasPageCount are unused slots left by TextCore's
        /// doubling growth and are genuinely null, not destroyed objects.
        /// </summary>
        internal static Texture2D[] GetAtlasPages(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            return asset.atlasTextures;
        }

        /// <summary>Installs a replacement page array (only ever used when the asset has none).</summary>
        internal static void SetAtlasPages(
            UnityEngine.TextCore.Text.FontAsset asset, Texture2D[] pages)
        {
            asset.atlasTextures = pages;
        }

        /// <summary>Number of atlas pages actually in use (TextCore's atlasTextureCount).</summary>
        internal static int GetUsedAtlasPageCount(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            return asset.atlasTextureCount;
        }

        /// <summary>Configured atlas width, or 0 when unavailable.</summary>
        internal static int GetAtlasWidth(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            return asset.atlasWidth;
        }

        /// <summary>Configured atlas height, or 0 when unavailable.</summary>
        internal static int GetAtlasHeight(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            return asset.atlasHeight;
        }

        /// <summary>
        /// Clears the glyph/character tables and collapses the atlas back
        /// to a single 1x1 page, which the next glyph add grows again.
        /// This is the ONLY public route that also resets the glyph
        /// packing rectangles (freeGlyphRects/usedGlyphRects are
        /// internal), and it is why the seam matters here: TextCore's own
        /// doc comment warns the method "might be changed to Internal".
        /// MUST NOT be called while the material or atlas page 0 is
        /// destroyed -- TextCore dereferences both (caller's job; see
        /// FontAssetLifecycle.TryRepair). Returns false instead of
        /// throwing.
        /// </summary>
        internal static bool TryResetFontAssetData(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            try
            {
                asset.ClearFontAssetData(true);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
