using System;
using Earth75Ma.Shared;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Earth75Ma.Ebony
{
    /// <summary>
    /// Ebony module (ex oregonebony 0.1.0): the Pacific Northwest ebony treegen row lives in assets/earth75ma/patches/oregon-ebony.json,
    /// a JSON patch conditioned on world config earthWorldgen=true (identical to the standalone mod's patch). The module adds the
    /// config switch: when Ebony.Enabled is false the Oregon row is removed after patching (AssetsFinalize).
    /// StartServerSide logs whether the row is actually present in the patched worldgen/treengenproperties.json.
    /// </summary>
    public class EarthEbonyModule : ModSystem
    {
        private const double RowMinRain = 185;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        // AssetsLoaded runs before JSON patching on this build but removing the patch asset there had no effect (headless run E, 2026-11-14),
        // so the switch works on the patched result instead: after patching, drop the Oregon ebony row from worldgen/treengenproperties.json.
        public override void AssetsFinalize(ICoreAPI api)
        {
            try
            {
                if (Earth75MaConfig.Load(api).Ebony.Enabled) return;
                IAsset a = api.Assets.TryGet(new AssetLocation("game", "worldgen/treengenproperties.json"), false);
                if (a == null) return;
                JObject root = JObject.Parse(a.ToText());
                JArray gens = root["treegens"] as JArray;
                if (gens == null) return;
                int removed = 0;
                for (int i = gens.Count - 1; i >= 0; i--)
                {
                    if (IsOregonRow(gens[i])) { gens.RemoveAt(i); removed++; }
                }
                if (removed > 0) a.Data = System.Text.Encoding.UTF8.GetBytes(root.ToString(Newtonsoft.Json.Formatting.Indented));
                api.Logger.Notification("[earth75ma] Ebony module: disabled in ModConfig/{0}; removed {1} Oregon ebony row(s) after patching.", Earth75MaConfig.FileName, removed);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[earth75ma] Ebony module: could not apply the off switch ({0}); the row stays as patched.", e.Message);
            }
        }

        private static bool IsOregonRow(JToken t)
        {
            return (string)t["Generator"] == "ebony" && t["MinRain"] != null && (double)t["MinRain"] == RowMinRain && t["MaxRain"] != null && (double)t["MaxRain"] == 255;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            Earth75MaConfig cfg = Earth75MaConfig.Load(api);
            bool? earth = EarthGate.IsEarthWorld(api);
            int rows = CountRows(api);
            if (!cfg.Ebony.Enabled)
            {
                api.Logger.Notification("[earth75ma] Ebony module: OFF (config). Oregon ebony rows present: {0}.", rows);
            }
            else if (earth == true)
            {
                api.Logger.Notification("[earth75ma] Ebony module: ON, Earth world. Oregon ebony treegen rows present: {0} (expected 1).", rows);
            }
            else
            {
                api.Logger.Notification("[earth75ma] Ebony module: ON but not an Earth Worldgen world: no ebony row added (rows present: {0}, expected 0).", rows);
            }
        }

        private static int CountRows(ICoreServerAPI api)
        {
            try
            {
                IAsset a = api.Assets.TryGet(new AssetLocation("game", "worldgen/treengenproperties.json"), false);
                if (a == null) return -1;
                JArray gens = JObject.Parse(a.ToText())["treegens"] as JArray;
                if (gens == null) return -1;
                int n = 0;
                foreach (JToken t in gens)
                {
                    if (IsOregonRow(t)) n++;
                }
                return n;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
