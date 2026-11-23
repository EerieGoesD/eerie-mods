"""Generate engine/menus/progress_data.asm for the PROGRESS start menu option.

Everything is read from the pokered disassembly, except the story steps, the gift list and the
special Pokemon list, which are written by hand below and checked against the disassembly's
constants. Run from anywhere: python3 gen_progress.py <pokered dir>
"""
import collections
import glob
import os
import re
import sys

ROOT = sys.argv[1]
OUT = os.path.join(ROOT, "engine", "menus", "progress_data.asm")


def rd(p):
    return open(os.path.join(ROOT, p), encoding="utf-8").read()


def strip_comments(s):
    return "\n".join(l.split(";")[0] for l in s.splitlines())


def red_only(s):
    """Drop IF DEF(_BLUE) blocks and unwrap IF DEF(_RED) blocks."""
    out, skip = [], []
    for l in s.splitlines():
        t = l.strip()
        if t.startswith("IF DEF(_BLUE)"):
            skip.append(True)
            continue
        if t.startswith("IF DEF(_RED)"):
            skip.append(False)
            continue
        if t == "ENDC" and skip:
            skip.pop()
            continue
        if any(skip):
            continue
        out.append(l)
    return "\n".join(out)


# ---------------------------------------------------------------- constants and names
MAPS = re.findall(r"map_const (\w+),", rd("constants/map_constants.asm"))
MAPID = {m: i for i, m in enumerate(MAPS)}

events_src = strip_comments(rd("constants/event_constants.asm"))
EVENTS = set(re.findall(r"const (EVENT_\w+)", events_src))

def const_list(text, macro="const"):
    """Constant names in value order; const_skip leaves None gaps."""
    out = []
    for l in strip_comments(text).splitlines():
        t = l.split()
        if not t:
            continue
        if t[0] == macro:
            out.append(t[1].rstrip(","))
        elif t[0] == "const_skip":
            out.extend([None] * (int(t[1]) if len(t) > 1 else 1))
        elif t[0] == "const_next":
            raise ValueError("const_next not handled")
    return out


SPECIES = const_list(rd("constants/pokemon_constants.asm"))
mon_names = re.findall(r'dname "([^"]*)"', rd("data/pokemon/names.asm"))
MONNAME = {SPECIES[i + 1]: mon_names[i] for i in range(len(mon_names))}
DEX = re.findall(r"const DEX_(\w+)", rd("constants/pokedex_constants.asm"))
assert len(DEX) == 151, len(DEX)
for d in DEX:
    assert d in MONNAME, d

item_src = strip_comments(rd("constants/item_constants.asm"))
item_consts = re.findall(r"^\s*const (\w+)", item_src.split("DEF NUM_ITEMS")[0], re.M)
item_names = re.findall(r'li "([^"]*)"', rd("data/items/names.asm"))
ITEMNAME = {}
for i, c in enumerate(item_consts[1:]):
    ITEMNAME[c] = item_names[i]
move_names = re.findall(r'li "([^"]*)"', rd("data/moves/names.asm"))
move_consts = re.findall(r"^\s*const (\w+)", strip_comments(rd("constants/move_constants.asm")), re.M)
MOVENAME = {move_consts[i + 1]: move_names[i] for i in range(len(move_names))}
for n, mv in enumerate(re.findall(r"^\s*add_tm (\w+)", item_src, re.M), 1):
    ITEMNAME["TM_" + mv] = "TM%02d" % n
    ITEMNAME["TM%02d" % n] = "TM%02d %s" % (n, MOVENAME[mv])
for n, mv in enumerate(re.findall(r"^\s*add_hm (\w+)", item_src, re.M), 1):
    ITEMNAME["HM%02d" % n] = "HM%02d %s" % (n, MOVENAME[mv])
TMFULL = {}
for n, mv in enumerate(re.findall(r"^\s*add_tm (\w+)", item_src, re.M), 1):
    TMFULL["TM_" + mv] = "TM%02d %s" % (n, MOVENAME[mv])

trainer_class_consts = re.findall(r"trainer_const (\w+)", rd("constants/trainer_constants.asm"))
trainer_class_names = re.findall(r'li "([^"]*)"', rd("data/trainers/names.asm"))
CLASSNAME = {trainer_class_consts[i + 1]: trainer_class_names[i] for i in range(len(trainer_class_names))}

# map header label <-> map const (script and object files are named after the label)
LABEL2MAP = {}
for f in glob.glob(os.path.join(ROOT, "data/maps/headers/*.asm")):
    m = re.search(r"map_header (\w+), (\w+),", open(f).read())
    LABEL2MAP[m.group(1)] = m.group(2)

# ---------------------------------------------------------------- map display names
SPECIAL_NAMES = {
    "REDS_HOUSE_1F": "RED'S HOUSE", "REDS_HOUSE_2F": "RED'S HOUSE 2F", "BLUES_HOUSE": "BLUE'S HOUSE",
    "OAKS_LAB": "OAK'S LAB", "SS_ANNE_1F": "S.S.ANNE 1F", "SS_ANNE_2F": "S.S.ANNE 2F", "SS_ANNE_3F": "S.S.ANNE 3F",
    "SS_ANNE_B1F": "S.S.ANNE B1F", "SS_ANNE_BOW": "S.S.ANNE BOW", "SS_ANNE_KITCHEN": "S.S.ANNE KITCHEN",
    "SS_ANNE_CAPTAINS_ROOM": "S.S.ANNE CAPTAIN", "SS_ANNE_1F_ROOMS": "S.S.ANNE 1F ROOMS",
    "SS_ANNE_2F_ROOMS": "S.S.ANNE 2F ROOMS", "SS_ANNE_B1F_ROOMS": "S.S.ANNE B1F ROOM",
    "MT_MOON_1F": "MT.MOON 1F", "MT_MOON_B1F": "MT.MOON B1F", "MT_MOON_B2F": "MT.MOON B2F",
    "MT_MOON_POKECENTER": "MT.MOON POKéCENTER", "POKEMON_FAN_CLUB": "POKéMON FAN CLUB",
    "UNDERGROUND_PATH_NORTH_SOUTH": "UNDERGROUND N-S", "UNDERGROUND_PATH_WEST_EAST": "UNDERGROUND W-E",
    "UNDERGROUND_PATH_ROUTE_5": "ROUTE 5 TUNNEL", "SILPH_CO_1F": "SILPH CO.1F",
    "MR_FUJIS_HOUSE": "MR.FUJI'S HOUSE", "MR_PSYCHICS_HOUSE": "MR.PSYCHIC'S HOUSE",
    "COPYCATS_HOUSE_2F": "COPYCAT'S HOUSE", "WARDENS_HOUSE": "WARDEN'S HOUSE",
    "GAME_CORNER": "GAME CORNER", "CELADON_MANSION_ROOF_HOUSE": "CELADON MANSION",
    "SAFARI_ZONE_SECRET_HOUSE": "SAFARI ZONE HOUSE", "SAFARI_ZONE_CENTER": "SAFARI CENTER", "ROUTE_16_FLY_HOUSE": "ROUTE 16 HOUSE",
    "ROUTE_12_SUPER_ROD_HOUSE": "ROUTE 12 HOUSE", "VERMILION_OLD_ROD_HOUSE": "VERMILION CITY",
    "FUCHSIA_GOOD_ROD_HOUSE": "FUCHSIA CITY", "CINNABAR_LAB_METRONOME_ROOM": "CINNABAR LAB",
    "CINNABAR_LAB_TRADE_ROOM": "CINNABAR LAB", "CINNABAR_LAB_FOSSIL_ROOM": "CINNABAR LAB",
    "ROUTE_2_TRADE_HOUSE": "ROUTE 2 HOUSE", "CERULEAN_TRADE_HOUSE": "CERULEAN CITY",
    "VERMILION_TRADE_HOUSE": "VERMILION CITY", "ROCKET_HIDEOUT_B1F": "ROCKET HIDEOUT B1",
    "ROCKET_HIDEOUT_B2F": "ROCKET HIDEOUT B2", "ROCKET_HIDEOUT_B3F": "ROCKET HIDEOUT B3",
    "ROCKET_HIDEOUT_B4F": "ROCKET HIDEOUT B4", "CELADON_MART_ROOF": "CELADON MART ROOF",
    "DIGLETTS_CAVE": "DIGLETT'S CAVE", "LORELEIS_ROOM": "INDIGO PLATEAU", "BRUNOS_ROOM": "INDIGO PLATEAU",
    "AGATHAS_ROOM": "INDIGO PLATEAU", "LANCES_ROOM": "INDIGO PLATEAU",
}


def mapname(m):
    if m in SPECIAL_NAMES:
        return SPECIAL_NAMES[m]
    n = m.replace("_", " ")
    n = re.sub(r"^POKEMON ", "POKéMON ", n)
    n = n.replace("POKECENTER", "POKéCENTER")
    n = re.sub(r"^POKéMON MANSION ", "MANSION ", n)
    n = re.sub(r"^SEAFOAM ISLANDS ", "SEAFOAM ", n)
    n = re.sub(r"^SILPH CO ", "SILPH CO.", n)
    n = re.sub(r" GATE (\dF)$", r" GATE", n)
    return n


def shortname(m):
    n = mapname(m)
    n = re.sub(r" (CITY|TOWN|ISLAND)$", "", n)
    n = n.replace("VERMILION DOCK", "S.S.ANNE DOCK")
    n = re.sub(r"^SAFARI ZONE .*", "SAFARI ZONE", n)
    n = re.sub(r"^(CERULEAN CAVE|VICTORY ROAD|POKéMON TOWER|ROCK TUNNEL|MT.MOON) .*", r"\1", n)
    return n


def with_suffix(m, suffix):
    for n in (mapname(m), shortname(m)):
        if len(n + suffix) <= 18:
            return n + suffix
    raise ValueError(m)


# ---------------------------------------------------------------- route: what unlocks what
# Every entry gets a gate (a milestone: what must be true before it can be done) and a place in
# travel order. NEXT is the not-done entry with the earliest gate, then the earliest place.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reach import Maps

