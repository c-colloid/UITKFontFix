using System.Collections.Generic;
using UnityEngine;

namespace Colloid.UitkFontFix
{
    /// <summary>
    /// Object-lifetime policy for the transient FontAssets this package
    /// creates. Exists because TextCore builds a DynamicOS FontAsset out
    /// of THREE objects -- the asset, its atlas material and its atlas
    /// page textures -- and stamps hideFlags on NONE of them, while the
    /// editor destroys unflagged objects across a Play Mode transition
    /// (the flag that prevents it, HideFlags.DontSave, is documented as
    /// "will not be destroyed when a new Scene is loaded"). An asset
    /// whose children died stays alive and non-null, so every later draw
    /// dereferences a destroyed object and throws
    /// MissingReferenceException from inside TextCore.
    ///
    /// Three layers, in this order:
    ///  1. PREVENT: stamp the children at creation and re-stamp before
    ///     every Play Mode transition (TextCore adds atlas pages lazily
    ///     at render time, and those are born unflagged too).
    ///  2. DETECT: never hand out an asset whose material or any in-use
    ///     page is destroyed.
    ///  3. RECOVER: repair the SAME instance in place. Instance identity
    ///     is the whole point -- UI Toolkit elements already hold the
    ///     FontAsset in their inline style, so a replacement instance
    ///     would not heal them.
    ///
    /// Every method is defensive and silent: this type is reached from
    /// editor callbacks and from resolver getters that promise never to
    /// throw.
    /// </summary>
    internal static class FontAssetLifecycle
    {
        /// <summary>
        /// Flags stamped on package-created materials and atlas pages.
        /// Matches what Unity itself stamps on the children of a cached
        /// runtime FontAsset (TextSettings.GetCachedFontAssetInternal);
        /// the asset itself keeps the package's HideAndDontSave, a
        /// superset.
        /// </summary>
        internal const HideFlags TransientChildFlags = HideFlags.DontSave;

        // Everything needed to rebuild a destroyed atlas material,
        // captured while it is still alive. A Shader is a project or
        // built-in asset and is not what a Play Mode transition
        // destroys, so holding the reference is safe; the floats are the
        // exact set TextCore writes when it creates the material.
        private struct MaterialTemplate
        {
            internal Shader Shader;
            internal float GradientScale;
            internal float TextureWidth;
            internal float TextureHeight;
            internal float WeightNormal;
            internal float WeightBold;
        }

        // Keyed by instance id: GetInstanceID returns a cached managed
        // field and stays valid (and callable) after destruction.
        private static readonly Dictionary<int, MaterialTemplate> _templates =
            new Dictionary<int, MaterialTemplate>();

        // Used-page count at the last stamp, keyed by instance id. Lets
        // the per-tick guard skip every asset whose atlas did not grow
        // with one dictionary lookup and no native call.
        private static readonly Dictionary<int, int> _stampedPageCounts =
            new Dictionary<int, int>();

        // A repair loads a system font face synchronously (through
        // ClearFontAssetData). FontEngine's current face is process
        // global, so a repair re-entered from inside TextCore's own
        // rasterization would swap the face under an in-flight glyph
        // add. Nested calls therefore do nothing at all.
        private static bool _repairing;

        // -- Adoption ----------------------------------------------------------

        /// <summary>
        /// Takes ownership of a freshly created asset: names it and its
        /// children with the kit tag, stamps the protective flags and
        /// records the material template used by a later repair.
        /// </summary>
        internal static void Adopt(UnityEngine.TextCore.Text.FontAsset asset)
        {
            Name(asset);
            Stamp(asset);
            CaptureMaterialTemplate(asset);
        }

        /// <summary>Drops the recorded template for an asset being destroyed.</summary>
        internal static void Forget(UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (ReferenceEquals(asset, null))
            {
                return;
            }
            try
            {
                _templates.Remove(asset.GetInstanceID());
                _stampedPageCounts.Remove(asset.GetInstanceID());
            }
            catch (System.Exception)
            {
                // Bookkeeping only.
            }
        }

        // -- Detection ---------------------------------------------------------

        /// <summary>
        /// True for a PRESENT null, i.e. a cached resolution MISS. Such
        /// an entry must never be treated as damage: re-probing it on
        /// every access would re-run CreateFontAsset and repeat
        /// TextCore's "unable to find a font file" log once per call.
        /// </summary>
        internal static bool IsCachedMiss(UnityEngine.TextCore.Text.FontAsset asset)
        {
            return ReferenceEquals(asset, null);
        }

