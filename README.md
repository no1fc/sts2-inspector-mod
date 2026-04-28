# STS2 Multiplayer Inspector

A Slay the Spire 2 mod that lets you inspect other players' **deck, hand, relics, and potions** in real time during multiplayer runs.

## Features

- **Overlay panel** — shows all players in the current run with a compact summary
- **Detail popup** — click any player to open a tabbed view (Deck / Hand / Relics / Potions)
- **Image tooltips** — hover over any card, relic, or potion to see its art and description
- **Draggable UI** — move the overlay panel and detail popup anywhere on screen
- **Persistent settings** — panel position and visible columns are saved between sessions
- Solo mode supported (works as a single-player run inspector too)

## Requirements

| | |
|---|---|
| Game | Slay the Spire 2 (Steam) |
| Runtime | .NET 9.0 |
| Build tools | Godot 4 + `dotnet` CLI |
| Image assets | `data_sts2/` folder (see below) |

## Installation

1. Build the mod:
   ```bash
   dotnet build testmode1.csproj
   ```
   The DLL is automatically copied to:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\testmode1\
   ```

2. Place image assets at the path configured in `ModSettings` (default: `C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2\`):
   ```
   data_sts2/
   ├─ images/
   │   ├─ relics/          # individual PNGs (256×256)
   │   ├─ potions/         # individual PNGs
   │   └─ atlases/
   │       ├─ card_atlas_0.png
   │       ├─ card_atlas_1.png
   │       ├─ card_atlas_2.png
   │       └─ card_atlas.sprites/   # .tres sprite region files (per-character folders)
   ```

3. Launch the game and enable **STS2 Multiplayer Inspector** in the mod menu.

## Configuration

Settings are stored at `user://sts2_inspector_settings.cfg`.

| Key | Default | Description |
|-----|---------|-------------|
| `display.show_deck` | `true` | Show deck column in overlay |
| `display.show_hand` | `true` | Show hand column in overlay |
| `display.show_relics` | `true` | Show relics column |
| `display.show_potions` | `true` | Show potions column |
| `display.panel_x/y` | `1600 / 300` | Overlay panel position |
| `performance.poll_interval_seconds` | `1.5` | How often to poll game state |
| `assets.image_base_path` | *(local path)* | Root folder for image assets |

## Architecture

```
ModStart (InspectorBootstrapper._Ready)
  ├─ ImageCache          — loads card/relic/potion textures from disk
  ├─ ImageTooltip        — hover tooltip (image + description)
  ├─ OverlayPanel        — always-visible player list
  ├─ PlayerDetailPopup   — per-player tabbed popup
  └─ PlayerDataPoller    — polls game state via Reflection every 1.5 s
       ├─ PlayerUpdated  → OverlayPanel + PlayerDetailPopup refresh
       └─ PlayerRemoved  → OverlayPanel cleanup
```

Game state is accessed through `RunManager.Instance` via Reflection, since STS2 does not expose a public modding API. The poller resolves `LocString` display names and maps them back to English image keys for asset loading.

## Debugging

Filter the game log with these prefixes:

```
[Inspector]   # polling and snapshot events
[ImageCache]  # texture load success / failure
```

## License

MIT

## Author

SD