RM = Maps(ROOT)
K_EVENT, K_TOGGLE, K_HIDDEN, K_TRADE, K_DEX, K_FLAGS1, K_FLAGS4 = (
    "PROG_EVENT", "PROG_TOGGLE", "PROG_HIDDEN", "PROG_TRADE", "PROG_DEX", "PROG_FLAGS1", "PROG_FLAGS4")


class Ms:
    def __init__(self, name, checks, rank, mode):
        self.name, self.checks, self.rank, self.mode = name, checks, rank, mode


MS = {}


def ms(name, checks, rank, mode="PROG_BOTH"):
    assert name not in MS, name
    assert 1 <= len(checks) <= 2
    MS[name] = Ms(name, checks, rank, mode)
    return name


def E(name, inv=False):
    assert name in EVENTS, name
    return ("PROG_EVENT", name, inv)


ATOMS = {}


def atoms(name):
    return ATOMS.get(name, {name})


STORY_OF = {}  # unlock -> the story step it can't happen before
IMPLIES = {"CUT": {"S_HM01", "S_MISTY"}, "SURF": {"S_HM03", "S_KOGA"}, "STRENGTH": {"S_HM04", "S_ERIKA"}}


def combine(*names):
    """Gate needing all of names. Story steps count as a chain, so only the latest one is kept."""
    a = set()
    for n in names:
        if n and n != "ALWAYS":
            a |= atoms(n)
    for k, v in IMPLIES.items():
        if k in a:
            a -= v
    # a later story step already needs these
    for hm, step in (("CUT", "S_SURGE"), ("SURF", "S_BLAINE"), ("STRENGTH", "S_LORELEI")):
        if hm in a and any(n.startswith("S_") and MS[n].rank >= MS[step].rank for n in a):
            a.discard(hm)
    # unlocks that can only happen after some story step make that step (and earlier) redundant
    implied = max([MS[STORY_OF[n]].rank for n in a if n in STORY_OF] or [-1])
    a = {n for n in a if not (n.startswith("S_") and MS[n].rank <= implied)}
    story = [n for n in a if n.startswith("S_")]
    if len(story) > 1:
        keep = max(story, key=lambda n: MS[n].rank)
        a = {n for n in a if not n.startswith("S_")} | {keep}
    if not a:
        return "ALWAYS"
    names = sorted(a, key=lambda n: (MS[n].rank, n))
    cur = names[0]
    for n in names[1:]:
        key = "(%s+%s)" % (cur, n)
        if key not in MS:
            x, y = MS[cur], MS[n]
            if len(x.checks) == 1 and len(y.checks) == 1:
                checks = [x.checks[0], y.checks[0]]
            else:
                checks = [("PROG_MILESTONE", cur, False), ("PROG_MILESTONE", n, False)]
            ms(key, checks, max(x.rank, y.rank) + 0.5)
            ATOMS[key] = atoms(cur) | atoms(n)
        cur = key
    return cur


ms("ALWAYS", [("PROG_ALWAYS", "0", False)], 0)

# ---------- STORY (the main path; each step is also a milestone)
STORY = [
    ("S_STARTER", [E("EVENT_GOT_STARTER")], "Choose a POKéMON", "OAK'S LAB", "OAKS_LAB", None),
    ("S_PARCEL", [E("EVENT_GOT_OAKS_PARCEL")], "Get OAK'S PARCEL", "VIRIDIAN MART", "VIRIDIAN_CITY", None),
    ("S_DEX", [E("EVENT_GOT_POKEDEX")], "Take parcel to OAK", "OAK'S LAB", "OAKS_LAB", None),
    ("S_BROCK", [E("EVENT_BEAT_BROCK")], "Beat BROCK", "PEWTER GYM", "PEWTER_GYM", None),
    ("S_MTMOON", [E("EVENT_BEAT_MT_MOON_EXIT_SUPER_NERD")], "Cross MT.MOON", "MT.MOON B2F", "MT_MOON_B2F", None),
    ("S_RIVAL1", [E("EVENT_BEAT_CERULEAN_RIVAL")], "Beat your rival", "CERULEAN CITY", "CERULEAN_CITY", None),
    ("S_MISTY", [E("EVENT_BEAT_MISTY")], "Beat MISTY", "CERULEAN GYM", "CERULEAN_GYM", None),
    ("S_TICKET", [E("EVENT_GOT_SS_TICKET")], "Help BILL", "ROUTE 25", "ROUTE_25", None),
    ("S_HM01", [E("EVENT_GOT_HM01")], "Get HM01 CUT", "S.S.ANNE CAPTAIN", "SS_ANNE_CAPTAINS_ROOM", None),
    ("S_SURGE", [E("EVENT_BEAT_LT_SURGE")], "Beat LT.SURGE", "VERMILION GYM", "VERMILION_GYM", "CUT"),
    ("S_THIEF", [E("EVENT_BEAT_CERULEAN_ROCKET_THIEF")], "Beat the thief", "CERULEAN CITY", "CERULEAN_CITY", None),
    ("S_ERIKA", [E("EVENT_BEAT_ERIKA")], "Beat ERIKA", "CELADON GYM", "CELADON_GYM", "CUT"),
    ("S_HIDEOUT", [E("EVENT_BEAT_ROCKET_HIDEOUT_GIOVANNI")], "Beat GIOVANNI", "ROCKET HIDEOUT B4", "ROCKET_HIDEOUT_B4F", "CUT"),
    ("S_RIVAL2", [E("EVENT_BEAT_POKEMON_TOWER_RIVAL")], "Beat your rival", "POKéMON TOWER 2F", "POKEMON_TOWER_2F", None),
    ("S_GHOST", [E("EVENT_BEAT_GHOST_MAROWAK")], "Calm the ghost", "POKéMON TOWER 6F", "POKEMON_TOWER_6F", None),
    ("S_FUJI", [E("EVENT_RESCUED_MR_FUJI")], "Rescue MR.FUJI", "POKéMON TOWER 7F", "POKEMON_TOWER_7F", None),
    ("S_FLUTE", [E("EVENT_GOT_POKE_FLUTE")], "Get the POKé FLUTE", "MR.FUJI'S HOUSE", "MR_FUJIS_HOUSE", None),
    ("S_SNORLAX", [E("EVENT_BEAT_ROUTE12_SNORLAX"), E("EVENT_BEAT_ROUTE16_SNORLAX")], "Wake a SNORLAX", "ROUTE 12 or 16", "ROUTE_12", None),
    ("S_KOGA", [E("EVENT_BEAT_KOGA")], "Beat KOGA", "FUCHSIA GYM", "FUCHSIA_GYM", None),
    ("S_HM03", [E("EVENT_GOT_HM03")], "Get HM03 SURF", "SAFARI ZONE", "SAFARI_ZONE_SECRET_HOUSE", None),
    ("S_HM04", [E("EVENT_GOT_HM04")], "Get HM04 STRENGTH", "WARDEN'S HOUSE", "WARDENS_HOUSE", None),
    ("S_DRINK", [("PROG_FLAGS1", "BIT_GAVE_SAFFRON_GUARDS_DRINK", False)], "Give guard a drink", "SAFFRON GATES", "SAFFRON_CITY", None),
    ("S_RIVAL3", [E("EVENT_BEAT_SILPH_CO_RIVAL")], "Beat your rival", "SILPH CO.7F", "SILPH_CO_7F", None),
    ("S_SILPH", [E("EVENT_BEAT_SILPH_CO_GIOVANNI")], "Beat GIOVANNI", "SILPH CO.11F", "SILPH_CO_11F", None),
    ("S_SABRINA", [E("EVENT_BEAT_SABRINA")], "Beat SABRINA", "SAFFRON GYM", "SAFFRON_GYM", None),
    ("S_BLAINE", [E("EVENT_BEAT_BLAINE")], "Beat BLAINE", "CINNABAR GYM", "CINNABAR_GYM", "SECRETKEY"),
    ("S_GIOVANNI", [E("EVENT_BEAT_VIRIDIAN_GYM_GIOVANNI")], "Beat GIOVANNI", "VIRIDIAN GYM", "VIRIDIAN_GYM", None),
    ("S_RIVAL4", [E("EVENT_BEAT_ROUTE22_RIVAL_2ND_BATTLE")], "Beat your rival", "ROUTE 22", "ROUTE_22", None),
    ("S_LORELEI", [E("EVENT_BEAT_LORELEIS_ROOM_TRAINER_0")], "Beat LORELEI", "INDIGO PLATEAU", "LORELEIS_ROOM", "VRGATE"),
    ("S_BRUNO", [E("EVENT_BEAT_BRUNOS_ROOM_TRAINER_0")], "Beat BRUNO", "INDIGO PLATEAU", "BRUNOS_ROOM", None),
    ("S_AGATHA", [E("EVENT_BEAT_AGATHAS_ROOM_TRAINER_0")], "Beat AGATHA", "INDIGO PLATEAU", "AGATHAS_ROOM", None),
    ("S_LANCE", [E("EVENT_BEAT_LANCE")], "Beat LANCE", "INDIGO PLATEAU", "LANCES_ROOM", None),
    ("S_CHAMPION", [E("EVENT_BEAT_CHAMPION_RIVAL")], "Become CHAMPION", "INDIGO PLATEAU", "LANCES_ROOM", None),
]
for i, (name, checks, a, b, mp, extra) in enumerate(STORY):
    ms(name, checks, 10 * (i + 1), "PROG_EITHER")
R = lambda n: MS[n].rank

