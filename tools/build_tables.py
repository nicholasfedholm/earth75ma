#!/usr/bin/env python3
"""earth75ma 0.0.7 table generator.

Rotates modern lat/lon to 75 Ma with the GPlates Web Service (model PALEOMAP,
the same call that produced modern_to_75Ma_coords.csv), checks every point
against the 75 Ma ocean mask, snaps sea points to land on the same
paleo-continent, and writes the JSON tables the mod ships.

usage: build_tables.py <work folder> <stock EWG unpack folder> [--offline]

Reads only: the 75 Ma masks (never written), the stock EWG place lists, the CSV.
Writes only under <work>/earth75ma-0.0.7 and <work>/earth75ma-0.0.7-src.
"""
import base64, csv, json, math, os, sys, time, unicodedata, urllib.request
import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from scipy.spatial import cKDTree

Image.MAX_IMAGE_PIXELS = None
SUFFIX_SEA = " (shore)"             # stock place whose 75 Ma position is under water
SUFFIX_NOPOS = " (nearest land)"    # stock place with no 75 Ma position at all (crust younger than 75 Ma)
WORK = sys.argv[1]
EWG = sys.argv[2]
OFFLINE = "--offline" in sys.argv
SRC = os.path.join(WORK, "earth75ma-0.0.7-src")
MOD = os.path.join(WORK, "earth75ma-0.0.7")
CACHE = os.path.join(SRC, "cache", "gws_paleomap75_points.json")
COMPAT_IN = os.path.join(WORK, "grok-audit-20261111", "out", "earth75ma-compat",
                         "assets", "earth75ma", "compatibility", "earthworldgen")
CSV_IN = os.path.join(WORK, "grok-audit-20261111", "out", "modern_to_75Ma_coords.csv")
GWS = "https://gws.gplates.org/reconstruct/reconstruct_points/?points={q}&time=75&model=PALEOMAP"
QX, QZ = 100352, 50176          # Quarter world, blocks
EARTH_R = 6371.0088
ERODE_PX = 2                    # "solid" land = at least this many mask px from water

# ---------------------------------------------------------------- rotation
os.makedirs(os.path.dirname(CACHE), exist_ok=True)
cache = json.load(open(CACHE)) if os.path.exists(CACHE) else {}


def key(lon, lat):
    return f"{lon:.4f},{lat:.4f}"


def fetch(batch):
    q = ",".join(key(lo, la) for lo, la in batch)
    req = urllib.request.Request(GWS.format(q=q), headers={"User-Agent": "python-urllib"})
    j = json.load(urllib.request.urlopen(req, timeout=120))
    c = j["coordinates"]
    assert len(c) == len(batch), (len(c), len(batch))
    return c


