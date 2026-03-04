using UnityEngine;

/// <summary>
/// ScriptableObject holding all data for a single product type.
/// Created via Assets → Create → ShelfLife → ProductData.
///
/// NOT yet wired into runtime systems — the hardcoded dictionaries in
/// ScoreManager, ReceiptScreen, SubtitleSystem, and ShelfSceneSetup still
/// drive the game. These assets exist as the canonical data source and
/// will replace the hardcoded values in a future session.
/// </summary>
[CreateAssetMenu(menuName = "ShelfLife/ProductData", fileName = "NewProduct")]
public sealed class ProductData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name on the receipt (e.g. 'Apple', 'Wine Bottle').")]
    public string productName;

    [Tooltip("Base shelf price in shekels.")]
    public float price;

    [Tooltip("The product's primary color (used for shelf placement and receipt).")]
    public Color productColor = Color.white;

    [Header("Receipt")]
    [Tooltip("Short cynical description printed on the receipt " +
             "(e.g. 'bruise-free, betrayed').")]
    public string receiptDescription;

    [Header("Personality Lines")]
    [Tooltip("Lines spoken when the player catches this product.")]
    [TextArea(1, 3)]
    public string[] catchLines;

    [Tooltip("Lines spoken when this product hits the floor (normal miss).")]
    [TextArea(1, 3)]
    public string[] missLines;

    [Tooltip("Lines spoken when this product was rejected " +
             "(another product caught while this one was falling).")]
    [TextArea(1, 3)]
    public string[] rejectionLines;
}
