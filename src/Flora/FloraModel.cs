using System;
using System.Collections.Generic;

using Earth75Ma.Shared;

namespace Earth75Ma.Flora
{
    /// <summary>
    /// One Floral Zones pack's species-to-realm tables, copied from the pack's
    /// own config/biomes.json (TreeBiomes and BlockPatchBiomes sections).
    /// Keys are the packs' patterns, values are realm name lists.
    /// An empty realm list is a deliberate exemption (the species is never
    /// realm-locked), mirroring the FotSA familiaris precedent.
    /// </summary>
    public sealed class PackFloraTable
    {
        public string Domain;
        public Dictionary<string, List<string>> TreeBiomes = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> BlockPatchBiomes = new Dictionary<string, List<string>>();
    }

    /// <summary>Aggregated assignments across all loaded packs, plus the matching rules.</summary>
    public sealed class FloraAssignments
    {
        private readonly List<(string Domain, string Pattern, List<string> Realms)> treeEntries =
            new List<(string, string, List<string>)>();
        private readonly List<(string Domain, string Pattern, List<string> Realms)> patchEntries =
            new List<(string, string, List<string>)>();

        public int PackCount { get; private set; }
        public int TreeSpeciesCount => treeEntries.Count;
        public int PatchSpeciesCount => patchEntries.Count;

        public void AddPack(PackFloraTable table)
        {
            if (table == null) return;
            PackCount++;
            foreach (var kv in table.TreeBiomes) treeEntries.Add((table.Domain, kv.Key, Sanitize(kv.Value)));
            foreach (var kv in table.BlockPatchBiomes) patchEntries.Add((table.Domain, kv.Key, Sanitize(kv.Value)));
        }

        private static List<string> Sanitize(List<string> realms)
        {
            var clean = new List<string>();
            if (realms == null) return clean;
            foreach (var r in realms)
            {
                var name = (r ?? "").Trim().ToLowerInvariant();
                foreach (var valid in Realms.All)
                {
                    if (valid == name && !clean.Contains(valid)) clean.Add(valid);
                }
            }
            return clean;
        }

        // ---- Tree matching -------------------------------------------------
        // TreeBiomes keys are treegen generator codes, optionally suffixed
        // with "*" (prefix match). Longest matching key wins.

        /// <summary>
        /// Assigned realms for a tree generator code. Resolution prefers the
        /// pack whose asset domain matches the generator's domain (a pack's
        /// table was authored for its own species); if no entry from that
        /// domain matches, a merged search across all packs applies (tree
        /// variants can sit under the game domain when a pack ships its
        /// treegen files there). Null = ungated.
        /// </summary>
        public List<string> RealmsForTreeGenerator(string domain, string generatorCodePath)
        {
            return ResolveTree(domain, generatorCodePath, domainScoped: true)
                ?? ResolveTree(domain, generatorCodePath, domainScoped: false);
        }

        private List<string> ResolveTree(string domain, string generatorCodePath, bool domainScoped)
        {
            if (string.IsNullOrEmpty(generatorCodePath)) return null;
            string code = generatorCodePath.ToLowerInvariant();
            List<string> best = null;
            int bestLen = -1;
            foreach (var (entryDomain, pattern, realms) in treeEntries)
            {
                if (domainScoped && !string.Equals(entryDomain, domain, StringComparison.OrdinalIgnoreCase)) continue;
                string key = pattern.ToLowerInvariant();
                bool match;
                string core = key;
                if (key.EndsWith("*"))
                {
                    core = key.Substring(0, key.Length - 1);
                    match = code.StartsWith(core);
                }
                else
                {
                    match = code == key;
                }
                if (match && core.Length > bestLen)
                {
                    bestLen = core.Length;
                    best = realms;
                }
            }
            return best;
        }

        // ---- Block patch matching ------------------------------------------
        // BlockPatchBiomes keys look like "*-strelitziaalba-*": the species
        // token sits between wildcards. A patch matches when one of its block
        // code paths contains the token as a whole segment.

        public static string PatchPatternToken(string pattern)
        {
            string p = (pattern ?? "").ToLowerInvariant();
            int colon = p.LastIndexOf(':');
            if (colon >= 0) p = p.Substring(colon + 1);
            if (p.StartsWith("*-")) p = p.Substring(2);
            else if (p.StartsWith("*")) p = p.Substring(1);
            if (p.EndsWith("-*")) p = p.Substring(0, p.Length - 2);
            else if (p.EndsWith("*")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        public static bool CodeContainsToken(string codePath, string token)
        {
            if (string.IsNullOrEmpty(codePath) || string.IsNullOrEmpty(token)) return false;
            string c = codePath.ToLowerInvariant();
            if (c == token) return true;
            if (c.StartsWith(token + "-")) return true;
            if (c.EndsWith("-" + token)) return true;
            if (c.Contains("-" + token + "-")) return true;
            return false;
        }

        /// <summary>
        /// Assigned realms for a generated block patch, from the block codes
        /// it can place. Same two-pass rule as trees: the pack whose domain
        /// matches a code's domain first (its patterns were authored for its
        /// own patches), then merged. The first code (in the patch's own
        /// order) with an assignment decides. Null = ungated.
        /// </summary>
        public List<string> RealmsForPatchCodes(IEnumerable<(string Domain, string Path)> blockCodes)
        {
            if (blockCodes == null) return null;
            var list = blockCodes as IList<(string Domain, string Path)> ?? new List<(string Domain, string Path)>(blockCodes);
            return ResolvePatch(list, domainScoped: true) ?? ResolvePatch(list, domainScoped: false);
        }

        private List<string> ResolvePatch(IList<(string Domain, string Path)> blockCodes, bool domainScoped)
        {
            foreach (var (domain, codePath) in blockCodes)
            {
                if (string.IsNullOrEmpty(codePath)) continue;
                List<string> best = null;
                int bestLen = -1;
                foreach (var (entryDomain, pattern, realms) in patchEntries)
                {
                    if (domainScoped && !string.Equals(entryDomain, domain, StringComparison.OrdinalIgnoreCase)) continue;
                    string token = PatchPatternToken(pattern);
                    if (token.Length == 0) continue;
                    if (CodeContainsToken(codePath, token) && token.Length > bestLen)
                    {
                        bestLen = token.Length;
                        best = realms;
                    }
                }
                if (best != null) return best;
            }
            return null;
        }

        // ---- Decisions -------------------------------------------------------

        /// <summary>Shared rule: ungated (null) or exempt (empty list) means allowed.</summary>
        private static bool Decide(List<string> realms, string realmHere)
        {
            if (realms == null) return true;
            if (realms.Count == 0) return true;
            if (realmHere == null) return false; // gated species, position has no realm (ocean)
            return realms.Contains(realmHere);
        }

        public bool TreeAllowed(string generatorDomain, string generatorCodePath, string realmHere)
            => Decide(RealmsForTreeGenerator(generatorDomain, generatorCodePath), realmHere);

        public bool PatchAllowed(IEnumerable<(string Domain, string Path)> blockCodes, string realmHere)
            => Decide(RealmsForPatchCodes(blockCodes), realmHere);
    }
}
