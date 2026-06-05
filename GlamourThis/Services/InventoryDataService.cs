using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using GlamourThis.Models;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaCabinet = Lumina.Excel.Sheets.Cabinet;

namespace GlamourThis.Services;

/// <summary>
/// Reads the player's glamour-relevant storage from the game and turns it into a flat,
/// UI-friendly list of <see cref="GlamourItemInfo"/>.
///
/// The Glamour Dresser and Armoire are not exposed through Dalamud's high-level inventory enum,
/// so they are read straight from FFXIVClientStructs: the dresser via <c>MirageManager</c> (whose
/// slot order matches the restore call) with a fall back to the <c>ItemFinderModule</c> cache when
/// away from a dresser, and the armoire via <c>UIState.Cabinet</c>. Bags and the Armoury Chest, which
/// only matter for duplicate detection, are read through the stable <see cref="IGameInventory"/>
/// service. Item names, icons, slots and item levels are resolved from the static <c>Item</c> sheet.
/// </summary>
public sealed class InventoryDataService
{
    /// <summary>The id offset the game adds to mark a stored item as high quality.</summary>
    private const uint HighQualityItemIdOffset = 1_000_000;

    private readonly IGameInventory gameInventory;
    private readonly IDataManager dataManager;
    private readonly OutfitSetService outfitSetService;
    private readonly IPluginLog log;

    /// <summary>
    /// Creates the data service.
    /// </summary>
    /// <param name="gameInventory">Dalamud inventory service used to read bag and armoury containers.</param>
    /// <param name="dataManager">Provides access to the static Excel sheets.</param>
    /// <param name="outfitSetService">Supplies outfit-set membership and glamour-ready flags.</param>
    /// <param name="log">Plugin log sink.</param>
    public InventoryDataService(
        IGameInventory gameInventory,
        IDataManager dataManager,
        OutfitSetService outfitSetService,
        IPluginLog log)
    {
        this.gameInventory = gameInventory;
        this.dataManager = dataManager;
        this.outfitSetService = outfitSetService;
        this.log = log;
    }

    /// <summary>
    /// The most recent item snapshot produced by <see cref="Refresh"/>. Empty until the first refresh.
    /// </summary>
    public IReadOnlyList<GlamourItemInfo> Items { get; private set; } = new List<GlamourItemInfo>();

    /// <summary>
    /// The outfit sets the player can interact with, rebuilt on each <see cref="Refresh"/>.
    /// </summary>
    public IReadOnlyList<OutfitSetInfo> Sets { get; private set; } = new List<OutfitSetInfo>();

    /// <summary>
    /// Rebuilds the in-memory snapshot from current game state.
    ///
    /// Must be called on the game's framework thread, because it reads live inventory containers and
    /// raw game memory. The dresser, bags and the Armoury Chest are always scanned: bags must be
    /// included so an item that was pulled out of the dresser still counts as owned (otherwise a set
    /// would wrongly look incomplete the moment one of its pieces is restored). The Armoire is scanned
    /// only when <paramref name="includeArmoire"/> is set, since it can hold an unlimited amount.
    /// </summary>
    /// <param name="includeArmoire">When true, include Armoire (cabinet) contents in the snapshot.</param>
    public void Refresh(bool includeArmoire)
    {
        var merged = new Dictionary<ItemKey, GlamourItemInfo>();

        ScanGlamourChest(merged);

        if (includeArmoire)
            ScanArmoire(merged);

        ScanGameInventory(GameInventoryType.Inventory1, GlamourLocation.Inventory, merged);
        ScanGameInventory(GameInventoryType.Inventory2, GlamourLocation.Inventory, merged);
        ScanGameInventory(GameInventoryType.Inventory3, GlamourLocation.Inventory, merged);
        ScanGameInventory(GameInventoryType.Inventory4, GlamourLocation.Inventory, merged);

        ScanGameInventory(GameInventoryType.ArmoryHead, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryBody, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryHands, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryLegs, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryFeets, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryEar, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryNeck, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryWrist, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryRings, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryMainHand, GlamourLocation.ArmouryChest, merged);
        ScanGameInventory(GameInventoryType.ArmoryOffHand, GlamourLocation.ArmouryChest, merged);

        var items = new List<GlamourItemInfo>(merged.Values);
        Items = items;
        Sets = outfitSetService.BuildSets(items);

        log.Debug($"Glamour This refresh: {items.Count} items, {Sets.Count} sets.");
    }

