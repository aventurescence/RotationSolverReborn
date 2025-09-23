namespace RotationSolver.ExtraRotations.Melee;

[Rotation("DSRViper by freddersly", CombatType.PvE, GameVersion = "7.31")]
[SourceCode(Path = "main/ExtraRotations/Melee/DSRViper.cs")]

public sealed class DSRViper : ViperRotation
{
    #region Config Options

    [RotationConfig(CombatType.PvE, Name = "Use Standard Double Reawaken burst (vs Immediate)")]
    public bool StandardDoubleReawaken { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Early Tincture timing (5s before Serpent's Ire)")]
    public bool EarlyTincture { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Force buff refresh before burst windows")]
    public bool ForceBurstBuffRefresh { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Save 50 Offerings for 2min burst windows")]
    public bool SaveOfferingsForBurst { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use Uncoiled Fury for movement optimization")]
    public bool UFMovementOptimization { get; set; } = true;

    [Range(3, 10, ConfigUnitType.None, 1)]
    [RotationConfig(CombatType.PvE, Name = "Start dual wield only combos X seconds before Serpent's Ire")]
    public int DualWieldPrepTime { get; set; } = 10;

    [Range(35, 45, ConfigUnitType.None, 1)]
    [RotationConfig(CombatType.PvE, Name = "Minimum buff time for safe Reawaken usage")]
    public int MinBuffTimeForReawaken { get; set; } = 40;
    #endregion

    #region Tracking Properties
    private bool IsPoolingForBurst()
    {
        return SaveOfferingsForBurst && SerpentsIrePvE.EnoughLevel && 
               SerpentsIrePvE.Cooldown.RecastTimeRemainOneCharge <= DualWieldPrepTime &&
               SerpentOffering < 100;
    }

    private bool IsBuffTimeSafeForReawaken()
    {
        return SwiftTime > MinBuffTimeForReawaken && HuntersTime > MinBuffTimeForReawaken;
    }

    private bool ShouldRefreshBuffsBeforeBurst()
    {
        return ForceBurstBuffRefresh && SerpentsIrePvE.Cooldown.RecastTimeRemainOneCharge <= DualWieldPrepTime &&
               (SwiftTime <= 50 || HuntersTime <= 50);
    }

    public override void DisplayRotationStatus()
    {
        ImGui.Text($"Pooling for Burst: {IsPoolingForBurst()}");
        ImGui.Text($"Buff Time Safe: {IsBuffTimeSafeForReawaken()}");
        ImGui.Text($"Should Refresh Buffs: {ShouldRefreshBuffsBeforeBurst()}");
        ImGui.Text($"Standard vs Immediate: {(StandardDoubleReawaken ? "Standard" : "Immediate")}");
    }
    #endregion

    #region Additional oGCD Logic
    [RotationDesc]
    protected override bool EmergencyAbility(IAction nextGCD, out IAction? act)
    {
        // Priority 1: Uncoiled Fury follow-ups - Highest Priority
        switch ((HasPoisedFang, HasPoisedBlood))
        {
            case (true, _):
                if (UncoiledTwinfangPvE.CanUse(out act))
                    return true;
                break;
            case (_, true):
                if (UncoiledTwinbloodPvE.CanUse(out act))
                    return true;
                break;
            case (false, false):
                if (TimeSinceLastAction.TotalSeconds < 2)
                    break;
                if (UncoiledTwinfangPvE.CanUse(out act))
                    return true;
                if (UncoiledTwinbloodPvE.CanUse(out act))
                    return true;
                break;
        }

        // Priority 2: Reawaken Legacy abilities
        if (HasReawakenedActive)
        {
            if (FirstLegacyPvE.CanUse(out act)) return true;
            if (SecondLegacyPvE.CanUse(out act)) return true;
            if (ThirdLegacyPvE.CanUse(out act)) return true;
            if (FourthLegacyPvE.CanUse(out act)) return true;
        }

        // Priority 3: Single Target Dread follow-ups
        switch ((HasHunterVenom, HasSwiftVenom))
        {
            case (true, _):
                if (TwinfangBitePvE.CanUse(out act))
                    return true;
                break;
            case (_, true):
                if (TwinbloodBitePvE.CanUse(out act))
                    return true;
                break;
            case (false, false):
                if (TimeSinceLastAction.TotalSeconds < 2)
                    break;
                if (TwinfangBitePvE.CanUse(out act))
                    return true;
                if (TwinbloodBitePvE.CanUse(out act))
                    return true;
                break;
        }

        // Priority 4: AOE Dread follow-ups
        switch ((HasFellHuntersVenom, HasFellSkinsVenom))
        {
            case (true, _):
                if (TwinfangThreshPvE.CanUse(out act, skipAoeCheck: true))
                    return true;
                break;
            case (_, true):
                if (TwinbloodThreshPvE.CanUse(out act, skipAoeCheck: true))
                    return true;
                break;
            case (false, false):
                if (TimeSinceLastAction.TotalSeconds < 2)
                    break;
                if (TwinfangThreshPvE.CanUse(out act, skipAoeCheck: true))
                    return true;
                if (TwinbloodThreshPvE.CanUse(out act, skipAoeCheck: true))
                    return true;
                break;
        }

        // Priority 5: Serpent Tail abilities
        if (LastLashPvE.CanUse(out act, skipAoeCheck: true))
            return true;

        if (DeathRattlePvE.CanUse(out act))
            return true;

        // Priority 6: Early Tincture timing (based on Balance guide)
        if (EarlyTincture && NoAbilityReady && SerpentsIrePvE.EnoughLevel && 
            SerpentsIrePvE.Cooldown.ElapsedAfter(115) && SerpentsIrePvE.Cooldown.RecastTimeRemainOneCharge <= 5 &&
            UseBurstMedicine(out act))
        {
            return true;
        }

        return base.EmergencyAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        // Use Serpent's Ire on cooldown for 2min burst alignment
        if (IsBurst && SerpentsIrePvE.CanUse(out act))
        {
            return true;
        }

        return base.AttackAbility(nextGCD, out act);
    }
    #endregion

    #region GCD Logic
    protected override bool GeneralGCD(out IAction? act)
    {
        // Priority 1: Reawaken Combo - Always highest priority
        if (OuroborosPvE.CanUse(out act)) return true;
        if (FourthGenerationPvE.CanUse(out act)) return true;
        if (ThirdGenerationPvE.CanUse(out act)) return true;
        if (SecondGenerationPvE.CanUse(out act)) return true;
        if (FirstGenerationPvE.CanUse(out act)) return true;

        // Priority 2: Reawaken usage (based on Balance guide conditions)
        if (SerpentOffering >= 50 && IsBuffTimeSafeForReawaken() && 
            IsBurst && ReawakenPvE.CanUse(out act, skipComboCheck: true))
        {
            return true;
        }

        // Priority 3: Resource management outside pooling periods
        if (!IsPoolingForBurst())
        {
            // Uncoiled Fury usage for movement and overcap protection
            if (UFMovementOptimization && !HasHostilesInRange && RattlingCoilStacks > 0 && 
                !HasReadyToReawaken && NoAbilityReady)
            {
                if (UncoiledFuryPvE.CanUse(out act, usedUp: true))
                    return true;
            }

            // Overcap protection
            if (RattlingCoilStacks == 3 && !HasReadyToReawaken && NoAbilityReady)
            {
                if (UncoiledFuryPvE.CanUse(out act, usedUp: true))
                    return true;
            }

            // Post-burst UF spending (within 30s of Serpent's Ire)
            if (RattlingCoilStacks > 1 && SerpentsIrePvE.Cooldown.JustUsedAfter(30) && 
                !HasReadyToReawaken && NoAbilityReady)
            {
                if (UncoiledFuryPvE.CanUse(out act, usedUp: true))
                    return true;
            }

            // Tincture window optimization - use all UF under Medicated
            if (Player.HasStatus(true, StatusID.Medicated) && RattlingCoilStacks > 0 && 
                !HasReadyToReawaken && NoAbilityReady)
            {
                if (UncoiledFuryPvE.CanUse(out act, usedUp: true))
                    return true;
            }
        }

        // Priority 4: AOE Dread Combo logic
        if (PitActive)
        {
            if (HasHunterAndSwift)
            {
                // Prioritize buffs about to expire
                if (WillSwiftEnd && SwiftskinsDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                    return true;
                if (WillHunterEnd && HuntersDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                    return true;

                // Standard priority based on timer
                switch (HunterOrSwiftEndsFirst)
                {
                    case "Hunter":
                        if (HuntersDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                            return true;
                        break;
                    case "Swift":
                        if (SwiftskinsDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                            return true;
                        break;
                    default:
                        if (HuntersDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                            return true;
                        if (SwiftskinsDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                            return true;
                        break;
                }
            }
            else
            {
                if (!IsSwift && SwiftskinsDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                    return true;
                if (!IsHunter && HuntersDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                    return true;
            }
        }

        if (!PitActive)
        {
            if (SwiftskinsDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                return true;
            if (HuntersDenPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true, skipAoeCheck: true))
                return true;
        }

        // Priority 5: Single Target Dread Combo logic with positional optimization
        if (DreadActive)
        {
            if (HasHunterAndSwift)
            {
                // Prioritize buffs about to expire
                if (WillSwiftEnd && SwiftskinsCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (WillHunterEnd && HuntersCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;

                // Positional optimization - prioritize hittable positionals
                if (HuntersCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true) && 
                    HuntersCoilPvE.Target.Target != null && CanHitPositional(EnemyPositional.Flank, HuntersCoilPvE.Target.Target))
                    return true;
                if (SwiftskinsCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true) && 
                    SwiftskinsCoilPvE.Target.Target != null && CanHitPositional(EnemyPositional.Rear, SwiftskinsCoilPvE.Target.Target))
                    return true;

                // Fallback to timer priority
                switch (HunterOrSwiftEndsFirst)
                {
                    case "Hunter":
                        if (HuntersCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                    case "Swift":
                        if (SwiftskinsCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                    default:
                        if (HuntersCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        if (SwiftskinsCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                }
            }
            else
            {
                // Use available Coils
                if (!IsSwift && SwiftskinsCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (!IsHunter && HuntersCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
            }
        }

        // Non-Dread Coil usage
        if (!DreadActive)
        {
            if (HuntersCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                return true;
            if (SwiftskinsCoilPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                return true;
        }

        // Priority 6: Vicewinder/Vicepit charge management
        if (IsSwift)
        {
            // Avoid overcapping charges
            if (VicewinderPvE.Cooldown.CurrentCharges == 2 ||
                (VicewinderPvE.Cooldown.CurrentCharges == 1 && VicewinderPvE.Cooldown.RecastTimeRemainOneCharge < 10))
            {
                if (VicewinderPvE.CanUse(out act, usedUp: true))
                    return true;
            }

            if (VicepitPvE.Cooldown.CurrentCharges == 2 ||
                (VicepitPvE.Cooldown.CurrentCharges == 1 && VicepitPvE.Cooldown.RecastTimeRemainOneCharge < 10))
            {
                if (VicepitPvE.CanUse(out act, usedUp: true))
                    return true;
            }
        }

        // Priority 7: AOE Serpent combo finishers
        switch ((HasGrimHunter, HasGrimSkin))
        {
            case (true, _):
                if (JaggedMawPvE.CanUse(out act, skipAoeCheck: true, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                break;
            case (_, true):
                if (BloodiedMawPvE.CanUse(out act, skipAoeCheck: true, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                break;
            case (false, false):
                if (JaggedMawPvE.CanUse(out act, skipAoeCheck: true, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (BloodiedMawPvE.CanUse(out act, skipAoeCheck: true, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                break;
        }

        // Priority 8: Single Target Serpent combo finishers (optimized order)
        switch ((HasHindstung, HasHindsbane, HasFlankstung, HasFlanksbane))
        {
            case (true, _, _, _):
                if (HindstingStrikePvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                break;
            case (_, true, _, _):
                if (HindsbaneFangPvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                break;
            case (_, _, true, _):
                if (FlankstingStrikePvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                break;
            case (_, _, _, true):
                if (FlanksbaneFangPvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                break;
            case (false, false, false, false):
                // Follow standard rotation order from Balance guide
                if (HindstingStrikePvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                if (FlankstingStrikePvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                if (HindsbaneFangPvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                if (FlanksbaneFangPvE.CanUse(out act, skipStatusProvideCheck: true))
                    return true;
                break;
        }

        // Priority 9: AOE Serpent combo second hits
        if (SwiftskinsBitePvE.EnoughLevel)
        {
            if (HasHunterAndSwift)
            {
                switch (HunterOrSwiftEndsFirst)
                {
                    case "Hunter":
                        if (HuntersBitePvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                    case "Swift":
                        if (SwiftskinsBitePvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                    default:
                        if (SwiftskinsBitePvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                }
            }
            else
            {
                if (!IsSwift && SwiftskinsBitePvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (!IsHunter && HuntersBitePvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
            }
        }

        // Priority 10: Single Target Serpent combo second hits with positional optimization
        if (SwiftskinsStingPvE.EnoughLevel)
        {
            if (HasHunterAndSwift)
            {
                // Use specific Stings based on positional buffs (Hind/Flank)
                if (HasHind && SwiftskinsStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (HasFlank && HuntersStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                
                // Fallback when no positional buffs
                switch (HunterOrSwiftEndsFirst)
                {
                    case "Hunter":
                        if (HuntersStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                    case "Swift":
                    default:
                        if (SwiftskinsStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                            return true;
                        break;
                }
            }
            else
            {
                // Prioritize based on available buffs
                if (HasHind && SwiftskinsStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (HasFlank && HuntersStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (!IsSwift && SwiftskinsStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
                if (!IsHunter && HuntersStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                    return true;
            }
        }
        else
        {
            if (HuntersStingPvE.CanUse(out act, skipStatusProvideCheck: true, skipComboCheck: true))
                return true;
        }

        // Priority 11: Combo starters with buff optimization
        switch ((HasSteel, HasReavers))
        {
            case (true, _):
                if (SteelFangsPvE.CanUse(out act))
                    return true;
                if (SteelMawPvE.CanUse(out act))
                    return true;
                break;
            case (_, true):
                if (ReavingFangsPvE.CanUse(out act))
                    return true;
                if (ReavingMawPvE.CanUse(out act))
                    return true;
                break;
            case (false, false):
                // Prefer Reaving/Steel based on upcoming burst prep
                if (ShouldRefreshBuffsBeforeBurst())
                {
                    if (ReavingFangsPvE.CanUse(out act))
                        return true;
                    if (ReavingMawPvE.CanUse(out act))
                        return true;
                }
                else
                {
                    if (SteelFangsPvE.CanUse(out act))
                        return true;
                    if (SteelMawPvE.CanUse(out act))
                        return true;
                }
                break;
        }

        // Priority 12: Ranged options
        if (UFMovementOptimization && !HasHostilesInRange)
        {
            if (UncoiledFuryPvE.CanUse(out act, usedUp: true))
                return true;
            if (WrithingSnapPvE.CanUse(out act))
                return true;
        }

        return base.GeneralGCD(out act);
    }
    #endregion
}
