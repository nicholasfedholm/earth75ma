namespace Earth75Ma.Flagships
{
    /// <summary>"Flagships" section of ModConfig/earth75ma.json (was ModConfig/earthflagships.json). Every switch defaults to on.</summary>
    public class FlagshipConfig
    {
        public bool Enabled = true;

        public bool Ceratopsidae = true;
        public bool Tyrannosauridae = true;
        public bool Spinosauridae = true;
        public bool Abelisauridae = true;
        public bool Carcharodontosauridae = true;
        public bool Herrerasauridae = true;
        public bool Allosauridae = true;
        public bool Iguanodontia = true;
        public bool Ankylosauria = true;
        public bool Plateosauridae = true;
        public bool Dromaeosauridae = true;
        public bool Macronaria = true;
        public bool Diplodocoidea = true;
        public bool Oviraptorosauria = true;

        /// <summary>Log a one-line notice the first time each variant is blocked per session.</summary>
        public bool LogFirstBlockPerVariant = true;

        public bool FamilyEnabled(string familyCode)
        {
            switch (familyCode)
            {
                case "ceratopsidae": return Ceratopsidae;
                case "tyrannosauridae": return Tyrannosauridae;
                case "spinosauridae": return Spinosauridae;
                case "abelisauridae": return Abelisauridae;
                case "carcharodontosauridae": return Carcharodontosauridae;
                case "herrerasauridae": return Herrerasauridae;
                case "allosauridae": return Allosauridae;
                case "iguanodontia": return Iguanodontia;
                case "ankylosauria": return Ankylosauria;
                case "plateosauridae": return Plateosauridae;
                case "dromaeosauridae": return Dromaeosauridae;
                case "macronaria": return Macronaria;
                case "diplodocoidea": return Diplodocoidea;
                case "oviraptorosauria": return Oviraptorosauria;
                default: return true;
            }
        }
    }
}
