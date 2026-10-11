# Extra Slots HotSwap

A BepInEx plugin for Valheim that adds switchable gear loadouts on top of shudnal's
[Extra Slots](https://github.com/shudnal/ExtraSlots).

A row of buttons appears below the Extra Slots equipment panel. Each numbered button is a
loadout: the armor, utility items, ammo, food and quick slot items sitting in the Extra Slots
panel. Switching swaps the whole panel for another set in one click or key press.

**Early test version.** It is developed against Extra Slots 1.2.12 and has only had light
in-game testing. Back up your character before using it.

## Requirements

- Valheim with [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4
- [Extra Slots](https://github.com/shudnal/ExtraSlots)
- Optional: a configuration manager mod to change settings in-game

## Install

Put `ExtraSlotsHotSwap.dll` into `BepInEx\plugins\ExtraSlotsHotSwap`, or install the zip with a
mod manager such as Vortex.

## Use

| Action | Result |
| --- | --- |
| Click a numbered button | Switch to that loadout |
| Click `+` | Add an empty loadout and switch to it |
| Click `-` | Remove the active loadout, its items go back to the inventory |
| Right click a numbered button | Remove that loadout, its items go back to the inventory |
| Alt + Left Mouse | Previous loadout |
| Alt + Right Mouse | Next loadout |

- Gear in loadouts that are not active stays with your character and counts toward carry weight,
  unless that loadout has a Megingjord equipped in it (see `Megingjord cancels loadout weight`).
- Removing a loadout needs enough free inventory slots for its items.
- The hotkeys work in the world and in the inventory screen. While Alt is held, attack and block
  are ignored so switching does not swing your weapon.
- Weapons in your hands are only part of a loadout if they sit in a quick slot.

## Settings

Settings live in `BepInEx\config\berv.ExtraSlotsHotSwap.cfg`.

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| General | Max loadouts | 8 | How many loadouts the `+` button will create (1 to 12) |
| General | Show switch message | on | Show a message when the loadout is switched |
| General | Drop all loadouts on death | off | Off: only the active loadout goes to the tombstone. On: the other loadouts drop too, in extra tombstones at the same spot |
| General | Repair stored loadouts | on | The workstation repair button also repairs items of loadouts that are not active |
| General | Upgrade stored loadouts | on | The workstation upgrade tab also lists items of loadouts that are not active. Needs one free inventory slot |
| General | Megingjord cancels loadout weight | on | A loadout with a Megingjord equipped in it weighs nothing, active or not. Each loadout needs its own belt |
| General | Share ammo between loadouts | off | Ammo slots stay in place when switching |
| General | Share food between loadouts | off | Food slots stay in place when switching |
| General | Share quick slots between loadouts | off | Quick slots stay in place when switching |
| Hotkeys | Previous loadout | Alt + Left Mouse | |
| Hotkeys | Next loadout | Alt + Right Mouse | |
| Hotkeys | Suppress attack and block while modifier is held | on | |
| Hotkeys | Switch loadouts in inventory | on | Hotkeys also work with the inventory open |
| Panel | Loadout bar offset | 0, 10 | Position of the button row relative to the panel |
| Panel | Button size | 36 | |

## Things to know

- Loadouts are saved inside the character file. Empty your extra loadouts before uninstalling
  the plugin.
- With `Drop all loadouts on death` on, recovered items return to the inventory, not to their
  loadouts.
- No gamepad support and no tooltips on the buttons yet.

## Build

The build runs in a .NET SDK Docker container, so no SDK is needed on the machine. It
references the game's own assemblies, BepInEx and `ExtraSlots.dll` from your Valheim install.

```powershell
.\build.ps1                       # builds to .\dist
.\build.ps1 -Install              # also copies the dll into BepInEx\plugins\ExtraSlotsHotSwap
.\build.ps1 -GameDir "D:\Valheim" # if the game is not in the default Steam folder
```
