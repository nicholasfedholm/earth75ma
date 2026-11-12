using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Earth75Ma;

// Shapes of assets/earth75ma/config/spawnpresets-75ma.json and modern2paleo-75ma.json.
public class SpawnEntry
{
    public string code;
    public string label;
    public double lat;        // where to spawn: on 75 Ma land
    public double lon;
    public double paleoLat;   // raw rotated position
    public double paleoLon;
    public double snapKm;
    public string maskAtPaleo;
}

public class SpawnTable
{
    public List<SpawnEntry> presets;
    public List<SpawnEntry> sites;
}

public class GridFile
{
    public int rows;
    public int cols;
    public double firstLat;
    public double firstLon;
    public double stepDeg;
    public string data;
}

/// <summary>
/// Earth 75 Ma. EarthWorldgen (EWG) is never edited: its EarthWorldgen.dll and its asset files stay as shipped.
/// Each world carries an era (world config earthEra, the same key Earth Epoch and Earth Chart 1.1.0 use; worlds made with
/// earth75ma 0.0.4 / 0.0.5 stored it as earth75maEra, still read as a fallback), chosen on the Earth tab of world creation:
///  modern (default)  nothing is touched. No asset swap, no Harmony patch, no spawn change: stock EWG.
///  campanian75       on the server, for this world only:
///   1. the 13 mask PNGs and 8 config tables EWG reads (earthworldgen:worldgen/*.png, earthworldgen:config/*.json)
///      are replaced IN MEMORY by this mod's 75 Ma files (earth75ma:worldgen/75ma, earth75ma:config/75ma), the same
///      way the game's compatibility loader replaces an asset (IAssetManager.Add). EWG loads its data lazily, at
///      world generation start at the earliest, so doing this in StartServerSide is early enough. The server builds a
///      new asset manager for every world, so nothing carries over to the next world.
///   2. EWG's hard-coded spawn Regions (modern cities) point at the 75 Ma position of the same city.
///   3. a Harmony prefix/postfix on EarthSpawn.Configured adds the fossil sites (earth75maSpawnSite, set from earth75maStart),
///      the "centre" preset, and a "modern: lat lon" prefix for the Start at box.
/// Regions and the patch are put back in Dispose (and again at the next server start, in case Dispose never ran).
/// </summary>
public class Earth75MaModSystem : ModSystem
{
    private const string HarmonyId = "earth75ma";
    private const string EraKey = "earthEra";                 // 0.0.6: back to the Earth Epoch key that Earth Chart 1.1.0 reads
    private const string LegacyEraKey = "earth75maEra";       // 0.0.4 / 0.0.5 worlds
    private const string EraCampanian = "campanian75";
    private const string EarthKey = "earthWorldgen";
    private const string SiteKey = "earth75maSpawnSite";      // 0.0.3 / 0.0.4 worlds; set from earth75maStart in 0.0.5
    private const string StartKey = "earth75maStart";          // 0.0.5: one era-prefixed "Where you start (by era)" dropdown
    private const string RegionKey = "earthSpawnRegion";       // EWG's own "Where you start"
    private const string StartEwg = "ewg";
    private const string StartModern = "modern-";
    private const string StartSite = "75ma-site-";
    private const string Start75 = "75ma-";
    private const string PlaceKey = "earthSpawnPlace";
    private const string SizeKey = "earthWorldSize";
    private const string QuarterSize = "100352";
    private const string ModernPrefix = "modern:";
    private const short NoValue = 32767;

    // What EWG 1.0.0 reads and this mod has a 75 Ma version of. EWG's config/redwoodbelt.json has no 75 Ma version and stays stock.
    private static readonly string[] Masks =
    {
        "bathymask", "biomes", "dryness", "freshmask", "icemask", "lakemask", "oceanmask", "rainfall", "reliefmask",
        "saltlakemask", "stillmask", "temperature"
    };

    private static readonly string[] Tables =
    {
        // places-*: real 75 Ma content. basins/lakes/landmarks/monuments: deliberately empty, because EWG uses them to CARVE
        // modern features (Dead Sea floor, Great Lakes levels, canyons, monument pads) at modern lat/lon, which would land on
        // unrelated 75 Ma terrain; none of those features existed at 75 Ma.
        "basins", "lakes", "landmarks", "monuments", "places-europe", "places-world"
    };