# ---------- other unlocks
ms("CUT", [E("EVENT_GOT_HM01"), E("EVENT_BEAT_MISTY")], R("S_HM01") + 1)
ms("BIKE", [E("EVENT_GOT_BICYCLE")], R("S_HM01") + 2)
ms("OLDROD", [("PROG_FLAGS1", "BIT_GOT_OLD_ROD", False)], R("S_MTMOON") + 1)
ms("COINCASE", [E("EVENT_GOT_COIN_CASE")], R("S_HM01") + 3)
ms("ITEMFINDER", [E("EVENT_GOT_ITEMFINDER")], R("S_HM01") + 4)
ms("SSANNE", [E("EVENT_GOT_SS_TICKET"), E("EVENT_SS_ANNE_LEFT", True)], R("S_TICKET") - 0.5)
ms("GOODROD", [("PROG_FLAGS1", "BIT_GOT_GOOD_ROD", False)], R("S_SNORLAX") + 1)
ms("SUPERROD", [("PROG_FLAGS1", "BIT_GOT_SUPER_ROD", False)], R("S_SNORLAX") + 2)
ms("SURF", [E("EVENT_GOT_HM03"), E("EVENT_BEAT_KOGA")], R("S_HM03") + 1)
ms("STRENGTH", [E("EVENT_GOT_HM04"), E("EVENT_BEAT_ERIKA")], R("S_HM04") + 1)
ms("SILPHTRAINERS", [("PROG_FLAGS1", "BIT_GAVE_SAFFRON_GUARDS_DRINK", False),
                     E("EVENT_BEAT_SILPH_CO_GIOVANNI", True)], R("S_DRINK") + 1)
ms("R22RIVAL1", [E("EVENT_1ST_ROUTE22_RIVAL_BATTLE")], R("S_DEX") - 0.5)
ms("OWN10", [("PROG_OWNCOUNT", "10", False)], R("S_BROCK") + 1)
ms("OWN30", [("PROG_OWNCOUNT", "30", False)], R("S_HM01") + 4)
ms("OWN50", [("PROG_OWNCOUNT", "50", False)], R("S_SNORLAX") + 3)
ms("FOSSIL", [E("EVENT_GOT_DOME_FOSSIL"), E("EVENT_GOT_HELIX_FOSSIL")], R("S_MTMOON") + 1, "PROG_EITHER")
ms("OLDAMBER", [E("EVENT_GOT_OLD_AMBER")], R("S_HM01") + 2)
ms("SNORLAXLEFT", [E("EVENT_BEAT_ROUTE12_SNORLAX", True), E("EVENT_BEAT_ROUTE16_SNORLAX", True)], 1, "PROG_EITHER")
STORY_OF.update({"CUT": "S_HM01", "SURF": "S_HM03", "STRENGTH": "S_HM04", "SSANNE": "S_TICKET",
                 "OLDROD": "S_MTMOON", "BIKE": "S_MTMOON", "COINCASE": "S_HM01", "ITEMFINDER": "S_MTMOON",
                 "GOODROD": "S_SNORLAX", "SUPERROD": "S_FLUTE", "SILPHTRAINERS": "S_DRINK", "R22RIVAL1": "S_DEX",
                 "FOSSIL": "S_BROCK", "OLDAMBER": "S_HM01"})
for leg in ("ARTICUNO", "ZAPDOS", "MOLTRES", "MEWTWO"):
    ms("NOT_" + leg, [E("EVENT_BEAT_" + leg, True)], 1)

# ---------- toggles (item balls)
toggle_states = []
cur = None
for l in strip_comments(rd("data/maps/toggleable_objects.asm")).splitlines():
    m = re.match(r"\s*toggleable_objects_for (\w+)", l)
    if m:
        cur = m.group(1)
    m = re.match(r"\s*toggle_object_state (\w+),\s*(\w+)", l)
    if m:
        toggle_states.append((cur, m.group(1), m.group(2)))
toggle_consts = []
cur = None
for l in strip_comments(rd("constants/toggle_constants.asm")).splitlines():
    m = re.match(r"\s*toggle_consts_for (\w+)", l)
    if m:
        cur = m.group(1)
    m = re.match(r"\s*const (TOGGLE_\w+)", l)
    if m:
        toggle_consts.append((cur, m.group(1)))
by_map_states = collections.defaultdict(list)
for s_ in toggle_states:
    by_map_states[s_[0]].append(s_)
by_map_consts = collections.defaultdict(list)
for c in toggle_consts:
    by_map_consts[c[0]].append(c[1])
OBJ2TOGGLE = {}
for mp, sts in by_map_states.items():
    cs = by_map_consts[mp]
    assert len(cs) == len(sts), (mp, len(cs), len(sts))
    for s_, c in zip(sts, cs):
        OBJ2TOGGLE[s_[1]] = (c, s_[2])

_sk = [o for o in RM.objects("POKEMON_MANSION_B1F")[1] if o["rest"] == ["SECRET_KEY"]]
_skc = re.findall(r"const_export (\w+)", strip_comments(rd("data/maps/objects/PokemonMansionB1F.asm")))
_sko = [o for o in RM.objects("POKEMON_MANSION_B1F")[1]]
ms("SECRETKEY", [(K_TOGGLE, OBJ2TOGGLE[_skc[_sko.index(_sk[0])]][0], False)], R("S_SABRINA") + 1)
STORY_OF["SECRETKEY"] = "S_HM03"

# ---------- where each map opens up
G = {}


def gate(name, *maps):
    for m in maps:
        assert m in MAPID, m
        G[m] = name


gate("ALWAYS", "PALLET_TOWN", "OAKS_LAB", "REDS_HOUSE_1F", "REDS_HOUSE_2F", "BLUES_HOUSE")
gate("S_STARTER", "ROUTE_1", "VIRIDIAN_CITY", "VIRIDIAN_MART")
gate("S_DEX", "ROUTE_22", "ROUTE_2", "VIRIDIAN_FOREST", "VIRIDIAN_FOREST_NORTH_GATE",
     "VIRIDIAN_FOREST_SOUTH_GATE", "PEWTER_CITY", "PEWTER_GYM", "MUSEUM_1F", "ROUTE_2_TRADE_HOUSE",
     "ROUTE_2_GATE")
gate("S_BROCK", "ROUTE_3", "MT_MOON_1F", "MT_MOON_B1F", "MT_MOON_B2F", "MT_MOON_POKECENTER")
gate("S_MTMOON", "ROUTE_4", "CERULEAN_CITY", "CERULEAN_GYM", "BIKE_SHOP", "CERULEAN_TRADE_HOUSE",
     "ROUTE_5", "UNDERGROUND_PATH_ROUTE_5", "UNDERGROUND_PATH_NORTH_SOUTH", "UNDERGROUND_PATH_ROUTE_6",
     "ROUTE_6", "VERMILION_CITY", "VERMILION_TRADE_HOUSE", "VERMILION_OLD_ROD_HOUSE", "POKEMON_FAN_CLUB",
     "ROUTE_11", "ROUTE_11_GATE_2F", "DIGLETTS_CAVE", "DIGLETTS_CAVE_ROUTE_2", "DIGLETTS_CAVE_ROUTE_11")
gate("S_RIVAL1", "ROUTE_24", "ROUTE_25", "BILLS_HOUSE")
gate("S_TICKET", "VERMILION_DOCK")
gate("SSANNE", "SS_ANNE_1F", "SS_ANNE_2F", "SS_ANNE_3F", "SS_ANNE_B1F", "SS_ANNE_BOW", "SS_ANNE_KITCHEN",
     "SS_ANNE_CAPTAINS_ROOM", "SS_ANNE_1F_ROOMS", "SS_ANNE_2F_ROOMS", "SS_ANNE_B1F_ROOMS")
gate("CUT", "ROUTE_9", "ROUTE_10", "ROCK_TUNNEL_1F", "ROCK_TUNNEL_B1F", "ROCK_TUNNEL_POKECENTER",
     "LAVENDER_TOWN", "MR_FUJIS_HOUSE", "ROUTE_8", "UNDERGROUND_PATH_ROUTE_8", "UNDERGROUND_PATH_WEST_EAST",
     "UNDERGROUND_PATH_ROUTE_7", "ROUTE_7", "CELADON_CITY", "CELADON_GYM", "CELADON_MART_3F",
     "CELADON_MART_ROOF", "CELADON_DINER", "CELADON_MANSION_ROOF_HOUSE", "GAME_CORNER",
     "ROCKET_HIDEOUT_B1F", "ROCKET_HIDEOUT_B2F", "ROCKET_HIDEOUT_B3F", "ROCKET_HIDEOUT_B4F",
     "POKEMON_TOWER_1F", "POKEMON_TOWER_2F", "POKEMON_TOWER_3F", "POKEMON_TOWER_4F", "POKEMON_TOWER_5F",
     "VERMILION_GYM")
gate("S_HIDEOUT", "POKEMON_TOWER_6F", "POKEMON_TOWER_7F")
gate("S_FLUTE", "ROUTE_12", "ROUTE_13", "ROUTE_14", "ROUTE_15", "ROUTE_12_GATE_2F", "ROUTE_12_SUPER_ROD_HOUSE",
     "ROUTE_15_GATE_2F", "FUCHSIA_CITY", "FUCHSIA_GYM", "FUCHSIA_GOOD_ROD_HOUSE", "SAFARI_ZONE_GATE",
     "SAFARI_ZONE_CENTER", "SAFARI_ZONE_EAST", "SAFARI_ZONE_NORTH", "SAFARI_ZONE_WEST",
     "SAFARI_ZONE_SECRET_HOUSE", "WARDENS_HOUSE", "ROUTE_16", "ROUTE_16_FLY_HOUSE")
ms("CYCLING", [("PROG_MILESTONE", "S_FLUTE", False), ("PROG_MILESTONE", "BIKE", False)], R("S_FLUTE") + 0.5)
gate("CYCLING", "ROUTE_17", "ROUTE_18", "ROUTE_18_GATE_2F")
gate("SURF", "ROUTE_19", "ROUTE_20", "ROUTE_21", "CINNABAR_ISLAND", "POKEMON_MANSION_1F", "POKEMON_MANSION_2F",
     "POKEMON_MANSION_3F", "POKEMON_MANSION_B1F", "CINNABAR_LAB", "CINNABAR_LAB_TRADE_ROOM",
     "CINNABAR_LAB_METRONOME_ROOM", "CINNABAR_LAB_FOSSIL_ROOM", "POWER_PLANT", "SEAFOAM_ISLANDS_1F")
