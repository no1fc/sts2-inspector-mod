# STS2 Multiplayer Inspector

A Slay the Spire 2 mod that lets you inspect other players' **deck, hand, relics, potions, and buffs** in real time during multiplayer runs.

> Korean documentation: [README_KR.md](README_KR.md)

## Features

- **Overlay panel** — shows all players with a compact summary; displays Steam display name and selected character class
- **Detail popup** — click any player to open a tabbed view (Draw Pile / Hand / Relics / Potions / Buffs)
- **Real-time hand stats** — hand tab shows each card's current cost, damage, and block as calculated by the game engine (including strength/dexterity scaling and mid-combat bonuses)
- **Image tooltips** — hover over any card, relic, or potion to see its art and description
- **Draggable UI** — move the overlay panel and detail popup anywhere on screen
- **Collapse toggle** — click `─` to shrink the overlay to the toggle bar only; click `+` to expand
- **F7 hotkey** — hide or show the entire mod UI at any time
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

## Keyboard Shortcut

| Key | Action |
|-----|--------|
| `F7` | Toggle the entire mod UI on / off |

## Configuration

Settings are stored at `user://sts2_inspector_settings.cfg`.

| Key | Default | Description |
|-----|---------|-------------|
| `display.show_deck` | `true` | Show Draw Pile tab |
| `display.show_hand` | `true` | Show Hand tab |
| `display.show_relics` | `true` | Show Relics tab |
| `display.show_potions` | `true` | Show Potions tab |
| `display.show_buffs` | `true` | Show Buffs tab |
| `display.panel_x/y` | `1600 / 300` | Overlay panel position |
| `performance.poll_interval_seconds` | `1.5` | How often to poll game state |
| `assets.image_base_path` | *(local path)* | Root folder for image assets |

## Architecture

```
ModStart (InspectorBootstrapper._Ready)
  ├─ ImageCache          — loads card/relic/potion textures from disk
  ├─ ImageTooltip        — hover tooltip (image + description)
  ├─ OverlayPanel        — always-visible player list with collapse support
  ├─ PlayerDetailPopup   — per-player tabbed popup (Draw Pile/Hand/Relics/Potions/Buffs)
  └─ PlayerDataPoller    — polls game state via Reflection every 1.5 s
       ├─ PlayerUpdated  → OverlayPanel + PlayerDetailPopup refresh
       └─ PlayerRemoved  → OverlayPanel cleanup
```

Game state is accessed through `RunManager.Instance` via Reflection, since STS2 does not expose a public modding API.

### Hand card damage resolution

Cards use one of two VarSet patterns:

| Pattern | Example cards | Key structure |
|---------|--------------|---------------|
| Simple | Strike, Grand Entrance | `Damage(BaseValue=6)` |
| Scaling | Soul Storm, Unleash | `CalculationBase(base=9)` + `CalculatedDamage` |

The poller reads `CanonicalValue` (the live engine value) when available, falling back to `BaseValue`. When a canonical value is used, strength/dexterity bonuses are not added manually since they are already included.

## Debugging

Filter the game log with these prefixes:

```
[Inspector]   # polling, snapshot events, hand card stats
[ImageCache]  # texture load success / failure
```

First-encounter dumps (one per session per type):
- `[Inspector] PlayerProps: ...` — player object properties (helps identify Steam name field)
- `[Inspector] VarSet '...': ...` — card variable keys and values

## License

MIT

## Author

SD
