using System;
using System.Collections.Generic;
using Earth75Ma.Shared;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Earth75Ma.Flagships
{
    /// <summary>
    /// Flagships module (ex earthflagships 1.1.0): region-locks 14 LoP dinosaur families to their fossil home regions, Earth worlds only.
    /// The veto sits on IServerEventAPI.OnTrySpawnEntity, the natural-spawn attempt event: breeding, hatching, chunk restores and
    /// commands never use it, so existing animals are never touched. The Earth gate is SaveGame.WorldConfiguration "earthWorldgen";
    /// in any other world the module is a complete no-op. Logic is unchanged from the standalone mod.
    /// </summary>
    public class EarthFlagshipsModule : ModSystem
    {
        private ICoreServerAPI sapi;
        private FlagshipConfig config;
        private bool? earthWorld;          // null = not yet readable
        private int mapSizeX, mapSizeZ;
        private readonly HashSet<string> loggedBlocks = new HashSet<string>();
        private int blockedCount;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            config = Earth75MaConfig.Load(api).Flagships;
            if (!config.Enabled)
            {
                api.Logger.Notification("[earth75ma] Flagships module: OFF (ModConfig/{0}, Flagships.Enabled=false).", Earth75MaConfig.FileName);
                return;
            }

            mapSizeX = api.WorldManager.MapSizeX;
            mapSizeZ = api.WorldManager.MapSizeZ;

            sapi.Event.OnTrySpawnEntity += OnTrySpawnEntity;
            sapi.Logger.Notification(
                "[earth75ma] Flagships module loaded: {0} flagship variants across {1} families gated to their fossil regions (Earth worlds only).",
                FlagshipTable.Homes.Count, FlagshipTable.Families.Length);
            // Log the world verdict now when the world config is already readable (the standalone mod only logged it on the first spawn).
            IsEarthWorld();
        }

        private bool IsEarthWorld()
        {
            if (earthWorld.HasValue) return earthWorld.Value;
            bool? e = EarthGate.IsEarthWorld(sapi);
            if (e == null) return false;   // too early; retry on the next spawn
            earthWorld = e.Value;
            if (earthWorld.Value)
            {
                sapi.Logger.Notification(
                    "[earth75ma] Flagships: Earth world detected (earthWorldgen=true), map {0} x {1}: flagship veto ARMED.",
                    mapSizeX, mapSizeZ);
            }
            else
            {
                sapi.Logger.Notification("[earth75ma] Flagships: not an Earth Worldgen world: veto stays inert (no-op).");
            }
            return earthWorld.Value;
        }

        private bool OnTrySpawnEntity(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d spawnPosition, long herdId)
        {
            string family = FlagshipTable.FamilyOfCodePath(properties?.Code?.Path);
            if (family == null) return true;
            if (config == null || !config.Enabled) return true;
            if (!config.FamilyEnabled(family)) return true;

            string variant = null;
            properties.Variant?.TryGetValue("type", out variant);
            if (variant == null) return true;
            if (!FlagshipTable.TryGetHomes(family, variant, out _)) return true; // unknown variant: fail open

            if (!IsEarthWorld()) return true; // standard worlds: complete no-op

            double lat = EarthCoords.LatitudeAt(spawnPosition.Z, mapSizeZ);
            double lon = EarthCoords.LongitudeAt(spawnPosition.X, mapSizeX);

            var verdict = FlagshipGate.Decide(
                earthWorld: true,
                masterEnabled: true,
                familyEnabled: null, // already checked above
                family: family,
                typeVariant: variant,
                lat: lat,
                lon: lon);

            if (verdict == GateVerdict.Block)
            {
                blockedCount++;
                if (config.LogFirstBlockPerVariant && loggedBlocks.Add(family + ":" + variant))
                {
                    sapi.Logger.Notification(
                        "[earth75ma] Flagships: blocked natural spawn of {0} ({1}) at lat {2:F1}, lon {3:F1}: outside its home region(s) {4}.",
                        variant, family, lat, lon,
                        string.Join(", ", FlagshipTable.Homes[(family, variant)]));
                }
                else
                {
                    sapi.Logger.Debug("[earth75ma] Flagships: blocked {0}:{1} at lat {2:F1}, lon {3:F1} (total blocked: {4})",
                        family, variant, lat, lon, blockedCount);
                }
                return false;
            }
            return true;
        }
    }
}
