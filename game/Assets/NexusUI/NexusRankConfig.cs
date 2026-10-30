using UnityEngine;

namespace Nexus
{
    /// <summary>
    /// Tier ladder Solo 1v1 — HARUS sama dengan backend Services/Ranks.TierFor (rating Glicko-2, center 1500).
    /// TIDAK ada sistem placement: tier langsung dari rating. Progress bar = posisi rating dalam band tier.
    /// Angka MMR TIDAK ditampilkan ke pemain — hanya tier + bar.
    /// </summary>
    public static class NexusRankConfig
    {
        public struct Band { public string Key; public int Min; public int Max; }

        // Band = batas TierFor backend (Rookie<1200, Contender<1400, Rival<1600, Gladiator<1800, Conqueror<2000, Nexus Legend).
        public static readonly Band[] Bands =
        {
            new Band { Key = "rookie",    Min = 1000, Max = 1200 },
            new Band { Key = "contender", Min = 1200, Max = 1400 },
            new Band { Key = "rival",     Min = 1400, Max = 1600 },
            new Band { Key = "gladiator", Min = 1600, Max = 1800 },
            new Band { Key = "conqueror", Min = 1800, Max = 2000 },
            new Band { Key = "nexus",     Min = 2000, Max = 2400 }, // "Nexus Legend"
        };

        /// <summary>Tier dari rating (mirror backend). Dipakai untuk kolom tier di leaderboard.</summary>
        public static string TierForRating(int rating) =>
            rating < 1200 ? "Rookie" :
            rating < 1400 ? "Contender" :
            rating < 1600 ? "Rival" :
            rating < 1800 ? "Gladiator" :
            rating < 2000 ? "Conqueror" : "Nexus Legend";

        /// <summary>Poin Ladder 0–100 dalam tier (mirror backend Ranks.LadderPoints). Untuk standing saat ini.</summary>
        public static int LadderPoints(int rating)
        {
            int lo = rating < 1200 ? 1000 : rating < 1400 ? 1200 : rating < 1600 ? 1400 : rating < 1800 ? 1600 : rating < 2000 ? 1800 : 2000;
            int hi = lo + (lo >= 2000 ? 400 : 200);
            return Mathf.Clamp(Mathf.RoundToInt(100f * (rating - lo) / (hi - lo)), 0, 100);
        }

        /// <summary>Indeks tier 0 Rookie … 5 Nexus Legend (mirror backend Ranks.TierOrder).</summary>
        public static int TierOrder(int rating) =>
            rating < 1200 ? 0 : rating < 1400 ? 1 : rating < 1600 ? 2 : rating < 1800 ? 3 : rating < 2000 ? 4 : 5;

        /// <summary>Poin Ladder GLOBAL monotonik (tier*100 + poin dalam tier) → DELTA selalu searah rating.
        /// WAJIB dipakai untuk perubahan "+X/-X Poin" (bukan LadderPoints per-tier yang reset di batas tier).</summary>
        public static int LadderPointsGlobal(int rating) => TierOrder(rating) * 100 + LadderPoints(rating);

        /// <summary>0..1 posisi rating dalam band tier (match substring). -1 bila tier tak dikenal.</summary>
        public static float Progress(string tier, int rating)
        {
            if (string.IsNullOrEmpty(tier)) return -1f;
            string t = tier.ToLowerInvariant();
            foreach (var b in Bands)
                if (t.Contains(b.Key))
                    return b.Max <= b.Min ? 0f : Mathf.Clamp01((rating - b.Min) / (float)(b.Max - b.Min));
            return -1f;
        }
    }
}
