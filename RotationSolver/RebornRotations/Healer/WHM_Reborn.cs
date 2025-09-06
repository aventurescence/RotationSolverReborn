using System.ComponentModel;

namespace RotationSolver.RebornRotations.Healer;

[Rotation("Reborn", CombatType.PvE, GameVersion = "7.31")]
[SourceCode(Path = "main/RebornRotations/Healer/WHM_Reborn.cs")]

public sealed class WHM_Reborn : WhiteMageRotation
{
    #region Config Options
    [RotationConfig(CombatType.PvE, Name = "Use Tincture/Gemdraught when about to use Presence of Mind")]
    public bool UseMedicine { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Enable Swiftcast Restriction Logic to attempt to prevent actions other than Raise when you have swiftcast")]
    public bool SwiftLogic { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use GCDs to heal. (Ignored if you are the only healer in party)")]
    public bool GCDHeal { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use DOT while moving even if it does not need refresh (disabling is a damage down)")]
    public bool DOTUpkeep { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Lily at max stacks/about to overcap.")]
    public bool UseLilyWhenFull { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Lily if about to overcap and no valid target nearby.")]
    public bool UseLilyDowntime { get; set; } = true;

    [Range(1, 13, ConfigUnitType.None, 1)]
    [RotationConfig(CombatType.PvE, Name = "Number of GCDs before you cap on blue lillies that overcap protection will consider 'near full'.")]
    public int LilyOvercapTime { get; set; } = 3;

    [RotationConfig(CombatType.PvE, Name = "Regen on Tank at 5 seconds remaining on Prepull Countdown.")]
    public bool UsePreRegen { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Divine Caress as soon as its available")]
    public bool UseDivine { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Use Asylum as soon as a single player heal (i.e. tankbusters) while moving, in addition to normal logic")]
    public bool AsylumSingle { get; set; } = false;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Minimum health threshold party member needs to be to use Benediction")]
    public float BenedictionHeal { get; set; } = 0.3f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "If a party member's health drops below this percentage, the Regen healing ability will not be used on them")]
    public float RegenHeal { get; set; } = 0.3f;

    [Range(0, 10000, ConfigUnitType.None, 100)]
    [RotationConfig(CombatType.PvE, Name = "Casting cost requirement for Thin Air to be used")]

    public float ThinAirNeed { get; set; } = 1000;

    [RotationConfig(CombatType.PvE, Name = "How to manage the last thin air charge")]
    public ThinAirUsageStrategy ThinAirLastChargeUsage { get; set; } = ThinAirUsageStrategy.ReserveLastChargeForRaise;

    public enum ThinAirUsageStrategy : byte
    {
        [Description("Use all thin air charges on expensive spells")]
        UseAllCharges,

        [Description("Reserve the last charge for raise")]
        ReserveLastChargeForRaise,

        [Description("Reserve the last charge for manual use")]
        ReserveLastCharge,
    }
    #endregion

    #region Countdown Logic
    protected override IAction? CountDownAction(float remainTime)
    {
        if (remainTime < StonePvE.Info.CastTime + CountDownAhead
            && StonePvE.CanUse(out IAction? act))
        {
            return act;
        }

        if (remainTime < 3 && UseBurstMedicine(out act))
        {
            return act;
        }

        if (UsePreRegen && remainTime <= 5 && remainTime > 3)
        {
            if (RegenPvE.CanUse(out act))
            {
                return act;
            }

            if (DivineBenisonPvE.CanUse(out act))
            {
                return act;
            }
        }
        return base.CountDownAction(remainTime);
    }
    #endregion

    #region oGCD Logic
    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        bool useLastThinAirCharge = ThinAirLastChargeUsage == ThinAirUsageStrategy.UseAllCharges || (ThinAirLastChargeUsage == ThinAirUsageStrategy.ReserveLastChargeForRaise && nextGCD == RaisePvE);
        if (((nextGCD is IBaseAction action && action.Info.MPNeed >= ThinAirNeed && IsLastAction() == IsLastGCD()) || ((MergedStatus.HasFlag(AutoStatus.Raise) || (nextGCD == RaisePvE)) && IsLastAction() == IsLastGCD())) &&
            ThinAirPvE.CanUse(out act, usedUp: useLastThinAirCharge))
        {
            return true;
        }

        if (Player.WillStatusEndGCD(2, 0, true, StatusID.DivineGrace) && DivineCaressPvE.CanUse(out act))
        {
            return true;
        }

        if (UseMedicine && !PresenceOfMindPvE.Cooldown.IsCoolingDown && UseBurstMedicine(out act))
        {
            return true;
        }

        if (nextGCD.IsTheSameTo(true, AfflatusRapturePvE, MedicaPvE, MedicaIiPvE, CureIiiPvE)
            && (MergedStatus.HasFlag(AutoStatus.HealAreaSpell) || MergedStatus.HasFlag(AutoStatus.HealSingleSpell)))
        {
            if (PlenaryIndulgencePvE.CanUse(out act))
            {
                return true;
            }
        }

        return base.EmergencyAbility(nextGCD, out act);
    }

    protected override bool GeneralAbility(IAction nextGCD, out IAction? act)
    {
        if (UseDivine && DivineCaressPvE.CanUse(out act))
        {
            return true;
        }
        return base.GeneralAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.TemperancePvE, ActionID.LiturgyOfTheBellPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        if ((TemperancePvE.Cooldown.IsCoolingDown && !TemperancePvE.Cooldown.WillHaveOneCharge(100))
            || (LiturgyOfTheBellPvE.Cooldown.IsCoolingDown && !LiturgyOfTheBellPvE.Cooldown.WillHaveOneCharge(160)))
        {
            return base.DefenseAreaAbility(nextGCD, out act);
        }

        if (TemperancePvE.CanUse(out act))
        {
            return true;
        }

        if (DivineCaressPvE.CanUse(out act))
        {
            return true;
        }

        if (LiturgyOfTheBellPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        return base.DefenseAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.DivineBenisonPvE, ActionID.AquaveilPvE)]
    protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
    {
        if ((DivineBenisonPvE.Cooldown.IsCoolingDown && !DivineBenisonPvE.Cooldown.WillHaveOneCharge(15))
            || (AquaveilPvE.Cooldown.IsCoolingDown && !AquaveilPvE.Cooldown.WillHaveOneCharge(52)))
        {
            return base.DefenseSingleAbility(nextGCD, out act);
        }

        if (DivineBenisonPvE.CanUse(out act))
        {
            return true;
        }

        if (AquaveilPvE.CanUse(out act))
        {
            return true;
        }

        return base.DefenseSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AsylumPvE)]
    protected override bool HealAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (AquaveilPvE.CanUse(out act))
        {
            return true;
        }
        return base.HealAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.BenedictionPvE, ActionID.AsylumPvE, ActionID.DivineBenisonPvE, ActionID.TetragrammatonPvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        if (BenedictionPvE.CanUse(out act) &&
            RegenPvE.Target.Target?.GetHealthRatio() < BenedictionHeal)
        {
            return true;
        }

        if (IsLastAction(ActionID.BenedictionPvE))
        {
            return base.HealSingleAbility(nextGCD, out act);
        }

        if (AsylumSingle && !IsMoving && AsylumPvE.CanUse(out act))
        {
            return true;
        }

        if (DivineBenisonPvE.CanUse(out act))
        {
            return true;
        }

        if (TetragrammatonPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }

        return base.HealSingleAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        if (InCombat)
        {
            if (PresenceOfMindPvE.CanUse(out act))
            {
                return true;
            }

            if (AssizePvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }
        }

        return base.AttackAbility(nextGCD, out act);
    }
    #endregion

    #region GCD Logic
    [RotationDesc(ActionID.AfflatusRapturePvE, ActionID.MedicaIiPvE, ActionID.CureIiiPvE, ActionID.MedicaPvE)]
    protected override bool HealAreaGCD(out IAction? act)
    {
        if ((HasSwift || IsLastAction(ActionID.SwiftcastPvE)) && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise))
        {
            return base.HealAreaGCD(out act);
        }

        if (AfflatusRapturePvE.CanUse(out act))
        {
            return true;
        }

        int hasMedica2 = 0;
        foreach (IBattleChara n in PartyMembers)
        {
            if (n.HasStatus(true, StatusID.MedicaIi))
            {
                hasMedica2++;
            }
        }

        int partyCount = 0;
        foreach (IBattleChara _ in PartyMembers)
        {
            partyCount++;
        }

        if (MedicaIiPvE.CanUse(out act) && hasMedica2 < partyCount / 2 && !IsLastAction(true, MedicaIiPvE))
        {
            return true;
        }

        if (CureIiiPvE.CanUse(out act))
        {
            return true;
        }

        if (MedicaPvE.CanUse(out act))
        {
            return true;
        }

        return base.HealAreaGCD(out act);
    }

    [RotationDesc(ActionID.AfflatusSolacePvE, ActionID.RegenPvE, ActionID.CureIiPvE, ActionID.CurePvE)]
    protected override bool HealSingleGCD(out IAction? act)
    {
        if ((HasSwift || IsLastAction(ActionID.SwiftcastPvE)) && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise))
        {
            return base.HealSingleGCD(out act);
        }

        if (AfflatusSolacePvE.CanUse(out act))
        {
            return true;
        }

        if (RegenPvE.CanUse(out act) && (RegenPvE.Target.Target?.GetHealthRatio() > RegenHeal))
        {
            return true;
        }

        if (CureIiPvE.CanUse(out act))
        {
            return true;
        }

        if (CurePvE.CanUse(out act))
        {
            return true;
        }

        return base.HealSingleGCD(out act);
    }

