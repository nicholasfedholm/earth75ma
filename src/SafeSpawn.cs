using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Earth75Ma;

/// <summary>
/// 0.0.6, Campanian worlds only. Makes the start land on the surface.
/// Why: EarthWorldgen's EarthSpawn stores the world spawn with y = 0 and loads no chunk. The engine resolves y = 0 lazily
/// from the column's terrain heightmap (0 while the column is not loaded). With spawnRadius above 0 the engine runs its
/// random spawn search around that point (15 columns); when every try fails it keeps the centre's Y: 0 (under the world)
/// or, once the column is loaded, the seabed when the 75 Ma point is coastal sea (seen on the Wapiti fossil site, Quarter
/// Earth: seabed Y 94 to 98, sea level 110).
/// What this does:
///  1. At RunGame, load columns around EWG's point in order of distance (dense near the point, sparser further out, up to
///     ~850 blocks), take the nearest dry surface cell (top solid terrain block at or above sea level, no liquid, two free
///     blocks above) and store it as the world default spawn with Y = top solid + 2.
///     If no dry cell is found, the spawn goes to the sea surface at EWG's point (Y = sea level), never the seabed.
///  2. Harmony prefix on ServerMain.LocateRandomPosition: when the engine's random search ends with no position, it gets
///     the verified spot instead. If the check is still running, the join waits for it (at most WaitMs).
/// </summary>
public static class SafeSpawn
{
    private const int MaxRing = 26;       // chunk columns from EWG's point (26 = ~830 blocks)
    private const int MaxColumns = 250;   // columns generated at most
    private const long WaitMs = 240000;   // longest a join waits for the check
    private static ICoreServerAPI api;
    private static ILogger log;
    private static BlockPos safe;         // verified spawn block (feet position)
    private static bool resolving, done;
    private static readonly List<Action<BlockPos>> waiting = new List<Action<BlockPos>>();

    public static BlockPos Safe => safe;

