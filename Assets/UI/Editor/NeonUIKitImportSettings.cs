using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Drop this in any Editor/ folder. It auto-configures every sprite inside
/// Assets/NeonUIKit/Sprites on (re)import: Sprite type, no mipmaps, point-free
/// bilinear filtering, and the 9-slice borders each frame needs.
/// Borders are in pixels: L, B, R, T.
/// </summary>
public class NeonUIKitImportSettings : AssetPostprocessor
{
    const string Root = "/NeonUIKit/Sprites/";

    static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
    {
        // clipped-corner frames: slice past the diagonal cut
        { "btn_primary",       new Vector4(44, 44, 44, 44) },
        { "btn_primary_glow",  new Vector4(72, 72, 72, 72) },
        { "btn_secondary",     new Vector4(36, 36, 36, 36) },
        { "btn_ghost",         new Vector4(36, 36, 36, 36) },
        { "btn_danger",        new Vector4(36, 36, 36, 36) },
        { "btn_disabled",      new Vector4(36, 36, 36, 36) },
        { "input_idle",        new Vector4(40, 40, 40, 40) },
        { "input_focus",       new Vector4(40, 40, 40, 40) },
        { "input_error",       new Vector4(40, 40, 40, 40) },
        { "icon_btn",          new Vector4(36, 36, 36, 36) },
        { "panel",             new Vector4(62, 62, 62, 62) },
        { "gauge_track",       new Vector4(30, 30, 10, 10) },
        { "namebar_frame",     new Vector4(120, 60, 220, 60) },
        // 장식 메시지 박스
        { "msgbox_frame",        new Vector4(110, 110, 110, 110) },
        { "msgbox_panel",        new Vector4(40, 40, 40, 40) },
        { "msgbox_edge_top",     new Vector4(2, 0, 2, 0) },
        { "msgbox_edge_bottom",  new Vector4(2, 0, 2, 0) },
        { "msgbox_edge_left",    new Vector4(0, 2, 0, 2) },
        { "msgbox_edge_right",   new Vector4(0, 2, 0, 2) },
        // stretch-only strips (fills, divider): horizontal slice only
        { "gauge_fill_hp",     new Vector4(8, 0, 8, 0) },
        { "gauge_fill_mp",     new Vector4(8, 0, 8, 0) },
        { "gauge_fill_xp",     new Vector4(8, 0, 8, 0) },
        { "divider",           new Vector4(8, 0, 8, 0) },
    };

    void OnPreprocessTexture()
    {
        if (!assetPath.Contains(Root)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.spritePixelsPerUnit = 100f;

        var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        Vector4 border;
        if (Borders.TryGetValue(name, out border))
            importer.spriteBorder = border;

        var platform = importer.GetDefaultPlatformTextureSettings();
        platform.format = TextureImporterFormat.Automatic;
        platform.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SetPlatformTextureSettings(platform);
    }
}
