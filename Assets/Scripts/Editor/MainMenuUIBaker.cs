using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-time (re-runnable) Editor tool that bakes the main menu's static
/// shells — MainMenuShellBuilder's logo/button stack and
/// LevelSelectShellBuilder's level-select panel scaffold — into real
/// .prefab assets, the UI equivalent of DevicePrefabBuilder's device-mesh
/// baking. Both builders are also called at runtime as a fallback (see
/// MainMenuBootstrap/LevelSelectManager), so the baked prefab and the
/// runtime fallback can never visually diverge — they're the same code.
///
/// Run via Tools > Capstone > Build Main Menu Prefabs (UI). Output:
///   Assets/Resources/MainMenuPrefabs/MainMenuShell.prefab
///   Assets/Resources/MainMenuPrefabs/LevelSelectShell.prefab
///   Assets/Resources/LevelCatalog.asset (seeded once, never overwritten on
///     re-runs — see BuildAll — so hand-edited level data survives regeneration)
///
/// After running this, drag instances of both prefabs into MainMenu.unity's
/// Canvas and wire MainMenuBootstrap's shellView/levelSelectPrePlaced fields
/// to them in the Inspector for full "open the scene and see it" editing —
/// baking alone only makes the prefabs themselves hand-editable in Prefab Mode.
/// </summary>
public static class MainMenuUIBaker
{
    const string PrefabFolder = "Assets/Resources/MainMenuPrefabs";
    const string CatalogPath  = "Assets/Resources/LevelCatalog.asset";
    const string FontResourcePath = "Fonts/PixeloidMono SDF";

    // Mirrors MainMenuBootstrap's [SerializeField] defaults — kept in sync by
    // hand since the baked prefab has no live MainMenuBootstrap instance to
    // read them from.
    static readonly Color TextColor = Color.white;

    // Mirrors LevelSelectManager's [SerializeField] Container Box defaults.
    static readonly Color ContainerFill   = new Color(0.055f, 0.07f, 0.11f);
    static readonly Color ContainerBorder = new Color(0.14f, 0.32f, 0.50f);
    static readonly Color ContainerTrim   = new Color(0.02f, 0.03f, 0.05f);
    const int ContainerBorderThickness = 5;
    const int ContainerChamfer         = 34;
    const int ContainerPixelScale      = 6;

    [MenuItem("Tools/Capstone/Build Main Menu Prefabs (UI)")]
    public static void BuildAll()
    {
        // Wipe and rebuild the generated prefab folder so re-running this
        // tool after a layout tweak doesn't leave a stale prefab behind.
        // LevelCatalog.asset lives outside this wipe (seeded separately,
        // below, only if missing) so hand-edited level data isn't stomped.
        if (AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.DeleteAsset(PrefabFolder);
        EnsureFolder(PrefabFolder);

        var font = Resources.Load<TMP_FontAsset>(FontResourcePath);

        // Build under a scratch Canvas so RectTransform layout math (which
        // assumes a UI parent) behaves correctly — the Canvas itself is
        // never saved, only the shells built underneath it.
        var scratchCanvasGo = new GameObject("__MainMenuUIBaker_Scratch", typeof(RectTransform));
        var scratchRt = scratchCanvasGo.GetComponent<RectTransform>();

        var shellView = MainMenuShellBuilder.Build(scratchRt, font, TextColor);
        PersistGeneratedSprites(shellView.gameObject, "MainMenuShell");
        SavePrefab("MainMenuShell", shellView.gameObject);

        var levelSelectView = LevelSelectShellBuilder.Build(scratchRt, font, TextColor,
            ContainerFill, ContainerBorder, ContainerTrim, ContainerBorderThickness, ContainerChamfer, ContainerPixelScale);
        PersistGeneratedSprites(levelSelectView.gameObject, "LevelSelectShell");
        SavePrefab("LevelSelectShell", levelSelectView.gameObject);

        Object.DestroyImmediate(scratchCanvasGo);

        SeedLevelCatalogIfMissing();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[MainMenuUIBaker] Generated main menu prefabs into {PrefabFolder}.");
    }

    // PlateSpriteBuilder (CarouselBackground, DescriptionPopup's panel) bakes
    // its sprite from a plain runtime Texture2D — never a real project asset
    // — the same reason DevicePrefabBuilder has to bake ProBuilder meshes
    // into .asset files before saving a device prefab (see its class doc).
    // A PrefabUtility.SaveAsPrefabAsset round-trip can't preserve a
    // non-persistent object reference, so any Image whose sprite isn't
    // already a real asset (an Image with no sprite yet, or one loaded via
    // Resources.Load, is left untouched) gets its texture/sprite saved as a
    // real .asset here first — otherwise the prefab reloads with that
    // Image.sprite silently nulled out (renders as a plain white rect).
    static void PersistGeneratedSprites(GameObject root, string namePrefix)
    {
        string folder = $"{PrefabFolder}/GeneratedSprites";
        EnsureFolder(folder);

        int counter = 0;
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img.sprite == null || EditorUtility.IsPersistent(img.sprite)) continue;

            var runtimeSprite = img.sprite;
            // Copy the texture rather than saving the shared one directly —
            // PlateSpriteBuilder caches sprites by parameter combination, so
            // the same Texture2D object could otherwise be asked to save
            // itself to two different asset paths.
            var savedTex = Object.Instantiate(runtimeSprite.texture);
            savedTex.name = $"{namePrefix}_Texture{counter}";
            string path = $"{folder}/{namePrefix}_Sprite{counter}.asset";
            counter++;

            AssetDatabase.CreateAsset(savedTex, path);
            var savedSprite = Sprite.Create(savedTex, runtimeSprite.rect, runtimeSprite.pivot,
                runtimeSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, runtimeSprite.border);
            savedSprite.name = "Sprite";
            AssetDatabase.AddObjectToAsset(savedSprite, path);

            img.sprite = savedSprite;
        }
    }

    static void SeedLevelCatalogIfMissing()
    {
        if (AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath) != null) return;

        var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
        catalog.levels.AddRange(LevelSelectManager.DefaultLevelList("SampleScene"));
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        Debug.Log($"[MainMenuUIBaker] Seeded {CatalogPath} with the default level list.");
    }

    static void SavePrefab(string name, GameObject built)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(built, path, out bool success);
        Object.DestroyImmediate(built);
        if (!success) Debug.LogError($"[MainMenuUIBaker] Failed to save {path}");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
