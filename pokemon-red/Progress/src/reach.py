"""Which spots on a map need Cut, Surf or Strength, from the map's own tiles.

Walks the map's 16x16 cells from every warp and every connected edge, the way the game checks
collision (the bottom-left tile of each cell against the tileset's passable list). Water needs
Surf, cut trees need Cut, boulders need Strength, ledges are one-way. Things the game blocks by
script (guards, badges, Snorlax) are not modelled here: those are the map gates.
"""
import collections
import glob
import os
import re

CAPS = [(), ("cut",), ("str",), ("surf",), ("cut", "str"), ("cut", "surf"), ("surf", "str"),
        ("cut", "surf", "str")]
DIRS = {"down": (0, 1), "up": (0, -1), "left": (-1, 0), "right": (1, 0)}


class Maps:
    def __init__(self, root):
        self.root = root
        rd = lambda p: open(os.path.join(root, p), encoding="utf-8").read()
        strip = lambda s: "\n".join(l.split(";")[0] for l in s.splitlines())
        self.dims = {m: (int(w), int(h)) for m, w, h in
                     re.findall(r"map_const (\w+),\s*(\d+),\s*(\d+)", rd("constants/map_constants.asm"))}
        self.blkfile = {}
        pending = []
        for l in rd("maps.asm").splitlines():
            m = re.match(r"^(\w+)_Blocks:\s*(?:INCBIN \"([^\"]+)\")?", l)
            if m:
                pending.append(m.group(1))
                if m.group(2):
                    for n in pending:
                        self.blkfile[n] = m.group(2)
                    pending = []
        tsconsts = re.findall(r"const (\w+)", strip(rd("constants/tileset_constants.asm")))
        tsnames = re.findall(r"^\s*tileset (\w+),", rd("data/tilesets/tileset_headers.asm"), re.M)
        self.tsname = dict(zip(tsconsts, tsnames))
        # blocksets and collision lists, with labels that share one INCBIN / list
        self.blockset = {}
        pending = []
        for l in rd("gfx/tilesets.asm").splitlines():
            m = re.match(r"^(\w+)_Block::\s*(?:INCBIN \"([^\"]+)\")?", l)
            if m:
                pending.append(m.group(1))
                if m.group(2):
                    data = open(os.path.join(root, m.group(2)), "rb").read()
                    for n in pending:
                        self.blockset[n] = data
                    pending = []
        self.coll = {}
        pending = []
        for l in strip(rd("data/tilesets/collision_tile_ids.asm")).splitlines():
            m = re.match(r"^(\w+)_Coll::", l)
            if m:
                pending.append(m.group(1))
            m = re.match(r"\s*coll_tiles\s*(.*)", l)
            if m and pending:
                tiles = {int(x.strip().lstrip("$"), 16) for x in m.group(1).split(",") if x.strip()}
                for n in pending:
                    self.coll[n] = tiles
                pending = []
        self.water_ts = set(re.findall(r"db (\w+)", strip(rd("data/tilesets/water_tilesets.asm"))))
        self.ledges = set()
        for d, a, b, _ in re.findall(r"db SPRITE_FACING_(\w+),\s*\$(\w+),\s*\$(\w+),\s*(\w+)",
                                     rd("data/tilesets/ledge_tiles.asm")):
            self.ledges.add((d.lower(), int(a, 16), int(b, 16)))
        self.header = {}
        self.neighbors = {}
        for f in glob.glob(os.path.join(root, "data/maps/headers/*.asm")):
            s = open(f).read()
            m = re.search(r"map_header (\w+), (\w+), (\w+)", s)
            conns = re.findall(r"connection (\w+),", s)
            self.header[m.group(2)] = (m.group(1), m.group(3), conns)
            self.neighbors[m.group(2)] = dict(re.findall(r"connection (\w+), \w+, (\w+),", s))

    def objects(self, mp):
        label = self.header[mp][0]
        src = open(os.path.join(self.root, "data/maps/objects", label + ".asm")).read()
        src = "\n".join(l.split(";")[0] for l in src.splitlines())
        warps = [(int(x), int(y)) for x, y in re.findall(r"warp_event\s+(\d+),\s*(\d+),", src)]
        self.warpdest = getattr(self, "warpdest", {})
        self.warpdest[mp] = [(int(x), int(y), d, int(n)) for x, y, d, n in
                             re.findall(r"warp_event\s+(\d+),\s*(\d+),\s*(\w+),\s*(\d+)", src)]
        objs = []
        for o in re.findall(r"object_event ([^\n]+)", src):
            p = [x.strip() for x in o.split(",")]
            objs.append(dict(x=int(p[0]), y=int(p[1]), sprite=p[2], move=p[3], text=p[5], rest=p[6:]))
        return warps, objs

    def grid(self, mp):
        label, ts, conns = self.header[mp]
        w, h = self.dims[mp]
        blk = open(os.path.join(self.root, self.blkfile[label]), "rb").read()
        blk += bytes(max(0, w * h - len(blk)))  # one map's file is shorter than the map
        name = self.tsname[ts]
        bs, coll = self.blockset[name], self.coll[name]
        water = ts in self.water_ts
        cut_tile = 0x3D if ts == "OVERWORLD" else (0x50 if ts == "GYM" else None)
        W, H = w * 2, h * 2
        tile = {}
        kind = {}
        for cy in range(H):
            for cx in range(W):
                b = blk[(cy // 2) * w + (cx // 2)]
                t = bs[b * 16 + (2 * (cy % 2) + 1) * 4 + 2 * (cx % 2)]
                tile[(cx, cy)] = t
                if t in coll:
                    kind[(cx, cy)] = "walk"
                elif water and t == 0x14:
                    kind[(cx, cy)] = "surf"
                elif t == cut_tile:
                    kind[(cx, cy)] = "cut"
                else:
                    kind[(cx, cy)] = "wall"
        return W, H, tile, kind, conns

    def entries(self, mp, gated):
        """[(start cells, gate map or None)] and pass-through links {cell: {cells}}.
        gated: maps that have their own gate; an entry through them uses that gate."""
        W, H, tile, kind, conns = self.grid(mp)
        self.objects(mp)
        warps = self.warpdest[mp]
        out = []
        for d in conns:
            cells = set()
            for i in range(max(W, H)):
                c = {"north": (i, 0), "south": (i, H - 1), "west": (0, i), "east": (W - 1, i)}[d]
                if c in kind and kind[c] in ("walk", "surf"):
                    cells.add(c)
            out.append((cells, self.neighbors[mp][d]))
        links = collections.defaultdict(set)
        bydest = collections.defaultdict(list)
        for i, (x, y, dest, n) in enumerate(warps):
            if dest == "LAST_MAP":
                out.append(({(x, y)}, None))
                continue
            bydest[dest].append((i + 1, (x, y)))
        for dest, ws in bydest.items():
            # an independent way in: the building leads on to somewhere with its own outside door
            gate = self._through(dest, mp, gated)
            if gate is not False:
                out.append(({c for _, c in ws}, gate))
            # a building whose doors come out at different warps of this map joins them
            self.objects(dest)
            back = {n for (_, _, d2, n) in self.warpdest[dest] if d2 == "LAST_MAP"}
            cells = {c for i, c in ws}
            targets = [warps[n - 1][:2] for n in back if 0 < n <= len(warps)]
            group = cells | set(targets)
            if len(group) > 1:
                for a in group:
                    links[a] |= group - {a}
        return out, links

    def _through(self, b, a, gated):
        """False if b is a dead end seen from a; else the gate map to use (or None)."""
        seen, q, first = {b}, [b], None
        if b in gated:
            first = b
        while q:
            m = q.pop(0)
            self.objects(m)
            for (_, _, d, _) in self.warpdest[m]:
                if d == "LAST_MAP":
                    if m != b:
                        return first
                    continue
                if d not in seen and d != a and d in self.header:
                    seen.add(d)
                    q.append(d)
                    if first is None and d in gated:
                        first = d
            if self.neighbors.get(m) and m != b:
                return first
        return False

    def reach(self, mp, caps, starts, links):
        W, H, tile, kind, conns = self.grid(mp)
        warps, objs = self.objects(mp)
        blocked = set()
        boulders = set()
        for o in objs:
            if o["sprite"] == "SPRITE_BOULDER":
                boulders.add((o["x"], o["y"]))
            elif o["move"] == "STAY":
                blocked.add((o["x"], o["y"]))

        def can_enter(c):
            if c in blocked:
                return False
            if c in boulders:
                return "str" in caps
            k = kind.get(c)
            if k == "walk":
                return True
            if k == "surf":
                return "surf" in caps
            if k == "cut":
                return "cut" in caps
            return False

        start = {c for c in starts if c in kind and (kind[c] != "surf" or "surf" in caps)}
        seen = set(start)
        q = collections.deque(start)
        while q:
            c = q.popleft()
            nexts = []
            for dname, (dx, dy) in DIRS.items():
                n = (c[0] + dx, c[1] + dy)
                if n not in kind:
                    continue
                if (dname, tile[c], tile[n]) in self.ledges:
                    n2 = (n[0] + dx, n[1] + dy)
                    if n2 in kind and can_enter(n2):
                        nexts.append(n2)
                    continue
                if can_enter(n):
                    nexts.append(n)
            nexts += [l for l in links.get(c, ()) if l in kind]
            for n in nexts:
                if n not in seen:
                    seen.add(n)
                    q.append(n)
        return seen

    def options(self, mp, cell, gated, _cache={}):
        """[(gate map or None, caps)]: every way in that reaches next to cell, with the HMs it needs."""
        key = (mp, tuple(sorted(gated)))
        if key not in _cache:
            ents, links = self.entries(mp, gated)
            _cache[key] = (ents, links, {})
        ents, links, rc = _cache[key]
        out = []
        x, y = cell
        around = [(x, y), (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
        for ei, (starts, gate) in enumerate(ents):
            for caps in CAPS:
                k = (ei, caps)
                if k not in rc:
                    rc[k] = self.reach(mp, caps, starts, links)
                if any(c in rc[k] for c in around):
                    out.append((gate, caps))
                    break
        return out
