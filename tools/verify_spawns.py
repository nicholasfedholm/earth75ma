#!/usr/bin/env python3
"""Independent check of the shipped earth75ma 0.0.7 spawn table and place lists.

usage: verify_spawns.py <mod folder> <out folder> <stock EWG unpack folder>
Reads the SHIPPED files only (spawnpresets-75ma.json, the 75 Ma oceanmask/freshmask, the 75 Ma places lists),
plus the stock EarthWorldgen places lists (for the name test) and <out folder>/places-snapped.csv (snap distances).
Prints, for every preset and fossil site: modern lat/lon, 75 Ma lat/lon, mask land/sea there, spawn lat/lon,
mask there, Quarter block x/z, and what EarthWorldgen's LandFinder would do at Quarter size
(port of EarthOceanMap centre sampling, cell 32, and LandFinder.Margin == 2; coast wobble ignored).
Name test: Python port of EarthWorldgen's Gazetteer (Normalize, name + alias index, population ranking).
Writes VERIFY.md, places-snapped-over-500km.csv and spawn-preview.png (2048 px wide: world + North America inset).
"""
import csv, json, math, os, sys, unicodedata
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

MOD, OUT, EWG = sys.argv[1], sys.argv[2], sys.argv[3]
VERSION = json.load(open(os.path.join(MOD, "modinfo.json"), encoding="utf-8"))["version"]
os.makedirs(OUT, exist_ok=True)
assert not os.path.exists(os.path.join(MOD, "assets", "earth75ma", "compatibility")), "compatibility override folder must not ship"
CFG = os.path.join(MOD, "assets", "earth75ma", "config", "75ma")
WGD = os.path.join(MOD, "assets", "earth75ma", "worldgen", "75ma")
tab = json.load(open(os.path.join(MOD, "assets", "earth75ma", "config", "spawnpresets-75ma.json"), encoding="utf-8"))
ocean = np.array(Image.open(os.path.join(WGD, "oceanmask.png")))
fresh = np.array(Image.open(os.path.join(WGD, "freshmask.png"))).astype(bool)
H, W = ocean.shape
QX, QZ, CELL = 100352, 50176, 32


def at(lon, lat):
    x = int((lon + 180) / 360 * W) % W; y = min(H - 1, int((90 - lat) / 180 * H))
    return "river" if (fresh[y, x] and ocean[y, x] == 0) else ("land" if ocean[y, x] == 0 else "sea")


