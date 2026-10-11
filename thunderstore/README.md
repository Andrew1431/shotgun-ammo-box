# ShotgunAmmoBox

A steel ammo can for shotgun shells.

- **Buy it** from the terminal for $400 (`buy ammo box`).
- **Holds 20 shells.** Hold the box and press [LMB] while looking at a shell to pack it, or with no shell in sight to
  pack every shell you're carrying. [Q] takes one back out.
- **Reload from the box**: with the box anywhere in your inventory, the shotgun reloads straight from it once you're
  out of loose shells.
- **Shells in the store**: `buy shells` (or `buy shotgun`) for $100 each.
- Shells keep their scrap value while they're in the box. The box remembers its shells between sessions.

Everyone in the lobby needs the mod.

## Config

`BepInEx/config/ShotgunAmmoBox.cfg`, created the first time you launch:

| Setting | Default | |
|---|---|---|
| `Store.ShellPrice` | 100 | Credits per shell |
| `Store.BoxPrice` | 400 | Credits per ammo box |
| `Box.Capacity` | 20 | Shells per box (host's value is used) |
