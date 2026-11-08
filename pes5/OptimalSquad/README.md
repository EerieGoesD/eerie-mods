# Optimal Squad

Adds **Optimal Squad** to the formation screen menu in Pro Evolution Soccer 5, in place of Controls. It picks the best starting eleven for your squad, and the best formation too if you want.

## The menu

Pressing Optimal Squad opens four rows:

- **Optimal Ability:** the starting eleven with the highest total Overall.
- **Optimal Condition:** the players in the best form start (the red up arrow is best, the grey down arrow worst). Overall only breaks ties.
- **Both:** each player's Overall after his form arrow, using the same form-adjusted stats the match uses.
- **[ ] Keep Formation:** press X to tick or untick. Ticked, the three options only change the players and your formation stays. Unticked, they also pick the best formation.

After an option runs, the menu stays open on it. Press Circle to go back to the normal menu.

## How it works

1. Reads every player in your squad: his Overall, his form and the positions the game lets him play.
2. With Keep Formation unticked, it checks each of the game's 100 formation presets (4-4-2 A-1, 3-4-3 C-2 and so on) and finds the starting eleven with the best total for each. With it ticked, it only checks your current formation.
3. Applies the best one. Each player only goes in a position he can play.
4. Everyone else goes to the bench in their previous order.
5. If a starter who was captain or a kicker goes to the bench, the player who takes his place takes over the duty.

A player's Overall is the rounded average of his 26 main stats, the same number the [Overall Rating](../OverallRating) patch shows. Optimal Squad works with or without that patch.

With Keep Formation ticked, if your formation has a position nobody in the squad can play, nothing changes.

The formation label on screen counts players line by line, so a preset such as 4-4-2 C-1 can show as 4-2-2-2.

## Requirements

- [PCSX2](https://pcsx2.net)
- Pro Evolution Soccer 5, PAL release, serial SLES-53544, CRC BFDF7C5A. The patch does not load for other regions or versions.

## Install

1. Copy `SLES-53544_BFDF7C5A.pnach` into the `patches` folder inside PCSX2's data folder. On a Steam Deck this is `/home/deck/.config/PCSX2/patches/`.
2. Open the game's Properties in PCSX2, go to Patches and tick **Optimal Squad**.
3. Start the game.

If the `patches` folder already has a `SLES-53544_BFDF7C5A.pnach` of yours, copy the `[Optimal Squad]` section into it instead of replacing the file. If it has an older `[Optimal Formation]` section, replace that section.

## Contact

Questions or bug reports: eeriegoesd@gmail.com, [eeriegoesd.com](https://eeriegoesd.com) or [Report Issue](https://github.com/EerieGoesD/eerie-mods/issues)

## Support

If you'd like to support my mods or other projects, feel free to [buy me a coffee](https://buymeacoffee.com/eeriegoesd).
