#!/usr/bin/env python3
"""earth75ma 0.0.8: check the shipped worldconfig.json and lang file for the era-prefixed start dropdown.
usage: check_start_dropdown.py <mod folder> <stock EWG unpack folder>   (exit 0 = pass)"""
import json, os, sys
MOD, EWG = sys.argv[1], sys.argv[2]
wc = json.load(open(os.path.join(MOD, "worldconfig.json"), encoding="utf-8-sig"))
lang = json.load(open(os.path.join(MOD, "assets", "game", "lang", "worldconfig-en.json"), encoding="utf-8-sig"))
ewg = json.load(open(os.path.join(EWG, "worldconfig.json"), encoding="utf-8-sig"))
spawn = json.load(open(os.path.join(MOD, "assets", "earth75ma", "config", "spawnpresets-75ma.json"), encoding="utf-8"))
fails = []
def check(ok, msg):
    print(("PASS " if ok else "FAIL ") + msg)
    if not ok: fails.append(msg)
check(wc.get("playstyles") == [], 'worldconfig "playstyles": []')
attrs = {a["code"]: a for a in wc["worldConfigAttributes"]}
check(sorted(attrs) == ["earth75maStart", "earthEra"], f"attributes = {sorted(attrs)}")
check("earth75maSpawnSite" not in attrs, "no earth75maSpawnSite dropdown (folded into earth75maStart)")
era = attrs["earthEra"]; st = attrs["earth75maStart"]
check(era["values"] == ["modern", "campanian75"] and era["default"] == "modern", "era values modern/campanian75, default modern")
for a in (era, st):
    check(a["category"] == "earth" and a["onlyDuringWorldCreate"] is True and a["dataType"] == "dropdown", a["code"] + " earth tab, create only")
    check(a["names"] == a["values"], a["code"] + " names == values (no IndexOutOfRange)")
    check(f"worldattribute-{a['code']}" in lang, a["code"] + " has a title label")
    miss = [v for v in a["values"] if f"worldconfig-{a['code']}-{v}" not in lang]
    check(not miss, f"{a['code']}: {len(a['values'])} values, {len(miss)} without a label {miss[:3]}")
v = st["values"]
check(st["default"] == "ewg" and v[0] == "ewg", "start default ewg")
ewg_r = [a for a in ewg["worldConfigAttributes"] if a["code"] == "earthSpawnRegion"][0]["values"]
mod_r = [x[len("modern-"):] for x in v if x.startswith("modern-")]
check(mod_r == ewg_r, f"modern-* == EWG earthSpawnRegion values exactly ({len(mod_r)}: {','.join(mod_r)})")
p75 = [x[len("75ma-"):] for x in v if x.startswith("75ma-") and not x.startswith("75ma-site-")]
check(p75 == [p["code"] for p in spawn["presets"]] and all(r in ewg_r for r in p75), f"75ma-* == spawnpresets-75ma.json presets ({len(p75)})")
s75 = [x[len("75ma-site-"):] for x in v if x.startswith("75ma-site-")]
check(s75 == [s["code"] for s in spawn["sites"]] and len(s75) == 17, f"75ma-site-* == spawnpresets-75ma.json sites ({len(s75)})")
check(len(v) == len(set(v)) == 1 + len(mod_r) + len(p75) + len(s75), f"{len(v)} values, unique, nothing else")
check(all(lang[f"worldconfig-earth75maStart-{x}"].startswith("Modern: ") for x in v if x.startswith("modern-")), 'modern labels start "Modern: "')
check(all(lang[f"worldconfig-earth75maStart-{x}"].startswith("75 Ma: ") for x in v if x.startswith("75ma-")), '75 Ma labels start "75 Ma: "')
check(not any(c in s for s in lang.values() for c in "–—"), "no en/em dashes in labels")
print("RESULT", "FAIL" if fails else "PASS", f"({len(fails)} fail)")
sys.exit(1 if fails else 0)