def rotate(points, what):
    """points: list of (lon, lat). Returns list of (plon, plat) or None (not reconstructable)."""
    todo = sorted({key(lo, la) for lo, la in points} - set(cache))
    if todo and OFFLINE:
        raise SystemExit(f"{len(todo)} points for {what} are not in the cache and --offline is set")
    size, i, t0 = 300, 0, time.time()
    while i < len(todo):
        batch = [tuple(map(float, k.split(","))) for k in todo[i:i + size]]
        try:
            res = fetch(batch)
        except Exception as e:                      # too long a URL, timeout, 5xx
            if size > 25:
                size //= 2
                print(f"  [{what}] batch failed ({type(e).__name__}), batch size now {size}", flush=True)
                continue
            raise
        for k, (plo, pla) in zip(todo[i:i + size], res):
            cache[k] = None if (abs(plo) > 360 or abs(pla) > 90) else [round(plo, 4), round(pla, 4)]
        i += size
        if (i // size) % 10 == 0 or i >= len(todo):
            json.dump(cache, open(CACHE, "w"))
            print(f"  [{what}] {min(i, len(todo))}/{len(todo)} rotated, {time.time() - t0:.0f} s", flush=True)
    if todo:
        json.dump(cache, open(CACHE, "w"))
    out = []
    for lo, la in points:
        v = cache[key(lo, la)]
        out.append(None if v is None else (v[0], v[1]))
    return out


# ---------------------------------------------------------------- mask
ocean = np.array(Image.open(os.path.join(COMPAT_IN, "worldgen", "oceanmask.png")))
fresh = np.array(Image.open(os.path.join(COMPAT_IN, "worldgen", "freshmask.png"))).astype(bool)
H, Wd = ocean.shape
land = ocean == 0


def px(lon, lat):
    return int((lon + 180) / 360 * Wd) % Wd, min(H - 1, max(0, int((90 - lat) / 180 * H)))


def wrap_pad(a, n):
    return np.concatenate([a[:, -n:], a, a[:, :n]], axis=1)


solid = ndi.binary_erosion(wrap_pad(land & ~fresh, 8), iterations=ERODE_PX)[:, 8:-8]
lab, nlab = ndi.label(land, structure=np.ones((3, 3)))
parent = list(range(nlab + 1))


def find(a):
    while parent[a] != a:
        parent[a] = parent[parent[a]]
        a = parent[a]
    return a


for y in range(H):                                  # join components across the date line
    for dy in (-1, 0, 1):
        y2 = y + dy
        if 0 <= y2 < H and lab[y, 0] and lab[y2, Wd - 1]:
            parent[find(lab[y, 0])] = find(lab[y2, Wd - 1])
comp = np.array([find(i) for i in range(nlab + 1)])[lab]
comp_px = np.bincount(comp.ravel())
sy, sx = np.nonzero(solid)
s_lon = (sx + 0.5) / Wd * 360 - 180
s_lat = 90 - (sy + 0.5) / H * 180
s_comp = comp[sy, sx]
px_km = 2 * math.pi * EARTH_R / Wd
has_solid = np.bincount(s_comp, minlength=nlab + 1) > 0
_order = np.argsort(s_comp, kind="stable")
_bounds = np.searchsorted(s_comp[_order], np.arange(nlab + 2))


def unit(lon, lat):
    l, p = np.radians(lon), np.radians(lat)
    return np.stack([np.cos(p) * np.cos(l), np.cos(p) * np.sin(l), np.sin(p)], axis=-1)


s_xyz = unit(s_lon, s_lat)
_trees = {}


def nearest_solid(plon, plat, comps):
    """Index of the nearest solid-land mask pixel, limited to the given land components (None = any)."""
    q = unit(float(plon), float(plat))
    best_d, best_i = 9.0, -1
    for c in (sorted(comps) if comps else [None]):
        if c not in _trees:
            idx = np.arange(len(s_comp)) if c is None else _order[_bounds[c]:_bounds[c + 1]]
            _trees[c] = (cKDTree(s_xyz[idx]), idx)
        t, idx = _trees[c]
        dd, j = t.query(q)
        if dd < best_d:
            best_d, best_i = dd, int(idx[j])
    return best_i


def gc_km(lon1, lat1, lon2, lat2):
    l1, p1, l2, p2 = map(np.radians, (lon1, lat1, lon2, lat2))
    a = np.sin((p2 - p1) / 2) ** 2 + np.cos(p1) * np.cos(p2) * np.sin((l2 - l1) / 2) ** 2
    return 2 * EARTH_R * np.arcsin(np.sqrt(np.clip(a, 0, 1)))


def cover(lon, lat):
    x, y = px(lon, lat)
    return "river" if (fresh[y, x] and land[y, x]) else ("land" if land[y, x] else "sea")


# ---------------------------------------------------------------- gazetteer
def load_places(name):
    return json.load(open(os.path.join(EWG, "assets", "earthworldgen", "config", name), encoding="utf-8"))


gaz = {n: load_places(n) for n in ("places-europe.json", "places-world.json")}
allp = [(n, e) for n in gaz for e in gaz[n]["places"]]
_MAP = {"ø": "o", "đ": "d", "ł": "l", "ß": "ss", "æ": "ae", "œ": "oe", "þ": "th", "ð": "d", "ħ": "h", "ı": "i"}


def norm(s):
    """Port of EarthWorldgen Gazetteer.Normalize: lower case, special letters mapped, accents stripped, letters and digits only."""
    s = "".join(_MAP.get(c, c) for c in s.lower())
    return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c)[0] == "L" or unicodedata.category(c) == "Nd")