SEAFOAM = combine("SURF", "STRENGTH")
gate(SEAFOAM, "SEAFOAM_ISLANDS_B1F", "SEAFOAM_ISLANDS_B2F", "SEAFOAM_ISLANDS_B3F", "SEAFOAM_ISLANDS_B4F")
gate("S_DRINK", "SAFFRON_CITY", "COPYCATS_HOUSE_2F", "FIGHTING_DOJO", "MR_PSYCHICS_HOUSE", "SILPH_CO_1F",
     "SILPH_CO_2F", "SILPH_CO_3F", "SILPH_CO_4F", "SILPH_CO_5F", "SILPH_CO_6F", "SILPH_CO_7F", "SILPH_CO_8F",
     "SILPH_CO_9F", "SILPH_CO_10F", "SILPH_CO_11F")
gate("S_SILPH", "SAFFRON_GYM")
gate("S_SABRINA", "VIRIDIAN_GYM")  # opens with seven badges; Blaine comes later in this story order
VRGATE = combine("S_GIOVANNI", "STRENGTH")
gate(combine("S_GIOVANNI", "SURF"), "ROUTE_23", "ROUTE_22_GATE")
gate(VRGATE, "VICTORY_ROAD_1F", "VICTORY_ROAD_2F", "VICTORY_ROAD_3F", "INDIGO_PLATEAU_LOBBY",
     "LORELEIS_ROOM", "BRUNOS_ROOM", "AGATHAS_ROOM", "LANCES_ROOM")
gate("S_CHAMPION", "CERULEAN_CAVE_1F", "CERULEAN_CAVE_2F", "CERULEAN_CAVE_B1F")
gate("CUT", "CELADON_MANSION_3F")
gate(VRGATE, "INDIGO_PLATEAU")

gate("SECRETKEY", "CINNABAR_GYM")

# ---------- travel order (what is next to what)
ORDER2 = """PALLET_TOWN REDS_HOUSE_1F REDS_HOUSE_2F BLUES_HOUSE OAKS_LAB ROUTE_1 VIRIDIAN_CITY VIRIDIAN_MART
ROUTE_22 ROUTE_2 ROUTE_2_TRADE_HOUSE VIRIDIAN_FOREST_SOUTH_GATE VIRIDIAN_FOREST VIRIDIAN_FOREST_NORTH_GATE
PEWTER_CITY MUSEUM_1F PEWTER_GYM ROUTE_3 MT_MOON_POKECENTER MT_MOON_1F MT_MOON_B1F MT_MOON_B2F ROUTE_4
CERULEAN_CITY CERULEAN_TRADE_HOUSE CERULEAN_GYM BIKE_SHOP ROUTE_24 ROUTE_25 BILLS_HOUSE ROUTE_5
UNDERGROUND_PATH_ROUTE_5 UNDERGROUND_PATH_NORTH_SOUTH UNDERGROUND_PATH_ROUTE_6 ROUTE_6 VERMILION_CITY
VERMILION_OLD_ROD_HOUSE POKEMON_FAN_CLUB VERMILION_TRADE_HOUSE VERMILION_DOCK SS_ANNE_1F SS_ANNE_1F_ROOMS
SS_ANNE_2F SS_ANNE_2F_ROOMS SS_ANNE_3F SS_ANNE_BOW SS_ANNE_B1F SS_ANNE_B1F_ROOMS SS_ANNE_KITCHEN
SS_ANNE_CAPTAINS_ROOM VERMILION_GYM ROUTE_11 ROUTE_11_GATE_2F DIGLETTS_CAVE_ROUTE_11 DIGLETTS_CAVE
DIGLETTS_CAVE_ROUTE_2 ROUTE_2_GATE ROUTE_9 ROUTE_10 ROCK_TUNNEL_POKECENTER ROCK_TUNNEL_1F ROCK_TUNNEL_B1F
LAVENDER_TOWN MR_FUJIS_HOUSE POKEMON_TOWER_1F POKEMON_TOWER_2F POKEMON_TOWER_3F POKEMON_TOWER_4F
POKEMON_TOWER_5F POKEMON_TOWER_6F POKEMON_TOWER_7F ROUTE_8 UNDERGROUND_PATH_ROUTE_8 UNDERGROUND_PATH_WEST_EAST
UNDERGROUND_PATH_ROUTE_7 ROUTE_7 CELADON_CITY CELADON_MART_3F CELADON_MART_ROOF CELADON_MANSION_3F CELADON_MANSION_ROOF_HOUSE
CELADON_DINER CELADON_GYM GAME_CORNER ROCKET_HIDEOUT_B1F ROCKET_HIDEOUT_B2F ROCKET_HIDEOUT_B3F
ROCKET_HIDEOUT_B4F ROUTE_12 ROUTE_12_GATE_2F ROUTE_12_SUPER_ROD_HOUSE ROUTE_13 ROUTE_14 ROUTE_15
ROUTE_15_GATE_2F ROUTE_16 ROUTE_16_FLY_HOUSE ROUTE_17 ROUTE_18 ROUTE_18_GATE_2F FUCHSIA_CITY
FUCHSIA_GOOD_ROD_HOUSE FUCHSIA_GYM WARDENS_HOUSE SAFARI_ZONE_GATE SAFARI_ZONE_CENTER SAFARI_ZONE_EAST
SAFARI_ZONE_NORTH SAFARI_ZONE_WEST SAFARI_ZONE_SECRET_HOUSE SAFFRON_CITY COPYCATS_HOUSE_2F MR_PSYCHICS_HOUSE
FIGHTING_DOJO SILPH_CO_1F SILPH_CO_2F SILPH_CO_3F SILPH_CO_4F SILPH_CO_5F SILPH_CO_6F SILPH_CO_7F
SILPH_CO_8F SILPH_CO_9F SILPH_CO_10F SILPH_CO_11F SAFFRON_GYM ROUTE_19 ROUTE_20 SEAFOAM_ISLANDS_1F
SEAFOAM_ISLANDS_B1F SEAFOAM_ISLANDS_B2F SEAFOAM_ISLANDS_B3F SEAFOAM_ISLANDS_B4F CINNABAR_ISLAND
POKEMON_MANSION_1F POKEMON_MANSION_2F POKEMON_MANSION_3F POKEMON_MANSION_B1F CINNABAR_LAB
CINNABAR_LAB_TRADE_ROOM CINNABAR_LAB_METRONOME_ROOM CINNABAR_LAB_FOSSIL_ROOM CINNABAR_GYM ROUTE_21
POWER_PLANT VIRIDIAN_GYM ROUTE_22_GATE ROUTE_23 VICTORY_ROAD_1F VICTORY_ROAD_2F VICTORY_ROAD_3F
INDIGO_PLATEAU INDIGO_PLATEAU_LOBBY LORELEIS_ROOM BRUNOS_ROOM AGATHAS_ROOM LANCES_ROOM CERULEAN_CAVE_1F CERULEAN_CAVE_2F
CERULEAN_CAVE_B1F""".split()
assert len(ORDER2) == len(set(ORDER2)) and len(ORDER2) < 250
for m in ORDER2:
    assert m in MAPID, m
    assert m in G, ("no gate", m)
LATE = 253  # trades with another game, level-ups after the story
OK = lambda m: ORDER2.index(m)

GATED = set(G)


def capgates(caps):
    return [{"cut": "CUT", "surf": "SURF", "str": "STRENGTH"}[c] for c in caps]


def place_gate(mp, cell, extra=()):
    """The gate for something at cell on map mp: its map, the way in, the HMs, and extra needs."""
    best = None
    for via, caps in RM.options(mp, cell, GATED):
        g = combine(G[mp], G.get(via) if via else None, *capgates(caps), *extra)
        if best is None or (MS[g].rank, len(g)) < (MS[best].rank, len(best)):
            best = g
    if best is None:
        print("unreachable, map gate only:", mp, cell)
        best = combine(G[mp], *extra)
    return best


# ---------------------------------------------------------------- entries


class Entry:
    def __init__(self, checks, l1, l2, gate, order, mode="PROG_EITHER", mp=None):
        self.checks, self.l1, self.l2, self.gate, self.order, self.mode = checks, l1, l2, gate, order, mode
        self.mp, self.label = mp, None


LINE1_MAX, LINE2_MAX = 18, 18
CHARS = set("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ().,'-:/?!é♂♀▶×")


def check_text(s, n):
    assert len(s) <= n, (s, len(s), n)
    for c in s:
        assert c in CHARS, (s, c)


categories = []


def category(title, heading, entries, sort=True):
    for e in entries:
        check_text(e.l1, LINE1_MAX)
        check_text(e.l2, LINE2_MAX)
        assert e.gate in MS, e.gate
    if sort:
        entries = sorted(entries, key=lambda e: (MS[e.gate].rank, e.order))
    categories.append((title, heading, entries))


SHOWN_BY = {"TOGGLE_ROCKET_HIDEOUT_B4F_ITEM_4": "EVENT_BEAT_ROCKET_HIDEOUT_GIOVANNI",
            "TOGGLE_ROCKET_HIDEOUT_B4F_ITEM_5": "EVENT_ROCKET_DROPPED_LIFT_KEY"}
items = []
for f in sorted(glob.glob(os.path.join(ROOT, "data/maps/objects/*.asm"))):
    src = strip_comments(open(f).read())
    label = os.path.basename(f)[:-4]
    mp = LABEL2MAP[label]
    objconsts = re.findall(r"const_export (\w+)", src)
    objs = re.findall(r"object_event ([^\n]+)", src)
    assert len(objconsts) >= len(objs), f
    for oc, o in zip(objconsts, objs):
        p = [x.strip() for x in o.split(",")]
        if p[2] != "SPRITE_POKE_BALL" or len(p) != 7:
            continue
        item = p[6]
        tog, state = OBJ2TOGGLE[oc]
        name = TMFULL.get(item, ITEMNAME.get(item))
        checks, mode, extra = [(K_TOGGLE, tog)], "PROG_EITHER", []
        if state != "ON":
            checks.append((K_EVENT, SHOWN_BY[tog]))
            mode = "PROG_BOTH"
            ms("SHOWN_" + tog, [E(SHOWN_BY[tog])],
               R("S_HIDEOUT") - 1 if SHOWN_BY[tog].endswith("GIOVANNI") else R("S_ERIKA") - 0.5)
            extra = ["SHOWN_" + tog]
        items.append((mp, (int(p[0]), int(p[1])), name, checks, mode, extra))
