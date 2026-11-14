using System;
using System.Collections.Generic;

using Earth75Ma.Shared;

namespace Earth75Ma.Flagships
{
    /// <summary>
    /// Flagship dino home regions: named subcontinent boxes (REGIONS.md Part B)
    /// and the per-variant assignments extracted from the installed LoP packs
    /// (variantgroup "type", 2026-10-30). A spawn is in-region when it falls in
    /// ANY of its variant's boxes.
    /// </summary>
    public static class FlagshipRegions
    {
        public static readonly IReadOnlyDictionary<string, LatLonBox[]> Boxes =
            new Dictionary<string, LatLonBox[]>
            {
                ["North America"] = new[]
                {
                    new LatLonBox(15, 72, -168, -52),
                    new LatLonBox(58, 72, -58, -20)   // Greenland
                },
                ["Central Asia"] = new[] { new LatLonBox(33, 50, 55, 115) },
                ["East Asia"] = new[] { new LatLonBox(20, 54, 100, 142) },
                ["Southeast Asia"] = new[] { new LatLonBox(8, 30, 92, 110) },
                ["Europe"] = new[] { new LatLonBox(36, 60, -10, 32) },
                ["North Africa"] = new[] { new LatLonBox(10, 37, -18, 36) },
                ["East Africa"] = new[] { new LatLonBox(-12, 12, 25, 52) },
                ["Madagascar"] = new[] { new LatLonBox(-26, -12, 43, 51) },
                ["India"] = new[] { new LatLonBox(8, 36, 68, 90) },
                ["South America"] = new[] { new LatLonBox(-57, 12, -82, -34) },
                ["Australia"] = new[] { new LatLonBox(-44, -10, 112, 154) },
                ["Antarctica"] = new[] { new LatLonBox(-90, -60, -180, 180) },
            };

        public static bool InRegion(string regionName, double lat, double lon)
        {
            if (!Boxes.TryGetValue(regionName, out var boxes)) return false;
            for (int i = 0; i < boxes.Length; i++) if (boxes[i].Contains(lat, lon)) return true;
            return false;
        }
    }

    public static class FlagshipTable
    {
        /// <summary>Family entity code -> config switch name used in the ModConfig file.</summary>
        public static readonly string[] Families =
        {
            "ceratopsidae", "tyrannosauridae", "spinosauridae",
            "abelisauridae", "carcharodontosauridae", "herrerasauridae",
            // 1.1.0 lopgapfill families (only their gapfill genera are in Homes; the rest fail open)
            "allosauridae", "iguanodontia", "ankylosauria", "plateosauridae",
            "dromaeosauridae", "macronaria", "diplodocoidea", "oviraptorosauria"
        };

        /// <summary>(family, type variant) -> home region names.</summary>
        public static readonly IReadOnlyDictionary<(string Family, string Variant), string[]> Homes = Build();