    // 0.0.6: left STOCK in Campanian worlds. countries.json is names only (place lookup, /earth goto); the 0.0.4 stub emptied
    // it and broke country names. rivermouths.json only steers river current at two named mouths; no 75 Ma version exists.
    private static readonly string[] StockTables = { "countries", "rivermouths" };

    // 0.0.6: EWG's redwood belt is the modern Northern California coast. There is no 75 Ma belt; the 0.0.4 mask was blank and
    // EWG threw "the mask holds no belt". The mask is removed from the asset manager for Campanian worlds, so EWG takes its own
    // "redwood mask is missing" path (a warning; the coast grows the ordinary species) instead of an error.
    private static readonly AssetLocation RedwoodMask = new AssetLocation("earthworldgen", "worldgen/redwoodmask.png");

    private static ILogger log;
    private static Harmony harmony;
    private static SpawnTable table;
    private static GridFile gridInfo;
    private static short[] grid;
    private static bool loaded;

    private static Array regions;            // EarthSpawn.Regions (array of a private struct)
    private static object[] stockRegions;    // boxed copies of the stock values, for Dispose

    // state of one Configured call
    private static bool placeChanged;
    private static string placeTyped;
    private static bool placeGiven;
    private static string pendingLabel;
    private static readonly List<string> notes = new List<string>();

    public override bool ShouldLoad(EnumAppSide side)
    {
        return side == EnumAppSide.Server;
    }

    public override double ExecuteOrder()
    {
        return 0.06;   // just after EarthWorldgen (0.05)
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        log = api.Logger;
        Reset();    // leftovers of a server that ended without Dispose
        if (!api.ModLoader.IsModEnabled("earthworldgen"))
        {
            log.Warning("[earth75ma] EarthWorldgen is not enabled, nothing to do.");
            return;
        }
        ITreeAttribute cfg = api.WorldManager?.SaveGame?.WorldConfiguration;
        if (cfg == null || !cfg.GetAsBool(EarthKey, false))
        {
            log.Notification("[earth75ma] era=none, not an Earth Worldgen world (earthWorldgen is off): nothing changed.");
            return;
        }
        string era = cfg.GetString(EraKey, null);
        if (string.IsNullOrWhiteSpace(era))
        {
            string legacy = cfg.GetString(LegacyEraKey, null);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                era = legacy;
                cfg.SetString(EraKey, era);
                log.Notification("[earth75ma] This world was created with earth75ma 0.0.4/0.0.5 ({0}={1}); stored as {2}={1} so Earth Chart picks the right map.", LegacyEraKey, legacy, EraKey);
            }
        }
        if (string.IsNullOrWhiteSpace(era) && cfg.HasAttribute(SiteKey))
        {
            // Made with earth75ma 0.0.3: it had the fossil-site setting but no era, and every Earth world was 75 Ma.
            era = EraCampanian;
            cfg.SetString(EraKey, era);
            log.Notification("[earth75ma] This world was created with earth75ma 0.0.3 (always 75 Ma, no era setting). Era stored as campanian75 so it keeps its map.");
        }
        ApplyStart(cfg, string.Equals(era, EraCampanian, StringComparison.OrdinalIgnoreCase));
        string site = cfg.GetString(SiteKey, "none") ?? "none";
        if (!string.Equals(era, EraCampanian, StringComparison.OrdinalIgnoreCase))
        {
            log.Notification("[earth75ma] era=Modern, stock data ({0}={1}): EarthWorldgen is left exactly as shipped for this world.",
                EraKey, string.IsNullOrWhiteSpace(era) ? "not set" : era);
            if (site != "none")
            {
                log.Notification("[earth75ma] {0}={1} is ignored: fossil sites are for Campanian worlds only.", SiteKey, site);
            }
            return;
        }
        if (!SwapData(api))
        {
            return;
        }
        log.Notification("[earth75ma] era=Campanian, 75 Ma data ({0}={1}): {2} masks and {3} tables swapped in for this world ({4}); stock tables kept: {5}.",
            EraKey, era, Masks.Length, Tables.Length, string.Join(", ", Tables), string.Join(", ", StockTables));
        RemoveRedwood(api);