it_entries = [Entry([(K_EVENT, "PROGRESS_PC_POTION_EVENT")], "POTION", "PC in your room", "ALWAYS",
                    OK("REDS_HOUSE_2F"), mp="REDS_HOUSE_2F")]  # the mod marks it (no game flag)
for mp, cell, name, checks, mode, extra in items:
    g = place_gate(mp, cell, extra)
    it_entries.append(Entry(checks, name, mapname(mp), g, OK(mp), mode, mp))


# ---------- HIDDEN items: need the Itemfinder to find them
coords = re.findall(r"hidden_item (\w+),\s*(\d+),\s*(\d+)(.*)", rd("data/events/hidden_item_coords.asm"))
hidden_ev = {}
cur = None
for l in strip_comments(rd("data/events/hidden_events.asm")).splitlines():
    m = re.match(r"\s*hidden_events_for (\w+)", l)
    if m:
        cur = m.group(1)
    m = re.match(r"\s*hidden_event\s+(\d+),\s*(\d+),\s*HiddenItems,\s*(\w+)", l)
    if m:
        hidden_ev[(cur, int(m.group(1)), int(m.group(2)))] = m.group(3)
hid_entries = []
for i, (mp, x, y, rest) in enumerate(coords):
    if "inaccessible" in rest or mp not in G:
        print("hidden item left out:", mp, x, y, rest.strip())
        continue
    item = hidden_ev[(mp, int(x), int(y))]
    g = place_gate(mp, (int(x), int(y)), ["ITEMFINDER"])
    hid_entries.append(Entry([(K_HIDDEN, str(i))], ITEMNAME[item], mapname(mp), g, OK(mp), mp=mp))


def obj_cell(mp, text):
    for o in RM.objects(mp)[1]:
        if o["text"] == text:
            return (o["x"], o["y"])
    raise KeyError((mp, text))


coin_coords = re.findall(r"hidden_coin (\w+),\s*(\d+),\s*(\d+)", rd("data/events/hidden_coins.asm"))
coin_amount = {}
cur = None
for l in strip_comments(rd("data/events/hidden_events.asm")).splitlines():
    m = re.match(r"\s*hidden_events_for (\w+)", l)
    if m:
        cur = m.group(1)
    m = re.match(r"\s*hidden_event\s+(\d+),\s*(\d+),\s*HiddenCoins,\s*COIN \+ (\d+)", l)
    if m:
        coin_amount[(cur, int(m.group(1)), int(m.group(2)))] = int(m.group(3))
for i, (mp, x, y) in enumerate(coin_coords):
    g = place_gate(mp, (int(x), int(y)), ["COINCASE"])
    hid_entries.append(Entry([("PROG_HIDDENCOIN", str(i))], "%d COINS" % coin_amount[(mp, int(x), int(y))],
                             mapname(mp) + " floor", g, OK(mp), mp=mp))

# ---------- NPCs: find who gives what, for Cut/Surf checks on gifts
def npc_cell(mp, event):
    """Cell of the NPC whose text sets event on map mp, if it can be found."""
    label = RM.header[mp][0]
    try:
        src = strip_comments(rd("scripts/%s.asm" % label))
    except FileNotFoundError:
        return None
    text2const = {lab: c for lab, c in re.findall(r"dw_const (\w+),\s*(TEXT_\w+)", src)}
    owner = None
    for m in re.finditer(r"^(\w+):\s*\n(.*?)(?=^\w+:\s*\n|\Z)", src, re.M | re.S):
        if re.search(r"\b%s\b" % event, m.group(2)) and m.group(1) in text2const:
            owner = text2const[m.group(1)]
            break
    if not owner:
        return None
    for o in RM.objects(mp)[1]:
        if o["text"] == owner:
            return (o["x"], o["y"])
    return None


# ---------- GIFTS: (check, name, where, map, extra gates)
GIFTS = [
    (E("EVENT_GOT_POTION_SAMPLE"), "POTION", "ROUTE 1", "ROUTE_1", []),
    (E("EVENT_GOT_POKEBALLS_FROM_OAK"), "POKé BALL x5", "OAK'S LAB, 0 BALLS", "OAKS_LAB", ["S_DEX", "R22BEATEN"]),
    (E("EVENT_GOT_TOWN_MAP"), "TOWN MAP", "BLUE'S HOUSE", "BLUES_HOUSE", ["S_DEX"]),
    (E("EVENT_GOT_TM42"), "TM42", "VIRIDIAN CITY", "VIRIDIAN_CITY", []),
    (E("EVENT_GOT_TM34"), "TM34", "PEWTER GYM", "PEWTER_GYM", ["S_BROCK"]),
    (E("EVENT_GOT_OLD_AMBER"), "OLD AMBER", "MUSEUM 1F", "MUSEUM_1F", ["CUT"]),
    ((E("EVENT_GOT_DOME_FOSSIL"), E("EVENT_GOT_HELIX_FOSSIL")), "DOME/HELIX FOSSIL", "MT.MOON B2F", "MT_MOON_B2F", []),
    (E("EVENT_GOT_TM11"), "TM11", "CERULEAN GYM", "CERULEAN_GYM", ["S_MISTY"]),
    (E("EVENT_GOT_NUGGET"), "NUGGET", "ROUTE 24", "ROUTE_24", []),
    (E("EVENT_GOT_BIKE_VOUCHER"), "BIKE VOUCHER", "POKéMON FAN CLUB", "POKEMON_FAN_CLUB", []),
    (E("EVENT_GOT_BICYCLE"), "BICYCLE", "BIKE SHOP", "BIKE_SHOP", [None]),
    (("PROG_FLAGS1", "BIT_GOT_OLD_ROD", False), "OLD ROD", "VERMILION CITY", "VERMILION_OLD_ROD_HOUSE", []),
    (E("EVENT_GOT_TM24"), "TM24", "VERMILION GYM", "VERMILION_GYM", ["S_SURGE"]),
    (E("EVENT_GOT_HM05"), "HM05", "ROUTE 2 GATE", "ROUTE_2_GATE", ["OWN10", "CUT"]),
    (E("EVENT_GOT_ITEMFINDER"), "ITEMFINDER", "ROUTE 11 GATE", "ROUTE_11_GATE_2F", ["OWN30"]),
    (E("EVENT_GOT_TM39"), "TM39", "ROUTE 12 GATE", "ROUTE_12_GATE_2F", []),
    (("PROG_FLAGS1", "BIT_GOT_SUPER_ROD", False), "SUPER ROD", "ROUTE 12 HOUSE", "ROUTE_12_SUPER_ROD_HOUSE", []),
    (E("EVENT_GOT_TM41"), "TM41", "CELADON CITY", "CELADON_CITY", []),
    (E("EVENT_GOT_TM18"), "TM18", "CELADON MART 3F", "CELADON_MART_3F", []),
    (E("EVENT_GOT_TM13"), "TM13", "CELADON MART ROOF", "CELADON_MART_ROOF", []),
    (E("EVENT_GOT_TM48"), "TM48", "CELADON MART ROOF", "CELADON_MART_ROOF", []),
    (E("EVENT_GOT_TM49"), "TM49", "CELADON MART ROOF", "CELADON_MART_ROOF", []),
    (E("EVENT_GOT_TM21"), "TM21", "CELADON GYM", "CELADON_GYM", ["S_ERIKA"]),
    (E("EVENT_GOT_COIN_CASE"), "COIN CASE", "CELADON DINER", "CELADON_DINER", []),
    (E("EVENT_GOT_10_COINS"), "10 COINS", "GAME CORNER", "GAME_CORNER", ["COINCASE"]),
    (E("EVENT_GOT_20_COINS"), "20 COINS", "GAME CORNER", "GAME_CORNER", ["COINCASE"]),
    (E("EVENT_GOT_20_COINS_2"), "20 COINS", "GAME CORNER", "GAME_CORNER", ["COINCASE"]),
    (E("EVENT_GOT_EXP_ALL"), "EXP.ALL", "ROUTE 15 GATE", "ROUTE_15_GATE_2F", ["OWN50"]),
    (E("EVENT_GOT_HM02"), "HM02", "ROUTE 16 HOUSE", "ROUTE_16_FLY_HOUSE", ["CUT"]),
    (("PROG_FLAGS1", "BIT_GOT_GOOD_ROD", False), "GOOD ROD", "FUCHSIA CITY", "FUCHSIA_GOOD_ROD_HOUSE", []),
    (E("EVENT_GOT_TM06"), "TM06", "FUCHSIA GYM", "FUCHSIA_GYM", ["S_KOGA"]),
    (E("EVENT_GOT_TM29"), "TM29", "MR.PSYCHIC'S HOUSE", "MR_PSYCHICS_HOUSE", []),
    (E("EVENT_GOT_TM31"), "TM31", "COPYCAT'S HOUSE", "COPYCATS_HOUSE_2F", []),
    (E("EVENT_GOT_TM36"), "TM36", "SILPH CO.2F", "SILPH_CO_2F", []),
    (E("EVENT_GOT_MASTER_BALL"), "MASTER BALL", "SILPH CO.11F", "SILPH_CO_11F", ["S_SILPH"]),
    (E("EVENT_GOT_TM46"), "TM46", "SAFFRON GYM", "SAFFRON_GYM", ["S_SABRINA"]),
    (E("EVENT_GOT_TM35"), "TM35", "CINNABAR LAB", "CINNABAR_LAB_METRONOME_ROOM", []),
    (E("EVENT_GOT_TM38"), "TM38", "CINNABAR GYM", "CINNABAR_GYM", ["S_BLAINE"]),
    (E("EVENT_GOT_TM27"), "TM27", "VIRIDIAN GYM", "VIRIDIAN_GYM", ["S_GIOVANNI"]),
]
ms("VOUCHER", [E("EVENT_GOT_BIKE_VOUCHER")], R("S_MTMOON") + 2)
# Oak only gives the balls after the first Route 22 rival battle, to a player with none left
ms("R22BEATEN", [E("EVENT_BEAT_ROUTE22_RIVAL_1ST_BATTLE")], R("S_DEX") - 0.4)
STORY_OF["R22BEATEN"] = "S_DEX"
STORY_OF["VOUCHER"] = "S_MTMOON"
ms("OWN150", [("PROG_OWNCOUNT", "150", False)], R("S_CHAMPION") + 1)  # all but Mew, as the game designer checks
gift_entries = [Entry([(K_EVENT, "PROGRESS_DIPLOMA_EVENT")], "DIPLOMA", "CELADON MANSION 3F",
                      combine(G["CELADON_MANSION_3F"], "OWN150"), OK("CELADON_MANSION_3F"))]  # the mod marks it
