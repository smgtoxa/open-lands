// The cursor pictures (Assets/Resources/Cursors, the Windows system cursors a browser shows) import as
// cursors: readable and uncompressed, as Cursor.SetCursor needs them. The item artwork (Assets/Resources/Art,
// tools/art_png.py) imports readable and uncompressed too: a canvas drawImage reads its pixels.
using UnityEditor;

public sealed class CursorImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        bool cursor = assetPath.StartsWith("Assets/Resources/Cursors/");
        if (assetPath.StartsWith("Assets/Resources/UI/hud/"))
        {
            // pixel art drawn at 2x: exact pixels, no blur, no smaller copies
            var hud = (TextureImporter)assetImporter;
            hud.alphaIsTransparency = true;
            hud.textureCompression = TextureImporterCompression.Uncompressed;
            hud.mipmapEnabled = false;
            hud.filterMode = UnityEngine.FilterMode.Point;
            hud.npotScale = TextureImporterNPOTScale.None;
            return;
        }
        if (!cursor && !assetPath.StartsWith("Assets/Resources/Art/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = cursor ? TextureImporterType.Cursor : TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
    }
}
