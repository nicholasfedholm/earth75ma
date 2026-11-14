using System;
using System.Collections.Generic;
using System.Linq;
using Earth75Ma.Shared;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace Earth75Ma.Flora
{
    /// <summary>Shape of a Floral Zones pack's config/biomes.json (the packs' own tables).</summary>
    public class PackBiomesJson
    {
        public Dictionary<string, List<string>> TreeBiomes { get; set; }
        public Dictionary<string, List<string>> BlockPatchBiomes { get; set; }
    }

    /// <summary>
    /// Flora module (ex earthflora 1.0.0): keeps Floral Zones plants in their home floristic regions, Earth worlds only.
    /// Species-to-realm assignments are copied verbatim from the packs' own config/biomes.json tables; the realm of a position
    /// comes from generous lat/lon boxes on the real Earth (Earth75Ma.Shared.Realms).
    ///  - Block patches: IBlockPatchModifier registered with GenVegetationAndPatches; per chunk it supplies the game's patch
    ///    list with out-of-realm species filtered out.
    ///  - Trees and shrubs: Harmony prefix on WgenTreeSupplier.GetRandomGenForClimate (see EarthFloraTreeGate).
    /// In any world whose config does not set earthWorldgen=true, every entry point returns "allow / pass through".
    /// Logic is unchanged from the standalone mod; only the config source (ModConfig/earth75ma.json, Flora section) differs.
    /// </summary>
    public class EarthFloraModule : ModSystem, IBlockPatchModifier
    {
        internal static EarthFloraModule Instance { get; private set; }

        private ICoreServerAPI sapi;
        private FloraConfig config;
        private FloraAssignments assignments;
        private bool? earthWorld;
        private int mapSizeX, mapSizeZ;
        private Harmony harmony;
        private bool treesActive;
        private readonly Dictionary<string, BlockPatch[]> filteredCache = new Dictionary<string, BlockPatch[]>();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override double ExecuteOrder() => 0.6;

        internal bool GateActive => config != null && config.Enabled && IsEarthWorld() && assignments != null;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            Instance = this;

            config = Earth75MaConfig.Load(api).Flora;
            if (!config.Enabled)
            {
                api.Logger.Notification("[earth75ma] Flora module: OFF (ModConfig/{0}, Flora.Enabled=false).", Earth75MaConfig.FileName);
                return;
            }

            mapSizeX = api.WorldManager.MapSizeX;
            mapSizeZ = api.WorldManager.MapSizeZ;

            assignments = LoadPackTables(api);

            if (config.GateBlockPatches)
            {
                var veg = api.ModLoader.GetModSystem<GenVegetationAndPatches>();
                if (veg != null)
                {
                    veg.RegisterPatchModifier(this);
                    sapi.Logger.Notification("[earth75ma] Flora: block patch realm filter registered.");
                }
                else
                {
                    sapi.Logger.Error("[earth75ma] Flora: GenVegetationAndPatches not found: block patch gating INERT.");
                }
            }

            if (config.GateTrees)
            {
                treesActive = EarthFloraTreeGate.ApplyPatches(api, out string error);
                if (!treesActive)
                {
                    sapi.Logger.Error("[earth75ma] Flora: tree realm gating INERT: {0}", error);
                }
            }

            // Verdict now, when the world config is already readable (the standalone mod logged it on first use only).
            IsEarthWorld();
        }

        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchAll("earth75ma.flora.realmgate");
                harmony = null;
            }
            if (Instance == this) Instance = null;
            base.Dispose();
        }

        internal void SetHarmony(Harmony h) => harmony = h;
        internal bool TreesActive => treesActive;

        private FloraAssignments LoadPackTables(ICoreServerAPI api)
        {
            var result = new FloraAssignments();
            try
            {
                var assets = api.Assets.GetMany("config/biomes.json");
                foreach (var asset in assets)
                {
                    string domain = asset.Location.Domain;
                    if (!domain.StartsWith("floralzones")) continue;
                    var parsed = asset.ToObject<PackBiomesJson>();
                    if (parsed == null) continue;
                    var table = new PackFloraTable
                    {
                        Domain = domain,
                        TreeBiomes = parsed.TreeBiomes ?? new Dictionary<string, List<string>>(),
                        BlockPatchBiomes = parsed.BlockPatchBiomes ?? new Dictionary<string, List<string>>()
                    };
                    result.AddPack(table);
                    sapi.Logger.Notification(
                        "[earth75ma] Flora: copied realm tables from {0}: {1} tree species, {2} patch species.",
                        domain, table.TreeBiomes.Count, table.BlockPatchBiomes.Count);
                }
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[earth75ma] Flora: failed to read pack realm tables: {0}", ex.Message);
            }
            sapi.Logger.Notification(
                "[earth75ma] Flora: realm tables loaded: {0} packs, {1} tree species, {2} patch species.",
                result.PackCount, result.TreeSpeciesCount, result.PatchSpeciesCount);
            return result;
        }

        internal bool IsEarthWorld()
        {
            if (earthWorld.HasValue) return earthWorld.Value;
            bool? e = EarthGate.IsEarthWorld(sapi);
            if (e == null) return false;
            earthWorld = e.Value;
            if (earthWorld.Value)
            {
                sapi.Logger.Notification(
                    "[earth75ma] Flora: Earth world detected (earthWorldgen=true), map {0} x {1}: flora realm gate ACTIVE.",
                    mapSizeX, mapSizeZ);
            }
            else
            {
                sapi.Logger.Notification("[earth75ma] Flora: not an Earth Worldgen world: realm gate stays inert (no-op).");
            }
            return earthWorld.Value;
        }

        internal string RealmForChunk(int chunkX, int chunkZ)
        {
            double bx = chunkX * 32 + 16;
            double bz = chunkZ * 32 + 16;
            return Realms.RealmAtBlock(bx, bz, mapSizeX, mapSizeZ);
        }

        internal bool TreeAllowedHere(string generatorDomain, string generatorCodePath, string realm)
            => assignments != null && assignments.TreeAllowed(generatorDomain, generatorCodePath, realm);

        // ---- IBlockPatchModifier -------------------------------------------

        public bool PreventPlacementAt(int x, int z, int category) => false; // species veto lives in the provider filter

        public bool PreventPlacementBroadlyAt(int chunkX, int chunkZ) => false;

        public BlockPatchConfig GetPatchProviderAt(int chunkX, int chunkZ, ref EnumHandling handling)
        {
            if (config == null || !config.GateBlockPatches || !GateActive) return null;

            var veg = sapi.ModLoader.GetModSystem<GenVegetationAndPatches>();
            var source = veg?.bpc?.PatchesNonTree;
            if (source == null) return null; // game config not loaded yet: pass through

            string realm = RealmForChunk(chunkX, chunkZ);
            string key = realm ?? "<none>";
            if (!filteredCache.TryGetValue(key, out var filtered))
            {
                var kept = new List<BlockPatch>();
                foreach (var patch in source)
                {
                    var codes = patch.blockCodes?.Select(c => (c.Domain, c.Path)).ToArray();
                    if (codes == null || codes.Length == 0 || assignments.PatchAllowed(codes, realm))
                    {
                        kept.Add(patch);
                    }
                }
                filtered = kept.ToArray();
                filteredCache[key] = filtered;
            }

            handling = EnumHandling.PreventSubsequent;
            return new BlockPatchConfig
            {
                ChanceMultiplier = veg.bpc.ChanceMultiplier,
                Patches = filtered,
                PatchesNonTree = filtered
            };
        }
    }
}