stock_keys = {norm(x.split(",")[0]) for _, e in allp for x in [e["n"]] + list(e.get("a") or [])}
print(f"stock places: europe {len(gaz['places-europe.json']['places'])}, world {len(gaz['places-world.json']['places'])}")
rot = rotate([(e["lon"], e["lat"]) for _, e in allp], "places")
g_mlon = np.array([e["lon"] for _, e in allp]); g_mlat = np.array([e["lat"] for _, e in allp])
g_ok = np.array([r is not None for r in rot])
g_plon = np.array([r[0] if r else 0.0 for r in rot]); g_plat = np.array([r[1] if r else 0.0 for r in rot])
g_land = np.zeros(len(allp), bool); g_comp = np.zeros(len(allp), int)
for i, r in enumerate(rot):
    if r:
        x, y = px(*r)
        g_land[i] = land[y, x]; g_comp[i] = comp[y, x]


# ---------------------------------------------------------------- snapping
def snap(mlon, mlat, plon, plat):
    """Returns dict: raw cover, snapped lon/lat, distance km, rule used."""
    raw = cover(plon, plat)
    x, y = px(plon, plat)
    if solid[y, x]:
        return dict(raw=raw, lon=plon, lat=plat, km=0.0, rule="on solid land, unchanged")
    # same paleo-continent = land components carrying gazetteer places that were near this
    # point in the modern world AND are still the same distance away at 75 Ma (moved with it).
    allowed, rule = set(), ""
    if land[y, x] and has_solid[comp[y, x]]:
        allowed, rule = {int(comp[y, x])}, "coast or river cell: moved inland on its own landmass"
    else:
        for radius in (1000.0, 2000.0, 3500.0):
            dm = gc_km(mlon, mlat, g_mlon, g_mlat)
            dp = gc_km(plon, plat, g_plon, g_plat)
            near = g_ok & g_land & (dm < radius) & (np.abs(dp - dm) < 0.05 * radius + 60)
            allowed = {int(c) for c in set(g_comp[near]) if has_solid[c]}
            if allowed:
                rule = (f"sea at 75 Ma: nearest solid land on the landmass(es) that carry {int(near.sum())} "
                        f"co-moving stock places within {radius:.0f} km")
                break
    if not allowed:
        rule = "NO co-moving landmass found: nearest solid land of any landmass (check by eye)"
    j = nearest_solid(plon, plat, allowed)
    return dict(raw=raw, lon=round(float(s_lon[j]), 4), lat=round(float(s_lat[j]), 4),
                km=round(float(gc_km(plon, plat, s_lon[j], s_lat[j])), 1), rule=rule, comp=int(s_comp[j]))


def qblock(lon, lat):
    return int(math.floor((lon + 180) * QX / 360)), int(math.floor((90 - lat) * QZ / 180))


# ---------------------------------------------------------------- presets + sites
PRESETS = [  # code, EWG label, modern lat, lon (EarthSpawn.cs Regions; centre = fallback 0N 0E)
    ("europe", "London, United Kingdom", 51.5085, -0.1257),
    ("northamerica", "New York City, United States", 40.7143, -74.006),
    ("southamerica", "Rio de Janeiro, Brazil", -22.9064, -43.1822),
    ("africa", "Cape Town, South Africa", -33.9258, 18.4232),
    ("asia", "Tokyo, Japan", 35.6895, 139.6917),
    ("australia", "Sydney, Australia", -33.8678, 151.2073),
    # EWG "centre" is 0N 0E, which stock EWG resolves to "nearest land (Ghana)". 0N 0E is ocean
    # crust younger than 75 Ma, so the Ghana landfall is rotated instead: Cape Three Points.
    ("centre", "Cape Three Points, Ghana (nearest land to 0N 0E)", 4.7440, -2.0890),
]
SITE_META = {  # csv name -> (code, label, country, region, marine unit?)
    "Wapiti": ("wapiti", "Wapiti Formation", "CA", "01", False),
    "Oldman": ("oldman", "Oldman Formation", "CA", "01", False),
    "Dinosaur Park": ("dinosaurpark", "Dinosaur Park Formation", "CA", "01", False),
    "Milk River": ("milkriver", "Milk River Formation", "CA", "01", False),
    "Bearpaw Shale": ("bearpaw", "Bearpaw Shale", "US", "MT", True),
    "Two Medicine": ("twomedicine", "Two Medicine Formation", "US", "MT", False),
    "Judith River": ("judithriver", "Judith River Formation", "US", "MT", False),
    "Pierre Shale": ("pierre", "Pierre Shale", "US", "SD", True),
    "Niobrara": ("niobrara", "Niobrara Formation", "US", "KS", True),
    "Kaiparowits": ("kaiparowits", "Kaiparowits Formation", "US", "UT", False),
    "Kirtland": ("kirtland", "Kirtland Formation", "US", "NM", False),
    "Aguja": ("aguja", "Aguja Formation", "US", "TX", False),
    "Cerro del Pueblo": ("cerrodelpueblo", "Cerro del Pueblo Formation", "MX", "07", False),
    "Ripley": ("ripley", "Ripley Formation", "US", "TN", True),
    "Eutaw": ("eutaw", "Eutaw Formation", "US", "AL", True),
    "Tar Heel / Coachman": ("tarheel", "Tar Heel / Coachman Formation", "US", "NC", False),
    "Merchantville": ("merchantville", "Merchantville Formation", "US", "NJ", True),
}
csv_rows = [r for r in csv.DictReader(open(CSV_IN, encoding="utf-8")) if not r["name"].startswith("preset")]
assert len(csv_rows) == 17 and all(r["name"] in SITE_META for r in csv_rows), "CSV site list changed"


