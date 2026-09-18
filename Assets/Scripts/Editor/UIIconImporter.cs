using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces every texture dropped into Assets/Resources/UIIcons/ to import as a
/// crisp, uncompressed UI sprite (Texture Type = Sprite, no mip maps, bilinear
/// filtering, point-ish clarity at HUD scale) instead of Unity's default
/// "Default" texture settings. This keeps sidebar icons looking correct
/// without anyone having to remember to fix import settings by hand in the
/// Inspector after adding/replacing an icon.
/// </summary>
public class UIIconImporter : AssetPostprocessor
{
    const string IconFolder = "Assets/Resources/UIIcons/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(IconFolder)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType        = TextureImporterType.Sprite;
        importer.spriteImportMode   = SpriteImportMode.Single;
        importer.mipmapEnabled      = false;
        importer.filterMode         = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.spritePixelsPerUnit = 100;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot     = new UnityEngine.Vector2(0.5f, 0.5f);
        importer.SetTextureSettings(settings);
    }
}
