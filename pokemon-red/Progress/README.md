# Progress

Pokémon Red (USA, Europe) (SGB Enhanced). Adds PROGRESS to the Start menu, above EXIT. It shows everything still left to do for 100%, and the next step to get there.

## What it shows

The first screen shows the overall percentage, the NEXT step and where to do it, and a count for each list. Pick a list with A to see what is still missing, two lines per entry: what it is and where to get it.

| List | What it counts |
| --- | --- |
| STORY | 33 main story steps in order: badges, rival battles, Rocket hideouts, Elite Four |
| POKéDEX | All 151, with where to catch, evolve or trade for each one in Red |
| ITEMS | 104 item balls on the maps, plus the POTION a new game leaves in your PC |
| HIDDEN | 52 hidden items and 12 hidden coin spots on the Game Corner floor |
| GIFTS | 41 items, TMs, HMs and rods people give you, and the diploma for a full Pokédex |
| TRAINERS | 319 trainer battles, by place, including every rival and Rocket fight outside the story |
| TRADES | 9 in-game trades |
| SPECIAL | 20 one-off Pokémon: legendaries, Snorlax, gifts, fossils, the Power Plant Voltorb and Electrode |
| TOWNS | All 11 towns, each counted once you have walked into it (the places FLY can take you) |

Each list shows done and total, plus done and left as percentages. Up and down scroll by one entry, left and right by a page, B goes back.

## NEXT

NEXT is one route through all nine lists. It only shows something once it can be done: after the badge, HM, rod, Itemfinder or number of Pokémon it needs, and from the right side of the map (items behind a Cut tree, water or a boulder wait for that HM). Everything reachable is cleared before the story moves on, so getting a rod sends you back to the places where that rod catches something new. Things the game takes away for good (the S.S. Anne after it sails, Silph Co. trainers after Giovanni, the first Route 22 rival battle after Brock) are skipped once they are gone. Trades with another game and level-ups come last.

## Warnings

Walking into a place where something can be lost for good pauses the game with a WARNING box at the bottom, while there is still something there you don't have. Where it lists things to finish, it says how many are not done yet. Any button closes it.

| Where | What it warns about |
| --- | --- |
| Oak's Lab | Only one of the three starters; the others need trades |
| Pewter Gym | Beating Brock ends the first Route 22 rival battle |
| Mt. Moon B2F | Only one fossil, Dome or Helix |
| S.S. Anne Captain's room | Getting HM01 makes the ship sail when you leave (with how many items and trainers are left on board) |
| S.S. Anne 1F, holding HM01 | The ship sails as soon as you step off (same count) |
| Route 12 and Route 16 | If that Snorlax faints it is gone, while you don't own one |
| Fighting Dojo | Only one gift, Hitmonlee or Hitmonchan |
| Silph Co. 11F | Beating Giovanni sends the Rockets away (with how many Silph Co. trainers are left) |
| Power Plant, Seafoam B4F, Victory Road 2F, Cerulean Cave B1F | Zapdos, Articuno, Moltres or Mewtwo is gone for good if it faints, while you don't own it |

## In-game HUD

The last row of the PROGRESS screen, IN-GAME HUD, switches between YES and NO with A. With YES, pressing SELECT while walking shows the NEXT step in a box at the bottom of the screen; any button closes it. The setting is kept in the save.

Existing saves work: nothing that was already in the game moved. The Start menu is one column wider so PROGRESS fits.

## Install

Apply `Progress.ips` to your own copy of the game (SHA-1 `ea9bcae617fdf159b045185467ae58b2e4a48b9a`) with any IPS patcher, then keep the original file name so the emulator finds your save.

## Building from source

`src/pokered.diff` applies to [pret/pokered](https://github.com/pret/pokered) at commit `ba9c056`. `src/gen_progress.py` (with `src/reach.py` next to it) writes `engine/menus/progress_data.asm` from the disassembly's own data and maps:

```
python3 src/gen_progress.py <pokered folder>
make red
```

Built with RGBDS 1.0.4.
