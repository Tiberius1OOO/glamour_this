namespace GlamourThis.Services;

/// <summary>
/// Maps <c>EquipSlotCategory</c> row ids to short, human-readable slot names.
///
/// The ids are the stable game-side values for equipment slots; only the slots that can be
/// glamoured are mapped, everything else falls back to a generic label.
/// </summary>
public static class SlotLabels
{
    /// <summary>
    /// Returns a short label such as "Head" or "Body" for the given equip slot category id.
    /// </summary>
    /// <param name="equipSlotCategory">The <c>EquipSlotCategory</c> row id from the Item sheet.</param>
    public static string ForEquipSlotCategory(uint equipSlotCategory) => equipSlotCategory switch
    {
        1 => "Weapon",
        2 => "Shield",
        3 => "Head",
        4 => "Body",
        5 => "Hands",
        6 => "Waist",
        7 => "Legs",
        8 => "Feet",
        9 => "Earrings",
        10 => "Necklace",
        11 => "Bracelets",
        12 => "Ring",
        13 => "Ring",
        17 => "Soul Crystal",
        _ => "Other",
    };
}
