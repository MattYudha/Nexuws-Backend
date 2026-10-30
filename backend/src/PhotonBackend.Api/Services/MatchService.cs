using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Api.Services;

public enum ReportStatus
{
    Accepted,      // stored, waiting for the opponent's report
    Finished,      // both reports agree, match finalized
    Disputed,      // reports disagree / sanity-check gagal, nothing is rated
    AlreadyFinal,  // match was already finished/disputed/closed
    Invalid,       // scores or slot are not a legal result
    SlotTaken,     // another player already claimed this slot
}

public sealed record ReportOutcome(ReportStatus Status, string? Message = null);

// Payload hasil LENGKAP (docs/32 KEAMANAN MINIMUM): statistik akumulator per pemain + per hero.
public sealed record ReportHero(int HeroId, int PlayTicks, int Kills, int Damage);
public sealed record ReportPlayer(
    int Slot, int Kills, int BotKills, int Deaths, int HeroId,
    int DamageDealt, int DamageTaken, int SkillsUsed, int SkillsHit,
    int HitsBlocked, int Dodges, int GuardCrushes, int Criticals, int GearDrops,
    bool FirstBlood, int MaxKillStreak, int MaxDeficit, int HeroSwitches,
    List<ReportHero>? Heroes);

/// <summary>
/// Records Pong results. Clients run a deterministic simulation, so both honest players
/// report identical scores. A result is only rated once both players agree.
/// </summary>
public sealed class MatchService(AppDbContext db, IOptions<GameOptions> options, ILogger<MatchService> logger, FactionService factions)
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly GameOptions _game = options.Value;

    // Single-instance guard: striped in-process locks keyed by game id. Scaling the API out to
    // several replicas needs a database advisory lock instead (see docs/ARCHITECTURE.md).
    private static SemaphoreSlim GateFor(string gameId) => Gates[(gameId.GetHashCode() & int.MaxValue) % Gates.Length];

    public async Task<Match> GetOrCreateMatchAsync(string gameId, string? region, CancellationToken ct)
    {
        var match = await db.Matches.Include(m => m.Players).FirstOrDefaultAsync(m => m.GameId == gameId, ct);
        if (match is not null) return match;

        match = new Match { GameId = gameId, Region = region ?? "" };
        db.Matches.Add(match);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another request creating the same room.
            db.Entry(match).State = EntityState.Detached;
            match = await db.Matches.Include(m => m.Players).FirstAsync(m => m.GameId == gameId, ct);
        }
        return match;
    }

    public async Task CloseAsync(string gameId, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.GameId == gameId, ct);
        if (match is null || match.Status != MatchStatus.Created) return;
        match.Status = MatchStatus.Closed;
        match.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReportOutcome> ReportAsync(
        string gameId, Guid playerId, int slot, int scoreLeft, int scoreRight,
        int kills, int deaths, int assists,
        int heroId, string mode, int durationSec, int partySize,
        IReadOnlyList<ReportPlayer>? players, CancellationToken ct)
    {
        if (!IsLegalResult(slot, scoreLeft, scoreRight))
            return new ReportOutcome(ReportStatus.Invalid, "Not a legal result.");

        var gate = GateFor(gameId);
        await gate.WaitAsync(ct);
        try
        {
            var match = await GetOrCreateMatchAsync(gameId, null, ct);
            if (match.Status != MatchStatus.Created)
                return new ReportOutcome(ReportStatus.AlreadyFinal);

            if (match.Players.Any(p => p.Slot == slot && p.PlayerId != playerId))
                return new ReportOutcome(ReportStatus.SlotTaken);

            var mine = match.Players.FirstOrDefault(p => p.PlayerId == playerId);
            if (mine is null)
            {
                if (match.Players.Count >= 2)
                    return new ReportOutcome(ReportStatus.SlotTaken, "Match already has two players.");
                mine = new MatchPlayer { MatchId = match.Id, PlayerId = playerId, Slot = slot };
                db.MatchPlayers.Add(mine); // EF fix-up also adds it to match.Players
            }
            mine.Slot = slot;
            mine.ReportedScoreLeft = scoreLeft;
            mine.ReportedScoreRight = scoreRight;
            mine.ReportedKills = Math.Max(0, kills);
            mine.ReportedDeaths = Math.Max(0, deaths);
            mine.ReportedAssists = Math.Max(0, assists);
            mine.HeroId = heroId;
            mine.PartySize = Math.Max(1, partySize);
            if (players is { Count: > 0 }) mine.ResultPayload = Canonical(players);
            if (string.IsNullOrEmpty(match.Mode) && !string.IsNullOrEmpty(mode)) match.Mode = mode;
            if (durationSec > match.DurationSec) match.DurationSec = durationSec;

            var reported = match.Players.Where(p => p.ReportedScoreLeft.HasValue).ToList();
            if (reported.Count < 2)
            {
                await db.SaveChangesAsync(ct);
                return new ReportOutcome(ReportStatus.Accepted);
            }

            // (1) Skor harus setuju.
            var scoreAgree = reported.All(p =>
                p.ReportedScoreLeft == reported[0].ReportedScoreLeft &&
                p.ReportedScoreRight == reported[0].ReportedScoreRight);

            // (2) Payload hasil LENGKAP harus setuju bila KEDUA klien mengirimnya (deterministik → identik).
            var payloads = reported.Select(p => p.ResultPayload).ToList();
            bool bothPayloads = payloads.All(s => !string.IsNullOrEmpty(s));
            bool payloadAgree = !bothPayloads || payloads.All(s => s == payloads[0]);

            if (!scoreAgree || !payloadAgree)
            {
                match.Status = MatchStatus.Disputed;
                match.EndedAt = DateTime.UtcNow;
                logger.LogWarning("Match {GameId} DISPUTED (scoreAgree={Score}, payloadAgree={Payload}).", gameId, scoreAgree, payloadAgree);
                await db.SaveChangesAsync(ct);
                return new ReportOutcome(ReportStatus.Disputed);
            }

            // (3) Sanity-check: durasi masuk akal & laju KO wajar.
            int totalKo = scoreLeft + scoreRight;
            if (match.DurationSec > 0 &&
                (match.DurationSec < _game.MinMatchSeconds || (double)totalKo / match.DurationSec > _game.MaxKoPerSecond))
            {
                match.Status = MatchStatus.Disputed;
                match.EndedAt = DateTime.UtcNow;
                logger.LogWarning("Match {GameId} REJECTED sanity (dur={Dur}s, ko={Ko}).", gameId, match.DurationSec, totalKo);
                await db.SaveChangesAsync(ct);
                return new ReportOutcome(ReportStatus.Disputed, "Hasil tidak lolos sanity-check.");
            }

            var agreedPayload = reported.Select(p => p.ResultPayload).FirstOrDefault(s => !string.IsNullOrEmpty(s));

            // Format + validasi aturan 1v1 untuk ranked (docs/32).
            string format;
            if (string.Equals(match.Mode, "ranked", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(agreedPayload))
                    format = "legacy5v5"; // ranked tanpa statistik terverifikasi → K/D tak dihitung
                else
                {
                    List<ReportPlayer>? rows = null;
                    try { rows = JsonSerializer.Deserialize<List<ReportPlayer>>(agreedPayload); } catch (JsonException) { }
                    if (!ValidateRanked1v1(rows, scoreLeft, scoreRight, out var why))
                    {
                        match.Status = MatchStatus.Disputed;
                        match.EndedAt = DateTime.UtcNow;
                        logger.LogWarning("Match {GameId} ranked INVALID 1v1: {Why}", gameId, why);
                        await db.SaveChangesAsync(ct);
                        return new ReportOutcome(ReportStatus.Disputed, "Statistik ranked melanggar 1v1: " + why);
                    }
                    format = "ranked1v1";
                }
            }
            else format = "battle";

            await FinalizeAsync(match, scoreLeft, scoreRight, agreedPayload, format, ct);
            return new ReportOutcome(ReportStatus.Finished);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsLegalResult(int slot, int left, int right) =>
        slot is 0 or 1 &&
        left >= 0 && right >= 0 &&
        Math.Max(left, right) == _game.WinScore &&
        Math.Min(left, right) < _game.WinScore;

    // Serialisasi kanonik payload (urut slot & hero) → dua klien yang setuju menghasilkan string identik.
    private static string Canonical(IReadOnlyList<ReportPlayer> players)
    {
        var sorted = players
            .OrderBy(p => p.Slot)
            .Select(p => p with { Heroes = (p.Heroes ?? new()).OrderBy(h => h.HeroId).ToList() })
            .ToList();
        return JsonSerializer.Serialize(sorted);
    }

    // Aturan 1v1 (docs/32): kills A = deaths B, dan kills tiap pemain = skor timnya (pemenang = 5). Tanpa KO bot.
    private static bool ValidateRanked1v1(List<ReportPlayer>? rows, int scoreLeft, int scoreRight, out string why)
    {
        why = "";
        if (rows is null || rows.Count != 2) { why = "butuh tepat 2 pemain (1v1)"; return false; }
        var p0 = rows.FirstOrDefault(r => r.Slot == 0);
        var p1 = rows.FirstOrDefault(r => r.Slot == 1);
        if (p0 is null || p1 is null) { why = "slot 0/1 tak lengkap"; return false; }
        if (p0.BotKills != 0 || p1.BotKills != 0) { why = "ada KO bot di ranked"; return false; }
        if (p0.Kills != scoreLeft || p1.Kills != scoreRight) { why = $"kills≠skor ({p0.Kills}/{p1.Kills} vs {scoreLeft}/{scoreRight})"; return false; }
        if (p0.Deaths != scoreRight || p1.Deaths != scoreLeft) { why = $"deaths≠skor lawan ({p0.Deaths}/{p1.Deaths})"; return false; }
        return true;
    }

    private async Task FinalizeAsync(Match match, int left, int right, string? payloadJson, string format, CancellationToken ct)
    {
        var winnerSlot = left > right ? 0 : 1;
        var winner = match.Players.Single(p => p.Slot == winnerSlot);
        var loser = match.Players.Single(p => p.Slot != winnerSlot);

        var players = await db.Players
            .Where(p => p.Id == winner.PlayerId || p.Id == loser.PlayerId)
            .ToDictionaryAsync(p => p.Id, ct);
        var w = players[winner.PlayerId];
        var l = players[loser.PlayerId];

        // Glicko-2: hitung kedua pemain dari rating lawan PRA-match (tangkap dulu sebelum menulis balik).
        var wR = new Glicko2Rating(w.Rating, w.Rd, w.Volatility);
        var lR = new Glicko2Rating(l.Rating, l.Rd, l.Volatility);
        var wNew = Glicko2.Update(wR, new[] { new Glicko2Game(lR.Rating, lR.Rd, 1.0) }, _game.Tau);
        var lNew = Glicko2.Update(lR, new[] { new Glicko2Game(wR.Rating, wR.Rd, 0.0) }, _game.Tau);

        var wBefore = w.Rating;
        var lBefore = l.Rating;
        int wDelta = (int)Math.Round(wNew.Rating) - wBefore;
        int lDelta = Math.Max(0, (int)Math.Round(lNew.Rating)) - lBefore;

        // Anti-kolusi: pasangan yang sama bertemu berkali-kali dalam jendela singkat → redam perolehan rating.
        int pairCount = await RecentPairMatchesAsync(winner.PlayerId, loser.PlayerId, ct);
        if (pairCount >= _game.CollusionThreshold)
        {
            wDelta = (int)Math.Round(wDelta * _game.CollusionRatingFactor);
            lDelta = (int)Math.Round(lDelta * _game.CollusionRatingFactor);
            logger.LogWarning("Match {GameId} anti-kolusi: pasangan bertemu {Count}x dalam {Min} menit → rating diredam.", match.GameId, pairCount + 1, _game.CollusionWindowMinutes);
        }

        w.Rating = wBefore + wDelta; w.Rd = wNew.Rd; w.Volatility = wNew.Volatility;
        l.Rating = Math.Max(0, lBefore + lDelta); l.Rd = lNew.Rd; l.Volatility = lNew.Volatility;
        w.Wins++;
        l.Losses++;

        // Akumulasi statistik tempur (lapor sendiri, sudah dicocokkan) + XP akun.
        w.Kills += winner.ReportedKills; w.Deaths += winner.ReportedDeaths; w.Assists += winner.ReportedAssists;
        l.Kills += loser.ReportedKills; l.Deaths += loser.ReportedDeaths; l.Assists += loser.ReportedAssists;
        w.AccountXp += 100; // menang
        l.AccountXp += 60;  // kalah (partisipasi, biar Account Rank tetap naik)

        // Bonus "Butuh Pejuang" (docs/35): pemain faksi minoritas dapat tambahan XP (insentif seimbang).
        var fs = await factions.GetAsync(ct);
        if (fs.NeedsWarriors != 0 && fs.BonusPercent > 0)
        {
            if (w.Faction == fs.NeedsWarriors) w.AccountXp += 100 * fs.BonusPercent / 100;
            if (l.Faction == fs.NeedsWarriors) l.AccountXp += 60 * fs.BonusPercent / 100;
        }

        winner.IsWinner = true;
        winner.RatingDelta = w.Rating - wBefore;
        loser.RatingDelta = l.Rating - lBefore;
        winner.RatingBefore = wBefore;
        loser.RatingBefore = lBefore;

        // Proteksi demosi (docs/33 opsi 1): perbarui floor tier LAYER TAMPILAN. Rating/RD di atas tetap murni.
        StepFloor(w);
        StepFloor(l);

        ApplyDetailedStats(match, payloadJson);

        match.ScoreLeft = left;
        match.ScoreRight = right;
        match.Format = format;
        match.Status = MatchStatus.Finished;
        match.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // Proteksi demosi: hitung ulang state floor pemain dari rating baru (Ranks.StepTierFloor = logika murni).
    private void StepFloor(Data.Player p)
    {
        var (last, floor, left) = Ranks.StepTierFloor(
            p.LastRankedTierOrder, p.Rating, p.TierFloorOrder, p.TierFloorLeft, _game.DemotionGraceMatches);
        p.LastRankedTierOrder = last;
        p.TierFloorOrder = floor;
        p.TierFloorLeft = left;
    }

    // Berapa match Finished yang memuat KEDUA pemain ini dalam CollusionWindowMinutes terakhir.
    private async Task<int> RecentPairMatchesAsync(Guid a, Guid b, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddMinutes(-_game.CollusionWindowMinutes);
        var aMatches = await db.MatchPlayers
            .Where(mp => mp.PlayerId == a && mp.Match.Status == MatchStatus.Finished && mp.Match.EndedAt > since)
            .Select(mp => mp.MatchId)
            .ToListAsync(ct);
        if (aMatches.Count == 0) return 0;
        return await db.MatchPlayers.CountAsync(mp => mp.PlayerId == b && aMatches.Contains(mp.MatchId), ct);
    }

    // Tulis statistik akumulator per pemain + per hero dari payload yang sudah disepakati (docs/32 Lapisan 1).
    private void ApplyDetailedStats(Match match, string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson)) return;
        List<ReportPlayer>? rows;
        try { rows = JsonSerializer.Deserialize<List<ReportPlayer>>(payloadJson); }
        catch (JsonException) { return; }
        if (rows is null) return;

        foreach (var row in rows)
        {
            var mp = match.Players.FirstOrDefault(p => p.Slot == row.Slot);
            if (mp is null) continue;
            mp.BotKills = row.BotKills;
            mp.DamageDealt = row.DamageDealt; mp.DamageTaken = row.DamageTaken;
            mp.SkillsUsed = row.SkillsUsed; mp.SkillsHit = row.SkillsHit;
            mp.HitsBlocked = row.HitsBlocked; mp.Dodges = row.Dodges;
            mp.GuardCrushes = row.GuardCrushes; mp.Criticals = row.Criticals; mp.GearDrops = row.GearDrops;
            mp.FirstBlood = row.FirstBlood; mp.MaxKillStreak = row.MaxKillStreak;
            mp.MaxDeficit = row.MaxDeficit; mp.HeroSwitches = row.HeroSwitches;
            mp.HeroId = row.HeroId; // hero dominan
            mp.HasDetailedStats = true;

            // Finalize hanya sekali per match → tambah langsung (tanpa Clear navigasi yang belum di-load).
            foreach (var h in row.Heroes ?? new())
                db.MatchPlayerHeroes.Add(new MatchPlayerHero
                {
                    MatchId = mp.MatchId, PlayerId = mp.PlayerId,
                    HeroId = h.HeroId, PlayTicks = h.PlayTicks, Kills = h.Kills, Damage = h.Damage,
                });
        }
    }
}
