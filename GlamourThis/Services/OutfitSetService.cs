using System.Collections.Generic;
using Dalamud.Plugin.Services;
using GlamourThis.Models;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaMirageSet = Lumina.Excel.Sheets.MirageStoreSetItem;

namespace GlamourThis.Services;

/// <summary>
/// Owns all static knowledge about outfit glamour sets.
///
/// The game stores outfit sets in the <c>MirageStoreSetItem</c> sheet: each row's id is the
/// condensed "outfit" item, and its slot columns (Head, Body, Hands, ...) link to the individual
/// pieces. This service reads that sheet once, builds fast lookups from it, and then joins those
/// definitions with what the player actually owns to produce <see cref="OutfitSetInfo"/> groups.
/// </summary>
public sealed class OutfitSetService
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    /// <summary>Maps a piece's item id to the outfit set id it belongs to.</summary>
    private readonly Dictionary<uint, uint> itemToSet = new();

    /// <summary>Maps an outfit set id to the ordered pieces that define it.</summary>
    private readonly Dictionary<uint, List<SetMember>> setMembers = new();

    /// <summary>
    /// Creates the set service. Call <see cref="Build"/> once before using any lookups.
    /// </summary>
    /// <param name="dataManager">Provides access to the static Excel sheets.</param>
    /// <param name="log">Plugin log sink.</param>
    public OutfitSetService(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    /// <summary>
    /// Reads the <c>MirageStoreSetItem</c> sheet and populates the static set lookups.
    /// Safe to call more than once; each call rebuilds the lookups from scratch.
    /// </summary>
    public void Build()
    {
        itemToSet.Clear();
        setMembers.Clear();

        var setSheet = dataManager.GetExcelSheet<LuminaMirageSet>();
        if (setSheet is null)
        {
            log.Warning("Glamour This: MirageStoreSetItem sheet unavailable; outfit sets disabled.");
            return;
        }

        foreach (var set in setSheet)
        {
            var setId = set.RowId;
            if (setId == 0)
                continue;

            var members = new List<SetMember>(11);
            AddMember(members, set.MainHand.RowId, "Weapon");
            AddMember(members, set.OffHand.RowId, "Shield");
            AddMember(members, set.Head.RowId, "Head");
            AddMember(members, set.Body.RowId, "Body");
            AddMember(members, set.Hands.RowId, "Hands");
            AddMember(members, set.Legs.RowId, "Legs");
            AddMember(members, set.Feet.RowId, "Feet");
            AddMember(members, set.Earrings.RowId, "Earrings");
            AddMember(members, set.Necklace.RowId, "Necklace");
            AddMember(members, set.Bracelets.RowId, "Bracelets");
            AddMember(members, set.Ring.RowId, "Ring");

            if (members.Count == 0)
                continue;

            setMembers[setId] = members;
            foreach (var member in members)
                itemToSet[member.ItemId] = setId;
        }

        log.Debug($"Glamour This: loaded {setMembers.Count} outfit sets.");
    }

    /// <summary>
    /// Returns true when an item can be condensed into an outfit glamour, i.e. it is a member of
    /// any set. Mirrors the in-game "Outfit Glamour-ready Item" flag.
    /// </summary>
    /// <param name="itemId">The normal-quality item id to test.</param>
    public bool IsGlamourReady(uint itemId) => itemToSet.ContainsKey(itemId);

    /// <summary>
    /// Looks up the outfit set an item belongs to.
    /// </summary>
    /// <param name="itemId">The normal-quality item id to test.</param>
    /// <param name="setId">Receives the set id when the item is part of a set.</param>
    /// <returns>True when the item belongs to a set.</returns>
    public bool TryGetSetId(uint itemId, out uint setId) => itemToSet.TryGetValue(itemId, out setId);

    /// <summary>
    /// Joins the static set definitions with the player's owned items to produce the set groups
    /// shown in the UI. Only sets the player has some stake in (owns a piece, or has the set stored
    /// as a condensed outfit) are returned, so the list stays focused on the player's collection.
    /// </summary>
    /// <param name="ownedItems">The current owned-item snapshot from the inventory service.</param>
    public IReadOnlyList<OutfitSetInfo> BuildSets(IReadOnlyList<GlamourItemInfo> ownedItems)
    {
        var ownedById = new Dictionary<uint, GlamourItemInfo>();
        var ownedItemIds = new HashSet<uint>();
        foreach (var item in ownedItems)
        {
            ownedById[item.ItemId] = item;
            ownedItemIds.Add(item.ItemId);
        }

        var itemSheet = dataManager.GetExcelSheet<LuminaItem>();
        var result = new List<OutfitSetInfo>();

        foreach (var (setId, members) in setMembers)
        {
            // A set is "stored as an outfit" when the condensed set item id is itself owned.
            var storedAsOutfit = ownedItemIds.Contains(setId);

            var ownsAnyPiece = false;
            var pieces = new List<OutfitSetPiece>(members.Count);
            foreach (var member in members)
            {
                ownedById.TryGetValue(member.ItemId, out var ownedItem);
                if (ownedItem is not null)
                    ownsAnyPiece = true;

                pieces.Add(new OutfitSetPiece
                {
                    ItemId = member.ItemId,
                    Name = ResolveName(itemSheet, member.ItemId),
                    SlotLabel = member.SlotLabel,
                    OwnedItem = ownedItem,
                });
            }

            if (!ownsAnyPiece && !storedAsOutfit)
                continue;

            result.Add(new OutfitSetInfo
            {
                SetId = setId,
                Name = ResolveSetName(itemSheet, setId, pieces),
                IconId = ResolveIcon(itemSheet, setId, members),
                Pieces = pieces,
                IsStoredAsOutfit = storedAsOutfit,
            });
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    /// <summary>
    /// Appends a set member to the working list when the slot actually links to an item.
    /// </summary>
    private static void AddMember(List<SetMember> members, uint itemId, string slotLabel)
    {
        if (itemId != 0)
            members.Add(new SetMember(itemId, slotLabel));
    }

    /// <summary>
    /// Resolves a display name for a single item id, falling back to the raw id when the item is
    /// missing from the sheet.
    /// </summary>
    private static string ResolveName(Lumina.Excel.ExcelSheet<LuminaItem>? sheet, uint itemId)
    {
        if (sheet is not null && sheet.TryGetRow(itemId, out var row))
            return row.Name.ExtractText();
        return $"#{itemId}";
    }

    /// <summary>
    /// Resolves a name for the whole set, preferring the condensed set item's name and falling back
    /// to the body piece, then any piece.
    /// </summary>
    private static string ResolveSetName(
        Lumina.Excel.ExcelSheet<LuminaItem>? sheet,
        uint setId,
        List<OutfitSetPiece> pieces)
    {
        if (sheet is not null && sheet.TryGetRow(setId, out var setRow))
        {
            var name = setRow.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        foreach (var piece in pieces)
            if (!string.IsNullOrWhiteSpace(piece.Name))
                return piece.Name;

        return $"Set #{setId}";
    }

    /// <summary>
    /// Resolves an icon for the set, preferring the condensed set item and falling back to the
    /// first member piece.
    /// </summary>
    private static ushort ResolveIcon(
        Lumina.Excel.ExcelSheet<LuminaItem>? sheet,
        uint setId,
        List<SetMember> members)
    {
        if (sheet is null)
            return 0;

        if (sheet.TryGetRow(setId, out var setRow) && setRow.Icon != 0)
            return setRow.Icon;

        foreach (var member in members)
            if (sheet.TryGetRow(member.ItemId, out var memberRow))
                return memberRow.Icon;

        return 0;
    }

    /// <summary>
    /// A single piece definition within a set: the piece's item id and its slot label.
    /// </summary>
    private readonly record struct SetMember(uint ItemId, string SlotLabel);
}