gift_entries += [Entry([(K_TOGGLE, OBJ2TOGGLE["CERULEANCITY_ROCKET"][0])], ITEMNAME["TM28"], "CERULEAN CITY",
                      combine("S_THIEF", place_gate("CERULEAN_CITY", obj_cell("CERULEAN_CITY",
                                                                               "TEXT_CERULEANCITY_ROCKET"))),
                      OK("CERULEAN_CITY"))]
for chk, a, b, mp, extra in GIFTS:
    checks = list(chk) if isinstance(chk[0], tuple) else [chk]
    extra = ["VOUCHER" if x is None else x for x in extra]
    a = ITEMNAME.get(a, a) if re.match(r"^[TH]M\d\d$", a) else a
    ev_names = [c[1] for c in checks if c[0] == "PROG_EVENT"]
    cell = npc_cell(mp, ev_names[0]) if ev_names else None
    g = place_gate(mp, cell, extra) if cell else combine(G[mp], *extra)
    gift_entries.append(Entry([(k, v) for k, v, inv in checks], a, b, g, OK(mp)))

# ---------- TRAINERS
tr_entries = []
for f in sorted(glob.glob(os.path.join(ROOT, "scripts/*.asm"))):
    label = os.path.basename(f)[:-4]
    if label not in LABEL2MAP:
        continue
    mp = LABEL2MAP[label]
    if mp in ("LORELEIS_ROOM", "BRUNOS_ROOM", "AGATHAS_ROOM", "LANCES_ROOM", "CHAMPIONS_ROOM"):
        continue
    src = strip_comments(open(f).read())
    headers = dict(re.findall(r"^(\w+TrainerHeader\d+):\s*\n\s*trainer (EVENT_\w+),", src, re.M))
    if not headers:
        continue
    text2const = {lab: c for lab, c in re.findall(r"dw_const (\w+),\s*(TEXT_\w+)", src)}
    header_of_text = {}
    for m in re.finditer(r"^(\w+):\s*\n(.*?)(?=^\w+:|\Z)", src, re.M | re.S):
        h = re.search(r"ld hl, (\w+TrainerHeader\d+)\b", m.group(2))
        if h:
            header_of_text[m.group(1)] = h.group(1)
    objs = {o["text"]: o for o in RM.objects(mp)[1]}
    used = set()
    for lab, h in header_of_text.items():
        o = objs.get(text2const.get(lab))
        if not o or not o["rest"] or not o["rest"][0].startswith("OPP_"):
            continue
        evn = headers[h]
        if evn in used:
            continue
        used.add(evn)
        extra = []
        if mp.startswith("SILPH_CO_"):
            extra = ["SILPHTRAINERS"]  # Team Rocket leaves Silph Co. after Giovanni
        g = place_gate(mp, (o["x"], o["y"]), extra)
        tr_entries.append(Entry([(K_EVENT, evn)], CLASSNAME[o["rest"][0][4:]], mapname(mp), g, OK(mp), mp=mp))
tr_entries.append(Entry([(K_EVENT, "EVENT_BEAT_ROUTE22_RIVAL_1ST_BATTLE")], "RIVAL", "ROUTE 22", "R22RIVAL1",
                        OK("ROUTE_22")))
# battles the game runs from its own scripts rather than trainer headers
tr_entries.append(Entry([(K_EVENT, "EVENT_BATTLED_RIVAL_IN_OAKS_LAB")], "RIVAL", "OAK'S LAB", "S_STARTER",
                        OK("OAKS_LAB")))
tr_entries.append(Entry([("PROG_SSANNE2F", "2")], "RIVAL", "S.S.ANNE 2F", "SSANNE", OK("SS_ANNE_2F"),
                        mp="SS_ANNE_2F"))  # its map script ends on SCRIPT_SSANNE2F_NOOP (4)
for mp, text, event, name in [
        ("ROUTE_24", "TEXT_ROUTE24_COOLTRAINER_M1", (K_EVENT, "EVENT_BEAT_ROUTE24_ROCKET"), "ROCKET"),
        ("FIGHTING_DOJO", "TEXT_FIGHTINGDOJO_KARATE_MASTER", (K_EVENT, "EVENT_BEAT_KARATE_MASTER"), "KARATE MASTER"),
        ("GAME_CORNER", "TEXT_GAMECORNER_ROCKET", (K_TOGGLE, OBJ2TOGGLE["GAMECORNER_ROCKET"][0]), "ROCKET")] + [
        ("CINNABAR_GYM", "TEXT_CINNABARGYM_SUPER_NERD%d" % n, (K_EVENT, "EVENT_BEAT_CINNABAR_GYM_TRAINER_%d" % (n - 1)),
         None) for n in range(1, 8)]:
    o = [o for o in RM.objects(mp)[1] if o["text"] == text][0]
    name = name or CLASSNAME[o["rest"][0][4:]]
    tr_entries.append(Entry([event], name, mapname(mp), place_gate(mp, (o["x"], o["y"])), OK(mp), mp=mp))

# ---------- POKEDEX: the earliest way to get each one
wild = collections.defaultdict(list)  # species -> [(gate, order, text)]
_wp = strip_comments(rd("data/wild/grass_water.asm")).split("WildDataPointers:")[1]
_labs = re.findall(r"^\s*dw (\w+)", _wp, re.M)
label2file = {}
for f in glob.glob(os.path.join(ROOT, "data/wild/maps/*.asm")):
    for lab in re.findall(r"^(\w+WildMons):", open(f).read(), re.M):
        label2file[lab] = f
TOWER_WILD = {"POKEMON_TOWER_%dF" % i for i in range(1, 8)}
for i, lab in enumerate(_labs[:len(MAPS)]):
    mp = MAPS[i]
    if lab == "NothingWildMons" or mp not in G:
        continue
    src = red_only(strip_comments(open(label2file[lab]).read()))
    grass, water = src.split("end_grass_wildmons")[0], src.split("end_grass_wildmons")[1]
    base = combine(G[mp], "S_DEX", "S_HIDEOUT" if mp in TOWER_WILD else None)
    for sp in re.findall(r"db\s+\d+,\s*(\w+)", grass):
        wild[sp].append((base, OK(mp), mapname(mp)))
    for sp in re.findall(r"db\s+\d+,\s*(\w+)", water):
        wild[sp].append((combine(base, "SURF"), OK(mp), with_suffix(mp, " surf")))
superrod = red_only(strip_comments(rd("data/wild/super_rod.asm")))
groups = {}
for g_, body in re.findall(r"\.(Group\d+):\s*db \d+\s*((?:db\s+\d+,\s*\w+\s*)+)", superrod):
    groups[g_] = re.findall(r"db\s+\d+,\s*(\w+)", body)
for mp, g_ in re.findall(r"dbw (\w+),\s*\.(Group\d+)", superrod):
    if mp not in G:
        continue
    for sp in groups[g_]:
        wild[sp].append((combine(G[mp], "SUPERROD"), OK(mp), with_suffix(mp, " fish")))
wild["MAGIKARP"].append(("OLDROD", OK("VERMILION_CITY"), "OLD ROD"))
wild["GOLDEEN"].append(("GOODROD", OK("FUCHSIA_CITY"), "GOOD ROD"))
wild["POLIWAG"].append(("GOODROD", OK("FUCHSIA_CITY"), "GOOD ROD"))
prizes = red_only(strip_comments(rd("data/events/prizes.asm")))
for blk in re.findall(r"PrizeMenuMon\dEntries:(.*?)db \"@\"", prizes, re.S):
    for sp in re.findall(r"db (\w+)", blk):
        wild[sp].append(("COINCASE", OK("GAME_CORNER"), "GAME CORNER prize"))
trades = re.findall(r'npctrade (\w+),\s*(\w+),\s*\w+,\s*"(\w+)"\s*; (?:used in (\w+)|unused)',
                    rd("data/events/trades.asm"))
TRADE_TOWN = {"ROUTE_11_GATE_2F": "ROUTE 11", "ROUTE_2_TRADE_HOUSE": "ROUTE 2",
              "CINNABAR_LAB_FOSSIL_ROOM": "CINNABAR LAB", "VERMILION_TRADE_HOUSE": "VERMILION",
              "ROUTE_18_GATE_2F": "ROUTE 18", "CERULEAN_TRADE_HOUSE": "CERULEAN",
              "CINNABAR_LAB_TRADE_ROOM": "CINNABAR LAB", "UNDERGROUND_PATH_ROUTE_5": "ROUTE 5 TUNNEL"}
STARTERS = {"BULBASAUR", "CHARMANDER", "SQUIRTLE"}
ONE_OF = {"HITMONLEE": "Dojo gift or trade", "HITMONCHAN": "Dojo gift or trade",
          "OMANYTE": "Helix fossil/trade", "KABUTO": "Dome fossil/trade", "VAPOREON": "WATER ST./trade",
          "JOLTEON": "THUNDER ST./trade", "FLAREON": "FIRE ST./trade"}