def resolve(code, label, mlat, mlon, extra=None):
    r = rotate([(mlon, mlat)], code)[0]
    note = ""
    if r is None:   # not reconstructable: borrow the nearest reconstructable stock place
        dm = gc_km(mlon, mlat, g_mlon, g_mlat); dm[~g_ok] = 1e9
        j = int(np.argmin(dm)); r = (g_plon[j], g_plat[j])
        note = f"point itself not reconstructable; used {allp[j][1]['n']} {dm[j]:.0f} km away. "
    s = snap(mlon, mlat, r[0], r[1])
    bx, bz = qblock(s["lon"], s["lat"])
    d = dict(code=code, label=label, modernLat=mlat, modernLon=mlon, paleoLat=round(r[1], 4), paleoLon=round(r[0], 4),
             maskAtPaleo=s["raw"], lat=s["lat"], lon=s["lon"], snapKm=s["km"], maskAtSpawn=cover(s["lon"], s["lat"]),
             quarterBlockX=bx, quarterBlockZ=bz, note=note + s["rule"])
    if extra:
        d.update(extra)
    return d


presets = [resolve(c, l, la, lo) for c, l, la, lo in PRESETS]
for p in presets:
    p["label"] += " at 75 Ma"
sites = []
for i, r in enumerate(csv_rows, 1):
    code, label, cc, rr, marine = SITE_META[r["name"]]
    mlon, mlat = float(r["modern_lon"]), float(r["modern_lat"])
    s = resolve(code, label, mlat, mlon, dict(number=i, country=cc, region=rr, marineUnit=marine,
                                                csvPaleoLat=float(r["paleo75_lat_GWS"]), csvPaleoLon=float(r["paleo75_lon_GWS"])))
    s["csvDiffKm"] = round(float(gc_km(s["paleoLon"], s["paleoLat"], s["csvPaleoLon"], s["csvPaleoLat"])), 1)
    if s["maskAtPaleo"] == "sea":
        s["label"] += " (shore)"
    elif marine:
        s["note"] += " Marine unit, but the frozen D1 geometry has land at its 75 Ma position, so no shore snap."
    sites.append(s)

os.makedirs(os.path.join(MOD, "assets", "earth75ma", "config"), exist_ok=True)
META = dict(model="PALEOMAP", time_Ma=75, service="GPlates Web Service gws.gplates.org reconstruct_points",
            mask="oceanmask.png 4096x2048, value 0 = land", solidLandErosionPx=ERODE_PX)
json.dump(dict(note="Generated by tools/build_tables.py - do not hand edit. lat/lon = where the mod spawns you (on land); "
                    "paleoLat/paleoLon = the raw rotated position.", **META, presets=presets, sites=sites),
          open(os.path.join(MOD, "assets", "earth75ma", "config", "spawnpresets-75ma.json"), "w", encoding="utf-8"),
          indent=1, ensure_ascii=False)

