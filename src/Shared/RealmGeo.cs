using System;
using System.Collections.Generic;

namespace Earth75Ma.Shared
{
    /// <summary>An inclusive lat/lon rectangle. Lat: +N/-S. Lon: +E/-W.</summary>
    public readonly struct LatLonBox
    {
        public readonly double LatMin, LatMax, LonMin, LonMax;
        public LatLonBox(double latMin, double latMax, double lonMin, double lonMax)
        {
            LatMin = latMin; LatMax = latMax; LonMin = lonMin; LonMax = lonMax;
        }
        public bool Contains(double lat, double lon)
            => lat >= LatMin && lat <= LatMax && lon >= LonMin && lon <= LonMax;
    }

    /// <summary>The Earth mod's own block/lat-lon mapping (EarthCoords.cs).</summary>
    public static class EarthCoords
    {
        public static double LongitudeAt(double blockX, double mapSizeX) => blockX * 360.0 / mapSizeX - 180.0;
        public static double LatitudeAt(double blockZ, double mapSizeZ) => 90.0 - blockZ * 180.0 / mapSizeZ;
        public static double BlockXOf(double longitude, double mapSizeX) => (longitude + 180.0) * mapSizeX / 360.0;
        public static double BlockZOf(double latitude, double mapSizeZ) => (90.0 - latitude) * mapSizeZ / 180.0;
    }

    /// <summary>
    /// The 12 Biomes realms as generous lat/lon boxes on the real Earth.
    /// Mirrors REGIONS.md exactly. Evaluation is first-match in list order;
    /// the three exception boxes (Caribbean, Philippines, New Guinea) sit at
    /// the head of the list where they overlap a northern realm or a sibling.
    /// A position in no box has no realm (null): open ocean, far polar.
    /// </summary>
    public static class Realms
    {
        public const string PacificNearctic = "pacific nearctic";
        public const string AtlanticNearctic = "atlantic nearctic";
        public const string AtlanticPalearctic = "atlantic palearctic";
        public const string CentralPalearctic = "central palearctic";
        public const string EasternPalearctic = "eastern palearctic";
        public const string PacificPalearctic = "pacific palearctic";
        public const string PacificNeotropic = "pacific neotropic";
        public const string AtlanticNeotropic = "atlantic neotropic";
        public const string AtlanticAfrotropic = "atlantic afrotropic";
        public const string EasternAfrotropic = "eastern afrotropic";
        public const string Australasian = "australasian";
        public const string Oceanic = "oceanic";

        public static readonly string[] All =
        {
            PacificNearctic, AtlanticNearctic, AtlanticPalearctic, CentralPalearctic,
            EasternPalearctic, PacificPalearctic, PacificNeotropic, AtlanticNeotropic,
            AtlanticAfrotropic, EasternAfrotropic, Australasian, Oceanic
        };

        // (realm, box) pairs in priority order. First match wins.
        private static readonly (string Realm, LatLonBox Box)[] Priority =
        {
            // Exceptions first: these overlap a northern realm or a sibling box.
            (AtlanticNeotropic, new LatLonBox(9, 28, -86, -60)),      // Caribbean islands
            (Oceanic,           new LatLonBox(5, 19, 117, 127)),      // Philippines
            (Oceanic,           new LatLonBox(18, 23, -162, -154)),   // Hawaii
            (Australasian,      new LatLonBox(-9, 1, 130, 148)),      // New Guinea
            // Northern realms, west to east.
            (PacificNearctic,   new LatLonBox(20, 72, -168, -100)),
            (AtlanticNearctic,  new LatLonBox(20, 72, -100, -45)),
            (AtlanticNearctic,  new LatLonBox(58, 72, -58, -20)),     // Greenland
            (AtlanticPalearctic,new LatLonBox(36, 71, -10, 45)),
            (CentralPalearctic, new LatLonBox(40, 72, 45, 100)),
            (CentralPalearctic, new LatLonBox(28, 40, 60, 100)),      // Central Asian highlands
            (EasternPalearctic, new LatLonBox(18, 55, 100, 142)),
            (EasternPalearctic, new LatLonBox(55, 72, 100, 140)),     // east-central Siberia
            (PacificPalearctic, new LatLonBox(42, 72, 140, 180)),
            // Southern realms.
            (PacificNeotropic,  new LatLonBox(-57, 12, -82, -66)),
            (PacificNeotropic,  new LatLonBox(12, 20, -105, -100)),   // Mexico, Pacific slope below the nearctic line
            (AtlanticNeotropic, new LatLonBox(7, 24, -100, -60)),     // Central America + Mexico east of 100W (incl. Yucatan)
            (AtlanticNeotropic, new LatLonBox(-57, 12, -66, -34)),    // South America east of the Andes
            (AtlanticAfrotropic,new LatLonBox(-35, 37, -18, 22)),
            (EasternAfrotropic, new LatLonBox(-35, 15, 22, 52)),
            (EasternAfrotropic, new LatLonBox(15, 33, 22, 38)),       // Egypt / Sudan / Red Sea
            (EasternAfrotropic, new LatLonBox(-26, -12, 43, 51)),     // Madagascar
            (Australasian,      new LatLonBox(-44, -10, 112, 154)),   // Australia + Tasmania
            (Australasian,      new LatLonBox(-47, -34, 166, 179)),   // New Zealand
            (Oceanic,           new LatLonBox(-25, 25, 155, 180)),    // Melanesia band
            (Oceanic,           new LatLonBox(-25, 25, -180, -135)),  // Polynesia band
            (Oceanic,           new LatLonBox(-9, 6, 95, 131)),       // Indonesian archipelago
            (Oceanic,           new LatLonBox(-2, 2, -92, -89)),      // Galapagos
        };

        /// <summary>Realm at a geographic position, or null when no box covers it.</summary>
        public static string RealmAt(double lat, double lon)
        {
            for (int i = 0; i < Priority.Length; i++)
            {
                if (Priority[i].Box.Contains(lat, lon)) return Priority[i].Realm;
            }
            return null;
        }

        /// <summary>Realm at a block position in a world of the given size.</summary>
        public static string RealmAtBlock(double blockX, double blockZ, double mapSizeX, double mapSizeZ)
            => RealmAt(EarthCoords.LatitudeAt(blockZ, mapSizeZ), EarthCoords.LongitudeAt(blockX, mapSizeX));

        /// <summary>All boxes belonging to one realm (unordered; for reporting/tests).</summary>
        public static List<LatLonBox> BoxesOf(string realm)
        {
            var list = new List<LatLonBox>();
            foreach (var (r, b) in Priority) if (r == realm) list.Add(b);
            return list;
        }
    }
}