SPECIAL_WILD = {
    "EEVEE": (combine(G["CELADON_CITY"], "S_DEX"), OK("CELADON_MANSION_ROOF_HOUSE"), "CELADON MANSION"),
    "LAPRAS": (combine("S_DRINK", "S_DEX"), OK("SILPH_CO_7F"), "Gift SILPH CO.7F"),
    "AERODACTYL": (combine("SURF", "OLDAMBER"), OK("CINNABAR_LAB_FOSSIL_ROOM"), "OLD AMBER"),
    "SNORLAX": (combine("S_FLUTE", "SNORLAXLEFT"), OK("ROUTE_12"), "ROUTE 12 or 16"),
    "ARTICUNO": (combine(SEAFOAM, "NOT_ARTICUNO"), OK("SEAFOAM_ISLANDS_B4F"), "SEAFOAM B4F"),
    "ZAPDOS": (combine("SURF", "NOT_ZAPDOS"), OK("POWER_PLANT"), "POWER PLANT"),
    "MOLTRES": (combine(VRGATE, "NOT_MOLTRES"), OK("VICTORY_ROAD_2F"), "VICTORY ROAD 2F"),
    "MEWTWO": (combine("S_CHAMPION", "NOT_MEWTWO"), OK("CERULEAN_CAVE_B1F"), "CERULEAN CAVE B1F"),
}
for sp, w in SPECIAL_WILD.items():
    wild[sp].append(w)

evos_src = strip_comments(rd("data/pokemon/evos_moves.asm"))
prev = {}
STONES = {"FIRE_STONE": "FIRE", "WATER_STONE": "WATER", "THUNDER_STONE": "THUNDER", "LEAF_STONE": "LEAF",
          "MOON_STONE": "MOON"}
STONE_GATE = {"FIRE_STONE": "CUT", "WATER_STONE": "CUT", "THUNDER_STONE": "CUT", "LEAF_STONE": "CUT",
              "MOON_STONE": "S_BROCK"}
for m in re.finditer(r"^(\w+)EvosMoves:\s*\n(.*?)db 0", evos_src, re.M | re.S):
    for line in m.group(2).splitlines():
        p = [x.strip() for x in line.replace("db", "").split(",") if x.strip()]
        if not p:
            continue
        if p[0] == "EVOLVE_LEVEL":
            prev.setdefault(p[2], []).append(("L", m.group(1), p[1]))
        elif p[0] == "EVOLVE_ITEM":
            prev.setdefault(p[3], []).append(("I", m.group(1), p[1]))
        elif p[0] == "EVOLVE_TRADE":
            prev.setdefault(p[2], []).append(("T", m.group(1), None))
lab2species = {}
for sp in MONNAME:
    lab2species[re.sub(r"[^A-Za-z0-9]", "", MONNAME[sp].title().replace("♂", "M").replace("♀", "F"))] = sp
lab2species.update({"NidoranM": "NIDORAN_M", "NidoranF": "NIDORAN_F", "MrMime": "MR_MIME", "Farfetchd": "FARFETCHD"})
for give, get, nick, where in trades:
    if where:
        pass  # added after DEX milestones exist (they need the species given away)

ACQ = {}


def dex_ms(sp):
    """Milestone: this species is in the Pokedex (owned)."""
    name = "DEX_" + sp
    if name not in MS:
        g, o, t = acquire(sp)
        ms(name, [(K_DEX, str(DEX.index(sp)), False)], MS[g].rank + 0.1)
        st = [n for n in atoms(g) if n.startswith("S_")] + [STORY_OF[n] for n in atoms(g) if n in STORY_OF]
        if st:
            STORY_OF[name] = max(st, key=lambda n: MS[n].rank)
    return name


def acquire(sp, depth=0):
    """(gate, order, text): the earliest way to get sp in Red."""
    if sp in ACQ:
        return ACQ[sp]
    if sp in STARTERS or sp in ONE_OF or sp == "MEW":
        text = "Starter or trade" if sp in STARTERS else ("Event only" if sp == "MEW" else ONE_OF[sp])
        ACQ[sp] = ("S_CHAMPION", LATE, text)
        return ACQ[sp]
    opts = list(wild.get(sp, []))
    for kind, lab, arg in prev.get(sp, []):
        src = lab2species[lab]
        if depth > 6:
            continue
        sg, so, st = acquire(src, depth + 1)
        n = MONNAME[src]
        if kind == "I" and sg != "S_CHAMPION":
            opts.append((combine(dex_ms(src), STONE_GATE[arg]), so, "%s ST.%s" % (STONES[arg], n)))
        elif kind == "L":
            t = "Evolve %s %s" % (n, arg)
            opts.append(("S_CHAMPION", LATE, t if len(t) <= LINE2_MAX else "Evo %s %s" % (n, arg)))
        elif kind == "T":
            opts.append(("S_CHAMPION", LATE, "Trade " + n))
    for give, get, nick, where in trades:
        if where and get == sp and depth < 6:
            opts.append((combine(G[where], dex_ms(give)), OK(where), "Trade " + TRADE_TOWN[where]))
    if not opts:
        ACQ[sp] = ("S_CHAMPION", LATE, "Trade from BLUE")
        return ACQ[sp]
    ACQ[sp] = min(opts, key=lambda o: (MS[o[0]].rank, o[1]))
    return ACQ[sp]


dex_entries = []
for i, sp in enumerate(DEX):
    g, o, t = acquire(sp)
    dex_entries.append(Entry([(K_DEX, str(i))], "%03d %s" % (i + 1, MONNAME[sp]), t, g, o))

# ---------- TRADES
tr2 = []
for i, (give, get, nick, wh) in enumerate(trades):
    if not wh:
        continue
    g = combine(G[wh], dex_ms(give))
    tr2.append(Entry([(K_TRADE, str(i))], "%s▶%s" % (MONNAME[give], MONNAME[get]), mapname(wh), g, OK(wh)))

# ---------- SPECIAL Pokemon
SPECIAL = [
    ([(K_TOGGLE, "TOGGLE_CELADON_MANSION_EEVEE_GIFT")], "EEVEE", "CELADON MANSION", G["CELADON_MANSION_ROOF_HOUSE"],
     "CELADON_MANSION_ROOF_HOUSE"),
    ([(K_FLAGS4, "BIT_GOT_LAPRAS")], "LAPRAS", "SILPH CO.7F", "S_DRINK", "SILPH_CO_7F"),
    ([(K_EVENT, "EVENT_GOT_HITMONLEE"), (K_EVENT, "EVENT_GOT_HITMONCHAN")], "HITMONLEE/CHAN", "FIGHTING DOJO",
     "S_DRINK", "FIGHTING_DOJO"),
    ([(K_EVENT, "EVENT_BOUGHT_MAGIKARP")], "MAGIKARP", "MT.MOON CENTER", "S_BROCK", "MT_MOON_POKECENTER"),
    ([(K_DEX, str(DEX.index("OMANYTE"))), (K_DEX, str(DEX.index("KABUTO")))], "OMANYTE/KABUTO",
     "Fossil, CINNABAR", combine("SURF", "FOSSIL"), "CINNABAR_LAB_FOSSIL_ROOM"),
    ([(K_DEX, str(DEX.index("AERODACTYL")))], "AERODACTYL", "Amber, CINNABAR", combine("SURF", "OLDAMBER"),
     "CINNABAR_LAB_FOSSIL_ROOM"),
    ([(K_EVENT, "EVENT_BEAT_ROUTE12_SNORLAX")], "SNORLAX", "ROUTE 12", "S_FLUTE", "ROUTE_12"),
    ([(K_EVENT, "EVENT_BEAT_ROUTE16_SNORLAX")], "SNORLAX", "ROUTE 16", "S_FLUTE", "ROUTE_16"),
    ([(K_EVENT, "EVENT_BEAT_ZAPDOS")], "ZAPDOS", "POWER PLANT", "SURF", "POWER_PLANT"),
    ([(K_EVENT, "EVENT_BEAT_ARTICUNO")], "ARTICUNO", "SEAFOAM B4F", SEAFOAM, "SEAFOAM_ISLANDS_B4F"),
    ([(K_EVENT, "EVENT_BEAT_MOLTRES")], "MOLTRES", "VICTORY ROAD 2F", VRGATE, "VICTORY_ROAD_2F"),
    ([(K_EVENT, "EVENT_BEAT_MEWTWO")], "MEWTWO", "CERULEAN CAVE B1F", "S_CHAMPION", "CERULEAN_CAVE_B1F"),
]
sp_entries = [Entry(c, a, b, g, OK(m)) for c, a, b, g, m in SPECIAL]
_pp = strip_comments(rd("scripts/PowerPlant.asm"))
_pp_headers = dict(re.findall(r"^(\w+TrainerHeader\w*):\s*\n\s*trainer (EVENT_\w+),", _pp, re.M))
_pp_text = {lab: c for lab, c in re.findall(r"dw_const (\w+),\s*(TEXT_\w+)", _pp)}
for m in re.finditer(r"^(\w+):\s*\n(.*?)(?=^\w+:|\Z)", _pp, re.M | re.S):
    h = re.search(r"ld hl, (\w+TrainerHeader\w*)\b", m.group(2))
    if not h or m.group(1) not in _pp_text or "ZAPDOS" in _pp_headers[h.group(1)]:
        continue
    o = [o for o in RM.objects("POWER_PLANT")[1] if o["text"] == _pp_text[m.group(1)]][0]
    sp_entries.append(Entry([(K_EVENT, _pp_headers[h.group(1)])], o["rest"][0].title().upper(), "POWER PLANT",
                            place_gate("POWER_PLANT", (o["x"], o["y"])), OK("POWER_PLANT"), mp="POWER_PLANT"))
assert len([e for e in sp_entries if e.l2 == "POWER PLANT"]) == 9, [e.l1 for e in sp_entries]