# ---------------------------------------------------------------- places lists
cdir = os.path.join(MOD, "assets", "earth75ma", "config", "75ma")   # own domain: loaded only for a Campanian world
os.makedirs(cdir, exist_ok=True)
stats = {}
snaplog = []
idx = 0
for name in ("places-europe.json", "places-world.json"):
    src = gaz[name]; out = []; n_null = n_sea = 0
    for e in src["places"]:
        r = rot[idx]; ok, onland = g_ok[idx], g_land[idx]; idx += 1
        ne = dict(e)
        if ok and onland:
            ne["lat"], ne["lon"] = round(r[1], 4), round(r[0], 4)
            out.append(ne); continue
        # No place is dropped. Under water at 75 Ma, or no 75 Ma position: same snap rule as the presets.
        mlon, mlat, borrow = e["lon"], e["lat"], ""
        if ok:
            n_sea += 1; why, suffix = "sea", SUFFIX_SEA
        else:   # borrow the 75 Ma position of the nearest stock place that has one
            dm = gc_km(mlon, mlat, g_mlon, g_mlat); dm[~g_ok] = 1e9
            j = int(np.argmin(dm)); r = (float(g_plon[j]), float(g_plat[j]))
            borrow = f"{allp[j][1]['n']} {dm[j]:.0f} km"; mlon, mlat = float(g_mlon[j]), float(g_mlat[j])
            n_null += 1; why, suffix = "no75MaPosition", SUFFIX_NOPOS
        s = snap(mlon, mlat, r[0], r[1])
        ne["lat"], ne["lon"] = s["lat"], s["lon"]
        ne["n"] = e["n"] + suffix                       # label shows the suffix; the plain modern name stays an alias
        ne["a"] = [e["n"]] + list(e.get("a") or [])
        cls = "fallback-any-land" if s["rule"].startswith("NO ") else ("same-landmass" if s["rule"].startswith("sea") else "own-landmass")
        snaplog.append([name, e["n"], e["c"], e["lat"], e["lon"], why, round(r[1], 4), round(r[0], 4), s["lat"], s["lon"], s["km"], cls, borrow])
        out.append(ne)
    countries = dict(src.get("countries") or {}); regions = dict(src.get("regions") or {})
    if name == "places-world.json":
        for s in sites:                      # fossil sites as named places; p high so the bare name wins
            base = s["label"].replace(" (shore)", "")
            short = base.replace(" Formation", "")
            al = [a for a in dict.fromkeys([base, short] + ([short.split(" / ")[0], "Coachman"] if "/" in short else []))
                  if a != s["label"]]
            # The gazetteer ranks same-key entries by population only, and a site carries p = 30,000,000 - number.
            # A site alias equal to a stock key would therefore steal that name from the real town: leave it to the town.
            taken = [a for a in al if norm(a) in stock_keys]
            al = [a for a in al if a not in taken]
            assert norm(s["label"]) not in stock_keys, s["label"]
            if taken:
                print("site alias left to the stock place:", s["label"], taken)
            ent = {"n": s["label"], "a": al, "c": s["country"], "lat": s["lat"], "lon": s["lon"], "p": 30000000 - s["number"]}
            if f'{s["country"]}.{s["region"]}' in regions:
                ent["r"] = s["region"]
            out.append(ent)
    used_c = {e["c"] for e in out}; used_r = {f'{e["c"]}.{e["r"]}' for e in out if e.get("r")}
    doc = {"note": "earth75ma: stock EarthWorldgen places (GeoNames, geonames.org, CC BY 4.0) moved to their 75 Ma position "
                   "(GPlates PALEOMAP). No place is dropped: one whose 75 Ma position is under water sits on the nearest land of "
                   "the same landmass and is marked (shore); one with no 75 Ma position is marked (nearest land). Generated by "
                   "tools/build_tables.py - do not hand edit.",
           "countries": {k: v for k, v in countries.items() if k in used_c},
           "regions": {k: v for k, v in regions.items() if k in used_r},
           "places": out}
    with open(os.path.join(cdir, name), "w", encoding="utf-8") as f:
        f.write(json.dumps(doc, ensure_ascii=False, separators=(", ", ": ")).replace('}, {"n"', '},\n{"n"'))
    stats[name] = dict(stock=len(src["places"]), written=len(out), seaSnapped=n_sea, noPositionSnapped=n_null)
    print(name, stats[name])
