using FFXIVClientStructs.FFXIV.Client.Game.UI;
using RotationSolver.MathData;
using XIVCalc;
using XIVCalc.Calculations;
using Job = ECommons.ExcelServices.Job;

namespace RotationSolver.ExtraRotations.Healer;

public sealed partial class ChurinSCH
{
    private unsafe bool TryUseOptimizedHeal(out IAction? act)
    {
        act = null;

        try
        {
            var uiState = UIState.Instance();
            var lvl = uiState->PlayerState.CurrentLevel;
            var attr = uiState->PlayerState.Attributes;
            var det = attr[(int)StatType.Determination];
            var crit = attr[(int)StatType.CriticalHit];
            var critMult = StatEquations.CritDamage(crit, lvl);
            var critRate = StatEquations.CritChance(crit, lvl);
            var dh = attr[(int)StatType.DirectHitRate];
            var ten = attr[(int)StatType.Tenacity];
            var (ilvlSync, ilvlSyncType) = IlvlSync.GetCurrentIlvlSync();

            // when HasRecitation, certain heals should be adjusted to include crit expectation
            bool IsRecitationCritTarget(string n)
            {
                return n == AdloquiumPvE.Name || n == ExcogitationPvE.Name || n == IndomitabilityPvE.Name ||
                       n == ConcitationPvE.Name;
            }

            // recitation availability boolean (used for adjusting expected values)
            var recitationEffectivelyAvailable = HasRecitation || RecitationPvE.Cooldown.HasOneCharge || RecitationPvE.Cooldown.WillHaveOneChargeGCD(1);

            var schPotencies = HealerData.GetScholarHealingPotencies(); // name -> cure potency
            var schHotPerTick = HealerData.GetScholarHotPotenciesPerTick(); // name -> per-tick potency
            var schHotTotal = HealerData.GetScholarHotTotalPotencies(); // name -> total potency

            if (HealerData.LastScholarBaseHeals.Count == 0 || HealerData.LastScholarHotBasePerTick.Count == 0 ||
                HealerData.LastScholarHotTotalBase.Count == 0 || HealerData.LastScholarShieldStrengths.Count == 0)
            {
                HealerData.UpdateScholarCaches(uiState, (XIVCalc.Job)Job.SCH, det, critMult, critRate, dh, ten,
                    ilvlSync, ilvlSyncType);
            }

            var schShields = HealerData.LastScholarShieldStrengths; // try to use cached shield strengths

            // Known area actions are derived from HealerData so the HealTargetType drives area vs. single classification.
            var areaNames = HealerData.GetScholarAreaActionNames();

            // Retrieve heal metadata so we can prioritize OGCDs and distinguish instant vs HoT (enum-typed)
            var healTypeNames = HealerData.GetScholarHealTypeNames(); // name -> HealerData.HealType
            var healEffectNames = HealerData.GetScholarHealEffectTypeNames(); // name -> HealerData.HealEffectType

            // Build single-target candidates (exclude area actions). Include isOgcd/effectType for prioritization.
            var singleCandidates =
                new List<(IBaseAction action, string name, double healAmount, bool isHoT, bool isShield,
                    HealerData.HealType healType, HealerData.HealEffectType effectType)>();
            foreach (var kv in schPotencies)
            {
                var name = kv.Key;
                if (areaNames.Contains(name)) continue;
                var potency = kv.Value;
                // Prefer cached deterministic base heal
                if (!HealerData.LastScholarBaseHeals.TryGetValue(name, out var baseHeal))
                {
                    baseHeal = HealingCalculator.BaseHealCalculation(uiState, (XIVCalc.Job)Job.SCH, potency, det, critMult,
                        critRate, dh, ten, ilvlSync, ilvlSyncType);
                }

                // Apply recitation-crit expectation for specified healing
                baseHeal = RecitationAdjuster.MaybeApplyRecitationCrit(name, baseHeal, false, IsRecitationCritTarget(name), recitationEffectivelyAvailable, critRate, critMult);
                var action = GetActionByName(name);
                if (action != null)
                {
                    var healType = healTypeNames.GetValueOrDefault(name, HealerData.HealType.GCD);
                    var effectType = healEffectNames.GetValueOrDefault(name, HealerData.HealEffectType.Instant);
                    var isHot = effectType == HealerData.HealEffectType.HealOverTime;
                    singleCandidates.Add((action, name, baseHeal, isHot, false, healType, effectType));
                }
            }

            foreach (var kv in schHotPerTick)
            {
                var name = kv.Key;
                if (areaNames.Contains(name)) continue;
                if (!schHotTotal.TryGetValue(name, out var totalPotency)) continue;
                var perTick = kv.Value;
                if (perTick <= 0) continue;
                // Prefer cached per-tick base and total base if available
                if (!HealerData.LastScholarHotBasePerTick.TryGetValue(name, out var basePerTick))
                {
                    basePerTick = HealingCalculator.BaseHealCalculation(uiState, (XIVCalc.Job)Job.SCH, perTick, det,
                        critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                }

                if (!HealerData.LastScholarHotTotalBase.TryGetValue(name, out var totalBase))
                {
                    var ticks = totalPotency / perTick;
                    totalBase = basePerTick * ticks;
                }

                // Apply recitation-crit expectation to HoT total if applicable
                totalBase = RecitationAdjuster.MaybeApplyRecitationCrit(name, totalBase, false, IsRecitationCritTarget(name), recitationEffectivelyAvailable, critRate, critMult);
                var action = GetActionByName(name);
                if (action != null)
                {
                    var healType = healTypeNames.GetValueOrDefault(name, HealerData.HealType.GCD);
                    var effectType = healEffectNames.GetValueOrDefault(name, HealerData.HealEffectType.HealOverTime);
                    singleCandidates.Add((action, name, totalBase, true, false, healType, effectType));
                }
            }

            foreach (var kv in schShields)
            {
                var name = kv.Key;
                if (areaNames.Contains(name)) continue;
                var action = GetActionByName(name);
                if (action != null)
                {
                    // Treat shields as instant non-HoT effects; get heal type if available
                    var healType = healTypeNames.GetValueOrDefault(name, HealerData.HealType.GCD);
                    var shieldVal = RecitationAdjuster.MaybeApplyRecitationCrit(name, kv.Value, true, IsRecitationCritTarget(name), recitationEffectivelyAvailable, critRate, critMult);
                    singleCandidates.Add((action, name, shieldVal, false, true, healType,
                        HealerData.HealEffectType.Instant));
                }
            }

            // Determine injured members in range (20 yalms), fallback to the whole party
            var membersInRange = PartyMembers
                .Where(m => !m.IsDead && !m.NoNeedHealingInvuln() && m.DistanceToPlayer() <= 20).ToList();
            var injured = membersInRange.Where(m => m.CurrentHp < m.MaxHp).OrderBy(m => m.GetHealthRatio()).ToList();
            if (injured.Count == 0)
                injured = PartyMembers.Where(m => !m.IsDead && !m.NoNeedHealingInvuln() && m.CurrentHp < m.MaxHp)
                    .OrderBy(m => m.GetHealthRatio()).ToList();

            var injuredCount = injured.Count;

            // Build area candidates: for each area action, compute effective heal across injured members (cap per-target by missing HP)
            var areaCandidates = new List<(IBaseAction action, string name, double totalEffectiveHeal)>();
            foreach (var name in areaNames)
            {
                var action = GetActionByName(name);
                if (action == null) continue;
                if (!action.CanUse(out var ready) || ready == null) continue;

                // Compute separate heal and shield contributions (area actions may have either or both)
                var perTargetHeal = 0d;
                var perTargetShield = 0d;

                // Prefer cached base heal for cure potency
                if (schPotencies.TryGetValue(name, out var cPot))
                {
                    if (!HealerData.LastScholarBaseHeals.TryGetValue(name, out perTargetHeal))
                    {
                        perTargetHeal = HealingCalculator.BaseHealCalculation(uiState, (XIVCalc.Job)Job.SCH, cPot, det, critMult,
                            critRate, dh, ten, ilvlSync, ilvlSyncType);
                    }
                }
                // For HoTs, prefer cached total base if present
                else if (schHotPerTick.TryGetValue(name, out var perTick) &&
                         schHotTotal.TryGetValue(name, out var totalPot))
                {
                    if (!HealerData.LastScholarHotTotalBase.TryGetValue(name, out perTargetHeal))
                    {
                        if (!HealerData.LastScholarHotBasePerTick.TryGetValue(name, out var basePerTick))
                        {
                            basePerTick = HealingCalculator.BaseHealCalculation(uiState, (XIVCalc.Job)Job.SCH, perTick,
                                det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                        }

                        var ticks = totalPot / perTick;
                        perTargetHeal = basePerTick * ticks;
                    }
                }

                // Shields (per-target shield applied by this action, if any)
                if (schShields != null && schShields.TryGetValue(name, out var shieldVal2))
                {
                    perTargetShield = shieldVal2;
                }

                // Apply recitation adjustments via shared adjuster
                perTargetHeal = RecitationAdjuster.MaybeApplyRecitationCrit(name, perTargetHeal, false, IsRecitationCritTarget(name), recitationEffectivelyAvailable, critRate, critMult);
                perTargetShield = RecitationAdjuster.MaybeApplyRecitationCrit(name, perTargetShield, true, IsRecitationCritTarget(name), recitationEffectivelyAvailable, critRate, critMult);

                if (perTargetHeal <= 0 && perTargetShield <= 0) continue;

                var totalEffective = 0d;

                foreach (var mem in injured)
                {
                    var missingHp = (double)(mem.MaxHp - mem.CurrentHp);
                    // Heals restore up to missing HP, shields add effective HP restoration as well
                    var effectiveHeal = Math.Min(perTargetHeal, missingHp);
                    totalEffective += effectiveHeal + perTargetShield;
                }

                if (totalEffective > 0) areaCandidates.Add((action, name, totalEffective));
            }

            // Make a best-candidate diagnostic so the UI can display a suggestion even if no action is finally chosen.
            // Prefer to display a single-target suggestion in the UI; fallback to area if none exist
            if (singleCandidates.Count > 0)
            {
                // default to the highest-priority single-target candidate (OGCD instant > OGCD HoT > GCD instant > GCD HoT) then smallest healAmount
                var defaultSingle = singleCandidates.OrderBy(Priority).ThenBy(s => s.healAmount).First();
                OptimizedHealChoice = defaultSingle.name;
            }
            else if (areaCandidates.Count > 0)
            {
                var bestArea = areaCandidates.OrderByDescending(a => a.totalEffectiveHeal).First();
                OptimizedHealChoice = bestArea.name;
            }

            // Decide whether to prefer an area
            var preferArea = injuredCount > 1 || PartyMembersAverHP < HealthAreaAbility;
            if (preferArea && areaCandidates.Count > 0)
            {
                areaCandidates.Sort((a, b) => b.totalEffectiveHeal.CompareTo(a.totalEffectiveHeal));
                var bestArea = areaCandidates[0];

                // compute a single-target aggregate for injured members
                var singleAggregate = 0d;
                foreach (var mem in injured)
                {
                    var missing = (double)(mem.MaxHp - mem.CurrentHp);
                    var bestForThis = 0d;
                    foreach (var s in singleCandidates)
                    {
                        if (!s.action.CanUse(out var ready) || ready == null) continue;
                        bestForThis = Math.Max(bestForThis, Math.Min(s.healAmount, missing));
                    }

                    singleAggregate += bestForThis;
                }

                if (bestArea.totalEffectiveHeal >= singleAggregate && bestArea.action.CanUse(out var chosenArea) &&
                    chosenArea != null)
                {
                    act = chosenArea;
                    OptimizedHealChoice = chosenArea.Name;
                    return true;
                }
            }

            // Fallback: use the highest-priority sufficient single-target action per member
            if (singleCandidates.Count == 0) return false;
            singleCandidates.Sort((a, b) =>
            {
                var pa = Priority(a);
                var pb = Priority(b);
                return pa != pb ? pa.CompareTo(pb) : a.healAmount.CompareTo(b.healAmount);
            });

            var allMembers = PartyMembers.Where(m => !m.IsDead && !m.NoNeedHealingInvuln())
                .OrderBy(m => m.GetHealthRatio()).ToList();
            foreach (var member in allMembers)
            {
                var missingHp = (double)(member.MaxHp - member.CurrentHp);
                if (missingHp <= 0) continue;

                foreach (var s in singleCandidates)
                {
                    if (!s.action.CanUse(out var chosen) || chosen == null) continue;
                    if (s.healAmount >= missingHp)
                    {
                        act = chosen;
                        OptimizedHealChoice = chosen.Name;
                        return true;
                    }
                }
            }

            // LastBestHealByMissingHp is set right before returning when a choice is made.
        }
        catch
        {
            // ignored
        }

        return false;

        int Priority(
            (IBaseAction action, string name, double healAmount, bool isHoT, bool isShield, HealerData.HealType healType
                , HealerData.HealEffectType effectType) s)
        {
            if (s.healType == HealerData.HealType.OGCD)
            {
                switch (s.effectType)
                {
                    case HealerData.HealEffectType.Instant:
                        return 0;
                    case HealerData.HealEffectType.HealOverTime:
                        return 1;
                }
            }
            else
            {
                switch (s.effectType)
                {
                    case HealerData.HealEffectType.Instant:
                        return 2;
                    case HealerData.HealEffectType.HealOverTime:
                        return 3;
                }
            }

            return 4;
        }
    }

    private IBaseAction? GetActionByName(string name)
    {
        if (AdloquiumPvE.Name == name) return AdloquiumPvE;
        if (ManifestationPvE.Name == name) return ManifestationPvE;
        if (SuccorPvE.Name == name) return SuccorPvE;
        if (ConcitationPvE.Name == name) return ConcitationPvE;
        if (AccessionPvE.Name == name) return AccessionPvE;
        if (PhysickPvE.Name == name) return PhysickPvE;
        if (LustratePvE.Name == name) return LustratePvE;
        if (ExcogitationPvE.Name == name) return ExcogitationPvE;
        if (WhisperingDawnPvE.Name == name) return WhisperingDawnPvE;
        if (SacredSoilPvE.Name == name) return SacredSoilPvE;
        if (FeyIlluminationPvE.Name == name) return FeyIlluminationPvE;
        if (IndomitabilityPvE.Name == name) return IndomitabilityPvE;
        if (FeyBlessingPvE.Name == name) return FeyBlessingPvE;
        if (ConsolationPvE.Name == name) return ConsolationPvE;
        if (AetherpactPvE.Name == name) return AetherpactPvE;
        if (SeraphismPvE.Name == name) return SeraphismPvE;
        if (ProtractionPvE.Name == name) return ProtractionPvE;
        if (RecitationPvE.Name == name) return RecitationPvE;
        // Unknown or unmapped
        return null;
    }

}