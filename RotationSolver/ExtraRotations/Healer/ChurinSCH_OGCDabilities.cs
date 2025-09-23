namespace RotationSolver.ExtraRotations.Healer;

public sealed partial class ChurinSCH
{
    // Store the last healing chosen by TryUseBestHealByMissingHp for UI/display purposes
    private string? OptimizedHealChoice { get; set; }

    #region Offensive Abilities

    private bool TryUseChainStratagem(out IAction? act)
    {
        act = null;
        if (!AllowedToBurst || ChainStratagemPvE.Cooldown.IsCoolingDown || !ChainStratagemPvE.EnoughLevel)
        {
            return false;
        }

        return AllowedToBurst && !CombatElapsedLessGCD(2) && ChainStratagemPvE.CanUse(out act);
    }

    private bool TryUseEnergyDrain(out IAction? act)
    {
        act = null;
        if (!HasAetherflow || ChainStratagemPvE.EnoughLevel && ChainStratagemPvE.Cooldown.WillHaveOneChargeGCD(10))
        {
            return false;
        }

        if ((AetherflowPvE.Cooldown.IsCoolingDown || DissipationPvE.Cooldown.IsCoolingDown)
            && (AetherflowPvE.Cooldown.WillHaveOneChargeGCD(4) || DissipationPvE.Cooldown.WillHaveOneChargeGCD(4))
            && EnergyDrainPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }

        return IsBurst && EnergyDrainPvE.CanUse(out act, usedUp: true);
    }

    private bool TryUseBanefulImpaction(out IAction? act)
    {
        act = null;
        if (!BanefulImpactionPvE.EnoughLevel || !HasImpactImminent)
        {
            return false;
        }

        if (AetherflowPvE.Cooldown.IsCoolingDown && !AetherflowPvE.Cooldown.WillHaveOneChargeGCD(5)
                                                 && DissipationPvE.Cooldown.IsCoolingDown &&
                                                 !DissipationPvE.Cooldown.WillHaveOneChargeGCD(5)
                                                 && HasAetherflow)
        {
            return BanefulImpactionPvE.CanUse(out act);
        }

        return false;
    }

    #endregion

    #region Healing and Defensive Abilities

    private bool TryUseRecitation(IAction nextGCD, out IAction? act)
    {
        act = null;
        _ = nextGCD; // consume parameter to avoid unused-parameter warning
        if (!RecitationPvE.EnoughLevel || RecitationPvE.Cooldown.IsCoolingDown)
        {
            return false;
        }

        if ((!IndomitabilityPvE.Cooldown.IsCoolingDown || IndomitabilityPvE.Cooldown.WillHaveOneChargeGCD(1)) &&
            PartyMembersAverHP < HealthAreaAbility
            || PressPanicButton)
        {
            return RecitationPvE.CanUse(out act);
        }

        // Big Excog the tank if they look dangerous
        if (!ExcogitationPvE.Cooldown.IsCoolingDown)
        {
            var tankNeedsExcog = false;
            var tanks = PartyMembers.GetJobCategory(JobRole.Tank);
            foreach (var member in tanks)
                if (member.GetHealthRatio() <= 0.5f && !member.NoNeedHealingInvuln())
                {
                    tankNeedsExcog = true;
                    break;
                }

            if (tankNeedsExcog)
            {
                return RecitationPvE.CanUse(out act);
            }
        }

        if (NoAreaOGCD && ReciteSuccor && PartyMembersAverHP <= HealthAreaAbility)
        {
            // Or if we're desperate and hard casting Adlo (or potentially succor)
            var recitationActions = ReciteSuccor
                ? new IAction[] { AccessionPvE, ConcitationPvE, SuccorPvE, AdloquiumPvE, ManifestationPvE }
                : new IAction[] { AdloquiumPvE, ManifestationPvE };
            return nextGCD.IsTheSameTo(true, recitationActions) && RecitationPvE.CanUse(out act);
        }

        return false;
    }

    private bool TryUseIndomitability(out IAction? act)
    {
        act = null;
        if (!IndomitabilityPvE.EnoughLevel || IndomitabilityPvE.Cooldown.IsCoolingDown ||
            (!HasAetherflow && !HasRecitation)) return false;

        if (HasRecitation && IndomitabilityPvE.CanUse(out act)) return true;

        return HasAetherflow && IndomitabilityPvE.CanUse(out act);
    }

