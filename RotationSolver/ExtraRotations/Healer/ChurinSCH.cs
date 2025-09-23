using System.ComponentModel;
using ECommons.ExcelServices;

namespace RotationSolver.ExtraRotations.Healer;

[Rotation("Churin SCH", CombatType.PvE, GameVersion = "7.3",
    Description =
        "It’s okay to judge a book by its cover, especially if that cover is made from questionable leather.")]
[SourceCode(Path = "main/ExtraRotations/Healer/ChurinSCH.cs")]
[ExtraRotation]
public sealed partial class ChurinSCH : ScholarRotation
{
    #region Properties

    private const float CriticalHealThreshold = 0.4f;
    private const float SlidecastWindow = 0.5f;

    #region Status Properties
    private static bool HasChainStratagem => AllHostileTargets != null &&
                                             AllHostileTargets.Any(t => t.HasStatus(true, StatusID.ChainStratagem));
    private static bool AllowedToBurst => MergedStatus.HasFlag(AutoStatus.Burst);
    private bool RaiseRestriction => HasSwift && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise);
    private new bool IsBurst => ChainStratagemPvE.EnoughLevel && HasChainStratagem;
    private static bool HasEarthlyStar => PartyMembers.Any(h => h.IsJobs(Job.AST) && h.HasStatus(false, StatusID.EarthlyDominance, StatusID.GiantDominance));
    private static bool HasLilybell => PartyMembers.Any(h => h.IsJobs(Job.WHM) && h.HasStatus(false, StatusID.LiturgyOfTheBell));
    private static bool HasMacrocosmos => PartyMembers.Any(m => m.HasStatus(false, StatusID.Macrocosmos, StatusID.Macrocosmos_3989));
    private static bool EnoughMitigation => GetCurrentMitigationPercent() >= 0.50f && PartyMembersAverHP >= 0.9f||
                                            GetCurrentMitigationPercent() >= 0.45f && PartyMembersAverHP >= 0.95f;
    private bool TargetHasCatalyze => DeploymentTacticsPvE.Target.Target.IsParty() &&
                                      DeploymentTacticsPvE.Target.Target.HasStatus(true, StatusID.Catalyze);
    private bool TargetHasGalvanize => DeploymentTacticsPvE.Target.Target.IsParty() &&
                                       DeploymentTacticsPvE.Target.Target.HasStatus(true, StatusID.Galvanize);
    private static bool IsCalm => (HasEarthlyStar || HasLilybell || HasMacrocosmos);
    private bool PressPanicButton =>
        (InCombat && IsCastingMultiHit || AutoStatus == AutoStatus.DefenseArea && NoMitigations && !EnoughMitigation) &&
        CanSpreadLo;
    #endregion
    #region Hp and Mp Properties
    private bool RunningOutOfMp => CurrentMp < EmergencyHealingMpThreshold
                                   && (!AetherflowPvE.Cooldown.WillHaveOneChargeGCD(3)
                                       || !Player.HasStatus(true, StatusID.LucidDreaming));
    #endregion
    #region Cooldown related Properties
    private bool CanSpreadLo => (RecitationPvE.Cooldown.HasOneCharge || HasRecitation ||
                                 RecitationPvE.Cooldown.WillHaveOneChargeGCD(1))
                                && (DeploymentTacticsPvE.Cooldown.HasOneCharge
                                    || DeploymentTacticsPvE.Cooldown.WillHaveOneChargeGCD(1));
    private bool NoSingleTargetAetherflowAbility => ExcogitationOnCooldown && OutOfBullets;
    private bool NoAOEAetherflowAbility => OutOfBullets && IndomitabilityOnCooldown && SacredSoilOnCooldown;
    private bool OutOfBullets =>
        !HasAetherflow && AetherflowOnCooldown && DissipationOnCooldown && RecitationOnCooldown;
    private bool NoFairyAbilities => CantUseSeraphism && CantSummonSeraph && CantUseWhisperingDawn && CantUseFeyIllumination && CantUseFeyBlessing && CantUseFeyUnion;
    private bool AetherflowOnCooldown => AetherflowPvE.Cooldown.IsCoolingDown && !AetherflowPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool DissipationOnCooldown => DissipationPvE.Cooldown.IsCoolingDown && !DissipationPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool RecitationOnCooldown => RecitationPvE.Cooldown.IsCoolingDown && !RecitationPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool ExpedientOnCooldown => ExpedientPvE.Cooldown.IsCoolingDown && !ExpedientPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool ProtractionOnCooldown => ProtractionPvE.Cooldown.IsCoolingDown && !ProtractionPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool ExcogitationOnCooldown => ExcogitationPvE.Cooldown.IsCoolingDown && !ExcogitationPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool IndomitabilityOnCooldown => IndomitabilityPvE.Cooldown.IsCoolingDown && !IndomitabilityPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool SacredSoilOnCooldown => SacredSoilPvE.Cooldown.IsCoolingDown && !SacredSoilPvE.Cooldown.WillHaveOneChargeGCD(1);
    private bool CantUseSeraphism => SeraphismPvE.Cooldown.IsCoolingDown && !SeraphismPvE.Cooldown.WillHaveOneChargeGCD(1) || FairyDismissed || HasDissipation;
    private bool CantSummonSeraph => SummonSeraphPvE.Cooldown.IsCoolingDown && !SummonSeraphPvE.Cooldown.WillHaveOneChargeGCD(1) || FairyDismissed || HasDissipation;
    private bool CantUseWhisperingDawn => WhisperingDawnPvE.Cooldown.IsCoolingDown && !WhisperingDawnPvE.Cooldown.WillHaveOneChargeGCD(1) || FairyDismissed || HasDissipation;
    private bool CantUseFeyIllumination => FeyIlluminationPvE.Cooldown.IsCoolingDown && !WhisperingDawnPvE.Cooldown.WillHaveOneChargeGCD(1) || FairyDismissed || HasDissipation;
    private bool CantUseFeyBlessing => FeyBlessingPvE.Cooldown.IsCoolingDown && !FeyBlessingPvE.Cooldown.WillHaveOneChargeGCD(1) || FairyDismissed || HasDissipation;
    private static bool CantUseFeyUnion => FairyGauge < 20 || FairyDismissed || HasDissipation;

    private bool NoSingleTargetOGCD => NoSingleTargetAetherflowAbility && CantUseFeyUnion && ProtractionOnCooldown;
    private bool NoAreaOGCD => NoAOEAetherflowAbility && NoFairyAbilities;
    private bool NoMitigations => ExpedientOnCooldown && !HasAetherflow && AetherflowOnCooldown && DissipationOnCooldown && SacredSoilOnCooldown && CantUseFeyIllumination;
    private static bool CanWeave => !ECommons.GameHelpers.Player.IsAnimationLocked && WeaponRemain >= AnimationLock;
    private static int NearbyHostiles => NumberOfHostilesInRangeOf(5);

    #endregion

    #endregion

    #region Config Options

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE,
        Name = "Remove Aetherpact if the linked party member's HP is above this percentage")]
    private float AetherpactRemove { get; set; } = 0.9f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE,
        Name = "Do not start Aetherpact if the target's HP is above this percentage (prevents toggling)")]
    private float AetherpactMinimum { get; set; } = 0.8f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Estimated percent of HP dealt as DPS for ballpark calculations")]
    private float BallparkPercent { get; set; } = 0.08f;

    [Range(0, 5, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Seconds you must be moving before Ruin II will be used")]
    private float RuinTime { get; set; } = 0f;

    [Range(0, 10000, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE,
        Name = "Minimum MP before prioritizing emergency healing and rezzing (willing to use Seraphism sooner)")]
    private int EmergencyHealingMpThreshold { get; set; } = 2000;

    [Range(0, 2, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE,
        Name = "Number of fewer mobs required to favor AoW spam over Bio (0 = use Bio if break-even below 30s)")]
    private int DotOffsetMobs { get; set; } = 1;

    [Range(0, 100, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Minimum Fairy Gauge required before prioritizing Fey Union (link)")]
    private int LinkFairyGauge { get; set; } = 70;

    [RotationConfig(CombatType.PvE, Name = "Enable Swiftcast restriction: only allow Raise while Swiftcast is active")]
    private bool SwiftLogic { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use GCDs to heal. (Ignored if you are the only healer in party)")]
    private bool GCDHeal { get; set; } = true;

    [RotationConfig(CombatType.PvE,
        Name = "Use SpreadLo in the opener (Recitation + Protraction + Adloquium + Deployment Tactics)")]
    private bool UseSpreadLoInOpener { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Recitation with Succor, Concitation, or Accession")]
    private bool ReciteSuccor { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Sacred Soil's regeneration as a healing effect")]
    private bool SacredSoilHeal { get; set; } = false;

    [RotationConfig(CombatType.PvE,
        Name = "Enable ballpark DoT time-to-kill estimator (in addition to normal TTK configs)")]
    private bool UseBallparkTtk { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "How to use Deployment Tactics")]
    private DeploymentTacticsUsageStrategy DeploymentTacticsUsage { get; set; } =
        DeploymentTacticsUsageStrategy.CatalyzeOnly;

    private enum DeploymentTacticsUsageStrategy : byte
    {
        [Description("Use when a party member has Catalyze status")]
        CatalyzeOnly,

        [Description("Use when a party member has Catalyze or Galvanize status")]
        CatalyzeOrGalvanize,
    }

    [RotationConfig(CombatType.PvE, Name = "Aetherflow or Dissipation opener?")]
    private AetherflowStrategy AetherflowStrategyUse { get; set; } =
        AetherflowStrategy.AetherflowFirst;

    private enum AetherflowStrategy : byte
    {
        [Description("Use Aetherflow")] AetherflowFirst,

        [Description("Use Dissipation")] DissipationFirst,
    }

    #endregion

    #region Countdown Logic

    protected override IAction? CountDownAction(float remainTime)
    {
        if (SummonEosPvE.CanUse(out var act)
            || remainTime <= BroilIvPvE.Info.CastTime + 0.05f && BroilIvPvE.CanUse(out act)
            || remainTime <= 0 && UseBurstMedicine(out act)
            || remainTime <= 0 && BiolysisPvE.CanUse(out act)
            || remainTime is < 4 and > 3 && UseSpreadLoInOpener && DeploymentTacticsPvE.CanUse(out act)
            || remainTime is < 7 and > 6 && UseSpreadLoInOpener && AdloquiumPvE.CanUse(out act)
            || remainTime is < 10 and > 9 && UseSpreadLoInOpener && ProtractionPvE.CanUse(out act)
            || remainTime <= 15 && UseSpreadLoInOpener && RecitationPvE.CanUse(out act))
        {
            return act;
        }

        return base.CountDownAction(remainTime);
    }

    #endregion

    #region oGCD Logic

    [RotationDesc(ActionID.ChainStratagemPvE, ActionID.BanefulImpactionPvE, ActionID.AetherflowPvE, ActionID.DissipationPvE, ActionID.DeploymentTacticsPvE,ActionID.EmergencyTacticsPvE, ActionID.RecitationPvE)]
    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        RecordHpSample();

        if (!CanWeave)
        {
            return base.EmergencyAbility(nextGCD, out act);
        }

        if (TryUseAetherflow(out act))
        {
            return true;
        }

        if (TryUseDissipation(out act))
        {
            return true;
        }

        if (TryUseChainStratagem(out act))
        {
            return true;
        }

        if (TryUseBanefulImpaction(out act))
        {
            return true;
        }

        if (TryUseDeploymentTactics(out act))
        {
            return true;
        }

        if (TryUseEmergencyTactics(nextGCD, out act))
        {
            return true;
        }

        if (TryUseRecitation(nextGCD, out act))
        {
            return true;
        }

        if (TryRemoveAetherpact(out act))
        {
            return true;
        }

        return SeraphTime < 3 && ConsolationPvE.CanUse(out act, usedUp: true)
               || base.EmergencyAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.SummonSeraphPvE, ActionID.ConsolationPvE, ActionID.SacredSoilPvE, ActionID.IndomitabilityPvE,
        ActionID.WhisperingDawnPvE, ActionID.FeyBlessingPvE, ActionID.SeraphismPvE)]
    protected override bool HealAreaAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        if ((IsCalm || !CanWeave) && PartyMembersAverHP > HealthAreaAbility)
        {
            return base.HealAreaAbility(nextGCD, out act);
        }

        if (TryUseOptimizedHeal(out act))
        {
            return true;
        }

        if (!HasDissipation && FeyBlessingPvE.CanUse(out act) && FairyHelper.PartyInRange)
        {
            return true;
        }

        // Always try to use Indomitability if we've just used Recitation and are area healing
        if ((IsLastAbility(ActionID.RecitationPvE) || HasRecitation) && !PressPanicButton && TryUseIndomitability(out act))
        {
            return true;
        }

        // Always use Consolation if we have a Seraph out and need area healing/shielding
        if (ConsolationPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }

        if (PartyMembersAverHP is > 0.75f and < 1f && WhisperingDawnPvE.CanUse(out act))
        {
            return true;
        }

        if (WhisperingDawnPvE.Cooldown.IsCoolingDown && FeyBlessingPvE.Cooldown.IsCoolingDown)
        {
            if (SummonSeraphPvE.CanUse(out act))
            {
                return true;
            }
        }

        // Once we have enhanced regen on Sacred Soil, this becomes incredibly efficient for healing (500 potency HoT) in addition to being a defensive ability
        if (EnhancedSacredSoilTrait.EnoughLevel && SacredSoilHeal && TryUseSacredSoil(out act))
        {
            return true;
        }

        // Seraphism is really good, but we want to save it if we can, and should alternate it with Summon Seraph defensively outside the hardest content
        if ((SummonSeraphPvE.Cooldown.IsCoolingDown || CurrentMp <= EmergencyHealingMpThreshold || IsCastingMultiHit) && SeraphismPvE.CanUse(out act))
        {
            return true;
        }
        // Lower Priority Indomitability without Recitation
        return TryUseIndomitability(out act) || base.HealAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AetherpactPvE, ActionID.ExcogitationPvE, ActionID.LustratePvE, ActionID.SacredSoilPvE,
        ActionID.ProtractionPvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        if ((!CanWeave || IsCalm) && PartyMembersMinHP > HealthSingleAbility)
        {
            return base.HealSingleAbility(nextGCD, out act);
        }


        if (TryRemoveAetherpact(out act))
        {
            return true;
        }

        if (TryUseOptimizedHeal(out act))
        {
            return true;
        }

        if (TryUseExcogitation(out act))
        {
            return true;
        }

        if (TryUseAetherpact(out act))
        {
            return true;
        }

        if (ProtractionPvE.CanUse(out act))
        {
            return true;
        }

        if (EnhancedSacredSoilTrait.EnoughLevel && SacredSoilHeal && TryUseSacredSoil(out act))
        {
            return true;
        }

        return PartyMembersMinHP < 0.4f && LustratePvE.CanUse(out act) || base.HealSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.FeyIlluminationPvE, ActionID.ExpedientPvE, ActionID.SummonSeraphPvE, ActionID.ConsolationPvE,
        ActionID.SacredSoilPvE, ActionID.SeraphismPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        // Check mitigation based on an incoming damage type to decide on a single defense ability
        if (EnoughMitigation || !CanWeave || NoMitigations)
        {
            return base.DefenseAreaAbility(nextGCD, out act);
        }

        if (PressPanicButton)
        {
            if (TryUseRecitation(nextGCD, out act))
            {
                return true;
            }
        }

        if ((SummonSeraphPvE.Cooldown.IsCoolingDown || CurrentMp <= EmergencyHealingMpThreshold || IsCastingMultiHit)
            && SeraphismPvE.CanUse(out act))
        {
            return true;
        }

        if (TryUseSacredSoil(out act))
        {
            return true;
        }

        if (ExpedientPvE.CanUse(out act))
        {
            return true;
        }

        if (ConsolationPvE.CanUse(out act))
        {
            return true;
        }

        if (IsMagicalDamageIncoming() && FeyIlluminationPvE.CanUse(out act))
        {
            return true;
        }

        if (WhisperingDawnPvE.Cooldown.IsCoolingDown && FeyBlessingPvE.Cooldown.IsCoolingDown)
        {
            if (SummonSeraphPvE.CanUse(out act))
            {
                return true;
            }
        }

        return base.DefenseAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ProtractionPvE, ActionID.ExcogitationPvE, ActionID.SacredSoilPvE)]
    protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        if (!CanWeave)
        {
            return base.DefenseSingleAbility(nextGCD, out act);
        }

        if (ProtractionPvE.CanUse(out act))
        {
            return true;
        }

        if (TryUseExcogitation(out act))
        {
            return true;
        }

        return TryUseSacredSoil(out act) || base.DefenseSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ExpedientPvE)]
    protected override bool SpeedAbility(IAction nextGCD, out IAction? act)
    {
        // Should be using this manually in high-end content, but if it's enabled, let's use it
        if (InCombat && ExpedientPvE.CanUse(out act, usedUp: true))
        {
            return true;
        }

        return base.SpeedAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        return TryUseEnergyDrain(out act) || base.AttackAbility(nextGCD, out act);
    }

    #endregion

    #region GCD Logic

    [RotationDesc(ActionID.SuccorPvE, ActionID.ConcitationPvE, ActionID.AccessionPvE)]
    protected override bool HealAreaGCD(out IAction? act)
    {
        act = null;
        if (RaiseRestriction || (!NoAreaOGCD || IsCalm) && PartyMembersAverHP > HealthAreaSpell)
        {
            return base.HealAreaGCD(out act);
        }

        if (TryUseOptimizedHeal(out act))
        {
            return true;
        }

        // If Emergency Tactics is up, we are using Succor for raidwide recovery, not shields
        if (HasEmergencyTactics && SuccorPvE.CanUse(out act, skipStatusProvideCheck: true))
        {
            return true;
        }

        // Only have all 3 checks in case players have added their own custom configurations based on the current level.
        if (AccessionPvE.CanUse(out act, skipCastingCheck: true))
        {
            return true;
        }

        if (ConcitationPvE.CanUse(out act))
        {
            return true;
        }

        return SuccorPvE.CanUse(out act) || base.HealAreaGCD(out act);
    }

    [RotationDesc(ActionID.AdloquiumPvE, ActionID.ManifestationPvE, ActionID.PhysickPvE)]
    protected override bool HealSingleGCD(out IAction? act)
    {
        act = null;
        if (RaiseRestriction || (!NoSingleTargetOGCD|| IsCalm) && PartyMembersMinHP > HealthSingleSpell)
        {
            return base.HealSingleGCD(out act);
        }

        if (TryUseOptimizedHeal(out act))
        {
            return true;
        }

        if (PressPanicButton && HasRecitation && AdloquiumPvE.CanUse(out act))
        {
            return true;
        }

        if (ManifestationPvE.CanUse(out act, skipCastingCheck: true))
        {
            return true;
        }

        if (AdloquiumPvE.CanUse(out act))
        {
            return true;
        }

        return PhysickPvE.CanUse(out act) || base.HealSingleGCD(out act);
    }

    [RotationDesc(ActionID.SuccorPvE, ActionID.ConcitationPvE, ActionID.AccessionPvE)]
    protected override bool DefenseAreaGCD(out IAction? act)
    {
        act = null;
        if (RaiseRestriction || !NoMitigations || EnoughMitigation)
        {
            return base.DefenseAreaGCD(out act);
        }

        // Only have all 3 checks in case players have added their own custom configurations.
        if (AccessionPvE.CanUse(out act, skipCastingCheck: true))
        {
            return true;
        }

        if (ConcitationPvE.CanUse(out act))
        {
            return true;
        }

        return SuccorPvE.CanUse(out act) || base.DefenseAreaGCD(out act);
    }

    protected override bool GeneralGCD(out IAction? act)
    {
        act = null;
        if (RaiseRestriction) return false;
        if (SummonEosPvE.CanUse(out act)) return true;
        if (RunningOutOfMp) return false;
        if (TryNoBioIi(out act)) return true;
        if (TryNoArtOfWar(out act)) return true;
        if (TryNoBroilMastery(out act)) return true;
        if (TryNoBroilMasteryIi(out act)) return true;
        if (TryNoBroilIii(out act)) return true;
        if (TryNoBroilIv(out act)) return true;
        if (TryUseArtOfWar(out act)) return true;
        if (TryUseBiolysis(out act)) return true;
        if (TryUseRuinIi(out act)) return true;
        return BroilIvPvE.CanUse(out act) || base.GeneralGCD(out act);
    }

    #endregion
}