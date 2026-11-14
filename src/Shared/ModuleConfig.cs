using System;
using Earth75Ma.Ebony;
using Earth75Ma.Flagships;
using Earth75Ma.Flora;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Earth75Ma.Shared
{
    /// <summary>
    /// ModConfig/earth75ma.json: one section per module. The era system itself has no switch here; it is driven by the
    /// world config (earthEra) exactly as in 0.0.8.
    ///  Flagships  ex earthflagships 1.1.0: master switch + 14 family switches, all default on.
    ///  Flora      ex earthflora 1.0.0: master switch + block patch / tree halves, all default on.
    ///  Ebony      ex oregonebony 0.1.0: one switch, default on.
    /// Migration: when earth75ma.json does not exist yet, the values of an existing ModConfig/earthflagships.json and
    /// ModConfig/earthflora.json are copied into it once. After that earth75ma.json is the only file read; the old files
    /// are never modified or deleted.
    /// </summary>
    public class Earth75MaConfig
    {
        public const string FileName = "earth75ma.json";

        public FlagshipConfig Flagships = new FlagshipConfig();
        public FloraConfig Flora = new FloraConfig();
        public EbonyConfig Ebony = new EbonyConfig();

        private static readonly object Sync = new object();

        public static Earth75MaConfig Load(ICoreAPI api)
        {
            lock (Sync)
            {
                Earth75MaConfig cfg = null;
                try
                {
                    cfg = api.LoadModConfig<Earth75MaConfig>(FileName);
                }
                catch (Exception ex)
                {
                    api.Logger.Error("[earth75ma] ModConfig/{0} could not be read ({1}); module defaults are used and the file is left as is.", FileName, ex.Message);
                    return Normalize(new Earth75MaConfig());
                }
                if (cfg != null)
                {
                    return Normalize(cfg);
                }
                cfg = new Earth75MaConfig();
                FlagshipConfig oldFlag = TryLoad<FlagshipConfig>(api, "earthflagships.json");
                if (oldFlag != null)
                {
                    cfg.Flagships = oldFlag;
                    api.Logger.Notification("[earth75ma] Migrated ModConfig/earthflagships.json into the Flagships section of ModConfig/{0} (old file left untouched, no longer read).", FileName);
                }
                FloraConfig oldFlora = TryLoad<FloraConfig>(api, "earthflora.json");
                if (oldFlora != null)
                {
                    cfg.Flora = oldFlora;
                    api.Logger.Notification("[earth75ma] Migrated ModConfig/earthflora.json into the Flora section of ModConfig/{0} (old file left untouched, no longer read).", FileName);
                }
                Normalize(cfg);
                api.StoreModConfig(cfg, FileName);
                return cfg;
            }
        }

        private static Earth75MaConfig Normalize(Earth75MaConfig cfg)
        {
            cfg.Flagships ??= new FlagshipConfig();
            cfg.Flora ??= new FloraConfig();
            cfg.Ebony ??= new EbonyConfig();
            return cfg;
        }

        private static T TryLoad<T>(ICoreAPI api, string file) where T : class
        {
            try
            {
                return api.LoadModConfig<T>(file);
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[earth75ma] Old ModConfig/{0} could not be read ({1}); defaults used for that module.", file, ex.Message);
                return null;
            }
        }
    }

    /// <summary>The Earth Worldgen gate every module shares: SaveGame.WorldConfiguration "earthWorldgen".</summary>
    public static class EarthGate
    {
        public const string Key = "earthWorldgen";

        /// <summary>true / false, or null when the world config is not readable yet (then a module falls back to its lazy check).</summary>
        public static bool? IsEarthWorld(ICoreServerAPI sapi)
        {
            try
            {
                ITreeAttribute wc = sapi?.WorldManager?.SaveGame?.WorldConfiguration;
                if (wc == null) return null;
                return wc.GetAsBool(Key, false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Same read for ICoreAPI (asset stage): World.Config first, then the save game.</summary>
        public static bool? IsEarthWorld(ICoreAPI api)
        {
            try
            {
                ITreeAttribute wc = api?.World?.Config;
                if (wc != null) return wc.GetAsBool(Key, false);
            }
            catch
            {
            }
            return IsEarthWorld(api as ICoreServerAPI);
        }
    }
}
