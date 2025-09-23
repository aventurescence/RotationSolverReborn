namespace RotationSolver.ExtraRotations.Healer;

public sealed partial class ChurinSCH
{
    #region GCD spells

    private bool TryNoBioIi(out IAction? act)
    {
        act = null;
        if (BioIiPvE.EnoughLevel)
        {
            return false;
        }

        if (BioPvE.CanUse(out act) && BioPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds)
        {
            return true;
        }

        return RuinPvE.CanUse(out act) || BioPvE.CanUse(out act, skipTTKCheck: true);
    }

    private bool TryNoArtOfWar(out IAction? act)
    {
        act = null;
        if (ArtOfWarPvE.EnoughLevel)
        {
            return false;
        }

        if (BioIiPvE.CanUse(out act) && BioIiPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds)
        {
            return true; // No better options still, and configured TTK should cover whether we want to use it
        }

        return RuinPvE.CanUse(out act);
    }

    private bool TryNoBroilMastery(out IAction? act)
    {
        act = null;
        if (BroilMasteryTrait.EnoughLevel)
        {
            return false;
        }

        if (BioIiPvE.CanUse(out act) && NearbyHostiles < GetAoWBreakevenTargets() &&
            BioIiPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds)
        {
            return true; // This is better against 2 targets IFF it will last >= 24 seconds
        }

        if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && NearbyHostiles > 0)
        {
            return true;
        }

        return RuinPvE.CanUse(out act);
    }

    private bool TryNoBroilMasteryIi(out IAction? act)
    {
        act = null;
        if (BroilMasteryIiTrait.EnoughLevel)
        {
            return false;
        }

        if (BioIiPvE.CanUse(out act) && NearbyHostiles < GetAoWBreakevenTargets() &&
            BioIiPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds)
        {
            return true; // This is better against 3 targets IFF it will last >= 24 seconds
        }

        if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && NearbyHostiles > 1)
        {
            return true;
        }

        if (BroilPvE.CanUse(out act))
        {
            return true;
        }

        return ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && NearbyHostiles > 0;
    }

    private bool TryNoBroilIii(out IAction? act)
    {
        act = null;
        if (BroilIiiPvE.EnoughLevel)
        {
            return false;
        }

        if (BioIiPvE.CanUse(out act) && NearbyHostiles < GetAoWBreakevenTargets()
                                     && BioIiPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds)
        {
            return true;
        }

        if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && NearbyHostiles > 1)
        {
            return true;
        }

        return BroilIiPvE.CanUse(out act);
    }

    private bool TryNoBroilIv(out IAction? act)
    {
        act = null;
        if (BroilIvPvE.EnoughLevel)
        {
            return false;
        }

        if (BiolysisPvE.CanUse(out act) && NearbyHostiles < GetAoWBreakevenTargets() &&
            BiolysisPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds)
        {
            return true; // This is better against 4 targets IFF it will last >= 27 seconds
        }

        if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && NearbyHostiles > 1)
        {
            return true;
        }

        return BroilIiiPvE.CanUse(out act);
    }

    private bool TryUseBiolysis(out IAction? act)
    {
        act = null;
        if (IsBurst && ChainStratagemPvE.Cooldown.ElapsedAfter(17))
        {
            return BiolysisPvE.CanUse(out act, skipStatusProvideCheck: true);
        }

        return BiolysisPvE.CanUse(out act)
               && NearbyHostiles < GetAoWBreakevenTargets()
               && BiolysisPvE.Target.Target != null
               && BiolysisPvE.Target.Target.CurrentHp >= ExpectedHpToLive12Seconds;
    }

    private bool TryUseRuinIi(out IAction? act)
    {
        act = null;
        if (MovingTime < 0)
        {
            return false;
        }

        if (MovingTime > RuinTime)
        {
            if (!CanSlidecast())
            {
                return RuinIiPvE.CanUse(out act);
            }
        }

        return false;
    }

    private bool TryUseArtOfWar(out IAction? act)
    {
        act = null;
        return NearbyHostiles > 1 && ArtOfWarPvE.CanUse(out act, skipAoeCheck: true);
    }

    #endregion

}