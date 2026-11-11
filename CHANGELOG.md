# Changelog

## 1.1.2

Makes `config.json` safe: the mod no longer loses picks when the file is broken, empty or reset.
Nothing to do when updating, your picks carry over. Going back to 1.1.1 or 1.1.0 later is safe
too and keeps the same outfits (but not to 1.0.1, which already misread 1.1.x configs).

**Your settings file**
- Reading `config.json` no longer rewrites it. The mod used to write the file back every time it
  read it (at launch, every day, every time the menu opened), which deleted anything it didn't
  recognise. Now it writes only when you press Save or Default, and fields it doesn't know are
  kept. Comments in the file are still dropped when it saves.
- A `config.json` that can't be read (a typo, or another program holding it open) is never
  replaced. It used to be treated as empty, and the next Save wrote over it, losing every pick.
  Now a red notice at the top of the mod's page says so, Save and Default are refused until the
  file is fixed, and the SMAPI log has the error and the file's path. When a backup exists, the
  notice and the log say how to go back to it.
- New backups next to `config.json`: `config.backup.json` (the file before the mod first saves
  it in a game session) and `config.before-reset.json` (the file before you press Default). A
  backup is only replaced by a file that has something in it (a picked outfit or your own
  hotkey), so pressing Default twice, or a save cut off by a crash, can't wipe the last good copy.
- If `config.json` turns up empty while `config.backup.json` still has picks (for example after a
  crash during a save), the page says so and points at the backup instead of quietly starting
  over.
- `config.json` is still created with the default settings on first launch, as before, and is
  never recreated over a file that exists.
- A read-only `config.json` is now read and applied (1.1.1 started from scratch instead).
  Saving to it fails with a message in the log, and the read-only flag is no longer copied onto
  the backups, where it used to block every later save.
- Setting all picks, or one character's, to `null` by hand no longer crashes the mod. A `null`
  anywhere in the picks counts as nothing picked.

**The menu**
- Cancel really cancels. Outfits ticked and then cancelled could still show up on NPCs after the
  next reload, or be saved by GMCM's keybinds page.
- Searching (Enter or the clear button) keeps outfits you ticked but haven't saved yet.
- Saving on GMCM's keybinds page saves only this mod's hotkey, not unsaved outfit picks.
- Pressing Enter on another mod's GMCM page no longer jumps to this mod's page, which threw away
  the other mod's unsaved changes.
- Picking an outfit for a slot that a hand-typed key already fills updates that key (keeping your
  spelling) instead of adding a second one.
- `outfit_categories` shows what's saved, not ticks you haven't saved yet.

**Old configs**
- Updating an old config now only renames a lone 1.0.1 `Beach` category to `Island`. Every key
  used to be rebuilt from its parts, so hand-typed keys could lose parts (`Weather_Sunny::Spring`
  became `Weather_Sunny`) and a value could be dropped when two keys collided. Hand-typed keys now
  stay in the file as written and apply exactly as in 1.1.1. When two keys set the same slot,
  both are kept, the one 1.1.1 used still applies, and the SMAPI log names the other.
- 1.0.1 picks of your own PNGs with a weather or place in the file name (such as
  `Haley_Winter_Snowing_Outdoor.png`) work again. They had silently stopped applying in 1.1.0.
  Such a pick is now found by the character and the file name.

## 1.1.1

- Fixed: outfit checkboxes didn't respond to clicks when the game's Zoom Level and UI Scale were
  set to different values.