        Type spawn = AccessTools.TypeByName("EarthWorldgen.EarthSpawn");
        MethodInfo configured = spawn == null ? null : AccessTools.Method(spawn, "Configured");
        FieldInfo regionsField = spawn == null ? null : AccessTools.Field(spawn, "Regions");
        if (configured == null || regionsField == null)
        {
            log.Error("[earth75ma] This EarthWorldgen build has no EarthSpawn.Configured / Regions (made for 1.0.0). Spawn remap is off; the 75 Ma map itself still works.");
            return;
        }
        regions = regionsField.GetValue(null) as Array;
        harmony = new Harmony(HarmonyId);
        harmony.Patch(configured,
            prefix: new HarmonyMethod(typeof(Earth75MaModSystem), nameof(ConfiguredPrefix)),
            postfix: new HarmonyMethod(typeof(Earth75MaModSystem), nameof(ConfiguredPostfix)),
            finalizer: new HarmonyMethod(typeof(Earth75MaModSystem), nameof(ConfiguredFinalizer)));
        SafeSpawn.Install(api, harmony);
        log.Notification("[earth75ma] Spawn remap ready: EarthWorldgen presets and places resolve to 75 Ma positions (fossil site: {0}).", site);
    }

    /// <summary>
    /// 0.0.5 "Where you start (by era)" (earth75maStart). The world-create screen cannot re-fill EWG's own dropdown when the
    /// era changes (no mod code runs on the title screen before that screen is built, and its dropdowns have no key and no
    /// change callback), so one dropdown lists both eras' entries with a prefix. Only entries of this world's era are used:
    ///  ewg                 nothing; EWG's own Where you start decides (default).
    ///  modern-REGION       Modern world: earthSpawnRegion = REGION.        Campanian world: ignored, logged.
    ///  75ma-REGION         Campanian world: earthSpawnRegion = REGION (Regions then point at its 75 Ma position). Modern: ignored, logged.
    ///  75ma-site-CODE      Campanian world: earth75maSpawnSite = CODE.     Modern: ignored, logged.
    /// Never throws: a failure is logged and the world starts as EWG decides.
    /// </summary>
    private static void ApplyStart(ITreeAttribute cfg, bool campanian)
    {
        try
        {
            // 0.0.7: merged dropdown. earthSpawnRegion may carry "site-<code>" (fossil site) in addition to EWG's regions.
            string reg0 = (cfg.GetString(RegionKey, "centre") ?? "centre").Trim();
            if (reg0.StartsWith("site-", StringComparison.OrdinalIgnoreCase))
            {
                string scode = reg0.Substring(5);
                if (campanian && scode.Length > 0)
                {
                    cfg.SetString(SiteKey, scode);
                    cfg.SetString(RegionKey, "centre");
                    log.Notification("[earth75ma] start {0}: Campanian world, fossil site {1} = {2}; {3} reset to centre.", reg0, SiteKey, scode, RegionKey);
                }
                else
                {
                    cfg.SetString(RegionKey, "centre");
                    log.Notification("[earth75ma] start {0}: fossil sites need the Campanian era; {1} reset to centre (Start at / region still apply).", reg0, RegionKey);
                }
                return;
            }
            string start = (cfg.GetString(StartKey, StartEwg) ?? StartEwg).Trim();
            if (start.Length == 0 || string.Equals(start, StartEwg, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string region = cfg.GetString(RegionKey, "centre") ?? "centre";
            string eraName = campanian ? "Campanian" : "Modern";
            if (start.StartsWith(StartModern, StringComparison.OrdinalIgnoreCase))
            {
                string r = start.Substring(StartModern.Length);
                if (campanian || r.Length == 0)
                {
                    log.Notification("[earth75ma] start {0} is a Modern entry, ignored in a {1} world; using EWG's Where you start ({2}){3}.",
                        start, eraName, region, campanian ? " at its 75 Ma position" : "");
                    return;
                }
                cfg.SetString(RegionKey, r);
                log.Notification("[earth75ma] start {0}: Modern world, {1} = {2} (was {3}).", start, RegionKey, r, region);
                return;
            }
            if (start.StartsWith(Start75, StringComparison.OrdinalIgnoreCase))
            {
                if (!campanian)
                {
                    log.Notification("[earth75ma] start {0} is a 75 Ma entry, ignored in a Modern world; using EWG's Where you start ({1}).", start, region);
                    return;
                }
                if (start.StartsWith(StartSite, StringComparison.OrdinalIgnoreCase))
                {
                    string code = start.Substring(StartSite.Length);
                    if (code.Length > 0)
                    {
                        cfg.SetString(SiteKey, code);
                        log.Notification("[earth75ma] start {0}: Campanian world, fossil site {1} = {2}.", start, SiteKey, code);
                        return;
                    }
                }
                else
                {
                    string r = start.Substring(Start75.Length);
                    if (r.Length > 0)
                    {
                        cfg.SetString(RegionKey, r);
                        log.Notification("[earth75ma] start {0}: Campanian world, {1} = {2} at its 75 Ma position (was {3}).", start, RegionKey, r, region);
                        return;
                    }
                }
            }
            log.Warning("[earth75ma] start {0} is not a known entry; using EWG's Where you start ({1}).", start, region);
        }
        catch (Exception e)
        {
            log?.Warning("[earth75ma] Could not apply {0}, EWG's Where you start is used: {1}", StartKey, e.Message);
        }
    }

    /// <summary>
    /// Campanian world: make every earthworldgen: mask and table path return this mod's 75 Ma file. All or nothing.
    /// </summary>
    private static bool SwapData(ICoreServerAPI api)
    {
        List<KeyValuePair<AssetLocation, IAsset>> pairs = new List<KeyValuePair<AssetLocation, IAsset>>();
        List<string> missing = new List<string>();
        List<string> noTarget = new List<string>();
        foreach (string m in Masks)
        {
            Collect(api, "worldgen/" + m + ".png", "worldgen/75ma/" + m + ".png", pairs, missing, noTarget);
        }
        foreach (string c in Tables)
        {
            Collect(api, "config/" + c + ".json", "config/75ma/" + c + ".json", pairs, missing, noTarget);
        }
        if (missing.Count > 0)
        {
            log.Error("[earth75ma] era=Campanian was asked for, but {0} of this mod's 75 Ma files are missing or empty ({1}). NOTHING was swapped: this world would generate as MODERN Earth. Stop and reinstall earth75ma.",
                missing.Count, string.Join(", ", missing));
            return false;
        }
        if (noTarget.Count > 0)
        {
            log.Warning("[earth75ma] EarthWorldgen does not ship {0}; made for EarthWorldgen 1.0.0. Swapping anyway.", string.Join(", ", noTarget));
        }
        foreach (KeyValuePair<AssetLocation, IAsset> p in pairs)
        {
            api.Assets.Add(p.Key, p.Value);
        }
        List<string> wrong = new List<string>();
        long bytes = 0;
        foreach (KeyValuePair<AssetLocation, IAsset> p in pairs)
        {
            IAsset now = api.Assets.TryGet(p.Key, true);
            if (!ReferenceEquals(now, p.Value))
            {
                wrong.Add(p.Key.ToString());
            }
            bytes += p.Value.Data.Length;
        }
        if (wrong.Count > 0)
        {
            log.Error("[earth75ma] era=Campanian: {0} of {1} files did not take the 75 Ma version ({2}). This world would mix modern and 75 Ma data. Stop and report this.",
                wrong.Count, pairs.Count, string.Join(", ", wrong));
            return false;
        }
        log.Notification("[earth75ma] swap check: {0}/{0} earthworldgen: paths now return earth75ma 75 Ma files ({1} bytes).", pairs.Count, bytes);
        return true;
    }

    private static void RemoveRedwood(ICoreServerAPI api)
    {
        try
        {
            bool had = api.Assets.AllAssets.Remove(RedwoodMask);
            bool gone = api.Assets.TryGet(RedwoodMask, false) == null;
            log.Notification("[earth75ma] redwood: {0} {1} for this world (no 75 Ma redwood belt); EarthWorldgen will log that the redwood mask is missing and grow the ordinary species.",
                RedwoodMask, gone ? (had ? "removed" : "was not present") : "could NOT be removed");
        }
        catch (Exception e)
        {
            log.Warning("[earth75ma] redwood: could not remove {0} ({1}); the stock modern belt stays.", RedwoodMask, e.Message);
        }
    }

    private static void Collect(ICoreServerAPI api, string ewgPath, string ownPath,
        List<KeyValuePair<AssetLocation, IAsset>> pairs, List<string> missing, List<string> noTarget)
    {
        IAsset own = api.Assets.TryGet(new AssetLocation("earth75ma", ownPath), true);
        if (own == null || own.Data == null || own.Data.Length == 0)
        {
            missing.Add("earth75ma:" + ownPath);
            return;
        }
        AssetLocation target = new AssetLocation("earthworldgen", ewgPath);
        if (api.Assets.TryGet(target, false) == null)
        {
            noTarget.Add(target.ToString());
        }
        pairs.Add(new KeyValuePair<AssetLocation, IAsset>(target, own));
    }

    public override void Dispose()
    {
        Reset();
        base.Dispose();
    }

    private static void Reset()
    {
        try
        {
            harmony?.UnpatchAll(HarmonyId);
            SafeSpawn.Reset();
            if (regions != null && stockRegions != null)
            {
                for (int i = 0; i < stockRegions.Length; i++)
                {
                    regions.SetValue(stockRegions[i], i);
                }
            }
        }
        catch (Exception e)
        {
            log?.Warning("[earth75ma] Could not put the stock spawn table back: {0}", e.Message);
        }
        harmony = null;
        regions = null;
        stockRegions = null;
        table = null;
        grid = null;
        gridInfo = null;
        loaded = false;
        placeChanged = false;
    }

    // ------------------------------------------------------------------ data

    private static void EnsureLoaded(ICoreServerAPI sapi)
    {
        if (loaded)
        {
            return;
        }
        loaded = true;
        IAsset asset = sapi.Assets.TryGet(new AssetLocation("earth75ma:config/spawnpresets-75ma.json"), true);
        table = asset?.ToObject<SpawnTable>();
        if (table == null || table.presets == null)
        {
            log.Error("[earth75ma] earth75ma:config/spawnpresets-75ma.json is missing or unreadable. Presets stay at their modern coordinates.");
            return;
        }
        IAsset gridAsset = sapi.Assets.TryGet(new AssetLocation("earth75ma:config/modern2paleo-75ma.json"), true);
        GridFile g = gridAsset?.ToObject<GridFile>();
        if (g != null && g.data != null)
        {
            byte[] raw = Convert.FromBase64String(g.data);
            if (raw.Length == g.rows * g.cols * 4)
            {
                grid = new short[raw.Length / 2];
                for (int i = 0; i < grid.Length; i++)
                {
                    grid[i] = (short)(raw[2 * i] | (raw[2 * i + 1] << 8));
                }
                gridInfo = g;
            }
        }
        SwapRegions();
    }

    private static SpawnEntry Find(List<SpawnEntry> list, string code)
    {
        if (list == null || code == null)
        {
            return null;
        }
        foreach (SpawnEntry e in list)
        {
            if (e.code == code)
            {
                return e;
            }
        }
        return null;
    }

    private static void SwapRegions()
    {
        if (regions == null || stockRegions != null)
        {
            return;
        }
        Type rt = regions.GetType().GetElementType();
        FieldInfo fCode = rt.GetField("Code"), fLabel = rt.GetField("Label"), fLat = rt.GetField("Latitude"), fLon = rt.GetField("Longitude");
        if (fCode == null || fLabel == null || fLat == null || fLon == null)
        {
            log.Error("[earth75ma] EarthSpawn.Region has changed shape. Presets stay at their modern coordinates.");
            return;
        }
        stockRegions = new object[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            stockRegions[i] = regions.GetValue(i);     // boxed copy of the stock struct
            object boxed = regions.GetValue(i);
            string code = fCode.GetValue(boxed) as string;
            SpawnEntry e = Find(table.presets, code);
            if (e == null)
            {
                log.Warning("[earth75ma] No 75 Ma position for EarthWorldgen preset \"{0}\"; it stays at its modern coordinates.", code);
                continue;
            }
            fLat.SetValue(boxed, e.lat);
            fLon.SetValue(boxed, e.lon);
            fLabel.SetValue(boxed, e.label);
            regions.SetValue(boxed, i);
            log.Notification("[earth75ma] preset {0}: {1} -> {2} (rotated position was {3}, moved {4:0} km)",
                code, e.label, Fmt(e.lat, e.lon), e.maskAtPaleo, e.snapKm);
        }
    }

    // ------------------------------------------------------------------ patch on EarthSpawn.Configured(sapi, coords, out complaint)

    public static void ConfiguredPrefix(ICoreServerAPI __0)
    {
        placeChanged = false;
        placeGiven = false;
        pendingLabel = null;
        notes.Clear();
        try
        {
            EnsureLoaded(__0);
            ITreeAttribute cfg = __0.WorldManager.SaveGame.WorldConfiguration;
            if (cfg == null || table == null)
            {
                return;
            }
            string size = cfg.GetString(SizeKey, "");
            if (size != QuarterSize)
            {
                notes.Add("Earth 75 Ma: the 75 Ma map is drawn for Quarter Earth (100352 wide). This world is " + (size.Length == 0 ? "another size" : size) + " wide, so rivers and coasts come out blocky and far too wide.");
            }
            string typed = cfg.GetString(PlaceKey, "").Trim();
            string use = typed;
            if (typed.StartsWith(ModernPrefix, StringComparison.OrdinalIgnoreCase))
            {
                use = "";
                string rest = typed.Substring(ModernPrefix.Length).Trim();
                if (!TryParseLatLon(rest, out double mlat, out double mlon))
                {
                    notes.Add("Earth 75 Ma: after \"modern:\" give a latitude and a longitude in degrees, such as modern: 51.5 -0.13.");
                }
                else if (!TryModernToPaleo(mlat, mlon, out double plat, out double plon, out bool rough))
                {
                    notes.Add("Earth 75 Ma: modern " + Fmt(mlat, mlon) + " has no 75 Ma position (open ocean or crust younger than 75 Ma).");
                }
                else
                {
                    use = Num(plat) + " " + Num(plon);
                    pendingLabel = "modern " + Fmt(mlat, mlon) + " at 75 Ma";
                    notes.Add("Earth 75 Ma: modern " + Fmt(mlat, mlon) + " was at " + Fmt(plat, plon) + " 75 million years ago" + (rough ? " (rough, nearest known point)." : "."));
                }
            }
            if (use.Length == 0)
            {
                SpawnEntry site = Find(table.sites, cfg.GetString(SiteKey, "none"));
                if (site != null)
                {
                    use = Num(site.lat) + " " + Num(site.lon);
                    pendingLabel = site.label;
                    if (site.snapKm > 0.5)
                    {
                        notes.Add("Earth 75 Ma: " + site.label + " was " + site.maskAtPaleo + " at 75 Ma (" + Fmt(site.paleoLat, site.paleoLon) + "); you start on the nearest shore, " + Math.Round(site.snapKm) + " km away.");
                    }
                }
            }
            placeGiven = use.Length > 0;
            if (use != typed)
            {
                placeTyped = typed;
                placeChanged = true;
                cfg.SetString(PlaceKey, use);      // put back in the finalizer
            }
        }
        catch (Exception e)
        {
            log?.Error("[earth75ma] Spawn remap failed, EarthWorldgen decides on its own: {0}", e);
        }
    }

    public static void ConfiguredPostfix(object __result, ref string __2)
    {
        try
        {
            if (__result != null && table != null)
            {
                Traverse t = Traverse.Create(__result);
                bool hasBlock = t.Field("HasBlock").GetValue<bool>();
                string label = t.Field("Label").GetValue<string>();
                bool placeResolved = placeGiven && __2 == null;
                if (placeResolved && pendingLabel != null && label == null && !hasBlock)
                {
                    t.Field("Label").SetValue(pendingLabel);
                }
                else if (!placeResolved && label == null && !hasBlock
                    && t.Field("Latitude").GetValue<double>() == 0.0 && t.Field("Longitude").GetValue<double>() == 0.0)
                {
                    SpawnEntry centre = Find(table.presets, "centre");     // EWG's 0N 0E fallback ("nearest land, Ghana")
                    if (centre != null)
                    {
                        t.Field("Latitude").SetValue(centre.lat);
                        t.Field("Longitude").SetValue(centre.lon);
                        t.Field("Label").SetValue(centre.label);
                    }
                }
            }
            if (notes.Count > 0)
            {
                string mine = string.Join("\n", notes);
                __2 = __2 == null ? mine : mine + "\n" + __2;
                log?.Notification("[earth75ma] {0}", mine);
            }
        }
        catch (Exception e)
        {
            log?.Error("[earth75ma] Spawn remap postfix failed: {0}", e);
        }
    }

    public static Exception ConfiguredFinalizer(ICoreServerAPI __0, Exception __exception)
    {
        if (placeChanged)
        {
            placeChanged = false;
            try
            {
                __0.WorldManager.SaveGame.WorldConfiguration?.SetString(PlaceKey, placeTyped);
            }
            catch (Exception)
            {
            }
        }
        return __exception;
    }

    // ------------------------------------------------------------------ modern -> 75 Ma

    private static bool TryParseLatLon(string text, out double lat, out double lon)
    {
        lat = 0.0;
        lon = 0.0;
        string[] parts = text.Replace('−', '-').Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            && lat >= -90.0 && lat <= 90.0 && lon >= -180.0 && lon <= 180.0;
    }

    private static bool Node(int row, int col, out double plat, out double plon)
    {
        plat = 0.0;
        plon = 0.0;
        if (row < 0 || row >= gridInfo.rows)
        {
            return false;
        }
        col = ((col % gridInfo.cols) + gridInfo.cols) % gridInfo.cols;
        int i = (row * gridInfo.cols + col) * 2;
        if (grid[i] == NoValue)
        {
            return false;
        }
        plat = grid[i] / 100.0;
        plon = grid[i + 1] / 100.0;
        return true;
    }

    private static void Unit(double lat, double lon, out double x, out double y, out double z)
    {
        double p = lat * Math.PI / 180.0, l = lon * Math.PI / 180.0;
        x = Math.Cos(p) * Math.Cos(l);
        y = Math.Cos(p) * Math.Sin(l);
        z = Math.Sin(p);
    }

    /// <summary>Bilinear blend of the four surrounding 1 degree nodes when they moved together (same plate);
    /// otherwise the nearest node that has a value, within about 2 degrees, flagged rough.</summary>
    internal static bool TryModernToPaleo(double mlat, double mlon, out double plat, out double plon, out bool rough)
    {
        plat = 0.0;
        plon = 0.0;
        rough = false;
        if (grid == null)
        {
            return false;
        }
        double fy = (gridInfo.firstLat - mlat) / gridInfo.stepDeg, fx = (mlon - gridInfo.firstLon) / gridInfo.stepDeg;
        int y0 = (int)Math.Floor(fy), x0 = (int)Math.Floor(fx);
        double wy = fy - y0, wx = fx - x0;
        double sx = 0, sy = 0, sz = 0;
        double[] ux = new double[4], uy = new double[4], uz = new double[4];
        bool all = true;
        for (int k = 0; k < 4 && all; k++)
        {
            int dy = k >> 1, dx = k & 1;
            if (!Node(y0 + dy, x0 + dx, out double la, out double lo))
            {
                all = false;
                break;
            }
            Unit(la, lo, out ux[k], out uy[k], out uz[k]);
            double w = (dy == 1 ? wy : 1 - wy) * (dx == 1 ? wx : 1 - wx);
            sx += w * ux[k];
            sy += w * uy[k];
            sz += w * uz[k];
        }
        if (all)
        {
            double minDot = 1.0;       // nodes on one plate stay about as far apart as they are today (under 1.5 degrees)
            for (int a = 0; a < 4; a++)
            {
                for (int b = a + 1; b < 4; b++)
                {
                    minDot = Math.Min(minDot, ux[a] * ux[b] + uy[a] * uy[b] + uz[a] * uz[b]);
                }
            }
            if (minDot > Math.Cos(2.2 * Math.PI / 180.0))
            {
                double n = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                plat = Math.Asin(sz / n) * 180.0 / Math.PI;
                plon = Math.Atan2(sy, sx) * 180.0 / Math.PI;
                return true;
            }
        }
        rough = true;
        int ry = (int)Math.Round(fy), rx = (int)Math.Round(fx);
        double best = double.MaxValue;
        bool found = false;
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                double d = (ry + dy - fy) * (ry + dy - fy) + (rx + dx - fx) * (rx + dx - fx);
                if (d < best && Node(ry + dy, rx + dx, out double la, out double lo))
                {
                    best = d;
                    plat = la;
                    plon = lo;
                    found = true;
                }
            }
        }
        return found;
    }

    private static string Num(double v)
    {
        return v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Fmt(double lat, double lon)
    {
        return Math.Abs(lat).ToString("0.##", CultureInfo.InvariantCulture) + (lat >= 0 ? "N " : "S ")
            + Math.Abs(lon).ToString("0.##", CultureInfo.InvariantCulture) + (lon >= 0 ? "E" : "W");
    }
}
