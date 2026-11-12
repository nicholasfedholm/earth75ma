#!/usr/bin/env python3
"""Assemble the earth75ma 0.0.8 mod folder and zip.

usage: package.py <work folder>
Run after build_tables.py and after `dotnet build` (DLL in <src>/build/earth75ma.dll).
Copies the 12 mask PNGs (no redwoodmask) and the 4 small config tables of the 0.0.2 pack BYTE FOR BYTE into the mod's own
domain (assets/earth75ma/worldgen/75ma, assets/earth75ma/config/75ma). Nothing is written under a
compatibility/ folder: EarthWorldgen's own assets are never overridden on disk, the DLL swaps them in memory
for Campanian worlds only.
"""
import hashlib, json, os, shutil, sys, zipfile

WORK = sys.argv[1]
VERSION = "0.0.8"
SRC = WORK   # 0.0.8: session folder is the source folder
MOD = os.path.join(WORK, f"earth75ma-{VERSION}")
ZIP = os.path.join(WORK, f"earth75ma-{VERSION}.zip")
COMPAT_IN = os.path.join("C:/Work/vs/vs_scratch/cretaceous-75ma-detailed-map-20261110", "grok-audit-20261111", "out", "earth75ma-compat", "assets", "earth75ma", "compatibility", "earthworldgen")
PNGS = ["bathymask", "biomes", "dryness", "freshmask", "icemask", "lakemask", "oceanmask", "rainfall", "reliefmask",
        "saltlakemask", "stillmask", "temperature"]
SMALL = ["basins", "lakes", "landmarks", "monuments"]
GENERATED = ["places-europe", "places-world"]


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()


wg = os.path.join(MOD, "assets", "earth75ma", "worldgen", "75ma")
cf = os.path.join(MOD, "assets", "earth75ma", "config", "75ma")
os.makedirs(wg, exist_ok=True); os.makedirs(cf, exist_ok=True)
for n in PNGS:
    a, b = os.path.join(COMPAT_IN, "worldgen", n + ".png"), os.path.join(wg, n + ".png")
    shutil.copyfile(a, b); assert sha(a) == sha(b), n
for n in SMALL:
    a, b = os.path.join(COMPAT_IN, "config", n + ".json"), os.path.join(cf, n + ".json")
    shutil.copyfile(a, b); assert sha(a) == sha(b), n
for n in GENERATED:
    assert os.path.getsize(os.path.join(cf, n + ".json")) > 100000, n + " not generated"
assert sorted(os.listdir(wg)) == sorted(n + ".png" for n in PNGS)
assert sorted(os.listdir(cf)) == sorted(n + ".json" for n in SMALL + GENERATED)

shutil.copyfile(os.path.join(SRC, "build", "earth75ma.dll"), os.path.join(MOD, "earth75ma.dll"))
shutil.copyfile(os.path.join(SRC, "modinfo.json"), os.path.join(MOD, "modinfo.json"))
shutil.copyfile(os.path.join(SRC, "README.md"), os.path.join(MOD, "README.md"))
os.makedirs(os.path.join(MOD, "assets", "game", "lang"), exist_ok=True)
shutil.copyfile(os.path.join(SRC, "worldconfig-lang-en.generated.json"), os.path.join(MOD, "assets", "game", "lang", "worldconfig-en.json"))
assert json.load(open(os.path.join(MOD, "modinfo.json"), encoding="utf-8"))["version"] == VERSION
assert not os.path.exists(os.path.join(MOD, "assets", "earth75ma", "compatibility"))

files = []
for root, _, names in os.walk(MOD):
    for n in names:
        full = os.path.join(root, n)
        files.append((os.path.relpath(full, MOD).replace("\\", "/"), full))
files.sort()
bad = [r for r, _ in files if r.lower().endswith((".pdb", ".zip", ".exe")) or "/obj/" in r or "/bin/" in r]
assert not bad, bad
if os.path.exists(ZIP):
    os.remove(ZIP)
with zipfile.ZipFile(ZIP, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for rel, full in files:
        z.write(full, rel)
with zipfile.ZipFile(ZIP) as z:
    assert z.testzip() is None
    names = z.namelist()
assert "modinfo.json" in names and "earth75ma.dll" in names and not any("compatibility" in n for n in names)
print(f"{os.path.basename(ZIP)}: {os.path.getsize(ZIP):,} bytes, {len(names)} entries, CRC ok, sha256 {sha(ZIP)[:16]}")
for rel, full in files:
    print(f"  {os.path.getsize(full):>9,}  {rel}")
print("mask PNGs byte-identical to the 0.0.2 pack:", len(PNGS), "| small tables byte-identical:", len(SMALL))