    /// <summary>
    /// Reads the Glamour Dresser and folds every stored item (and its dresser slot index) into
    /// <paramref name="merged"/>.
    ///
    /// When the dresser has been loaded by the client (the player is at a dresser), the contents are
    /// read from <c>MirageManager</c>, whose slot order matches the <c>RestorePrismBoxItem</c> call so
    /// the per-item slot indices we record can be used to pull items back out. Away from a dresser
    /// that data is cleared, so we fall back to the persistent <c>ItemFinderModule</c> cache, which
    /// still lets the list be browsed (restore is disabled while the dresser is closed anyway).
    /// </summary>
    private unsafe void ScanGlamourChest(Dictionary<ItemKey, GlamourItemInfo> merged)
    {
        var mirage = MirageManager.Instance();
        if (mirage != null && mirage->PrismBoxLoaded)
        {
            var ids = mirage->PrismBoxItemIds;
            for (var slot = 0; slot < ids.Length; slot++)
                AddDresserSlot(ids[slot], slot, merged);
            return;
        }

        var itemFinder = ItemFinderModule.Instance();
        if (itemFinder == null || !itemFinder->IsGlamourDresserCached)
            return;

        foreach (var rawId in itemFinder->GlamourDresserItemIds)
            AddDresserSlot(rawId, -1, merged);
    }

    /// <summary>
    /// Decodes one raw dresser entry (HQ-encoded item id) and folds it into the accumulator, recording
    /// the dresser slot it came from when known.
    /// </summary>
    /// <param name="rawId">The raw, HQ-encoded item id stored in the dresser slot.</param>
    /// <param name="slot">The dresser slot index (0-799), or -1 when reading from the cache fallback.</param>
    /// <param name="merged">The accumulator keyed by (item id, quality).</param>
    private void AddDresserSlot(uint rawId, int slot, Dictionary<ItemKey, GlamourItemInfo> merged)
    {
        if (rawId == 0)
            return;

        // The dresser encodes high-quality pieces as (itemId + 1,000,000); decode that back
        // into the base item id plus an HQ flag so the row resolves against the Item sheet.
        var isHq = rawId >= HighQualityItemIdOffset;
        var itemId = isHq ? rawId - HighQualityItemIdOffset : rawId;

        AddRaw(itemId, isHq, GlamourLocation.GlamourDresser, merged, slot);
    }

    /// <summary>
    /// Reads the Armoire (Cabinet) by walking the static <c>Cabinet</c> sheet and asking the game
    /// which of those entries the player currently has stored. The armoire only reports its contents
    /// after it has been loaded by the client, so nothing is added until that happens.
    /// </summary>
    private unsafe void ScanArmoire(Dictionary<ItemKey, GlamourItemInfo> merged)
    {
        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded())
            return;

        var cabinetSheet = dataManager.GetExcelSheet<LuminaCabinet>();
        if (cabinetSheet == null)
            return;