    private bool TryUseAetherpact(out IAction? act)
    {
        act = null;
        // Check if any party member has Fey Union status
        var haveLink = false;
        foreach (var p in PartyMembers)
        {
            if (p.HasStatus(true, StatusID.FeyUnion_1223))
            {
                haveLink = true;
                break;
            }
        }

        // If we have fairy gauge to spend and don't have a link, we should use this resource first
        if (AetherpactPvE.CanUse(out act) && FairyGauge >= LinkFairyGauge && !haveLink &&
            AetherpactPvE.Target.Target.GetHealthRatio() <= AetherpactMinimum)
        {
            return true;
        }

        if (!HasAetherflow && !HasRecitation || RecitationPvE.Cooldown.IsCoolingDown)
        {
            if (!haveLink && FairyGauge > 20 && AetherpactPvE.CanUse(out act))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryRemoveAetherpact(out IAction? act)
    {
        act = null;
        foreach (var p in PartyMembers)
        {
            if (p.HasStatus(true, StatusID.FeyUnion_1223) && p.HasStatus(false, StatusID.LivingDead))
            {
                return AetherpactPvE.CanUse(out act);
            }
        }

        foreach (var item in PartyMembers)
        {
            if (!item.HasStatus(true, StatusID.FeyUnion_1223)) continue;

            if (item.GetHealthRatio() >= AetherpactRemove)
            {
                act = AetherpactPvE;
                return true;
            }
        }

        return false;
    }

    private bool TryUseSacredSoil(out IAction? act)
    {
        act = null;
        if (!HasAetherflow || SacredSoilPvE.Cooldown.IsCoolingDown)
        {
            return false;
        }

        return SacredSoilPvE.CanUse(out act);
    }

    private bool TryUseDeploymentTactics(out IAction? act)
    {
        act = null;
        if (!DeploymentTacticsPvE.EnoughLevel || !DeploymentTacticsPvE.IsEnabled) return false;

        if (DeploymentTacticsPvE.EnoughLevel && InCombat)
        {
            return DeploymentTacticsUsage switch
            {
                DeploymentTacticsUsageStrategy.CatalyzeOnly when TargetHasCatalyze => DeploymentTacticsPvE.CanUse(
                    out act),
                DeploymentTacticsUsageStrategy.CatalyzeOrGalvanize when TargetHasCatalyze ||
                                                                        TargetHasGalvanize &&
                                                                        IsShieldHpAboveThreshold(
                                                                            DeploymentTacticsPvE.Target.Target, 0.2f)
                    => DeploymentTacticsPvE.CanUse(out act),
                _ => false
            };
        }

        return false;
    }

    private bool TryUseEmergencyTactics(IAction nextGCD, out IAction? act)
    {
        act = null;
        _ = nextGCD; // consume parameter to avoid unused-parameter warning
        if (!EmergencyTacticsPvE.EnoughLevel || !EmergencyTacticsPvE.IsEnabled) return false;

        // Only use emergency tactics if we're healing from raid wide damage or multiple members need to be healed for doom
        if (nextGCD.IsTheSameTo(false, SuccorPvE, ConcitationPvE, AccessionPvE) && EmergencyTacticsPvE.CanUse(out act))
        {
            var count = 0;
            foreach (var member in PartyMembers)
                if (member.DistanceToPlayer() <= 15)
                    if (member.DoomNeedHealing() || member.GetHealthRatio() < CriticalHealThreshold ||
                        TakingContinuousDamage() || IsCastingMultiHit)
                    {
                        count++;
                        if (count > 1) break;
                    }

            if (count > 1) return true;
        }

        return false;
    }

    private bool TryUseAetherflow(out IAction? act)
    {
        act = null;
        if (!AetherflowPvE.EnoughLevel || HasAetherflow) return false;

        if ((AetherflowStrategyUse == AetherflowStrategy.AetherflowFirst
             || !DissipationPvE.EnoughLevel) && CombatElapsedLessGCD(2))
        {
            return AetherflowPvE.CanUse(out act);
        }

        if (DissipationPvE.EnoughLevel && AetherflowStrategyUse == AetherflowStrategy.DissipationFirst
                                       && CombatElapsedLessGCD(2))
        {
            return TryUseDissipation(out act);
        }

        return !HasAetherflow && AetherflowPvE.CanUse(out act);
    }

    private bool TryUseDissipation(out IAction? act)
    {
        act = null;
        if (!DissipationPvE.EnoughLevel || HasAetherflow) return false;

        if (AetherflowStrategyUse == AetherflowStrategy.DissipationFirst && CombatElapsedLessGCD(2))
        {
            return DissipationPvE.CanUse(out act);
        }

        return !HasAetherflow && AetherflowPvE.Cooldown.IsCoolingDown && DissipationPvE.CanUse(out act);
    }

    private bool TryUseExcogitation(out IAction? act)
    {
        act = null;
        if (!ExcogitationPvE.EnoughLevel || ExcogitationPvE.Cooldown.IsCoolingDown || !HasAetherflow && !HasRecitation)
        {
            return false;
        }

        // Check if any tank matches Excogitation target
        var tankHasExcogTarget = false;
        var tanks = PartyMembers.GetJobCategory(JobRole.Tank);
        foreach (var member in tanks)
        {
            if (member == ExcogitationPvE.Target.Target && member.GetHealthRatio() <= 0.5f &&
                !member.NoNeedHealingInvuln())
            {
                tankHasExcogTarget = true;
                break;
            }
        }

        if (HasRecitation && tankHasExcogTarget && ExcogitationPvE.CanUse(out act))
        {
            return true;
        }

        // Otherwise we'll spend aether charges; we didn't burn it on the tank above so use excog based on oGCD heal toggle
        return ExcogitationPvE.Target.Target.GetHealthRatio() <= 0.5f && ExcogitationPvE.CanUse(out act);
    }

    // Selects the most appropriate Scholar heal/HoT/shield based on each party member's missing HP and tries to use it.

    // Resolve an action object by its action Name (used by HealerData table keys)

    #endregion
}