    protected override bool GeneralGCD(out IAction? act)
    {
        if (HasThinAir && MergedStatus.HasFlag(AutoStatus.Raise))
        {
            return RaiseGCD(out act);
        }

        if ((HasSwift || IsLastAction(ActionID.SwiftcastPvE)) && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise))
        {
            return base.GeneralGCD(out act);
        }

        //if (NotInCombatDelay && RegenDefense.CanUse(out act)) return true;

        if (AfflatusMiseryPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        bool liliesNearlyFull = Lily == 2 && LilyTime < LilyOvercapTime;
        bool liliesFullNoBlood = Lily == 3;
        if (AfflatusMiseryPvE.EnoughLevel && UseLilyWhenFull && (liliesNearlyFull || liliesFullNoBlood) && AfflatusMiseryPvE.EnoughLevel && BloodLily < 3)
        {
            if (AfflatusRapturePvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }

            if (AfflatusSolacePvE.CanUse(out act))
            {
                return true;
            }
        }

        if (GlareIvPvE.CanUse(out act))
        {
            return true;
        }

        if (HolyPvE.CanUse(out act))
        {
            return true;
        }

        if (AeroPvE.CanUse(out act))
        {
            return true;
        }

        if (StonePvE.CanUse(out act))
        {
            return true;
        }

        if (AfflatusMiseryPvE.EnoughLevel && UseLilyDowntime && (liliesNearlyFull || liliesFullNoBlood))
        {
            if (AfflatusRapturePvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }

            if (AfflatusSolacePvE.CanUse(out act))
            {
                return true;
            }
        }

        if (AeroPvE.CanUse(out act, skipStatusProvideCheck: DOTUpkeep))
        {
            return true;
        }

        return base.GeneralGCD(out act);
    }
    #endregion

    #region Extra Methods
    public override bool CanHealSingleSpell
    {
        get
        {
            int aliveHealerCount = 0;
            IEnumerable<IBattleChara> healers = PartyMembers.GetJobCategory(JobRole.Healer);
            foreach (IBattleChara h in healers)
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
            int aliveHealerCount = 0;
            IEnumerable<IBattleChara> healers = PartyMembers.GetJobCategory(JobRole.Healer);
            foreach (IBattleChara h in healers)
            {
                if (!h.IsDead)
                    aliveHealerCount++;
            }

            return base.CanHealAreaSpell && (GCDHeal || aliveHealerCount == 1);
        }
    }
    #endregion
}