        foreach (var row in cabinetSheet)
        {
            if (row.RowId == 0)
                continue;

            var itemId = row.Item.RowId;
            if (itemId == 0)
                continue;

            if (uiState->Cabinet.IsItemInCabinet(row.RowId))
                AddRaw(itemId, false, GlamourLocation.Armoire, merged);
        }
    }

    /// <summary>
    /// Reads a single Dalamud inventory container (bags or armoury) and folds its contents into
    /// <paramref name="merged"/>.
    /// </summary>
    /// <param name="type">The Dalamud inventory container to read.</param>
    /// <param name="location">The logical location these items should be attributed to.</param>
    /// <param name="merged">The accumulator keyed by (item id, quality).</param>
    private void ScanGameInventory(
        GameInventoryType type,
        GlamourLocation location,
        Dictionary<ItemKey, GlamourItemInfo> merged)
    {
        var slots = gameInventory.GetInventoryItems(type);

        foreach (var slot in slots)
        {
            if (slot.BaseItemId == 0)
                continue;

            AddRaw(slot.BaseItemId, slot.IsHq, location, merged);
        }
    }

    /// <summary>
    /// Folds one copy of an item (identified by id and quality) into the accumulator, either bumping
    /// an existing entry's location count or resolving and inserting a new entry.
    /// </summary>
    private void AddRaw(
        uint itemId,
        bool isHighQuality,
        GlamourLocation location,
        Dictionary<ItemKey, GlamourItemInfo> merged,
        int dresserSlot = -1)
    {
        if (itemId == 0)
            return;

        var key = new ItemKey(itemId, isHighQuality);
        if (merged.TryGetValue(key, out var existing))
        {
            AddLocation(existing, location);
            if (dresserSlot >= 0 && !existing.DresserSlots.Contains(dresserSlot))
                existing.DresserSlots.Add(dresserSlot);
            return;
        }

        var resolved = ResolveItem(itemId, isHighQuality, location);
        if (resolved is not null)
        {
            if (dresserSlot >= 0)
                resolved.DresserSlots.Add(dresserSlot);
            merged[key] = resolved;
        }
    }

    /// <summary>
    /// Builds a <see cref="GlamourItemInfo"/> from a raw item id by joining it with the static
    /// <c>Item</c> sheet and the outfit-set lookups. Returns <c>null</c> if the id is not in the sheet.
    /// </summary>
    /// <param name="itemId">The normal-quality item row id.</param>
    /// <param name="isHighQuality">Whether the stored copy is high quality.</param>
    /// <param name="location">The location the first copy was found in.</param>
    private GlamourItemInfo? ResolveItem(uint itemId, bool isHighQuality, GlamourLocation location)
    {
        var sheet = dataManager.GetExcelSheet<LuminaItem>();
        if (sheet is null || !sheet.TryGetRow(itemId, out var row))
            return null;

        var info = new GlamourItemInfo
        {
            ItemId = itemId,
            Name = row.Name.ExtractText(),
            IconId = row.Icon,
            ItemLevel = row.LevelItem.RowId,
            RequiredLevel = row.LevelEquip,
            EquipSlotCategory = row.EquipSlotCategory.RowId,
            SlotLabel = SlotLabels.ForEquipSlotCategory(row.EquipSlotCategory.RowId),
            IsHighQuality = isHighQuality,
            IsOutfitGlamourReady = outfitSetService.IsGlamourReady(itemId),
            SetId = outfitSetService.TryGetSetId(itemId, out var setId) ? setId : null,
        };

        info.Locations.Add(new GlamourItemLocation(location, 1));
        return info;
    }

    /// <summary>
    /// Adds one more copy of an already-resolved item at the given location, either bumping the count
    /// of an existing location entry or appending a new one.
    /// </summary>
    private static void AddLocation(GlamourItemInfo item, GlamourLocation location)
    {
        for (var i = 0; i < item.Locations.Count; i++)
        {
            if (item.Locations[i].Location == location)
            {
                item.Locations[i] = item.Locations[i] with { Count = item.Locations[i].Count + 1 };
                return;
            }
        }

        item.Locations.Add(new GlamourItemLocation(location, 1));
    }

    /// <summary>
    /// Composite key that keeps normal-quality and high-quality copies of the same item separate.
    /// </summary>
    private readonly record struct ItemKey(uint ItemId, bool IsHighQuality);
}
