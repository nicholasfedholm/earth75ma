#!/usr/bin/env python3
"""earth75ma 0.0.7: regenerate ONLY worldconfig.json + worldconfig-lang-en.generated.json without the GPlates/mask pipeline.
Executes the world-config block of build_tables.py (single source) with presets/sites read from the shipped spawnpresets-75ma.json.
usage: gen_worldconfig.py <src folder> <mod folder> <stock EWG unpack> <spawnpresets-75ma.json>"""
import json, os, sys
SRC, MOD, EWG, SP = sys.argv[1:5]
d = json.load(open(SP, encoding="utf-8")); presets, sites = d["presets"], d["sites"]
t = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "build_tables.py"), encoding="utf-8").read()
a = t.index("# ---------------------------------------------------------------- world config dropdown")
b = t.index('json.dump(dict(stats=stats)')
stats = {}
g = {"json": json, "os": os, "SRC": SRC, "MOD": MOD, "EWG": EWG,
     "presets": presets, "sites": sites, "stats": stats}
exec(compile(t[a:b], "build_tables-worldconfig-block", "exec"), g)
json.dump(g["lang"], open(os.path.join(SRC, "worldconfig-lang-en.generated.json"), "w", encoding="utf-8"), indent="	", ensure_ascii=False)
json.dump(dict(stats=stats), open(os.path.join(SRC, "build_stats.json"), "w"), indent=1)
