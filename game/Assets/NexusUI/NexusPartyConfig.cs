namespace Nexus
{
    /// <summary>
    /// Ukuran tim (jumlah slot) per mode — SATU tempat, mudah diubah.
    /// Index mode sama dengan urutan slider: 0 Ranked, 1 Battle, 2 Faction War, 3 Nexus Cup.
    /// Catatan: matchmaking backend saat ini masih 1v1; ukuran >1 baru visual sampai team-mode nyata ada.
    /// </summary>
    public static class NexusPartyConfig
    {
        public const int Ranked = 1;
        public const int Battle = 3;
        public const int FactionWar = 5;
        public const int NexusCup = 5;

        public static int ForMode(int modeIndex)
        {
            switch (modeIndex)
            {
                case 0: return Ranked;
                case 1: return Battle;
                case 2: return FactionWar;
                case 3: return NexusCup;
                default: return 1;
            }
        }
    }
}
