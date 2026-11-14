using System;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace Earth75Ma.Flora
{
    /// <summary>
    /// Tree/shrub realm gating for the Flora module (ex earthflora).
    /// WgenTreeSupplier.GetRandomGenForClimate picks a tree type from climate alone; the chunk being generated is known one level up,
    /// in GenVegetationAndPatches.OnChunkColumnGen, on the same worldgen thread. A hook on OnChunkColumnGen records the current chunk
    /// in a ThreadLocal, and a prefix on GetRandomGenForClimate narrows the candidate variants to the species allowed in that
    /// chunk's realm. Both targets are resolved and verified before patching; on any failure nothing is patched and the caller logs
    /// the gate as inert. Harmony owner id is "earth75ma.flora.realmgate", separate from the era patch id "earth75ma".
    /// </summary>
    public static class EarthFloraTreeGate
    {
        private const string Owner = "earth75ma.flora.realmgate";

        private static readonly ThreadLocal<(int cx, int cz)?> CurrentChunk =
            new ThreadLocal<(int cx, int cz)?>();

        public static bool ApplyPatches(ICoreServerAPI api, out string error)
        {
            error = null;
            var ctxTarget = AccessTools.Method(typeof(GenVegetationAndPatches), "OnChunkColumnGen");
            var genTarget = AccessTools.Method(typeof(WgenTreeSupplier), "GetRandomGenForClimate");
            if (ctxTarget == null || genTarget == null)
            {
                error = string.Format("patch target missing (OnChunkColumnGen: {0}, GetRandomGenForClimate: {1}); game version mismatch?",
                    ctxTarget != null, genTarget != null);
                return false;
            }

            try
            {
                var harmony = new Harmony(Owner);
                harmony.Patch(ctxTarget,
                    prefix: new HarmonyMethod(typeof(EarthFloraTreeGate), nameof(ChunkGenPrefix)),
                    postfix: new HarmonyMethod(typeof(EarthFloraTreeGate), nameof(ChunkGenPostfix)),
                    finalizer: new HarmonyMethod(typeof(EarthFloraTreeGate), nameof(ChunkGenFinalizer)));
                harmony.Patch(genTarget,
                    prefix: new HarmonyMethod(typeof(EarthFloraTreeGate), nameof(GenPrefix)));

                var ctxInfo = Harmony.GetPatchInfo(ctxTarget);
                var genInfo = Harmony.GetPatchInfo(genTarget);
                bool ctxOk = ctxInfo != null && ctxInfo.Prefixes.Any(p => p.owner == Owner);
                bool genOk = genInfo != null && genInfo.Prefixes.Any(p => p.owner == Owner);
                if (!ctxOk || !genOk)
                {
                    error = string.Format("patch verification failed (context hook: {0}, supplier hook: {1})", ctxOk, genOk);
                    harmony.UnpatchAll(Owner);
                    return false;
                }

                EarthFloraModule.Instance?.SetHarmony(harmony);
                api.Logger.Notification("[earth75ma] Flora: tree realm gate hooks applied and verified.");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ---- GenVegetationAndPatches.OnChunkColumnGen hooks ------------------

        public static void ChunkGenPrefix(IChunkColumnGenerateRequest request)
        {
            if (request != null) CurrentChunk.Value = (request.ChunkX, request.ChunkZ);
        }

        public static void ChunkGenPostfix()
        {
            CurrentChunk.Value = null;
        }

        public static Exception ChunkGenFinalizer(Exception __exception)
        {
            CurrentChunk.Value = null;
            return __exception; // never swallow the original exception
        }

        // ---- WgenTreeSupplier.GetRandomGenForClimate prefix ------------------

        public static void GenPrefix(ref TreeVariant[] gens)
        {
            var mod = EarthFloraModule.Instance;
            if (mod == null || !mod.GateActive) return;
            var current = CurrentChunk.Value;
            if (current == null || gens == null || gens.Length == 0) return;

            string realm = mod.RealmForChunk(current.Value.cx, current.Value.cz);
            var kept = new System.Collections.Generic.List<TreeVariant>(gens.Length);
            for (int i = 0; i < gens.Length; i++)
            {
                var gen = gens[i]?.Generator;
                if (mod.TreeAllowedHere(gen?.Domain, gen?.Path, realm)) kept.Add(gens[i]);
            }
            // An empty result is fine: the supplier returns null and the caller skips this tree try, which is exactly the gate working.
            if (kept.Count != gens.Length) gens = kept.ToArray();
        }
    }
}
