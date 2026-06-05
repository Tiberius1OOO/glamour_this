# Glamour This

A Dalamud plugin for FINAL FANTASY XIV that puts a fast, filterable browser over your Glamour
Dresser. Find armor and outfit sets instantly, spot duplicates at a glance, group pieces into their
outfit glamour sets, and pull pieces out of the dresser into your bags while standing at a dresser.

The window can be opened any time to browse, search and sort. The pull action only works while an
in-game Glamour Dresser is open.

## Features

- A searchable overlay over the Glamour Dresser, Armoire, your bags and the Armoury Chest.
- Opens automatically when you open the in-game Glamour Dresser (can be turned off).
- Instant text search by item name.
- Filtering by:
  - Location: Dresser, Armoire, Bags / Armoury, or Everywhere.
  - Equipment slot (weapon, head, body, ring, and so on).
  - Expansion, approximated from each item's required level; level 1 cosmetic and event gear is
    grouped under "Special (Lv 1)".
  - Duplicates only, to show just the items you hold more than one of.
  - Outfit-ready only.
- Sorting by name, equipment slot, item level, or duplicate count (most copies first).
- Duplicate detection with row highlighting across the dresser, bags and Armoury, so wasted dresser
  space is easy to spot.
- An Armoire toggle to include or hide the (effectively unlimited) cabinet items and focus on the
  dresser.

### Items tab

Lists individual pieces with their slot, location, item level and how many copies you own. Each row
has a **Pull** button that moves one copy from the dresser into your bags. Pulling is left to the
dresser only; putting items back is done through the native dresser window.

### Sets tab

Groups pieces into the game's outfit glamour sets, showing how many of each set's pieces you own and
which slots are still missing. Markers show whether a set is **complete** (you own every piece) and
whether it is already **stored** as a single outfit slot. Each owned piece has its own **Pull**
button so you can take out one piece per click.

With the **Outfit-ready** filter enabled, the Sets tab lists only complete sets you can still store
as an outfit. Pulling a piece into your bags keeps the set on the list (the piece is still owned),
and a set drops off the list on its own once it has been stored as an outfit, so you can work
straight down the list.

## Pulling

- Each click pulls a single piece; the row greys out immediately once it is on its way to your bags.
- A short delay between pulls keeps every press a deliberate single action.
- If a piece can't be pulled because you already hold a one-of-a-kind copy (for example the same
  item in your Armoury or Armoire), the plugin reports the reason instead of failing silently.

## Commands

- `/glamthis` - open or close the browser.
- `/gt` - shortcut for the same.

## Settings

- Open automatically with the Glamour Dresser.
- Highlight duplicate items.
- Icon size for the item list.

## Building

This project uses the [Dalamud.NET.Sdk](https://dalamud.dev). With the .NET 10 SDK installed and
XIVLauncher/Dalamud set up, build from the repository root:

```
dotnet build GlamourThis/GlamourThis.csproj -c Release
```

The built plugin (and its packaging output) is produced under `GlamourThis/bin/`.

## Installing for testing

1. Build the project as above.
2. In-game, open Dalamud settings (`/xlsettings`) and add the build output folder, or drop the built
   folder into `%AppData%\XIVLauncher\devPlugins\`.
3. Enable the plugin from the Dev Plugins list.

## Self-hosted repository

`repo.json` in the repository root is a Dalamud custom plugin repository manifest. Add its raw URL to
Dalamud's custom repository list to install and update the plugin from GitHub releases.

## Disclaimer

FINAL FANTASY XIV (c) SQUARE ENIX CO., LTD. This project is not affiliated with or endorsed by
SQUARE ENIX.
