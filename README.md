# Earth 75 Ma 0.0.7 (TEST BUILD)

## What is new in 0.0.7

- **One start dropdown**: Earth Worldgen's `Where you start` (earthSpawnRegion) now also lists the 17 Campanian fossil sites (`site-<code>`). Pick one in a Campanian world and the server sets the fossil site and resets the region to `centre`; in a Modern world a site entry is ignored (logged). `Start at` (earthSpawnPlace) behaves as before and overrides both. The 0.0.6 `Where you start (by era)` (earth75maStart) is kept as a hidden legacy attribute so saved 0.0.6 worlds still work. Needs an in-game look: this relies on this mod's attribute winning the shared UI slot (mod name sorts first).
- **Extant fauna (vanilla animals)** tickbox (`lopExtantFauna`, default on). This mod has no code for it: LoP-Core 1.5.3+ reads the world setting. Without LoP-Core it has no effect.


Add-on for Earth Worldgen 1.0.0 by mcleanlt (Vintage Story 1.22). Earth Worldgen itself is not modified.

## What is new in 0.0.5

- **Where you start (by era)**: one dropdown on the Earth tab that replaces the 0.0.4 fossil-site dropdown. `Modern: ...` entries are Earth Worldgen's own 8 starts; `75 Ma: ...` entries are the 7 same cities at their 75 Ma position plus the 17 fossil sites. Only entries that match the Era are used; an entry of the other era is ignored and logged, and Where you start decides. Default `Same as Where you start` changes nothing.
- Why not re-fill Earth Worldgen's own list: the world-create screen is built before any mod code can run on the title screen, and its dropdowns have no ID and no change event, so the list cannot follow the Era choice live.
- Worlds made with 0.0.3 / 0.0.4 keep their stored fossil site. Everything else is as in 0.0.4.

## What was new in 0.0.4

- **Era dropdown** on the Earth tab of world creation: `Modern` (default) or `Campanian 75 Ma`. The choice is stored in the world (`earthEra`, the same key Earth Epoch uses; 0.0.4/0.0.5 stored it as legacy `earth75maEra`, still read as a fallback), so every world keeps its own era.
- **Modern** worlds are 100% stock Earth Worldgen: stock masks, stock tables, stock spawn presets. The mod changes nothing and patches nothing for them.
- **Campanian 75 Ma** worlds get the 75 Ma masks and tables, the spawn remap, the shore-snapped places and the fossil-site dropdown.
- The 75 Ma data no longer overrides Earth Worldgen's files through a `compatibility/` folder (that made EVERY Earth world 75 Ma in 0.0.3). It now lives in this mod's own domain and is swapped in at server start, in memory, only when the world's era is Campanian.
- No stock place is dropped. A place whose 75 Ma position is under water sits on the nearest land of the same landmass and is shown as `Name (shore)`; a place on crust that did not exist yet is shown as `Name (nearest land)`. The plain name still finds it: typing `London` gives London, UK.

- Fixes a crash of 0.0.3: opening the Singleplayer world list threw a NullReferenceException (`GuiScreenSingleplayer.getHoverText`) because 0.0.3's `worldconfig.json` had no `playstyles` list. 0.0.4 ships an empty one.

Remove earth75ma 0.0.1 / 0.0.2 / 0.0.3 and Earth Epoch before installing this one. Do not keep 0.0.3 installed: it crashes the world list.

## World creation

1. Create World, pick the Earth playstyle, Customize, **Earth** tab.
2. **Era**: `Modern` or `Campanian 75 Ma`.
3. Optional: **Where you start (by era)**. Modern world: pick a `Modern: ...` entry (same as EWG's Where you start). Campanian world: pick a `75 Ma: ...` city or one of the 17 `75 Ma: N. ... Formation` fossil sites. Entries of the other era are ignored.
4. World size: use **Quarter** (100352 wide). The 75 Ma masks are 4096 x 2048 and were drawn for Quarter; on larger sizes rivers and coasts come out blocky and far too wide. The mod logs a warning for other sizes.

Start point priority in a Campanian world: a place typed in **Start at** wins over a fossil site, which wins over the region (Where you start (by era), else Where you start).

- **Where you start** (Europe, North America, ...): the same city as stock, at its 75 Ma position (London, New York, Rio, Cape Town, Tokyo, Sydney). A city that was under water starts you on the nearest shore of the same landmass.
- **Start at**: modern names work (`London`, `Tokyo`, `Dinosaur Park`). `London, GB` or `London, Canada` picks between same-named places, as in stock. `modern: 51.5 -0.13` converts a modern latitude and longitude to where that spot was 75 million years ago. Plain coordinates are 75 Ma coordinates.

## How to tell which era a world runs (server-main.log)

- `[earth75ma] era=Modern, stock data (earthEra=modern): ...`
- `[earth75ma] era=Campanian, 75 Ma data (earthEra=campanian75): 12 masks and 6 tables swapped in for this world.` preceded by `[earth75ma] swap check: 21/21 earthworldgen: paths now return earth75ma 75 Ma files`
- `[earth75ma] era=none, not an Earth Worldgen world ...` for worlds without Earth geography.

A world created with 0.0.3 (which had no era and was always 75 Ma) is recognised by its fossil-site setting and stays Campanian; the log says so.

## Known limits

- Test build, not yet run in game by the author of this build.
- Earth Worldgen's `redwoodbelt.json` has no 75 Ma version and stays stock.
- Country borders, landmarks, monuments and lakes tables are the (mostly empty) 75 Ma tables of 0.0.2.
- Positions come from the GPlates Web Service, PALEOMAP model, 75 Ma. Place names: GeoNames (geonames.org), CC BY 4.0, via Earth Worldgen.

## Files

- `earth75ma.dll`: era gate, in-memory data swap, spawn remap (Harmony on `EarthSpawn.Configured`, Campanian worlds only).
- `worldconfig.json`: the two dropdowns (Era, Where you start (by era)).
- `assets/earth75ma/worldgen/75ma/*.png`: 12 masks (75 Ma, no redwoodmask).
- `assets/earth75ma/config/75ma/*.json`: 8 tables (75 Ma), including the two places lists.
- `assets/earth75ma/config/spawnpresets-75ma.json`, `modern2paleo-75ma.json`: spawn table and the modern to 75 Ma lookup grid.
- `assets/game/lang/worldconfig-en.json`: labels.
