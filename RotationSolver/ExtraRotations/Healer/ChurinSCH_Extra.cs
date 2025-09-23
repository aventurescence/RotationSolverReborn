namespace RotationSolver.ExtraRotations.Healer;

public sealed partial class ChurinSCH
{
    #region Miscellaneous

    private int GetAoWBreakevenTargets()
    {
        int targets;
        if (!ArtOfWarPvE.EnoughLevel)
        {
            targets = 100; // AoW is not available yet
        }
        else if (!BroilMasteryTrait.EnoughLevel)
        {
            targets = 3 - DotOffsetMobs; // Broil is not available yet
        }
        else if (!ArtOfWarMasteryTrait.EnoughLevel)
        {
            targets = 4 - DotOffsetMobs; // Broil3 is not available yet
        }
        else
        {
            targets = 5 - DotOffsetMobs; // Broil3 is available
        }

        return targets;
    }

    private static bool IsShieldHpAboveThreshold(IBattleChara target, float thresholdPercent)
    {
        if (target == null) return false;

        float totalShieldHp = 0;
        foreach (var status in target.StatusList)
        {
            if (status.StatusId is (uint)StatusID.Galvanize or (uint)StatusID.Catalyze)
            {
                totalShieldHp += status.Param;
            }
        }

        return totalShieldHp > thresholdPercent * target.MaxHp;
    }

    public override bool CanHealSingleSpell
    {
        get
        {
            var aliveHealerCount = 0;
            var healers = PartyMembers.GetJobCategory(JobRole.Healer);
            foreach (var h in healers)
            {
                if (!h.IsDead)
                    aliveHealerCount++;
            }

            return base.CanHealSingleSpell && (GCDHeal || aliveHealerCount == 1);
        }
    }

    public override bool CanHealAreaSpell
    {
        get
        {
            var aliveHealerCount = 0;
            var healers = PartyMembers.GetJobCategory(JobRole.Healer);
            foreach (var h in healers)
            {
                if (!h.IsDead)
                    aliveHealerCount++;
            }

            return base.CanHealAreaSpell && (GCDHeal || aliveHealerCount == 1);
        }
    }

    private float ExpectedHpToLive12Seconds
    {
        get
        {
            var expectedHpToLive12Seconds = 1f;
            // Expect that players do ~ 10% of healer hp as DPS and ballpark to ensure we're not wasting dots on something that's going to die immediately based on nobody hitting it
            // This is still wildly overestimating mob survival in some contexts, but initial TTK estimates from RSR can be poor based on how far mobs were being kited
            if (UseBallparkTtk)
            {
                expectedHpToLive12Seconds = BallparkPercent * Player.MaxHp * PartyMemberCount * 12;
            }

            return expectedHpToLive12Seconds;
        }
    }

    private static int PartyMemberCount
    {
        get
        {
            var count = 0;
            foreach (var _ in PartyMembers)
            {
                count++;
            }

            return count;
        }
    }

    private readonly Queue<(long TimestampMs, uint Hp)> _sampleHp = new();
    private const int DamageWindowMs = 5000; // damage window (5 s)
    private const float DamageThresholdPercent = 0.05f; // minimum 5% HP taken inside the window
    private const int MinDistinctHits = 2; // minimum of 2 damage events in the window

    private void RecordHpSample()
    {
        var now = Environment.TickCount64;
        _sampleHp.Enqueue((now, Player.CurrentHp));

        //prune old samples
        while (_sampleHp.Count > 0 && now - _sampleHp.Peek().TimestampMs > DamageWindowMs)
        {
            _sampleHp.Dequeue();
        }
    }

    private bool TakingContinuousDamage()
    {
        if (_sampleHp.Count < MinDistinctHits)
        {
            return false;
        }

        // ensure the queue is pruned to the window on call
        var now = Environment.TickCount64;
        while (_sampleHp.Count > 0 && now - _sampleHp.Peek().TimestampMs > DamageWindowMs)
        {
            _sampleHp.Dequeue();
        }

        if (_sampleHp.Count < 2) return false;

        var oldest = _sampleHp.Peek();
        var newest = _sampleHp.Last();

        // total loss ratio over the window
        var lost = (float)(oldest.Hp - newest.Hp) / Player.MaxHp;
        if (lost >= DamageThresholdPercent)
            return true;

        //count distinct drops (separate hit events)
        var distinctDrops = 0;
        var prevHp = _sampleHp.First().Hp;

        foreach (var sample in _sampleHp)
        {
            if (sample.Hp < prevHp - (int)(0.05f * Player.MaxHp)) // consider >5% drops as a hit
            {
                distinctDrops++;
                prevHp = sample.Hp;
            }
            else
            {
                prevHp = Math.Min(prevHp, sample.Hp); // keep the lowest seen to detect further drops
            }
        }

        return distinctDrops >= MinDistinctHits;
    }

    private static bool CanSlidecast()
    {
        var remainCastTime = Player.TotalCastTime - Player.CurrentCastTime;
        if (Player.IsCasting)
        {
            return remainCastTime >= SlidecastWindow;
        }

        return WeaponRemain >= SlidecastWindow;
    }

    #endregion
}