    public static void Install(ICoreServerAPI sapi, Harmony harmony)
    {
        api = sapi;
        log = sapi.Logger;
        safe = null;
        resolving = false;
        done = false;
        waiting.Clear();
        Type serverMain = AccessTools.TypeByName("Vintagestory.Server.ServerMain");
        MethodInfo locate = serverMain == null ? null : AccessTools.Method(serverMain, "LocateRandomPosition");
        if (locate != null)
        {
            harmony.Patch(locate, prefix: new HarmonyMethod(typeof(SafeSpawn), nameof(LocatePrefix)));
        }
        else
        {
            log.Warning("[earth75ma] ServerMain.LocateRandomPosition not found: the random-spawn fallback is not intercepted (default spawn is still made safe).");
        }
        sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, Resolve);
    }

    public static void Reset()
    {
        safe = null;
        resolving = false;
        done = false;
        waiting.Clear();
        api = null;
    }

    /// <summary>Wraps the search-over callback: a failed search (null) gets the verified spot.</summary>
    public static void LocatePrefix(ref Action<BlockPos> onSearchOver)
    {
        Action<BlockPos> inner = onSearchOver;
        if (inner == null || inner.Target is Fallback)
        {
            return;
        }
        onSearchOver = new Fallback(inner).Invoke;
    }

    private sealed class Fallback
    {
        private readonly Action<BlockPos> inner;
        public Fallback(Action<BlockPos> inner) { this.inner = inner; }

        public void Invoke(BlockPos pos)
        {
            if (pos != null)
            {
                inner(pos);
                return;
            }
            if (safe != null)
            {
                log?.Notification("[earth75ma] Random spawn search found no spot; using the verified surface spawn {0} {1} {2} instead of the engine fallback.",
                    safe.X, safe.Y, safe.Z);
                inner(safe.Copy());
                return;
            }
            if (resolving && !done && api != null)
            {
                log?.Notification("[earth75ma] Random spawn search found no spot and the surface check is still running; the join waits for it.");
                waiting.Add(inner);
                long t0 = Environment.TickCount64;
                Action<float> timeout = null;
                timeout = dt =>
                {
                    if (!waiting.Contains(inner)) return;
                    if (Environment.TickCount64 - t0 < WaitMs) { api.Event.RegisterCallback(timeout, 1000); return; }
                    waiting.Remove(inner);
                    log?.Warning("[earth75ma] Surface check did not finish in {0}s; the join goes ahead with the engine fallback.", WaitMs / 1000);
                    inner(null);
                };
                api.Event.RegisterCallback(timeout, 1000);
                return;
            }
            inner(null);
        }
    }

    private static void Finish()
    {
        done = true;
        List<Action<BlockPos>> list = new List<Action<BlockPos>>(waiting);
        waiting.Clear();
        foreach (Action<BlockPos> cb in list)
        {
            try
            {
                if (safe != null)
                {
                    log.Notification("[earth75ma] Waiting join placed at the verified spawn {0} {1} {2}.", safe.X, safe.Y, safe.Z);
                }
                cb(safe?.Copy());
            }
            catch (Exception e)
            {
                log.Error("[earth75ma] Waiting join callback failed: {0}", e);
            }
        }
    }

    private static void Resolve()
    {
        try
        {
            if (resolving || api == null)
            {
                return;
            }
            resolving = true;
            EntityPos dsp = api.World.DefaultSpawnPosition;
            if (dsp == null)
            {
                log.Warning("[earth75ma] No default spawn position to check.");
                Finish();
                return;
            }
            int x = (int)dsp.X, z = (int)dsp.Z;
            log.Notification("[earth75ma] Checking the world spawn chosen by EarthWorldgen at x={0} z={1} (stored Y {2:0.#}).", x, z, dsp.Y);
            int cx = x / 32, cz = z / 32;
            List<Vec2i> offs = new List<Vec2i>();
            for (int dx = -MaxRing; dx <= MaxRing; dx++)
            {
                for (int dz = -MaxRing; dz <= MaxRing; dz++)
                {
                    int r = Math.Max(Math.Abs(dx), Math.Abs(dz));
                    bool take = r <= 3 || (r <= 12 && dx % 2 == 0 && dz % 2 == 0) || (dx % 4 == 0 && dz % 4 == 0);
                    if (take) offs.Add(new Vec2i(dx, dz));
                }
            }
            offs.Sort((a, b) => (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y));
            int chunkMapX = api.WorldManager.MapSizeX / 32, chunkMapZ = api.WorldManager.MapSizeZ / 32;
            List<Vec2i> cols = new List<Vec2i>();
            foreach (Vec2i o in offs)
            {
                int ox = cx + o.X, oz = cz + o.Y;
                if (ox < 0 || oz < 0 || ox >= chunkMapX || oz >= chunkMapZ) continue;
                cols.Add(new Vec2i(ox, oz));
                if (cols.Count >= MaxColumns) break;
            }
            Step(cols, 0, x, z, Environment.TickCount64);
        }
        catch (Exception e)
        {
            log?.Error("[earth75ma] Spawn check failed, EarthWorldgen's spawn is left as is: {0}", e);
            Finish();
        }
    }

    private static void Step(List<Vec2i> cols, int i, int x0, int z0, long t0)
    {
        if (api == null)
        {
            return;   // server shutting down
        }
        if (i >= cols.Count)
        {
            int sea = api.World.SeaLevel;
            safe = new BlockPos(x0, sea, z0, 0);
            api.WorldManager.SetDefaultSpawnPosition(x0, sea, z0);
            log.Warning("[earth75ma] No dry surface in {0} chunk columns (up to ~{1} blocks) around x={2} z={3}; spawn set on the sea surface at Y={4} so nobody starts on the seabed or under the world ({5:0.0}s).",
                cols.Count, MaxRing * 32, x0, z0, sea, (Environment.TickCount64 - t0) / 1000.0);
            Finish();
            return;
        }
        Vec2i c = cols[i];
        api.WorldManager.LoadChunkColumnPriority(c.X, c.Y, new ChunkLoadOptions
        {
            KeepLoaded = i == 0,
            OnLoaded = () =>
            {
                if (api == null) return;
                bool found = false;
                try
                {
                    if (TryColumn(c, x0, z0, out BlockPos spot, out int top, out string topCode))
                    {
                        found = true;
                        safe = spot;
                        api.WorldManager.SetDefaultSpawnPosition(spot.X, spot.Y, spot.Z);
                        double dist = Math.Sqrt((double)(spot.X - x0) * (spot.X - x0) + (double)(spot.Z - z0) * (spot.Z - z0));
                        log.Notification("[earth75ma] Safe spawn set: block {0} {1} {2} (top solid block y={3} {4}, sea level {5}, {6:0} blocks from EarthWorldgen's point, {7} column(s) checked, {8:0.0}s). World default spawn updated.",
                            spot.X, spot.Y, spot.Z, top, topCode, api.World.SeaLevel, dist, i + 1, (Environment.TickCount64 - t0) / 1000.0);
                        Finish();
                    }
                }
                catch (Exception e)
                {
                    log.Warning("[earth75ma] Spawn check skipped column {0},{1}: {2}", c.X, c.Y, e.Message);
                }
                if (!found) Step(cols, i + 1, x0, z0, t0);
            }
        });
    }

    private static bool IsLiquidAt(IBlockAccessor ba, BlockPos p)
    {
        Block f = ba.GetBlock(p, BlockLayersAccess.Fluid);
        return f != null && f.IsLiquid();
    }

    /// <summary>Best dry spot in one loaded column: nearest to (x0, z0).</summary>
    private static bool TryColumn(Vec2i c, int x0, int z0, out BlockPos found, out int topY, out string topCode)
    {
        found = null; topY = 0; topCode = "";
        IMapChunk mc = api.WorldManager.GetMapChunk(c.X, c.Y);
        if (mc == null || mc.WorldGenTerrainHeightMap == null)
        {
            return false;
        }
        IBlockAccessor ba = api.World.BlockAccessor;
        int sea = api.World.SeaLevel;
        int mapY = api.WorldManager.MapSizeY;
        double best = double.MaxValue;
        BlockPos p = new BlockPos(0);
        for (int lx = 0; lx < 32; lx++)
        {
            for (int lz = 0; lz < 32; lz++)
            {
                int x = c.X * 32 + lx, z = c.Y * 32 + lz;
                int h = mc.WorldGenTerrainHeightMap[lz * 32 + lx];
                if (h <= 0 || h + 3 >= mapY || h + 1 < sea) continue;          // never below sea level
                double d = (double)(x - x0) * (x - x0) + (double)(z - z0) * (z - z0);
                if (d >= best) continue;
                p.Set(x, h, z);
                Block top = ba.GetBlock(p);
                if (top == null || top.Id == 0 || top.SideSolid == null || !top.SideSolid[BlockFacing.UP.Index]) continue;
                if (IsLiquidAt(ba, p)) continue;
                bool clear = true;
                for (int dy = 1; dy <= 2 && clear; dy++)
                {
                    p.Set(x, h + dy, z);
                    if (IsLiquidAt(ba, p)) { clear = false; break; }
                    Block b = ba.GetBlock(p);
                    if (b == null || (b.Id != 0 && b.Replaceable < 6000)) clear = false;
                }
                if (!clear) continue;
                best = d;
                found = new BlockPos(x, h + 2, z, 0);
                topY = h;
                topCode = top.Code?.ToShortString() ?? "?";
            }
        }
        return found != null;
    }
}