os.makedirs(os.path.join(SRC, "verify"), exist_ok=True)
with open(os.path.join(SRC, "verify", "places-snapped.csv"), "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow("file,name,country,modern_lat,modern_lon,why,paleo_lat,paleo_lon,snap_lat,snap_lon,snap_km,rule,borrowed_from".split(","))
    w.writerows(snaplog)
for why in ("sea", "no75MaPosition"):
    km = np.array([r[10] for r in snaplog if r[5] == why])
    cl = {c: sum(1 for r in snaplog if r[5] == why and r[11] == c) for c in ("own-landmass", "same-landmass", "fallback-any-land")}
    stats["snap_" + why] = dict(n=int(len(km)), medianKm=float(np.median(km)), p95Km=float(np.percentile(km, 95)), maxKm=float(km.max()),
                                over1000Km=int((km > 1000).sum()), **cl)
    print("snap", why, stats["snap_" + why])

# ---------------------------------------------------------------- modern -> paleo grid (for the "modern:" prefix)
stock = np.array(Image.open(os.path.join(EWG, "assets", "earthworldgen", "worldgen", "oceanmask.png"))) == 0
small = np.array(Image.fromarray((stock * 255).astype(np.uint8)).resize((720, 360), Image.BOX)) > 0
cell = small.reshape(180, 2, 360, 2).any(axis=(1, 3))                     # 1 degree cells holding modern land
want = ndi.binary_dilation(wrap_pad(cell, 2), iterations=1)[:, 2:-2]
gy, gx = np.nonzero(want)
g_lat = 89.5 - gy; g_lon = -179.5 + gx
grot = rotate(list(zip(g_lon.tolist(), g_lat.tolist())), "grid")
grid = np.full((180, 360, 2), 32767, np.int16)                            # [row from north][col from west] = lat*100, lon*100
for (y, x, r) in zip(gy, gx, grot):
    if r is not None:
        grid[y, x] = (int(round(r[1] * 100)), int(round(r[0] * 100)))
json.dump(dict(note="Modern -> 75 Ma lookup for the 'modern:' prefix. Node (row, col) is the modern point "
                    "lat = 89.5 - row, lon = -179.5 + col. data = base64 of little-endian int16 pairs (paleo lat*100, "
                    "paleo lon*100), 32767 = no value. Generated by tools/build_tables.py.",
               **META, rows=180, cols=360, firstLat=89.5, firstLon=-179.5, stepDeg=1.0,
               data=base64.b64encode(grid.astype("<i2").tobytes()).decode()),
          open(os.path.join(MOD, "assets", "earth75ma", "config", "modern2paleo-75ma.json"), "w"))
stats["grid"] = dict(nodesAsked=int(len(gy)), nodesWithValue=int((grid[:, :, 0] != 32767).sum()))
print("grid", stats["grid"])

# ---------------------------------------------------------------- world config dropdown + lang
# "playstyles": [] is required: the game's save list (GuiScreenSingleplayer.getHoverText) iterates PlayStyles without a null check
# 0.0.5: EWG's own "Where you start" list cannot be re-filled live when the era changes (see PROGRESS.md, START DROPDOWN
# RESULT), so ONE dropdown carries both eras with a prefix; the server uses only the entries of the world's era.
ewg_wc = json.load(open(os.path.join(EWG, "worldconfig.json"), encoding="utf-8-sig"))
ewg_reg = [x for x in ewg_wc["worldConfigAttributes"] if x["code"] == "earthSpawnRegion"]
assert len(ewg_reg) == 1, "EWG earthSpawnRegion not found"
EWG_REGIONS = list(ewg_reg[0]["values"])
ewg_lang = json.load(open(os.path.join(EWG, "assets", "game", "lang", "worldconfig-en.json"), encoding="utf-8-sig"))
assert all(f"worldconfig-earthSpawnRegion-{r}" in ewg_lang for r in EWG_REGIONS), "EWG region label missing"
assert all(p["code"] in EWG_REGIONS for p in presets), "preset code not an EWG region"
start_values = (["ewg"] + [f"modern-{r}" for r in EWG_REGIONS] + [f"75ma-{p['code']}" for p in presets]
                + [f"75ma-site-{s['code']}" for s in sites])
assert len(start_values) == len(set(start_values)) == 1 + len(EWG_REGIONS) + len(presets) + len(sites)
# 0.0.7: ONE merged dropdown. Our earthSpawnRegion copies EWG's definition exactly (same code/default 'centre') and appends the
# fossil sites as site-<code>; it wins the UI slot (sorts first by mod name). earth75maStart stays as a hidden LEGACY attribute.
import copy
merged = copy.deepcopy(ewg_reg[0])
merged["values"] = list(merged["values"]) + [f"site-{s['code']}" for s in sites]
merged["names"] = list(merged["values"])
assert merged["default"] == "centre"
wc = {"playstyles": [], "worldConfigAttributes": [{
    "category": "earth", "code": "earthEra", "dataType": "dropdown",
    "values": ["modern", "campanian75"], "names": ["modern", "campanian75"],
    "default": "modern", "onlyDuringWorldCreate": True}, merged, {
    "category": "earth", "code": "earth75maStart", "dataType": "dropdown",
    "values": start_values, "names": list(start_values),
    "default": "ewg", "onlyDuringWorldCreate": True, "onCustomizeScreen": False}, {
    "category": "earth", "code": "lopExtantFauna", "dataType": "bool", "default": "true", "onlyDuringWorldCreate": True}]}
json.dump(wc, open(os.path.join(MOD, "worldconfig.json"), "w"), indent="\t")
lang = {"worldattribute-earthEra": 'Earth era',
        "worldattribute-earthEra-desc": "Which Earth's geography this world is generated from. Campanian is Earth 75 million years ago: different continents, an inland sea across North America, no polar ice. Applies when the world is created; it cannot be changed afterwards.",
        "worldconfig-earthEra-modern": 'Modern Earth (present day)',
        "worldconfig-earthEra-campanian75": 'Campanian Earth (75 million years ago)',
        "worldattribute-earth75maStart": "Where you start (by era) (legacy, hidden)",
        "worldattribute-earth75maStart-desc": "Earth 75 Ma: pick a start that matches the Era above. \"Modern:\" entries are the stock "
        "Earth Worldgen starts and are used only in a Modern world. \"75 Ma:\" entries are the same cities at their 75 Ma position, "
        "plus 17 Campanian fossil sites, and are used only in a Campanian 75 Ma world. An entry of the other era is ignored (the server "
        "log says so) and Where you start decides. A place typed in Start at still wins over this.",
        "worldconfig-earth75maStart-ewg": "Same as Where you start",
        "worldattribute-earthSpawnRegion": "Where you start (region or fossil site)",
        "worldattribute-earthSpawnPlace": "Start at (place name or lat, lon; overrides Where you start)",
        "worldattribute-lopExtantFauna": "Extant fauna (vanilla animals)",
        "worldattribute-lopExtantFauna-desc": "Untick for a dino-only world: no new natural spawns of vanilla animals (needs LoP-Core 1.5.3+; drifters and other temporal creatures are unaffected)."}
assert "worldattribute-earthSpawnPlace" in ewg_lang
for r in EWG_REGIONS:
    lang[f"worldconfig-earthSpawnRegion-{r}"] = ewg_lang[f"worldconfig-earthSpawnRegion-{r}"]
for s in sites:
    lang[f"worldconfig-earthSpawnRegion-site-{s['code']}"] = f"75 Ma: {s['number']}. {s['label']}"
for r in EWG_REGIONS:
    lang[f"worldconfig-earth75maStart-modern-{r}"] = "Modern: " + ewg_lang[f"worldconfig-earthSpawnRegion-{r}"]
for p in presets:
    lang[f"worldconfig-earth75maStart-75ma-{p['code']}"] = ("75 Ma: " + ewg_lang[f"worldconfig-earthSpawnRegion-{p['code']}"]
                                                          + ", at its 75 Ma position" + (" (shore)" if p["maskAtPaleo"] == "sea" else ""))
for s in sites:
    lang[f"worldconfig-earth75maStart-75ma-site-{s['code']}"] = f"75 Ma: {s['number']}. {s['label']}"
assert all(f"worldconfig-earth75maStart-{v}" in lang for v in start_values)
stats["startDropdown"] = dict(values=len(start_values), modern=len(EWG_REGIONS), presets75=len(presets), sites=len(sites))
print("start dropdown", stats["startDropdown"])
json.dump(dict(stats=stats), open(os.path.join(SRC, "build_stats.json"), "w"), indent=1)
json.dump(lang, open(os.path.join(SRC, "worldconfig-lang-en.generated.json"), "w", encoding="utf-8"), indent="\t", ensure_ascii=False)
print("land components:", int((comp_px[1:] > 0).sum()), "| solid px:", len(sx), "| mask px =", round(px_km, 2), "km")
for p in presets + sites:
    print(f"{p['code']:15s} raw {p['maskAtPaleo']:5s} -> {p['maskAtSpawn']:5s} snap {p['snapKm']:7.1f} km  {p['note'][:70]}")
