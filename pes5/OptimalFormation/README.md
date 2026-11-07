# Optimal Formation

Adds **Optimal Formation** to the formation screen menu in Pro Evolution Soccer 5, in place of Controls. One press sets the best formation and starting eleven for your squad.

## What it does

1. Reads every player in your squad: his Overall and the positions the game lets him play.
2. For each of the game's 100 formation presets (4-4-2 A-1, 3-4-3 C-2 and so on), finds the starting eleven with the highest total Overall. Each player only goes in a position he can play.
3. Applies the preset with the highest total and puts those eleven players in it.
4. Everyone else goes to the bench in their previous order.
5. If a starter who was captain or a kicker goes to the bench, the player who takes his place takes over the duty.

A player's Overall is the rounded average of his 26 main stats, the same number the [Overall Rating](../OverallRating) patch shows. Optimal Formation works with or without that patch.

The formation label on screen counts players line by line, so a preset such as 4-4-2 C-1 can show as 4-2-2-2.

## Requirements

- [PCSX2](https://pcsx2.net)
- Pro Evolution Soccer 5, PAL release, serial SLES-53544, CRC BFDF7C5A. The patch does not load for other regions or versions.

## Install

1. Copy `SLES-53544_BFDF7C5A.pnach` into the `patches` folder inside PCSX2's data folder. On a Steam Deck this is `/home/deck/.config/PCSX2/patches/`.
2. Open the game's Properties in PCSX2, go to Patches and tick **Optimal Formation**.
3. Start the game.

If the `patches` folder already has a `SLES-53544_BFDF7C5A.pnach` of yours, copy the `[Optimal Formation]` section into it instead of replacing the file.

## Contact

Questions or bug reports: eeriegoesd@gmail.com, [eeriegoesd.com](https://eeriegoesd.com) or [Report Issue](https://github.com/EerieGoesD/eerie-mods/issues)

## Support

If you'd like to support my mods or other projects, feel free to [buy me a coffee](https://buymeacoffee.com/eeriegoesd).