gw, gh = (QX + CELL - 1) // CELL, (QZ + CELL - 1) // CELL
cx = np.clip(((np.arange(gw, dtype=np.int64) * CELL + CELL // 2) * W) // QX, 0, W - 1)
cz = np.clip(((np.arange(gh, dtype=np.int64) * CELL + CELL // 2) * H) // QZ, 0, H - 1)
cell_land = (ocean[np.ix_(cz, cx)] == 0) & ~fresh[np.ix_(cz, cx)]
margin2 = ndi.binary_erosion(cell_land, structure=np.ones((5, 5)))          # LandFinder.Margin == 2
dist_cells = ndi.distance_transform_edt(~margin2)

rows, bad = [], []
for kind, lst in (("preset", tab["presets"]), ("site", tab["sites"])):
    for e in lst:
        bx = int(math.floor((e["lon"] + 180) * QX / 360)); bz = int(math.floor((90 - e["lat"]) * QZ / 180))
        assert (bx, bz) == (e["quarterBlockX"], e["quarterBlockZ"]), e["code"]
        raw, sp = at(e["paleoLon"], e["paleoLat"]), at(e["lon"], e["lat"])
        d = float(dist_cells[bz // CELL, bx // CELL])
        eng = "exact (solid ground)" if d == 0 else f"LandFinder moves {d * CELL * 40075.017 / QX:.0f} km"
        if sp != "land" or raw != e["maskAtPaleo"] or d > 8:
            bad.append(e["code"])
        rows.append((kind, e.get("number", ""), e["code"], e["label"], e["modernLat"], e["modernLon"], e["paleoLat"], e["paleoLon"],
                     raw, e["lat"], e["lon"], sp, e["snapKm"], bx, bz, eng, e.get("csvDiffKm", "")))

hdr = "| kind | # | code | label | modern lat | modern lon | 75 Ma lat | 75 Ma lon | mask at 75 Ma pos | spawn lat | spawn lon | mask at spawn | moved km | Quarter x | Quarter z | EWG at Quarter | vs CSV km |"
lines = [hdr, "|" + "---|" * 17] + ["| " + " | ".join(str(v) for v in r) + " |" for r in rows]

# ---------------------------------------------------------------- places: counts, at sea, restored
NAMES = ("places-europe.json", "places-world.json")
new = {n: json.load(open(os.path.join(CFG, n), encoding="utf-8"))["places"] for n in NAMES}
old = {n: json.load(open(os.path.join(EWG, "assets", "earthworldgen", "config", n), encoding="utf-8"))["places"] for n in NAMES}
SUFFIXES = (" (shore)", " (nearest land)")


def orig(e):
    """Plain modern name of an entry: a snapped place keeps it as its first alias."""
    return e["a"][0] if e["n"].endswith(SUFFIXES) and e.get("a") else e["n"]


site_labels = {s["label"] for s in tab["sites"]}
counts, missing_total = {}, 0
for n in NAMES:
    have = {}
    for e in new[n]:
        if e["n"] in site_labels:
            continue
        have[(orig(e), e["c"])] = have.get((orig(e), e["c"]), 0) + 1
    want = {}
    for e in old[n]:
        want[(e["n"], e["c"])] = want.get((e["n"], e["c"]), 0) + 1
    miss = sum(max(0, k - have.get(key, 0)) for key, k in want.items())
    missing_total += miss
    counts[n] = dict(stock=len(old[n]), shipped=len(new[n]), sites=sum(e["n"] in site_labels for e in new[n]),
                     sea=sum(at(e["lon"], e["lat"]) == "sea" for e in new[n]),
                     shore=sum(e["n"].endswith(SUFFIXES[0]) for e in new[n]), nearest=sum(e["n"].endswith(SUFFIXES[1]) for e in new[n]),
                     missing=miss)

snapped = list(csv.DictReader(open(os.path.join(OUT, "places-snapped.csv"), encoding="utf-8")))
km = np.array([float(r["snap_km"]) for r in snapped])


def stat(mask, label):
    v = km[mask]
    return f"| {label} | {len(v)} | {np.median(v):.0f} | {np.percentile(v, 95):.0f} | {v.max():.0f} | {int((v > 500).sum())} | {int((v > 1000).sum())} |"


why = np.array([r["why"] for r in snapped])
ex = ["", "## Places restored (follow-up: no stock place is dropped)", "",
      f"Restored by snapping: {len(snapped)} places ({int((why == 'sea').sum())} under water at 75 Ma, "
      f"{int((why != 'sea').sum())} with no 75 Ma position). Stock places missing from the shipped lists: {missing_total}. "
      f"Shipped places at sea on the 75 Ma mask: {sum(c['sea'] for c in counts.values())}.", "",
      "| list | stock places | shipped places | of which fossil sites | marked (shore) | marked (nearest land) | at sea | stock (name, country) missing |",
      "|---|---|---|---|---|---|---|---|"]
ex += [f"| {n} | {c['stock']} | {c['shipped']} | {c['sites']} | {c['shore']} | {c['nearest']} | {c['sea']} | {c['missing']} |" for n, c in counts.items()]
ex += ["", "## Snap distance (km, raw 75 Ma position to where the place now sits)", "",
       "| group | places | median | p95 | max | over 500 km | over 1000 km |", "|---|---|---|---|---|---|---|",
       stat(np.ones(len(km), bool), "all snapped"), stat(why == "sea", "under water at 75 Ma"), stat(why != "sea", "no 75 Ma position (borrowed)")]
rules = {}
for r in snapped:
    rules[r["rule"]] = rules.get(r["rule"], 0) + 1
ex += ["", "Rule used: " + ", ".join(f"{k} {v}" for k, v in sorted(rules.items(), key=lambda kv: -kv[1])) + "."]

# ---------------------------------------------------------------- name test: port of EarthWorldgen Gazetteer
_MAP = {"ø": "o", "đ": "d", "ł": "l", "ß": "ss", "æ": "ae", "œ": "oe", "þ": "th", "ð": "d", "ħ": "h", "ı": "i"}


def norm(s):
    s = "".join(_MAP.get(c, c) for c in s.lower())
    return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c)[0] == "L" or unicodedata.category(c) == "Nd")


def index(lists):
    """Gazetteer.Load: europe list first, then world; the name and every alias (cut at the first comma) share one index."""
    by = {}
    for n in NAMES:
        for e in lists[n]:
            for x in [e["n"]] + list(e.get("a") or []):
                k = norm(x.split(",")[0])
                if k and not any(o is e for o in by.setdefault(k, [])):
                    by[k].append(e)
    return by


def find(by, query):
    """Gazetteer.TryFind without a qualifier: same key, biggest population first. Returns (winner, tie at the top)."""
    b = by.get(norm(query.split(",")[0]))
    if not b:
        return None, False
    top = max(e.get("p", 0) for e in b)
    w = [e for e in b if e.get("p", 0) == top]
    return w[0], len({(orig(e), e["c"]) for e in w}) > 1


by_old, by_new = index(old), index(new)
ex += ["", "## Name test (typing the plain modern name)", "",
       "Ranking in EarthWorldgen's Gazetteer is population only; a snapped place keeps its plain name as an alias, so the same entry wins as in stock.", "",
       "| typed | expected | stock EWG picks | earth75ma picks (label) | plain name | country | pop | 75 Ma lat | 75 Ma lon | mask | result |", "|---|---|---|---|---|---|---|---|---|---|---|"]
name_fail = []
for q, want_n, want_c in (("London", "London", "GB"), ("New York", "New York City", "US"), ("Sydney", "Sydney", "AU"), ("Tokyo", "Tokyo", "JP")):
    so, _ = find(by_old, q); sn, tie = find(by_new, q)
    ok = bool(so and sn and (so["n"], so["c"]) == (want_n, want_c) and (orig(sn), sn["c"]) == (want_n, want_c)
              and at(sn["lon"], sn["lat"]) != "sea" and not tie)
    if not ok:
        name_fail.append(q)
    ex.append(f"| {q} | {want_n}, {want_c} | {so['n'] + ', ' + so['c'] if so else 'NOT FOUND'} | {sn['n'] if sn else 'NOT FOUND'} | "
              f"{orig(sn) if sn else ''} | {sn['c'] if sn else ''} | {sn.get('p', 0) if sn else ''} | {sn['lat'] if sn else ''} | "
              f"{sn['lon'] if sn else ''} | {at(sn['lon'], sn['lat']) if sn else ''} | {'PASS' if ok else 'FAIL'} |")
    print("name test", q, "->", sn["n"] if sn else None, sn["c"] if sn else None, "PASS" if ok else "FAIL")
# other same-named places must still be reachable with a qualifier and must not win
lon_other = [e for e in by_new.get("london", []) if not (orig(e) == "London" and e["c"] == "GB")]
ex += ["", "Other entries under the key \"london\" (lose on population, reachable as \"London, <country>\"): "
       + (", ".join(f"{e['n']} {e['c']} p={e.get('p', 0)}" for e in lon_other) or "none") + "."]

mism, ties, new_keys = [], 0, 0
for k, b in by_old.items():
    so, tie_o = find(by_old, k) if False else (None, False)
    top = max(e.get("p", 0) for e in b); wo = {(e["n"], e["c"]) for e in b if e.get("p", 0) == top}
    bn = by_new.get(k)
    if not bn:
        mism.append((k, sorted(wo)[0], "KEY MISSING")); continue
    topn = max(e.get("p", 0) for e in bn); wn = {(orig(e), e["c"]) for e in bn if e.get("p", 0) == topn}
    if len(wo) > 1:
        ties += 1
    if wo != wn:
        mism.append((k, sorted(wo)[0], sorted(wn)[0]))
new_keys = len(set(by_new) - set(by_old))
ex += ["", f"Ranking regression over every stock lookup key ({len(by_old)} keys): {len(mism)} keys where earth75ma picks a different "
       f"place than stock; {ties} keys are population ties in stock already (unstable there too, compared as sets). "
       f"New keys added by earth75ma (suffixed labels and fossil sites): {new_keys}."]
ex += [f"- `{k}`: stock {a}, earth75ma {b}" for k, a, b in mism[:40]]

# ---------------------------------------------------------------- places snapped over 500 km
far = sorted((r for r in snapped if float(r["snap_km"]) > 500), key=lambda r: -float(r["snap_km"]))
with open(os.path.join(OUT, "places-snapped-over-500km.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, fieldnames=list(snapped[0].keys())); w.writeheader(); w.writerows(far)
ex += ["", f"## Places snapped over 500 km ({len(far)}; also in places-snapped-over-500km.csv, all {len(snapped)} in places-snapped.csv)", "",
       "| km | place | country | modern lat | modern lon | why | raw 75 Ma lat | raw 75 Ma lon | placed lat | placed lon | rule | borrowed from |", "|---|---|---|---|---|---|---|---|---|---|---|---|"]
ex += [f"| {float(r['snap_km']):.0f} | {r['name']} | {r['country']} | {r['modern_lat']} | {r['modern_lon']} | {r['why']} | {r['paleo_lat']} | "
       f"{r['paleo_lon']} | {r['snap_lat']} | {r['snap_lon']} | {r['rule']} | {r['borrowed_from']} |" for r in far]

place_fail = bool(missing_total or sum(c["sea"] for c in counts.values()) or name_fail or mism)
summary = [f"points: {len(rows)} ({len(tab['presets'])} presets, {len(tab['sites'])} sites); spawn on land: "
           f"{sum(r[11] == 'land' for r in rows)}/{len(rows)}; raw 75 Ma position at sea: {sum(r[8] == 'sea' for r in rows)}; FAIL: {bad or 'none'}"]
summary += [f"{n}: {c['shipped']} places ({c['stock']} stock + {c['sites']} fossil sites), {c['sea']} at sea on the mask, {c['missing']} stock places missing"
            for n, c in counts.items()]
summary += [f"places restored by snapping: {len(snapped)}; snap km median {np.median(km):.0f}, p95 {np.percentile(km, 95):.0f}, max {km.max():.0f}; over 500 km: {len(far)}",
            f"name test FAIL: {name_fail or 'none'}; ranking mismatches vs stock: {len(mism)}; places check: {'FAIL' if place_fail else 'PASS'}"]
open(os.path.join(OUT, "VERIFY.md"), "w", encoding="utf-8").write(
    f"# earth75ma {VERSION} spawn and places verification\n\n" + "\n".join("- " + s for s in summary) + "\n\n## Spawn points\n\n"
    + "\n".join(lines) + "\n" + "\n".join(ex) + "\n")
for r in rows:
    print(f"{str(r[1]):>2} {r[2]:14s} mod {r[4]:8.3f} {r[5]:9.3f} | 75Ma {r[6]:8.3f} {r[7]:9.3f} {r[8]:5s} | spawn {r[9]:8.3f} {r[10]:9.3f} {r[11]:5s} {r[12]:6.1f} km | Q {r[13]:6d} {r[14]:6d} | {r[15]} | csv {r[16]}")
print("\n".join(summary))

# ---------------------------------------------------------------- preview
def panel(lon0, lon1, lat0, lat1, pw, ph, label_all):
    x0, x1 = int((lon0 + 180) / 360 * W), int((lon1 + 180) / 360 * W); y0, y1 = int((90 - lat1) / 180 * H), int((90 - lat0) / 180 * H)
    rgb = np.zeros((H, W, 3), np.uint8); rgb[:] = (28, 52, 96); rgb[ocean == 0] = (196, 186, 140); rgb[fresh & (ocean == 0)] = (90, 150, 210)
    im = Image.fromarray(rgb[y0:y1, x0:x1]).resize((pw, ph), Image.LANCZOS if (x1 - x0) > pw else Image.NEAREST)
    d = ImageDraw.Draw(im)
    P = lambda lon, lat: ((lon - lon0) / (lon1 - lon0) * pw, (lat1 - lat) / (lat1 - lat0) * ph)
    step = 30 if lon1 - lon0 > 180 else 10
    for g in range(-180, 181, step):
        if lon0 <= g <= lon1: d.line([P(g, lat0), P(g, lat1)], fill=(70, 90, 130)); d.text((P(g, lat1)[0] + 2, 2), str(g), fill="white")
        if lat0 <= g <= lat1: d.line([P(lon0, g), P(lon1, g)], fill=(70, 90, 130)); d.text((2, P(lon0, g)[1] + 2), str(g), fill="white")
    for r in rows:
        if not (lon0 <= r[10] <= lon1 and lat0 <= r[9] <= lat1): continue
        a, b = P(r[7], r[6]), P(r[10], r[9]); col = (255, 60, 60) if r[0] == "preset" else (255, 220, 0)
        d.line([a, b], fill=(255, 255, 255), width=1); d.ellipse([a[0] - 3, a[1] - 3, a[0] + 3, a[1] + 3], outline=(255, 255, 255))
        d.ellipse([b[0] - 4, b[1] - 4, b[0] + 4, b[1] + 4], fill=col, outline=(0, 0, 0))
        if label_all or r[0] == "preset":
            t = r[2] if r[0] == "preset" else f"{r[1]} {r[3].replace(' Formation', '')}"
            d.text((b[0] + 6, b[1] - 5), t, fill=(255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0))
    return im


sheet = Image.new("RGB", (2048, 1024 + 1100 + 24), (0, 0, 0))
sheet.paste(panel(-180, 180, -90, 90, 2048, 1024, False), (0, 0))
sheet.paste(panel(-100, -30, 25, 72, 1639, 1100, True), (0, 1024))
ImageDraw.Draw(sheet).text((1650, 1040), f"earth75ma {VERSION} spawn points\non the 75 Ma land mask\n\nred = EWG presets\nyellow = fossil sites (numbered)\nhollow ring = raw rotated\n  75 Ma position\nfilled dot = spawn (on land)\nblue lines = rivers (freshmask)\n\ntop: world\nbottom: North America,\n  lon -100..-30, lat 25..72", fill="white")
sheet.save(os.path.join(OUT, "spawn-preview.png"))
print("preview", sheet.size, os.path.join(OUT, "spawn-preview.png"))
sys.exit(1 if (bad or place_fail) else 0)