        /// <summary>
        /// True when the asset can be handed to UI Toolkit: it is alive,
        /// its material is alive, and every atlas page IN USE is alive.
        /// Pages beyond the used count are unfilled slots from TextCore's
        /// doubling growth and are ignored on purpose.
        /// </summary>
        internal static bool IsUsable(UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (ReferenceEquals(asset, null) || asset == null)
            {
                return false;
            }
            try
            {
                if (FontShims.GetMaterial(asset) == null)
                {
                    return false;
                }
                Texture2D[] pages = FontShims.GetAtlasPages(asset);
                if (pages == null || pages.Length == 0)
                {
                    return false;
                }
                int used = UsedPageCount(asset, pages);
                for (int i = 0; i < used; i++)
                {
                    if (pages[i] == null)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True when the entry is a real object that is damaged --
        /// destroyed itself, or alive with a destroyed child. A cached
        /// miss answers false.
        /// </summary>
        internal static bool NeedsAttention(UnityEngine.TextCore.Text.FontAsset asset)
        {
            return !IsCachedMiss(asset) && !IsUsable(asset);
        }

        // -- Prevention --------------------------------------------------------

        /// <summary>
        /// Stamps the protective flags on the asset and every LIVE child.
        /// Only live objects are touched: hideFlags is a native property,
        /// so assigning it on a destroyed object throws. Cheap enough to
        /// run on every cached access, which is what keeps lazily added
        /// atlas pages covered.
        /// </summary>
        internal static void Stamp(UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (ReferenceEquals(asset, null) || asset == null)
            {
                return;
            }
            try
            {
                asset.hideFlags = HideFlags.HideAndDontSave;
                Material material = FontShims.GetMaterial(asset);
                if (material != null)
                {
                    material.hideFlags = TransientChildFlags;
                }
                Texture2D[] pages = FontShims.GetAtlasPages(asset);
                if (pages == null)
                {
                    return;
                }
                int used = UsedPageCount(asset, pages);
                for (int i = 0; i < used; i++)
                {
                    if (pages[i] != null)
                    {
                        pages[i].hideFlags = TransientChildFlags;
                    }
                }
                _stampedPageCounts[asset.GetInstanceID()] = used;
            }
            catch (System.Exception)
            {
                // Protection is best-effort; detection and repair cover
                // whatever it fails to protect.
            }
        }

        /// <summary>
        /// Stamps only when TextCore added atlas pages since the last
        /// stamp. Pages are added lazily at render time and are born
        /// unflagged, and the events that destroy unflagged objects are
        /// not limited to Play Mode: a new or opened scene does it too,
        /// with no hook that fires before the damage. A consumer that
        /// applied the font once and never calls back would leave every
        /// later page exposed, so this runs from the editor update tick.
        /// Cheap enough for that: the page count is a managed field, so
        /// an unchanged asset costs one dictionary lookup and no native
        /// call. Returns true when it stamped.
        /// </summary>
        internal static bool StampNewPages(UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (ReferenceEquals(asset, null))
            {
                return false;
            }
            try
            {
                int used = FontShims.GetUsedAtlasPageCount(asset);
                int last;
                if (_stampedPageCounts.TryGetValue(asset.GetInstanceID(), out last)
                    && last >= used)
                {
                    return false;
                }
                Stamp(asset);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // -- Recovery ----------------------------------------------------------

        /// <summary>
        /// Restores a damaged asset IN PLACE so that references already
        /// handed out keep working. Returns false when the damage cannot
        /// be repaired (asset itself destroyed, atlas geometry lost, no
        /// recorded material template, or TextCore refused the reset) --
        /// the caller then falls back to a full rebuild. Never throws.
        /// </summary>
        internal static bool TryRepair(UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (_repairing || ReferenceEquals(asset, null) || asset == null)
            {
                return false;
            }
            if (IsUsable(asset))
            {
                Stamp(asset);
                return true;
            }
            _repairing = true;
            try
            {
                // The atlas geometry is public get / internal set, so a
                // lost value cannot be corrected from here: rebuild.
                int width = FontShims.GetAtlasWidth(asset);
                int height = FontShims.GetAtlasHeight(asset);
                if (width <= 0 || height <= 0)
                {
                    return false;
                }

                // The material is replaced even when it survived. UI
                // Toolkit regenerates an element's cached text mesh only
                // when its generation-settings hash changes, and that
                // hash covers the font asset and its material -- nothing
                // else about an in-place repair is visible to it. With
                // the old material kept, every element already drawn
                // would keep mesh data that points at the dead page and
                // its next draw would throw again; a new material is the
                // one signal that makes the next repaint regenerate.
                if (!TryReplaceMaterial(asset))
                {
                    return false;
                }

                Texture2D[] pages = FontShims.GetAtlasPages(asset);
                if (pages == null || pages.Length == 0)
                {
                    pages = new Texture2D[1];
                    FontShims.SetAtlasPages(asset, pages);
                }
                if (pages[0] == null)
                {
                    // Same shape TextCore itself creates; the reset below
                    // grows it back on demand. Script-created textures are
                    // readable, which the reset requires.
                    pages[0] = new Texture2D(1, 1, TextureFormat.Alpha8, false);
                }

                Material material = FontShims.GetMaterial(asset);
                if (material == null || pages[0] == null)
                {
                    return false;
                }
                // Needed even when only ONE of the two died: a surviving
                // material still points at the dead texture.
                material.SetTexture(FontShims.MainTexId, pages[0]);

                // Naming precedes the reset because the reset recomputes
                // the asset's name-derived hashes.
                Adopt(asset);

                // Clears the glyph and character tables, whose rectangles
                // address atlas content that no longer exists, and drops
                // any remaining pages. Only legal now that page 0 and the
                // material are alive again.
                if (!FontShims.TryResetFontAssetData(asset))
                {
                    return false;
                }

                Stamp(asset);
                CaptureMaterialTemplate(asset);
                return IsUsable(asset);
            }
            catch (System.Exception)
            {
                return false;
            }
            finally
            {
                _repairing = false;
            }
        }

        // -- Internals ---------------------------------------------------------

        private static bool TryReplaceMaterial(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            // A surviving material is the best template there is; a
            // destroyed one falls back to the copy taken while it lived.
            Material previous = FontShims.GetMaterial(asset);
            if (previous != null)
            {
                CaptureMaterialTemplate(asset);
            }
            MaterialTemplate template;
            if (!_templates.TryGetValue(asset.GetInstanceID(), out template)
                || template.Shader == null)
            {
                return false;
            }
            var material = new Material(template.Shader);
            material.SetFloat(FontShims.GradientScaleId, template.GradientScale);
            material.SetFloat(FontShims.TextureWidthId, template.TextureWidth);
            material.SetFloat(FontShims.TextureHeightId, template.TextureHeight);
            material.SetFloat(FontShims.WeightNormalId, template.WeightNormal);
            material.SetFloat(FontShims.WeightBoldId, template.WeightBold);
            FontShims.SetMaterial(asset, material);
            if (previous != null)
            {
                // Installed first, destroyed second: nothing observes a
                // null material in between. Cached text meshes that still
                // name the old material are regenerated before their next
                // draw reads it (the replacement changed their hash).
                Object.DestroyImmediate(previous);
            }
            return true;
        }

        private static void CaptureMaterialTemplate(
            UnityEngine.TextCore.Text.FontAsset asset)
        {
            if (ReferenceEquals(asset, null) || asset == null)
            {
                return;
            }
            try
            {
                Material material = FontShims.GetMaterial(asset);
                if (material == null || material.shader == null)
                {
                    return;
                }
                var template = new MaterialTemplate();
                template.Shader = material.shader;
                template.GradientScale = ReadFloat(material, FontShims.GradientScaleId);
                template.TextureWidth = ReadFloat(material, FontShims.TextureWidthId);
                template.TextureHeight = ReadFloat(material, FontShims.TextureHeightId);
                template.WeightNormal = ReadFloat(material, FontShims.WeightNormalId);
                template.WeightBold = ReadFloat(material, FontShims.WeightBoldId);
                _templates[asset.GetInstanceID()] = template;
            }
            catch (System.Exception)
            {
                // Without a template a later repair falls back to a
                // rebuild, which is a supported outcome.
            }
        }

        private static float ReadFloat(Material material, int propertyId)
        {
            return material.HasProperty(propertyId)
                ? material.GetFloat(propertyId)
                : 0f;
        }

        private static void Name(UnityEngine.TextCore.Text.FontAsset asset)
        {
            try
            {
                // Distinguishing info first (narrow panels truncate from
                // the right), provenance tag last; the actual face names
                // from faceInfo, so a loose OS match stays visible.
                asset.name = asset.faceInfo.familyName + " - "
                    + asset.faceInfo.styleName + " " + FontFix.CreatedObjectNameTag;
                Material material = FontShims.GetMaterial(asset);
                if (material != null)
                {
                    material.name = asset.name + " Material";
                }
                Texture2D[] pages = FontShims.GetAtlasPages(asset);
                if (pages != null && pages.Length > 0 && pages[0] != null)
                {
                    pages[0].name = asset.name + " Atlas";
                }
            }
            catch (System.Exception)
            {
                // Naming is diagnostics-only; never let it fail resolution.
            }
        }

        private static int UsedPageCount(
            UnityEngine.TextCore.Text.FontAsset asset, Texture2D[] pages)
        {
            int used = FontShims.GetUsedAtlasPageCount(asset);
            if (used < 1)
            {
                used = 1;
            }
            return used > pages.Length ? pages.Length : used;
        }
    }
}
