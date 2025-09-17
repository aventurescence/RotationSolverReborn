using System.ComponentModel;
using ECommons.ExcelServices;

namespace RotationSolver.ExtraRotations.Healer;

[Rotation("Churin SCH", CombatType.PvE, GameVersion = "7.3", Description = "It’s okay to judge a book by its cover, especially if that cover is made from questionable leather.")]
[SourceCode(Path = "main/ExtraRotations/Healer/ChurinSCH.cs")]
[ExtraRotation]
public sealed partial class ChurinSCH : ScholarRotation
{
    #region  Properties

    private const float HealThreshold = 0.8f;
    private const float CriticalHealThreshold = 0.4f;
    private const float SlidecastWindow = 0.5f;
    private static bool HasChainStratagem => AllHostileTargets != null && AllHostileTargets.Any(t => t.HasStatus(true, StatusID.ChainStratagem));
    private static bool AllowedToBurst => MergedStatus.HasFlag(AutoStatus.Burst);
    private new bool IsBurst => ChainStratagemPvE.EnoughLevel && HasChainStratagem;
    private bool RaiseRestriction => HasSwift && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise);
    private static bool HasEarthlyStar => PartyMembers.Any (h => h.IsJobs(Job.AST) && h.HasStatus(false, StatusID.EarthlyDominance, StatusID.GiantDominance));
    private static bool HasLilybell => PartyMembers.Any(h => h.IsJobs(Job.WHM) && h.HasStatus(false, StatusID.LiturgyOfTheBell));
    private static bool HasMacrocosmos => PartyMembers.Any(m => m.HasStatus(false, StatusID.Macrocosmos, StatusID.Macrocosmos_3989));
    private static bool EnoughMitigation => GetCurrentMitigationPercent() >= 0.50f || GetCurrentMitigationPercent() >= 0.40f && PartyMembersAverHP >= 0.95f;
    private bool CanSpreadLo => (RecitationPvE.Cooldown.HasOneCharge || HasRecitation ||
                                RecitationPvE.Cooldown.WillHaveOneChargeGCD(1))
                                && (DeploymentTacticsPvE.Cooldown.HasOneCharge
                                    || DeploymentTacticsPvE.Cooldown.WillHaveOneChargeGCD(1));
    private bool NoSingleTargetOGCD => _singleTargetOGCD.All(ogcd => !ogcd.CanUse(out _));
    private bool NoAreaOGCD => _areaOGCD.All(ogcd => !ogcd.CanUse(out _));
    private bool NoMitigations => _mitigations.All(ogcd => !ogcd.CanUse(out _)) && !CanSpreadLo;
    private static bool CanWeave => WeaponRemain >= AnimationLock;
    private readonly IBaseAction[] _singleTargetOGCD;
    private readonly IBaseAction[] _areaOGCD;
    private readonly IBaseAction[] _mitigations;



    #endregion

    #region Config Options
    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Remove Aetherpact if the linked party member's HP is above this percentage")]
    private float AetherpactRemove { get; set; } = 0.9f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Do not start Aetherpact if the target's HP is above this percentage (prevents toggling)")]
    private float AetherpactMinimum { get; set; } = 0.8f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Estimated percent of HP dealt as DPS for ballpark calculations")]
    private float BallparkPercent { get; set; } = 0.08f;

    [Range(0, 5, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Seconds you must be moving before Ruin II will be used")]
    private float RuinTime { get; set; } = 0f;

    [Range(0, 10000, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Minimum MP before prioritizing emergency healing and rezzing (willing to use Seraphism sooner)")]
    private int EmergencyHealingMpThreshold { get; set; } = 2000;

    [Range(0, 2, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Number of fewer mobs required to favor AoW spam over Bio (0 = use Bio if break-even below 30s)")]
    private int DotOffsetMobs { get; set; } = 1;

    [Range(0, 100, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Minimum Fairy Gauge required before prioritizing Fey Union (link)")]
    private int LinkFairyGauge { get; set; } = 70;

    [RotationConfig(CombatType.PvE, Name = "Enable Swiftcast restriction: only allow Raise while Swiftcast is active")]
    private bool SwiftLogic { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use GCDs to heal. (Ignored if you are the only healer in party)")]
    private bool GCDHeal { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use SpreadLo in the opener (Recitation + Protraction + Adloquium + Deployment Tactics)")]
    private bool UseSpreadLoInOpener { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Recitation with Succor, Concitation, or Accession")]
    private bool ReciteSuccor { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Sacred Soil's regeneration as a healing effect")]
    private bool SacredSoilHeal { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Enable ballpark DoT time-to-kill estimator (in addition to normal TTK configs)")]
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
        [Description("Use Aetherflow")]
        AetherflowFirst,

        [Description("Use Dissipation")]
        DissipationFirst,
    }
    #endregion

    #region Countdown Logic
    protected override IAction? CountDownAction(float remainTime)
    {
        if (SummonEosPvE.CanUse(out var act)
            || remainTime < RuinPvE.Info.CastTime + 0.8f && RuinPvE.CanUse(out act)
            || remainTime <= 0 && UseBurstMedicine(out act)
            || remainTime is < 4 and > 3 && DeploymentTacticsPvE.CanUse(out act)
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

    [RotationDesc(ActionID.ChainStratagemPvE, ActionID.BanefulImpactionPvE, ActionID.AetherflowPvE, ActionID.DissipationPvE)]
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

    [RotationDesc(ActionID.SummonSeraphPvE, ActionID.ConsolationPvE, ActionID.SacredSoilPvE, ActionID.IndomitabilityPvE, ActionID.WhisperingDawnPvE, ActionID.FeyBlessingPvE, ActionID.SeraphismPvE)]
    protected override bool HealAreaAbility(IAction nextGCD, out IAction? act)
    {
        act = null;
        if ((HasEarthlyStar || HasLilybell || HasMacrocosmos) && PartyMembersMinHP > HealThreshold || !CanWeave || NoAreaOGCD)
        {
            return base.HealAreaAbility(nextGCD, out act);
        }

        // Always try to use Indomitability if we've just used Recitation and are area healing
        if (IsLastAbility(ActionID.RecitationPvE) || HasRecitation && TryUseIndomitability(out act))
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
        if (FeyBlessingPvE.CanUse(out act))
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
        if ((SummonSeraphPvE.Cooldown.IsCoolingDown || CurrentMp <= EmergencyHealingMpThreshold || TakingContinuousDamage()) && SeraphismPvE.CanUse(out act))
        {
            return true;
        }

        // Lower Priority Indomitability without Recitation
        return TryUseIndomitability(out act) || base.HealAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.AetherpactPvE, ActionID.ExcogitationPvE, ActionID.LustratePvE, ActionID.SacredSoilPvE, ActionID.ProtractionPvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        if (!CanWeave || NoSingleTargetOGCD || (HasEarthlyStar || HasLilybell || HasMacrocosmos) && PartyMembersMinHP > HealThreshold)
        {
            return base.HealSingleAbility(nextGCD, out act);
        }
        if (TryRemoveAetherpact(out act))
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

        return LustratePvE.CanUse(out act) || base.HealSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.FeyIlluminationPvE, ActionID.ExpedientPvE, ActionID.SummonSeraphPvE, ActionID.ConsolationPvE, ActionID.SacredSoilPvE, ActionID.SeraphismPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        act = null;

        // Check mitigation based on an incoming damage type to decide on a single defense ability
        if (EnoughMitigation && PartyMembersMinHP > 0.9f || !CanWeave || NoMitigations)
        {
            return base.DefenseAreaAbility(nextGCD, out act);
        }
        if (ConsolationPvE.CanUse(out act))
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
        if (FeyIlluminationPvE.CanUse(out act))
        {
            return true;
        }
        if (CanSpreadLo)
        {
            if (TryUseRecitation(AdloquiumPvE, out act))
            {
                QueueNextGCD(AdloquiumPvE);
                return true;
            }
        }
        if ((SummonSeraphPvE.Cooldown.IsCoolingDown || CurrentMp <= EmergencyHealingMpThreshold || TakingContinuousDamage())
            && SeraphismPvE.CanUse(out act))
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
        if (!CanWeave || NoSingleTargetOGCD)
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
        if (RaiseRestriction || !NoAreaOGCD || !TakingContinuousDamage() || PartyMembersAverHP > HealThreshold)
        {
            return base.HealAreaGCD(out act);
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
        if (RaiseRestriction || !NoSingleTargetOGCD || !TakingContinuousDamage() || PartyMembersMinHP > HealThreshold)
        {
            return base.HealSingleGCD(out act);
        }

        if (ManifestationPvE.CanUse(out act, skipCastingCheck: true))
        {
            return true;
        }

        if (HasRecitation && AdloquiumPvE.CanUse(out act))
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

        if (_queuedNextGCD != null)
        {
            if (_queuedNextGCD.CanUse(out act))
            {
                _queuedNextGCD = null;
                return true;
            }
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
        if (RaiseRestriction)
        {
            return false;
        }

        // Summon Eos
        if (SummonEosPvE.CanUse(out act))
        {
            return true;
        }

        // Don't use attacks if we're in a wipe scenario spamming rezzes and heals
        if (CurrentMp < EmergencyHealingMpThreshold && (!AetherflowPvE.Cooldown.WillHaveOneChargeGCD(3) || !Player.HasStatus(true, StatusID.LucidDreaming)))
        {
            return false;
        }

        var nearbyHostiles = NumberOfHostilesInRangeOf(5);

        var expectedHpToLive12Seconds = 1f;

        var partyMemberCount = 0;
        foreach (var _ in PartyMembers)
        {
            partyMemberCount++;
        }

        // Expect that players do ~ 10% of healer hp as DPS and ballpark to ensure we're not wasting dots on something that's going to die immediately based on nobody hitting it
        // This is still wildly overestimating mob survival in some contexts, but initial TTK estimates from RSR can be poor based on how far mobs were being kited
        if (UseBallparkTtk)
        {
            expectedHpToLive12Seconds = BallparkPercent * Player.MaxHp * partyMemberCount * 12;
        }

        /*
         *  Bio TTK should only be compared to Ruin 1; it's 24 seconds to break even; recommend 24 second TTK when not using it while moving
         *  Bio2/Biolysis TTK should be considered relative to other spells even for single target when adjusting TTK settings; recommend 12 seconds
         *      Ruin 2 [ level 54=12 seconds, level 64=9 seconds, level 72=12 seconds, level 94=9 seconds ]
         *      Ruin 1 [ level below 54=12 seconds ]
         *      Broil  [ level 54=18 seconds, level 64=18 seconds, level 72=15 seconds, level 82=15 seconds, level 94=12 seconds ]
         *
         *  Level 10-46 -> DoT targets and then spam appropriate Ruins at them; no better choices
         *  Level 46 gets Art of War as an instant cast beats out other options when in range (enabling weaving + auto attacks)
         *  Level 54 Broil beats out Art of War for single target
         *  Level 64 Ruin II beats out Art of War for single target and AoW becomes strictly an AoE ability
         *  Level 72 Biolysis now breaks even against 4 targets
         *  Level 82 Broil 3 changes to Broil 4
         *
        */
        if (!BioIiPvE.EnoughLevel)
        {
            if (BioPvE.CanUse(out act) && BioPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds)
            {
                return true;
            }

            if (RuinPvE.CanUse(out act))
            {
                return true;
            }

            if (BioPvE.CanUse(out act, skipTTKCheck: true))
            {
                return true;
            }
        }
        else if (!ArtOfWarPvE.EnoughLevel)
        {
            if (BioIiPvE.CanUse(out act) && BioIiPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds)
            {
                return true; // No better options still, and configured TTK should cover whether we want to use it
            }

            if (RuinPvE.CanUse(out act))
            {
                return true;
            }
        }
        else if (!BroilMasteryTrait.EnoughLevel)
        {
            if (BioIiPvE.CanUse(out act) && nearbyHostiles < GetAoWBreakevenTargets() && BioIiPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds)
            {
                return true; // This is better against 2 targets IFF it will last >= 24 seconds
            }

            if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && nearbyHostiles > 0)
            {
                return true;
            }

            if (RuinPvE.CanUse(out act))
            {
                return true; // 25 y range may still allow us to do better than AoW does even at the same potency and with a cast time
            }
        }
        else if (!BroilMasteryIiTrait.EnoughLevel)
        {
            if (BioIiPvE.CanUse(out act) && nearbyHostiles < GetAoWBreakevenTargets() && BioIiPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds)
            {
                return true; // This is better against 3 targets IFF it will last >= 24 seconds
            }

            if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && nearbyHostiles > 1)
            {
                return true;
            }

            if (BroilPvE.CanUse(out act))
            {
                return true;
            }

            if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && nearbyHostiles > 0)
            {
                return true;
            }
        }
        else if (!BroilIiiPvE.EnoughLevel)
        {
            if (
                BioIiPvE.CanUse(out act)
                && nearbyHostiles < GetAoWBreakevenTargets()
                && BioIiPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds
            )
            {
                return true; // This is better against 3 targets IFF it will last >= 24 seconds
            }

            if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && nearbyHostiles > 1)
            {
                return true;
            }

            if (BroilIiPvE.CanUse(out act))
            {
                return true;
            }
        }
        else if (!BroilIvPvE.EnoughLevel)
        {
            if (BiolysisPvE.CanUse(out act) && nearbyHostiles < GetAoWBreakevenTargets() && BiolysisPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds)
            {
                return true; // This is better against 4 targets IFF it will last >= 27 seconds
            }

            if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && nearbyHostiles > 1)
            {
                return true;
            }

            if (BroilIiiPvE.CanUse(out act))
            {
                return true;
            }
        }
        else
        {
            if (BiolysisPvE.CanUse(out act)
                && nearbyHostiles < GetAoWBreakevenTargets()
                && BiolysisPvE.Target.Target.CurrentHp >= expectedHpToLive12Seconds)
            {
                return true; // This is better against 4 targets IFF it will last >= 27 seconds
            }

            if (ArtOfWarPvE.CanUse(out act, skipAoeCheck: true) && nearbyHostiles > 1)
            {
                return true;
            }

            if (BroilIvPvE.CanUse(out act))
            {
                return true;
            }
        }

        // Starting at 38, we always default to Ruin II when moving and no bio targets
        if (MovingTime > RuinTime)
        {
            if (!CanSlidecast())
            {
                return RuinIiPvE.CanUse(out act);
            }
        }

        return base.GeneralGCD(out act);
    }
    #endregion

    #region Extra Methods
    #region oGCD abilities
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

        if (IsBurst || (DissipationPvE.EnoughLevel && DissipationPvE.Cooldown.WillHaveOneChargeGCD(5)
                        || AetherflowPvE.Cooldown.WillHaveOneChargeGCD(5)))
        {
            return EnergyDrainPvE.CanUse(out act, usedUp: true);
        }

        return false;
    }
    private bool TryUseBanefulImpaction(out IAction? act)
    {
        act = null;
        if (!BanefulImpactionPvE.EnoughLevel || !HasImpactImminent)
        {
            return false;
        }

        if (AetherflowPvE.Cooldown.IsCoolingDown && DissipationPvE.Cooldown.IsCoolingDown &&
            (!AetherflowPvE.Cooldown.WillHaveOneChargeGCD(1) ||
            !DissipationPvE.Cooldown.WillHaveOneChargeGCD(1)) && HasImpactImminent)
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
        if (!RecitationPvE.EnoughLevel || RecitationPvE.Cooldown.IsCoolingDown)
        {
            return false;
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

            if (tankNeedsExcog || MergedStatus.HasFlag(AutoStatus.DefenseSingle))
            {
                return RecitationPvE.CanUse(out act);
            }
        }

        // Check average hp as a quick check on whether we're in a raid wide healing situation
        if (!IndomitabilityPvE.Cooldown.IsCoolingDown && PartyMembersAverHP <= HealThreshold)
        {
            return RecitationPvE.CanUse(out act);
        }

        // Or if we're desperate and hard casting Adlo (or potentially succor)
        IAction[] recitationActions = ReciteSuccor
            ? [AccessionPvE, ConcitationPvE, SuccorPvE, AdloquiumPvE, ManifestationPvE]
            : [AdloquiumPvE, ManifestationPvE];
        return nextGCD.IsTheSameTo(true, recitationActions) && RecitationPvE.CanUse(out act);
    }

    private bool TryUseIndomitability(out IAction? act)
    {
        act = null;
        if (!IndomitabilityPvE.EnoughLevel || IndomitabilityPvE.Cooldown.IsCoolingDown ||
            (!HasAetherflow && !HasRecitation)) return false;

        if (HasRecitation && IndomitabilityPvE.CanUse(out act)) return true;

        if (HasAetherflow && ChainStratagemPvE.Cooldown.IsCoolingDown &&
            !ChainStratagemPvE.Cooldown.WillHaveOneCharge(30))
        {
            return IndomitabilityPvE.CanUse(out act);
        }
        return false;
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
        if (AetherpactPvE.CanUse(out act) && FairyGauge >= LinkFairyGauge && !haveLink && AetherpactPvE.Target.Target.GetHealthRatio() <= AetherpactMinimum)
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
        foreach (var item in PartyMembers)
        {
            if (!item.HasStatus(true, StatusID.FeyUnion_1223)) continue;

            if (item.GetHealthRatio() >= AetherpactRemove)
            {
                act = AetherpactPvE;
                return true;
            }
        }

        var haveLinkDRK = false;
        foreach (var p in PartyMembers)
        {
            if (p.HasStatus(true, StatusID.FeyUnion_1223) && p.HasStatus(false, StatusID.LivingDead))
            {
                haveLinkDRK = true;
                break;
            }
        }

        // remove the link if the party member has a link and also has Living Dead status
        return AetherpactPvE.CanUse(out act) && haveLinkDRK;
    }

    private bool TryUseSacredSoil(out IAction? act)
    {
        act = null;
        if (!HasAetherflow || SacredSoilPvE.Cooldown.IsCoolingDown || EnoughMitigation)
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
            if (DeploymentTacticsUsage is DeploymentTacticsUsageStrategy.CatalyzeOnly
             or DeploymentTacticsUsageStrategy.CatalyzeOrGalvanize)
            {
                if (DeploymentTacticsPvE.CanUse(out act))
                {
                    if (DeploymentTacticsPvE.Target.Target.IsParty() && (DeploymentTacticsPvE.Target.Target.HasStatus(true, StatusID.Catalyze)
                                                                         || IsShieldHpAboveThreshold(DeploymentTacticsPvE.Target.Target, 0.3f)))
                    {
                        return true;
                    }

                }

            }

        }
        return false;
    }

    private bool TryUseEmergencyTactics(IAction nextGCD, out IAction? act)
    {
        act = null;
        if (!EmergencyTacticsPvE.EnoughLevel || !EmergencyTacticsPvE.IsEnabled) return false;

        // Only use emergency tactics if we're healing from raid wide damage or multiple members need to be healed for doom
        if (nextGCD.IsTheSameTo(false, SuccorPvE, ConcitationPvE, AccessionPvE) && EmergencyTacticsPvE.CanUse(out act))
        {
            var count = 0;
            foreach (var member in PartyMembers)
                if (member.DistanceToPlayer() <= 15)
                    if (member.DoomNeedHealing() || member.GetHealthRatio() < CriticalHealThreshold || TakingContinuousDamage())
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
             || !DissipationPvE.EnoughLevel) && CombatElapsedLessGCD(2)
                                             && AetherflowPvE.CanUse(out act))
            return true;

        return !HasAetherflow && (DissipationPvE.Cooldown.IsCoolingDown || !DissipationPvE.EnoughLevel)
                              && AetherflowPvE.CanUse(out act);
    }

    private bool TryUseDissipation(out IAction? act)
    {
        act = null;
        if (!DissipationPvE.EnoughLevel || HasAetherflow) return false;

        if (AetherflowStrategyUse == AetherflowStrategy.DissipationFirst && CombatElapsedLessGCD(2) &&
            DissipationPvE.CanUse(out act)) return true;

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
            if (member == ExcogitationPvE.Target.Target && member.GetHealthRatio() <= 0.5f && !member.NoNeedHealingInvuln())
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
    #endregion
    #endregion

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

    public ChurinSCH()
    {
        _singleTargetOGCD =
        [
            AetherpactPvE,
            ExcogitationPvE,
            LustratePvE,
            ProtractionPvE
        ];

        _areaOGCD =
        [
            SummonSeraphPvE,
            ConsolationPvE,
            IndomitabilityPvE,
            FeyBlessingPvE,
            WhisperingDawnPvE,
        ];
        _mitigations =
        [
            SacredSoilPvE,
            FeyIlluminationPvE,
            ExpedientPvE,
            SeraphismPvE,
            SummonSeraphPvE,
            ConsolationPvE,
        ];
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
            if (sample.Hp < prevHp - (int)(0.01f * Player.MaxHp)) // consider >1% drops as a hit
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
    private IBaseAction? _queuedNextGCD;

    private void QueueNextGCD(IBaseAction act)
    {
        _queuedNextGCD = act;
    }

    private bool CanSlidecast()
    {
        var remainCastTime = Player.TotalCastTime - Player.CurrentCastTime;
        if (Player.IsCasting)
        {
            return  remainCastTime >= SlidecastWindow
                 || remainCastTime >= RuinTime
                 || MovingTime <= ( remainCastTime - SlidecastWindow)
                 || MovingTime <= ( remainCastTime - RuinTime);
        }
        return WeaponRemain >= SlidecastWindow;
    }
    #endregion
    #endregion
}