        private static Dictionary<(string, string), string[]> Build()
        {
            var d = new Dictionary<(string, string), string[]>();
            void Add(string fam, string region, params string[] variants)
            {
                foreach (var v in variants) d[(fam, v)] = new[] { region };
            }

            const string CER = "ceratopsidae";
            Add(CER, "North America",
                "triceratops", "pentaceratops", "styracosaurus", "centrosaurus", "nasutoceratops",
                "pachyrhinosaurus", "torosaurus", "xenoceratops", "machairoceratops", "nedoceratops",
                "regaliceratops", "leptoceratops");
            Add(CER, "East Asia", "sinoceratops");
            Add(CER, "Central Asia", "protoceratops");
            Add(CER, "Europe", "ajkaceratops");

            const string TYR = "tyrannosauridae";
            Add(TYR, "North America",
                "tyrannosaurus", "nanuqsaurus", "albertosaurus", "gorgosaurus",
                "bistahieversor", "thanatotheristes", "teratophoneus", "dynamoterror");
            Add(TYR, "Central Asia", "tarbosaurus", "alioramus");
            Add(TYR, "East Asia", "qianzhousaurus", "zhuchengtyrannus");

            const string SPI = "spinosauridae";
            Add(SPI, "North Africa", "spinosaurus", "suchomimus");
            Add(SPI, "South America", "oxalaia", "irritator");
            Add(SPI, "Europe", "baryonyx");
            Add(SPI, "Southeast Asia", "ichthyovenator");

            const string ABE = "abelisauridae";
            Add(ABE, "South America",
                "pycnonemosaurus", "ekrixinatosaurus", "carnotaurus", "skorpiovenator", "abelisaurus",
                "xenotarsosaurus", "aucasaurus", "ilokelesia", "viavenator", "thanos");
            Add(ABE, "North Africa", "chenanisaurus", "rugops");
            Add(ABE, "India", "rajasaurus", "rahiolisaurus");
            Add(ABE, "Madagascar", "majungasaurus", "dahalokely", "masiakasaurus");
            Add(ABE, "Europe", "arcovenator", "tarascosaurus");

            const string CAR = "carcharodontosauridae";
            Add(CAR, "South America",
                "giganotosaurus", "mapusaurus", "tyrannotitan", "lajasvenator");
            Add(CAR, "North Africa", "carcharodontosaurus", "tameryraptor");
            Add(CAR, "East Africa", "veterupristisaurus");
            Add(CAR, "Central Asia", "ulughbegsaurus");
            Add(CAR, "Southeast Asia", "siamraptor");
            Add(CAR, "East Asia", "shaochilong");
            Add(CAR, "Europe", "concavenator", "lusovenator");

            const string HER = "herrerasauridae";
            Add(HER, "South America", "herrerasaurus", "staurikosaurus");

            // ---- 1.1.0: lopgapfill genera (regions-gapfill.md) ----
            const string ALL = "allosauridae";
            Add(ALL, "Antarctica", "cryolophosaurus");
            Add(ALL, "Australia", "australovenator");
            Add(ALL, "South America", "maip");

            const string IGU = "iguanodontia";
            Add(IGU, "Australia", "muttaburrasaurus", "leaellynasaura");
            Add(IGU, "Antarctica", "trinisaura");

            Add("ankylosauria", "Australia", "minmi");
            Add("plateosauridae", "Antarctica", "glacialisaurus");
            Add("dromaeosauridae", "Antarctica", "imperobator");

            const string MAC = "macronaria";
            Add(MAC, "Australia", "savannasaurus");
            Add(MAC, "India", "isisaurus");
            Add(MAC, "North Africa", "paralititan");

            Add("diplodocoidea", "South America", "amargasaurus");
            Add("oviraptorosauria", "Central Asia", "gigantoraptor");

            return d;
        }

        /// <summary>
        /// Family from a resolved entity code path. The game resolves variant entities to
        /// "family-type-age-gender" (runtime.db: abelisauridae-carnotaurus-adult-male), so the
        /// path is never equal to the bare family code. Returns null when not a flagship family.
        /// </summary>
        public static string FamilyOfCodePath(string path)
        {
            if (path == null) return null;
            foreach (var f in Families)
                if (path == f || path.StartsWith(f + "-", StringComparison.Ordinal)) return f;
            return null;
        }

        public static bool IsFlagshipFamily(string family)
        {
            foreach (var f in Families) if (f == family) return true;
            return false;
        }

        public static bool TryGetHomes(string family, string variant, out string[] regions)
            => Homes.TryGetValue((family, variant), out regions);

        /// <summary>True when the geographic position is inside any of the variant's home boxes.</summary>
        public static bool IsInHome(string family, string variant, double lat, double lon)
        {
            if (!TryGetHomes(family, variant, out var regions)) return false;
            foreach (var r in regions) if (FlagshipRegions.InRegion(r, lat, lon)) return true;
            return false;
        }
    }

    public enum GateVerdict { Allow, Block }

    /// <summary>
    /// Pure spawn-gate decision for earthflagships. Fail-open by design for
    /// anything the table does not cover: a variant or family we do not know
    /// is never blocked.
    /// </summary>
    public static class FlagshipGate
    {
        public static GateVerdict Decide(
            bool earthWorld,
            bool masterEnabled,
            Func<string, bool> familyEnabled,
            string family,
            string typeVariant,
            double lat,
            double lon)
        {
            if (!earthWorld) return GateVerdict.Allow;          // standard worlds: complete no-op
            if (!masterEnabled) return GateVerdict.Allow;
            if (family == null || !FlagshipTable.IsFlagshipFamily(family)) return GateVerdict.Allow;
            if (familyEnabled != null && !familyEnabled(family)) return GateVerdict.Allow;
            if (typeVariant == null) return GateVerdict.Allow;
            if (!FlagshipTable.TryGetHomes(family, typeVariant, out _)) return GateVerdict.Allow;
            return FlagshipTable.IsInHome(family, typeVariant, lat, lon)
                ? GateVerdict.Allow
                : GateVerdict.Block;
        }
    }
}
