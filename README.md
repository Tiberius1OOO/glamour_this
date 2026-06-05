# Glamour This

A Dalamud plugin for FINAL FANTASY XIV that replaces the Glamour Dresser and Armoire workflow
with a fast, filterable browser. Find armor and outfit sets instantly, spot duplicates at a glance,
group pieces into their outfit glamour sets, and pull items in or out while standing at a dresser.

## Features

- A searchable, sortable overlay over the Glamour Dresser, Armoire and (optionally) your bags.
- Instant text search and filtering by equipment slot.
- Duplicate detection with highlighting, so wasted dresser space is easy to spot.
- Outfit set grouping built from the game's outfit glamour data, showing owned / total pieces and
  which slots are still missing.
- "Outfit Glamour-ready" badges that mirror the in-game marker.
- Pull-out / put-back actions for single items and complete outfits (active while a dresser is open).

## Commands

- `/glamthis` - open or close the browser.
- `/gt` - shortcut for the same.

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
