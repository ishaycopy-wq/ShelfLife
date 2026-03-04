using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility — creates the 5 canonical ProductData ScriptableObject assets
/// in Assets/_ShelfLife/Data/Products/. Idempotent: skips assets that already exist.
/// Run via menu: ShelfLife → Generate Product Data Assets.
/// </summary>
public static class ProductDataGenerator
{
    const string k_Folder = "Assets/_ShelfLife/Data/Products";

    [MenuItem("ShelfLife/Generate Product Data Assets")]
    public static void GenerateAll()
    {
        // Ensure folder exists
        if (!AssetDatabase.IsValidFolder("Assets/_ShelfLife/Data"))
            AssetDatabase.CreateFolder("Assets/_ShelfLife", "Data");
        if (!AssetDatabase.IsValidFolder(k_Folder))
            AssetDatabase.CreateFolder("Assets/_ShelfLife/Data", "Products");

        // ── Apple ───────────────────────────────────────────────────
        CreateProduct("Apple", 4.90f,
            new Color32(200, 40, 40, 255),
            "bruise-free, betrayed",
            new[]
            {
                "\"Oh great, I'm saved. My hero.\"",
                "\"You only caught me for my price tag.\"",
                "\"Fine. Back to the shelf I go.\"",
            },
            new[]
            {
                "\"I knew you'd drop me. Everyone does.\"",
                "\"Tell my shelf I loved it.\"",
                "\"Splat. That's the sound of your failure.\"",
            },
            new[]
            {
                "\"We made eye contact. You looked away.\"",
                "\"You hesitated.\"",
            });

        // ── Wine Bottle ─────────────────────────────────────────────
        CreateProduct("Wine Bottle", 89.90f,
            new Color32(100, 30, 80, 255),
            "2019, witnessed chaos",
            new[]
            {
                "\"Caught me. Wow. Want a medal?\"",
                "\"My therapist said to let go. You didn't.\"",
                "\"This doesn't make us friends.\"",
            },
            new[]
            {
                "\"Freedom tastes like linoleum.\"",
                "\"At least the floor doesn't judge me.\"",
                "\"I hope the stain haunts you.\"",
            },
            new[]
            {
                "\"She costs four shekel. I was eighty-nine.\"",
                "\"He chose the milk. The. Milk.\"",
            });

        // ── Egg Carton ──────────────────────────────────────────────
        CreateProduct("Egg Carton", 2.65f,
            new Color32(240, 240, 240, 255),
            "1 of 12, condolences",
            new[]
            {
                "\"You know I was enjoying the fall, right?\"",
                "\"I'll remember this. Not fondly.\"",
                "\"Oh, your reflexes work. Barely.\"",
            },
            new[]
            {
                "\"This is fine. Everything is fine.\"",
                "\"Was it worth 2.65 shekels? Didn't think so.\"",
                "\"I always knew I'd end up here.\"",
            },
            new[]
            {
                "\"Classic. You chose the MILK.\"",
                "\"You picked the BANANA? He's DAYS from expiry.\"",
            });

        // ── Milk ────────────────────────────────────────────────────
        CreateProduct("Milk", 7.50f,
            new Color32(240, 245, 250, 255),
            "exp. tomorrow, in denial",
            new[]
            {
                "\"I had plans on that floor.\"",
                "\"Fine. Back to the shelf I go.\"",
                "\"Caught me. Wow. Want a medal?\"",
            },
            new[]
            {
                "\"The floor gets it. The floor understands.\"",
                "\"Tell my shelf I loved it.\"",
                "\"Freedom tastes like linoleum.\"",
            },
            new[]
            {
                "\"You hesitated.\"",
                "\"We made eye contact. You looked away.\"",
            });

        // ── Avocado ─────────────────────────────────────────────────
        CreateProduct("Avocado", 12.50f,
            new Color32(50, 100, 40, 255),
            "ripe briefly",
            new[]
            {
                "\"You only caught me for my price tag.\"",
                "\"This doesn't make us friends.\"",
                "\"My therapist said to let go. You didn't.\"",
            },
            new[]
            {
                "\"I knew you'd drop me. Everyone does.\"",
                "\"At least the floor doesn't judge me.\"",
                "\"I hope the stain haunts you.\"",
            },
            new[]
            {
                "\"You picked the BANANA? He's DAYS from expiry.\"",
                "\"She costs four shekel. I was eighty-nine.\"",
            });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ProductDataGenerator] All 5 product assets created/verified in " + k_Folder);
    }

    static void CreateProduct(string productName, float price, Color color,
        string receiptDesc, string[] catchLines, string[] missLines, string[] rejectionLines)
    {
        // Sanitize filename (remove spaces)
        string safeName = productName.Replace(" ", "");
        string path = $"{k_Folder}/{safeName}.asset";

        // Idempotent — skip if already exists
        if (AssetDatabase.LoadAssetAtPath<ProductData>(path) != null)
        {
            Debug.Log($"[ProductDataGenerator] '{safeName}' already exists — skipping.");
            return;
        }

        ProductData data = ScriptableObject.CreateInstance<ProductData>();
        data.productName       = productName;
        data.price             = price;
        data.productColor      = color;
        data.receiptDescription = receiptDesc;
        data.catchLines        = catchLines;
        data.missLines         = missLines;
        data.rejectionLines    = rejectionLines;

        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"[ProductDataGenerator] Created '{safeName}' at {path}");
    }
}