# ---------- STORY entries
story = []
for i, (name, checks, a, b, mp, extra) in enumerate(STORY):
    prev = STORY[i - 1][0] if i else "ALWAYS"
    g = combine(prev, {"VRGATE": VRGATE}.get(extra, extra)) if extra else prev
    story.append(Entry([(k, v) if not inv else None for k, v, inv in checks], a, b, g, OK(mp)))
for e, (name, checks, *_r) in zip(story, STORY):
    e.checks = [(k, v) for k, v, inv in checks]
    e.mode = MS[name].mode

category("STORY", "STORY", story, sort=False)
category("POKéDEX", "POKéDEX", dex_entries, sort=False)
category("ITEMS", "ITEM", it_entries)
category("HIDDEN", "HIDDEN ITEM", hid_entries)
category("GIFTS", "GIFT", gift_entries)
category("TRAINERS", "TRAINER", tr_entries)
category("TRADES", "TRADE", tr2)
category("SPECIAL", "SPECIAL", sp_entries)
# ---------- TOWNS: the game marks a town when you first walk into it (that is what FLY lists)
towns = [Entry([("PROG_TOWN", m)], mapname(m), "Go there once", G[m], OK(m), mp=m) for m in MAPS[:MAPS.index("UNUSED_MAP_0B")]]
assert len(towns) == 11
category("TOWNS", "TOWN", towns)

# ---------------------------------------------------------------- warnings
# Shown when the player walks into a map where something can be lost for good.
# (map, condition, four lines, entries that would be lost: the warning only shows while some are not done)
def wms(name, checks, mode="PROG_BOTH"):
    return ms(name, checks, 1, mode)


wms("W_NOHM01", [E("EVENT_GOT_HM01", True)])
wms("W_SSLEAVING", [E("EVENT_GOT_HM01"), E("EVENT_SS_ANNE_LEFT", True)])
wms("W_SILPH", [E("EVENT_BEAT_SILPH_CO_GIOVANNI", True)])
wms("W_R22", [E("EVENT_1ST_ROUTE22_RIVAL_BATTLE"), E("EVENT_BEAT_BROCK", True)])
wms("W_STARTER", [E("EVENT_OAK_ASKED_TO_CHOOSE_MON"), E("EVENT_GOT_STARTER", True)])
wms("W_FOSSIL", [E("EVENT_GOT_DOME_FOSSIL", True), E("EVENT_GOT_HELIX_FOSSIL", True)])
wms("W_DOJO", [E("EVENT_GOT_HITMONLEE", True), E("EVENT_GOT_HITMONCHAN", True)])
for leg in ("ZAPDOS", "ARTICUNO", "MOLTRES", "MEWTWO"):
    wms("W_" + leg, [E("EVENT_BEAT_" + leg, True), (K_DEX, str(DEX.index(leg)), True)])
for r in ("12", "16"):
    wms("W_SNORLAX%s_X" % r, [E("EVENT_BEAT_ROUTE%s_SNORLAX" % r, True), (K_DEX, str(DEX.index("SNORLAX")), True)])
    wms("W_SNORLAX" + r, [("PROG_MILESTONE", "S_FLUTE", False), ("PROG_MILESTONE", "W_SNORLAX%s_X" % r, False)])

SS_GROUP = [e for e in it_entries + hid_entries + tr_entries if e.mp and e.mp.startswith("SS_ANNE")]
SILPH_GROUP = [e for e in tr_entries if e.mp and e.mp.startswith("SILPH_CO")]
R22_GROUP = [e for e in tr_entries if e.checks == [(K_EVENT, "EVENT_BEAT_ROUTE22_RIVAL_1ST_BATTLE")]]
assert SS_GROUP and SILPH_GROUP and len(R22_GROUP) == 1
LEGEND = lambda n: ("%s is here." % n, "If it faints, it", "is gone for good.", "SAVE before it!")
CHOICE = "Choose with care!"
WARNINGS = [
    ("OAKS_LAB", "W_STARTER", ("You can take only", "one of the three.", "The rest: trades.", CHOICE), None),
    ("PEWTER_GYM", "W_R22", ("Beating BROCK ends", "the first rival", "fight on ROUTE 22.", "Fight him first!"),
     R22_GROUP),
    ("MT_MOON_B2F", "W_FOSSIL", ("Only one fossil:", "DOME or HELIX. The", "other: a trade.", CHOICE), None),
    ("SS_ANNE_CAPTAINS_ROOM", "W_NOHM01", ("Getting HM01 here", "makes the S.S.ANNE", "sail when you go.",
                                           "Finish it first!"), SS_GROUP),
    ("SS_ANNE_1F", "W_SSLEAVING", ("The S.S.ANNE sails", "for good as soon", "as you step off.", "Finish it first!"),
     SS_GROUP),
    ("ROUTE_12", "W_SNORLAX12", ("If this SNORLAX", "faints, it is", "gone for good.", "SAVE before it!"), None),
    ("ROUTE_16", "W_SNORLAX16", ("If this SNORLAX", "faints, it is", "gone for good.", "SAVE before it!"), None),
    ("FIGHTING_DOJO", "W_DOJO", ("Only one gift:", "HITMONLEE or", "HITMONCHAN.", CHOICE), None),
    ("SILPH_CO_11F", "W_SILPH", ("Beating GIOVANNI", "makes every ROCKET", "leave SILPH CO.", "Beat them first!"),
     SILPH_GROUP),
    ("POWER_PLANT", "W_ZAPDOS", LEGEND("ZAPDOS"), None),
    ("SEAFOAM_ISLANDS_B4F", "W_ARTICUNO", LEGEND("ARTICUNO"), None),
    ("VICTORY_ROAD_2F", "W_MOLTRES", LEGEND("MOLTRES"), None),
    ("CERULEAN_CAVE_B1F", "W_MEWTWO", LEGEND("MEWTWO"), None),
]
n_label = 0
for mp, cond, lines, group in WARNINGS:
    assert mp in MAPID and cond in MS
    for l in lines:
        check_text(l, 18)
    for e in group or []:
        if not e.label:
            e.label = "ProgEnt%d" % n_label
            n_label += 1

# ---------------------------------------------------------------- output
order = sorted(MS.values(), key=lambda m: (m.rank, m.name))
assert len(order) <= 256, len(order)
IDX = {m.name: i for i, m in enumerate(order)}
strings = {}


def sref(s):
    if s not in strings:
        strings[s] = "ProgStr%d" % len(strings)
    return strings[s]


def arg(kind, a):
    return str(IDX[a]) if kind == "PROG_MILESTONE" else a


def checks_asm(checks, mode):
    (k1, a1, i1) = checks[0]
    inv1 = " | PROG_INVERT" if i1 else ""
    if len(checks) == 2:
        (k2, a2, i2) = checks[1]
        inv2 = " | PROG_INVERT" if i2 else ""
        return "\tdb %s%s | %s\n\tdw %s\n\tdb %s%s\n\tdw %s" % (k1, inv1, mode, arg(k1, a1), k2, inv2, arg(k2, a2))
    return "\tdb %s%s\n\tdw %s" % (k1, inv1, arg(k1, a1))


out = ["; Generated by gen_progress.py from the disassembly. Do not edit by hand.", ""]
out.append("DEF NUM_PROGRESS_CATEGORIES EQU %d" % len(categories))
out.append("DEF NUM_PROGRESS_MILESTONES EQU %d" % len(order))
out.append("")
out.append("ProgressCategories:")
for i, (title, heading, ents) in enumerate(categories):
    out.append("\tdw ProgCatName%d, ProgCatEntries%d, %d, ProgCatHeading%d" % (i, i, len(ents), i))
out.append("")
for i, (title, heading, ents) in enumerate(categories):
    out.append('ProgCatName%d: db "%s@"' % (i, title))
    out.append('ProgCatHeading%d: db "%s@"' % (i, heading))
out.append("")
out.append("ProgressMilestones:")
out.append("; 6 bytes each: first check, then a second check (unused unless the first has PROG_EITHER/BOTH)")
for m in order:
    out.append("; %d %s" % (IDX[m.name], m.name))
    out.append(checks_asm(m.checks, m.mode))
    if len(m.checks) == 1:
        out.append("\tdb 0\n\tdw 0")
out.append("")
for i, (title, heading, ents) in enumerate(categories):
    out.append("ProgCatEntries%d:" % i)
    for e in ents:
        if e.label:
            out.append(e.label + ":")
        checks = [(k, a, False) for k, a in e.checks]
        out.append(checks_asm(checks, e.mode))
        out.append("\tdb %d, %d ; %s" % (IDX[e.gate], e.order, e.gate))
        out.append("\tdw %s, %s" % (sref(e.l1), sref(e.l2)))
out.append("")
out.append("ProgressWarnings:")
out.append("; map, condition, four lines, entries that would be lost (0: none)")
for i, (mp, cond, lines, group) in enumerate(WARNINGS):
    out.append("\tdb %s, %d ; %s" % (mp, IDX[cond], cond))
    out.append("\tdw %s" % ", ".join(sref(l) for l in lines))
    out.append("\tdw %s" % ("ProgWarnGroup%d" % i if group else "0"))
out.append("\tdb -1 ; end")
for i, (mp, cond, lines, group) in enumerate(WARNINGS):
    if group:
        out.append("ProgWarnGroup%d:" % i)
        out.append("\tdw %s, 0" % ", ".join(e.label for e in group))
out.append("")
out.append("; the two lines of every entry and warning, read through ProgressPlaceString")
out.append('SECTION "Progress Strings", ROMX, BANK[$2E]')
out.append("")
for s_, lab in strings.items():
    out.append('%s: db "%s@"' % (lab, s_))
open(OUT, "w", encoding="utf-8").write("\n".join(out) + "\n")
tot = sum(len(e) for _, _, e in categories)
print("categories:", [(t, len(e)) for t, _, e in categories], "entries", tot, "milestones", len(order),
      "strings", len(strings), "string bytes", sum(len(s_) + 1 for s_ in strings))
