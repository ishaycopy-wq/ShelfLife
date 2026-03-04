using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor tool that builds the ProduceIsland_Hero prefab from primitives.
/// Menu: ShelfLife → Build ProduceIsland Hero Prefab
/// Pure geometry + MeshRenderer + BoxCollider. No MonoBehaviour logic.
/// Pivot centered at ground level (Y=0).
/// </summary>
public static class ProduceIslandBuilder
{
    [MenuItem("ShelfLife/Build ProduceIsland Hero Prefab")]
    public static void BuildPrefab()
    {
        // ── Materials ──────────────────────────────────────────────
        string matFolder = "Assets/_ShelfLife/Materials";
        if (!AssetDatabase.IsValidFolder(matFolder))
            AssetDatabase.CreateFolder("Assets/_ShelfLife", "Materials");

        Material matBase   = GetOrCreateMat(matFolder, "Mat_IslandBase",   new Color32(140,  95,  55, 255), 0.25f);
        Material matShelf  = GetOrCreateMat(matFolder, "Mat_IslandShelf",  new Color32(170, 130,  80, 255), 0.30f);
        Material matStrut  = GetOrCreateMat(matFolder, "Mat_IslandStrut",  new Color32(160, 160, 165, 255), 0.55f, 0.25f);
        Material matBack   = GetOrCreateMat(matFolder, "Mat_IslandBack",   new Color32(150, 145, 140, 255), 0.10f);
        Material matTag    = GetOrCreateMat(matFolder, "Mat_PriceTag",     new Color32(255, 255, 235, 255), 0.10f);

        Material[] binMats = {
            GetOrCreateMat(matFolder, "Mat_Bin_Tomato",   new Color32(185,  42,  38, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Lettuce",  new Color32( 55, 145,  50, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Lemon",    new Color32(215, 200,  50, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Carrot",   new Color32(220, 125,  35, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Eggplant", new Color32(112,  42, 135, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Potato",   new Color32(125,  85,  48, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Onion",    new Color32(230, 215, 175, 255), 0.20f),
            GetOrCreateMat(matFolder, "Mat_Bin_Kale",     new Color32( 38,  92,  42, 255), 0.20f),
        };

        // ── Root — pivot at ground level ───────────────────────────
        GameObject root = new GameObject("ProduceIsland_Hero");
        root.transform.position = Vector3.zero;

        // ── Base slab ──────────────────────────────────────────────
        float baseW = 4.2f, baseH = 0.3f, baseD = 1.0f;
        MakeChild(root, "Base", PrimitiveType.Cube,
            new Vector3(0f, baseH * 0.5f, 0f),
            new Vector3(baseW, baseH, baseD), matBase);

        // ── 3 shelf tiers ──────────────────────────────────────────
        float shelfW = 4.0f, shelfThick = 0.04f, shelfD = 0.8f;
        float[] tierY = { 0.5f, 1.0f, 1.5f };

        for (int i = 0; i < tierY.Length; i++)
        {
            MakeChild(root, $"ShelfTier_{i}", PrimitiveType.Cube,
                new Vector3(0f, tierY[i], 0f),
                new Vector3(shelfW, shelfThick, shelfD), matShelf);
        }

        // ── Back panel ─────────────────────────────────────────────
        float supportH = 2.0f;
        MakeChild(root, "BackPanel", PrimitiveType.Cube,
            new Vector3(0f, supportH * 0.5f, shelfD * 0.5f - 0.01f),
            new Vector3(shelfW, supportH, 0.02f), matBack);

        // ── Vertical supports ──────────────────────────────────────
        float strutW = 0.06f;
        float strutX = (shelfW * 0.5f) - (strutW * 0.5f);
        MakeChild(root, "Support_Left", PrimitiveType.Cube,
            new Vector3(-strutX, supportH * 0.5f, 0f),
            new Vector3(strutW, supportH, shelfD), matStrut);
        MakeChild(root, "Support_Right", PrimitiveType.Cube,
            new Vector3(strutX, supportH * 0.5f, 0f),
            new Vector3(strutW, supportH, shelfD), matStrut);

        // ── 8 produce bins (cube placeholders, subtle imperfection) ─
        float binW = 0.35f, binH = 0.20f, binD = 0.30f;
        // (xPos, tierIndex) — spread across 3 tiers
        float[][] binSlots = {
            new float[] { -1.40f, 0f }, new float[] { -0.65f, 0f },
            new float[] {  0.20f, 0f }, new float[] {  1.10f, 0f },
            new float[] { -1.00f, 1f }, new float[] {  0.00f, 1f },
            new float[] {  0.90f, 1f }, new float[] { -0.50f, 2f },
        };

        for (int i = 0; i < binSlots.Length; i++)
        {
            int tier = (int)binSlots[i][1];
            float binBaseY = tierY[tier] + shelfThick * 0.5f + binH * 0.5f;

            // Slight size variation (0.95–1.05)
            float sizeVar = 1f + ((i % 3) - 1) * 0.05f;
            Vector3 binScale = new Vector3(binW * sizeVar, binH * sizeVar, binD * sizeVar);

            GameObject bin = MakeChild(root, $"ProduceBin_{i}", PrimitiveType.Cube,
                new Vector3(binSlots[i][0], binBaseY, -0.05f), binScale,
                binMats[i % binMats.Length]);

            // Subtle imperfection rotation (deterministic pseudo-random)
            float rotY = ((i * 7 + 3) % 11) - 5f;   // –5° to +5°
            float rotZ = ((i * 3 + 1) % 5)  - 2f;    // –2° to +2°
            bin.transform.localRotation = Quaternion.Euler(0f, rotY, rotZ);
        }

        // ── 12 price tags (thin cubes — 4 per tier) ────────────────
        float tagW = 0.15f, tagH = 0.08f, tagD = 0.005f;
        float[] tagXPositions = { -1.5f, -0.5f, 0.5f, 1.5f };

        for (int tier = 0; tier < 3; tier++)
        {
            for (int t = 0; t < 4; t++)
            {
                int tagIdx = tier * 4 + t;
                // Hang just below shelf front edge
                float tagY = tierY[tier] - shelfThick * 0.5f - tagH * 0.5f - 0.02f;
                float tagZ = -(shelfD * 0.5f) + 0.02f;

                GameObject tag = MakeChild(root, $"PriceTag_{tagIdx}", PrimitiveType.Cube,
                    new Vector3(tagXPositions[t], tagY, tagZ),
                    new Vector3(tagW, tagH, tagD), matTag);

                // Tiny random tilt for imperfection
                float tiltZ = ((tagIdx * 5 + 2) % 7) - 3f;  // –3° to +3°
                tag.transform.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
            }
        }

        // ── Save as Prefab ─────────────────────────────────────────
        string prefabFolder = "Assets/_ShelfLife/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabFolder))
            AssetDatabase.CreateFolder("Assets/_ShelfLife", "Prefabs");

        string prefabPath = $"{prefabFolder}/ProduceIsland_Hero.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.UserAction);

        EditorUtility.SetDirty(root);
        Debug.Log($"[ProduceIslandBuilder] Prefab saved: {prefabPath} — " +
                  $"3 tiers, {binSlots.Length} bins, 12 price tags, 2 supports, back panel, base.");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    static GameObject MakeChild(GameObject parent, string name, PrimitiveType type,
        Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material GetOrCreateMat(string folder, string name, Color32 color,
        float smoothness, float metallic = 0f)
    {
        string path = $"{folder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        Material mat = new Material(litShader);
        mat.SetColor("_BaseColor", (Color)color);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
