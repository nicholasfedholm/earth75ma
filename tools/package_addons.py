#!/usr/bin/env python3
"""Assemble the earth75ma 0.0.9-addons staged branch build (era system + Flagships + Flora + Ebony modules) and zip it.

usage: package_addons.py <work folder> <asset base mod folder>
  <asset base>  an unpacked earth75ma 0.0.8 mod folder; its assets/ tree (12 masks, 8 tables, spawn table, lookup grid, lang) is
                copied byte for byte. The DLL is the new build (<repo>/build/earth75ma.dll); modinfo.json and README.md come from
                the repo; the Ebony patch comes from <repo>/assets/earth75ma/patches.
The zip is staged in <work folder> only; nothing is installed anywhere.
"""
import hashlib, json, os, shutil, sys, zipfile

WORK, BASE = sys.argv[1], sys.argv[2]
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LABEL = "0.0.9-addons"
MOD = os.path.join(WORK, f"earth75ma-{LABEL}")
ZIP = os.path.join(WORK, f"earth75ma-{LABEL}.zip")


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()


if os.path.exists(MOD):
    shutil.rmtree(MOD)
shutil.copytree(os.path.join(BASE, "assets"), os.path.join(MOD, "assets"))
os.makedirs(os.path.join(MOD, "assets", "earth75ma", "patches"), exist_ok=True)
shutil.copyfile(os.path.join(REPO, "assets", "earth75ma", "patches", "oregon-ebony.json"),
                os.path.join(MOD, "assets", "earth75ma", "patches", "oregon-ebony.json"))
shutil.copyfile(os.path.join(BASE, "worldconfig.json"), os.path.join(MOD, "worldconfig.json"))   # defines the earthEra / start dropdowns; unchanged from 0.0.8
shutil.copyfile(os.path.join(REPO, "build", "earth75ma.dll"), os.path.join(MOD, "earth75ma.dll"))
shutil.copyfile(os.path.join(REPO, "modinfo.json"), os.path.join(MOD, "modinfo.json"))
shutil.copyfile(os.path.join(REPO, "README.md"), os.path.join(MOD, "README.md"))
mi = json.load(open(os.path.join(MOD, "modinfo.json"), encoding="utf-8"))
assert mi["modid"] == "earth75ma" and mi["version"] == "0.0.9", mi
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
assert "worldconfig.json" in names and "modinfo.json" in names and "earth75ma.dll" in names and "assets/earth75ma/patches/oregon-ebony.json" in names
for banned in ("earthflagships", "earthflora", "oregonebony"):
    assert not any(banned in n.lower() for n in names), banned
print(f"{os.path.basename(ZIP)}: {os.path.getsize(ZIP):,} bytes, {len(names)} entries, CRC ok, sha256 {sha(ZIP)}")
