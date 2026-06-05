namespace GlamourThis.Services;

/// <summary>
/// The expansion bands used to group items in the UI.
///
/// FFXIV does not store an expansion on each item, so the band is approximated from the level
/// required to equip the item, which lines up with the level cap of each expansion. Items that have
/// no real level requirement (level 0 or 1) - typically cosmetic, event or tool gear - cannot be
/// placed this way, so they get their own <see cref="Special"/> bucket instead of being mislabelled.
/// </summary>
public enum ExpansionBand
{
    /// <summary>Level 0-1 items (cosmetic / event / special) that do not map to an expansion by level.</summary>
    Special = 0,

    /// <summary>Level 2-50 (A Realm Reborn).</summary>
    ARealmReborn = 1,

    /// <summary>Level 51-60 (Heavensward).</summary>
    Heavensward = 2,

    /// <summary>Level 61-70 (Stormblood).</summary>
    Stormblood = 3,

    /// <summary>Level 71-80 (Shadowbringers).</summary>
    Shadowbringers = 4,

    /// <summary>Level 81-90 (Endwalker).</summary>
    Endwalker = 5,

    /// <summary>Level 91-100 (Dawntrail).</summary>
    Dawntrail = 6,
}

/// <summary>
/// Maps an item's required level to an <see cref="ExpansionBand"/> and provides display labels.
/// </summary>
public static class Expansions
{
    /// <summary>
    /// Classifies an item by the level needed to equip it. Level 0 and 1 fall into
    /// <see cref="ExpansionBand.Special"/>; everything else maps to the expansion whose cap covers it.
    /// </summary>
    /// <param name="requiredLevel">The item's equip level (<c>Item.LevelEquip</c>).</param>
    public static ExpansionBand Classify(uint requiredLevel) => requiredLevel switch
    {
        <= 1 => ExpansionBand.Special,
        <= 50 => ExpansionBand.ARealmReborn,
        <= 60 => ExpansionBand.Heavensward,
        <= 70 => ExpansionBand.Stormblood,
        <= 80 => ExpansionBand.Shadowbringers,
        <= 90 => ExpansionBand.Endwalker,
        _ => ExpansionBand.Dawntrail,
    };

    /// <summary>Returns the user-facing label for an expansion band.</summary>
    /// <param name="band">The band to label.</param>
    public static string ToLabel(this ExpansionBand band) => band switch
    {
        ExpansionBand.Special => "Special (Lv 1)",
        ExpansionBand.ARealmReborn => "A Realm Reborn",
        ExpansionBand.Heavensward => "Heavensward",
        ExpansionBand.Stormblood => "Stormblood",
        ExpansionBand.Shadowbringers => "Shadowbringers",
        ExpansionBand.Endwalker => "Endwalker",
        ExpansionBand.Dawntrail => "Dawntrail",
        _ => "Unknown",
    };
}
