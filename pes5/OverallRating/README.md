# Overall Rating

Adds an Overall rating for every player and team in Pro Evolution Soccer 5.

- Edit Player: an Overall row above Attack, on the player page and in the stats editor.
- Formation screen before a match: an Overall row under Position in the player stats panel. Aggression moves to the top of the second page to make room.
- Select team: the team's Overall next to its name, for example `Arsenal | OVR 76`, in Exhibition, cups, leagues and Master League.

## How it is worked out

- Player Overall: the average of the 26 main stats, rounded, from 1 to 99. The stats are Attack, Defence, Balance, Stamina, Top speed, Acceleration, Response, Agility, Dribble accuracy, Dribble speed, Short pass accuracy, Short pass speed, Long pass accuracy, Long pass speed, Shot accuracy, Shot power, Shot technique, Free kick accuracy, Curling, Header, Jump, Technique, Aggression, Mentality, Goalkeeping skills and Teamwork. Consistency, Condition and both weak foot stats are not counted.
- Team Overall: the average Overall of the team's starting eleven.

## Requirements

- [PCSX2](https://pcsx2.net)
- Pro Evolution Soccer 5, PAL release, serial SLES-53544, CRC BFDF7C5A. The patch does not load for other regions or versions.

## Install

1. Copy `SLES-53544_BFDF7C5A.pnach` into the `patches` folder inside PCSX2's data folder. On a Steam Deck this is `/home/deck/.config/PCSX2/patches/`.
2. Open the game's Properties in PCSX2, go to Patches and tick **Overall Rating**.
3. Start the game.

If the `patches` folder already has a `SLES-53544_BFDF7C5A.pnach` of yours, copy the `[Overall Rating]` section into it instead of replacing the file.

## Contact

Questions or bug reports: eeriegoesd@gmail.com, [eeriegoesd.com](https://eeriegoesd.com) or [Report Issue](https://github.com/EerieGoesD/eerie-mods/issues)

## Support

If you'd like to support my mods or other projects, feel free to [buy me a coffee](https://buymeacoffee.com/eeriegoesd).
