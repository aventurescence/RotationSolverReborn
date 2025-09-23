using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using XIVCalc;
using XIVCalc.Calculations;
using XIVCalc.Interfaces;
using XIVCalc.Jobs;

namespace RotationSolver.MathData;

/// <summary>
/// Provides methods for calculating healing, damage, and HoT values for FFXIV jobs based on stat blocks and game formulas.
/// </summary>
public class HealingCalc
{
    private static (uint Ilvl, int Dmg)? _cachedIlvl;
    private static bool _loggedStatsInfo;
    private static bool _loggedResults;
    private static bool _loggedBaseHeal;
    private static int _lastLoggedStatHash;

    // Last calculated values (private backing fields)

    /// <summary>
    /// Gets the last calculated average damage value.
    /// </summary>
    public static double LastAvgDamage { get; private set; }

    /// <summary>
    /// Gets the last calculated normal (non-crit) damage value.
    /// </summary>
    public static double LastNormalDamage { get; private set; }

    /// <summary>
    /// Gets the last calculated critical damage value.
    /// </summary>
    public static double LastCritDamage { get; private set; }

    /// <summary>
    /// Gets the last calculated average healed value.
    /// </summary>
    public static double LastAvgHeal { get; private set; }

    /// <summary>
    /// Gets the last calculated normal (non-crit) heal value.
    /// </summary>
    public static double LastNormalHeal { get; private set; }

    /// <summary>
    /// Gets the last calculated critical healing value.
    /// </summary>
    public static double LastCritHeal { get; private set; }

    /// <summary>
    /// Gets the last calculated average HoT value.
    /// </summary>
    public static double LastAvgHot { get; private set; }

    /// <summary>
    /// Gets the last calculated normal (non-crit) HoT value.
    /// </summary>
    public static double LastNormalHot { get; private set; }

    /// <summary>
    /// Gets the last calculated critical HoT value.
    /// </summary>
    public static double LastCritHot { get; private set; }

    /// <summary>
    /// Calculates expected output values (damage, heal, HoT) for the given player state and job.
    /// </summary>
    /// <param name="uiState">Pointer to the UIState struct.</param>
    /// <param name="jobId">The job to calculate for.</param>
    /// <param name="det">Determination stat override.</param>
    /// <param name="critMult">Critical multiplier override.</param>
    /// <param name="critRate">Critical rate override.</param>
    /// <param name="dh">Direct hit stat override.</param>
    /// <param name="ten">Tenacity stat override.</param>
    /// <param name="ilvlSync">Item level sync value.</param>
    /// <param name="ilvlSyncType">Item level sync type.</param>
    /// <returns>Tuple of (AvgDamage, NormalDamage, CritDamage, AvgHeal, NormalHeal, CritHeal, AvgHot, NormalHot, CritHot).</returns>
    public static unsafe (double AvgDamage, double NormalDamage, double CritDamage, double AvgHeal, double NormalHeal,
        double CritHeal, double AvgHot, double NormalHot, double CritHot)
        CalcExpectedOutput(UIState* uiState, Job jobId, double det, double critMult, double critRate,
            double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
    {
        try
        {
            var lvl = uiState->PlayerState.CurrentLevel;
            var attrs = uiState->PlayerState.Attributes;

            var weaponDamageInt = GetWeaponBaseDamage(jobId, ilvlSync, ilvlSyncType, lvl);
            var (critOverride, detOverride, dhOverride, tenOverride) = GetStatOverrides(lvl, critMult, critRate, det, dh, ten);
            var attrsArr = attrs.ToArray();
            var statBlock = new UiStateStatBlock(lvl, weaponDamageInt, jobId, attrsArr, detOverride, critOverride, dhOverride, tenOverride);
            var eq = new StatBlockEquations(statBlock);

            ResetLogGuardsIfNeeded(jobId, statBlock);
            LogStatsInfo(eq, statBlock, lvl, jobId);

            const int potency = 100;
            var (normalDamage, avgDamage, critDamage) = CalculateDamage(eq, potency, critMult);
            var (normalHeal, avgHeal, critHeal) = CalculateHeal(eq, statBlock, potency, critMult, critRate);
            var (normalHot, avgHot, critHot) = CalculateHot(eq, statBlock, potency, critMult, critRate);

            LogResults(normalHeal, avgHeal, critHeal, critMult, statBlock, eq);

            // Store last-calculated values
            LastAvgDamage = avgDamage;
            LastNormalDamage = normalDamage;
            LastCritDamage = critDamage;
            LastAvgHeal = avgHeal;
            LastNormalHeal = normalHeal;
            LastCritHeal = critHeal;
            LastAvgHot = avgHot;
            LastNormalHot = normalHot;
            LastCritHot = critHot;

            return (avgDamage, normalDamage, critDamage, avgHeal, normalHeal, critHeal, avgHot, normalHot, critHot);
        }
        catch (Exception e)
        {
            PluginLog.Warning($"Failed to calculate raw damage:{e}");
            return (0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d);
        }
    }

    /// <summary>
    /// Calculates the average heal for a given potency.
    /// </summary>
    /// <param name="uiState">Pointer to the UIState struct.</param>
    /// <param name="jobId">The job to calculate for.</param>
    /// <param name="potency">The heal potency.</param>
    /// <param name="det">Determination stat override.</param>
    /// <param name="critMult">Critical multiplier override.</param>
    /// <param name="critRate">Critical rate override.</param>
    /// <param name="dh">Direct hit stat override.</param>
    /// <param name="ten">Tenacity stat override.</param>
    /// <param name="ilvlSync">Item level sync value.</param>
    /// <param name="ilvlSyncType">Item level sync type.</param>
    /// <returns>Average heal value for the given potency.</returns>
    public static unsafe double CalcAverageHealForPotency(UIState* uiState, Job jobId, int potency, double det, double critMult, double critRate,
        double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
    {
        try
        {
            var lvl = uiState->PlayerState.CurrentLevel;
            var attrs = uiState->PlayerState.Attributes;

            var weaponDamageInt = GetWeaponBaseDamage(jobId, ilvlSync, ilvlSyncType, lvl);
            var (critOverride, detOverride, dhOverride, tenOverride) = GetStatOverrides(lvl, critMult, critRate, det, dh, ten);
            var attrsArr = attrs.ToArray();
            var statBlock = new UiStateStatBlock(lvl, weaponDamageInt, jobId, attrsArr, detOverride, critOverride, dhOverride, tenOverride);
            var eq = new StatBlockEquations(statBlock);

            var (_, avgHeal, _) = CalculateHeal(eq, statBlock, potency, critMult, critRate);
            return avgHeal;
        }
        catch (Exception e)
        {
            PluginLog.Warning($"Failed to calculate average heal for potency {potency}: {e}");
            return 0d;
        }
    }

    /// <summary>
    /// Calculates the base heal for a given potency.
    /// </summary>
    /// <param name="uiState">Pointer to the UIState struct.</param>
    /// <param name="jobId">The job to calculate for.</param>
    /// <param name="potency">The heal potency.</param>
    /// <param name="det">Determination stat override.</param>
    /// <param name="critMult">Critical multiplier override.</param>
    /// <param name="critRate">Critical rate override.</param>
    /// <param name="dh">Direct hit stat override.</param>
    /// <param name="ten">Tenacity stat override.</param>
    /// <param name="ilvlSync">Item level sync value.</param>
    /// <param name="ilvlSyncType">Item level sync type.</param>
    /// <returns>Base heal value for the given potency.</returns>
    public static unsafe double BaseHealCalculation(UIState* uiState, Job jobId, int potency, double det, double critMult, double critRate,
        double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
    {
        try
        {
            var lvl = uiState->PlayerState.CurrentLevel;
            var attrs = uiState->PlayerState.Attributes;

            var weaponDamageInt = GetWeaponBaseDamage(jobId, ilvlSync, ilvlSyncType, lvl);
            var (critOverride, detOverride, dhOverride, tenOverride) = GetStatOverrides(lvl, critMult, critRate, det, dh, ten);
            var attrsArr = attrs.ToArray();
            var statBlock = new UiStateStatBlock(lvl, weaponDamageInt, jobId, attrsArr, detOverride, critOverride, dhOverride, tenOverride);
            var eq = new StatBlockEquations(statBlock);

            var baseHeal = BaseHeal(eq, statBlock, potency);
            return baseHeal;
        }
        catch (Exception e)
        {
            PluginLog.Warning($"Failed to calculate base heal for potency {potency}: {e}");
            return 0d;
        }
    }

    private static unsafe int GetWeaponBaseDamage(Job jobId, uint? ilvlSync, IlvlSyncType ilvlSyncType, int lvl)
    {
        var inventoryExcelData = (ushort*)((IntPtr)InventoryManager.Instance() + 9360);
        var weaponBaseDamage = inventoryExcelData[jobId.IsCaster() ? 21 : 20] + inventoryExcelData[33];
        if (ilvlSync != null && (inventoryExcelData[39] > lvl || ilvlSyncType == IlvlSyncType.Strict))
        {
            if (_cachedIlvl?.Ilvl != ilvlSync)
                _cachedIlvl = (ilvlSync.Value, Svc.Data.GetExcelSheet<ItemLevel>().GetRow(ilvlSync.Value).PhysicalDamage);
            weaponBaseDamage = Math.Min(_cachedIlvl.Value.Dmg, weaponBaseDamage);
        }
        return weaponBaseDamage;
    }

    private static (int? critOverride, int? detOverride, int? dhOverride, int? tenOverride) GetStatOverrides(int lvl, double critMult, double critRate, double det, double dh, double ten)
    {
        int? critOverride = null;
        if (!double.IsNaN(critMult) && critMult > 10)
        {
            critOverride = Convert.ToInt32(critMult);
        }
        else
        {
            try
            {
                if (critRate is > 0 and <= 1)
                {
                    var sub = LevelTable.SUB(lvl);
                    var div = LevelTable.DIV(lvl);
                    var estimate = ((critRate * 1000d) - 50d) * div / 200d + sub;
                    var estInt = (int)Math.Round(estimate);
                    var bestErr = double.MaxValue;
                    var best = Math.Max(0, estInt);
                    for (var c = Math.Max(0, estInt - 5); c <= estInt + 5; c++)
                    {
                        var cr = StatEquations.CritChance(c, lvl);
                        var err = Math.Abs(cr - critRate);
                        if (err < bestErr)
                        {
                            bestErr = err;
                            best = c;
                        }
                    }
                    critOverride = best;
                }
            }
            catch { /* fall back to using UI stat if inversion fails */ }
        }
        int? detOverride = double.IsNaN(det) ? null : Convert.ToInt32(det);
        int? dhOverride = double.IsNaN(dh) ? null : Convert.ToInt32(dh);
        int? tenOverride = double.IsNaN(ten) ? null : Convert.ToInt32(ten);
        return (critOverride, detOverride, dhOverride, tenOverride);
    }

    private static void ResetLogGuardsIfNeeded(Job jobId, UiStateStatBlock statBlock)
    {
        var currentStatHash = HashCode.Combine((int)jobId, statBlock.Level, statBlock.WeaponDamage, statBlock.Mind, statBlock.Determination, statBlock.CriticalHit, statBlock.DirectHit, statBlock.Tenacity);
        currentStatHash = HashCode.Combine(currentStatHash, statBlock.SpellSpeed);
        if (_lastLoggedStatHash != currentStatHash)
        {
            _loggedStatsInfo = false;
            _loggedResults = false;
            _loggedBaseHeal = false;
            _lastLoggedStatHash = currentStatHash;
        }
    }

    private static void LogStatsInfo(StatBlockEquations eq, UiStateStatBlock statBlock, int lvl, Job jobId)
    {
        try
        {
            if (!_loggedStatsInfo)
            {
                PluginLog.Information($"[CalcExpectedOutput] Level={lvl} Job={jobId} MainStat={statBlock.Mind} WeaponDamage={statBlock.WeaponDamage}");
                PluginLog.Information($"[CalcExpectedOutput] CriticalHitStat={statBlock.CriticalHit} CritChance={eq.CritChance():P2} CritDamage={eq.CritDamage():P0}");
                PluginLog.Information($"[CalcExpectedOutput] DeterminationStat={statBlock.Determination} DeterminationMultiplier={eq.DeterminationMultiplier():P4}");
                PluginLog.Information($"[CalcExpectedOutput] DirectHitStat={statBlock.DirectHit} DirectHitChance={eq.DirectHitChance():P2}");
                PluginLog.Information($"[CalcExpectedOutput] TenacityStat={statBlock.Tenacity} TenacityOffensive={eq.TenacityOffensiveModifier():P4}");
                PluginLog.Information($"[CalcExpectedOutput] WeaponDamageMultiplier={eq.WeaponDamageMultiplier():F4} MainStatMultiplier={eq.MainStatMultiplier():F4} Trait={eq.GetTraitModifier():F4}");
                _loggedStatsInfo = true;
            }
        }
        catch { /* ignored */ }
    }

    private static (double normalDamage, double avgDamage, double critDamage) CalculateDamage(StatBlockEquations eq, int potency, double critMult)
    {
        var normalDamage = Math.Floor(eq.BaseDamage(potency));
        var avgDamage = Math.Floor(eq.AverageSkillDamage(potency));
        var critMultiplier = (critMult > 0) ? critMult : eq.CritDamage();
        var critDamage = Math.Floor(normalDamage * critMultiplier);
        return (normalDamage, avgDamage, critDamage);
    }

    private static (double normalHeal, double avgHeal, double critHeal) CalculateHeal(StatBlockEquations eq, UiStateStatBlock statBlock, int potency, double critMult, double critRate)
    {
        var normalHeal = Math.Floor(BaseHeal(eq, statBlock, potency));
        var avgHeal = Math.Floor(10 * BaseHeal(eq, statBlock, potency) * (1 + (critMult - 1) * critRate)) / 10;
        var critMultiplier = (critMult > 0) ? critMult : eq.CritDamage();
        var critHeal = Math.Floor(normalHeal * critMultiplier);
        LogBaseHeal(eq, statBlock, potency, normalHeal);
        return (normalHeal, avgHeal, critHeal);
    }

    private static (double normalHot, double avgHot, double critHot) CalculateHot(StatBlockEquations eq, UiStateStatBlock statBlock, int potency, double critMult, double critRate)
    {
        var normalHot = Math.Floor(ComputeHotForRoll(eq, statBlock, potency, 100));
        var critMultiplierForHot = (critMult > 0) ? critMult : eq.CritDamage();
        var critHot = Math.Floor(normalHot * critMultiplierForHot);
        var cr = (critRate is > 0 and <= 1) ? critRate : eq.CritChance();
        var avgHot = Math.Floor(10 * ((1 - cr) * normalHot + cr * critHot)) / 10.0;
        try
        {
            if (!_loggedResults)
            {
                PluginLog.Information($"[HealingCalc.HoT] normalMid={normalHot} avgHot={avgHot} critMid={critHot}");
            }
        }
        catch
        {
            // ignored
        }

        return (normalHot, avgHot, critHot);
    }

    private static void LogResults(double normalHeal, double avgHeal, double critHeal, double critMultiplier, UiStateStatBlock statBlock, StatBlockEquations eq)
    {
        try
        {
            if (!_loggedResults)
            {
                PluginLog.Information($"[HealingCalc.Result] normalHeal={normalHeal} avgHeal={avgHeal} critHeal={critHeal} critMultiplier={critMultiplier:F4}");
                if (normalHeal == 0)
                {
                    PluginLog.Warning($"[HealingCalc.ZeroHeal] normalHeal==0. Stats: Mind={statBlock.Mind} HealingMagicPotency={statBlock.HealingMagicPotency} Determination={statBlock.Determination} WeaponDamage={statBlock.WeaponDamage}");
                    PluginLog.Warning($"[HealingCalc.ZeroHeal] Multipliers: MainStat={eq.MainStatMultiplier():F6} Determination={eq.DeterminationMultiplier():F6} WeaponDamageMult={eq.WeaponDamageMultiplier():F6} Trait={eq.GetTraitModifier():F6}");
                }
                _loggedResults = true;
            }
        }
        catch
        {
            // ignored
        }
    }

    private static void LogBaseHeal(StatBlockEquations eq, UiStateStatBlock statBlock, int potency, double h)
    {
        try
        {
            if (!_loggedBaseHeal)
            {
                var hmpMulti = eq.MainStatMultiplier();
                var detMulti = eq.DeterminationMultiplier();
                var h1 = Math.Floor(potency * hmpMulti * detMulti);
                h1 = Math.Floor(h1 / 100.0);
                var h2 = Math.Floor(h1 * statBlock.WeaponDamage);
                var h3 = Math.Floor(h2 * eq.GetTraitModifier());
                const int rand = 100;

                PluginLog.Information($"[HealingCalc.BaseHeal] int H1={h1} H2={h2} H3={h3} deterministicRoll={rand} final={h} (trait={eq.GetTraitModifier():F4})");
                _loggedBaseHeal = true;
            }
        }
        catch
        {
            // ignored
        }
    }

    private static double BaseHeal(StatBlockEquations eq, UiStateStatBlock statBlock, int potency)
    {
        var hmpMulti = eq.MainStatMultiplier();
        var detMulti = eq.DeterminationMultiplier();
        var h1 = Math.Floor(potency * hmpMulti * detMulti);
        h1 = Math.Floor(h1 / 100.0);
        var h2 = Math.Floor(h1 * statBlock.WeaponDamage);
        var h3 = Math.Floor(h2 * eq.GetTraitModifier());
        const int rand = 100;
        var h = Math.Floor(h3 * rand / 100.0);
        return h;
    }

    private static double ComputeHotForRoll(StatBlockEquations eq, UiStateStatBlock statBlock, int potency, int roll)
    {
        var hmp = eq.MainStatMultiplier();
        var detm = eq.DeterminationMultiplier();
        var spd = StatEquations.HotMultiplier(statBlock.SpellSpeed, statBlock.Level);
        var wd = statBlock.WeaponDamage;
        var trait = eq.GetTraitModifier();
        var h1 = Math.Floor(potency * hmp * detm);
        h1 = Math.Floor(h1 / 100.0);
        var h2 = Math.Floor(h1 * wd);
        var h3 = Math.Floor(h2 * trait);
        h3 = Math.Floor(h3 * spd / 100.0);
        var outv = Math.Floor(h3 * roll / 100.0);
        return outv;
    }

    // Explicit normal class implementation to avoid primary-constructor syntax issues
    private class UiStateStatBlock(
        int level,
        int weaponDamage,
        Job job,
        int[] attrs,
        int? detOverride = null,
        int? critOverride = null,
        int? dhOverride = null,
        int? tenOverride = null)
        : IJobStatBlock
    {
        private static IJobModifiers GetModifiers(Job job) => job switch
        {
            Job.ADV => StaticJobs.ADV,
            Job.GLA => StaticJobs.GLA,
            Job.PGL => StaticJobs.PGL,
            Job.MRD => StaticJobs.MRD,
            Job.LNC => StaticJobs.LNC,
            Job.ARC => StaticJobs.ARC,
            Job.CNJ => StaticJobs.CNJ,
            Job.THM => StaticJobs.THM,
            Job.PLD => StaticJobs.PLD,
            Job.MNK => StaticJobs.MNK,
            Job.WAR => StaticJobs.WAR,
            Job.DRG => StaticJobs.DRG,
            Job.BRD => StaticJobs.BRD,
            Job.WHM => StaticJobs.WHM,
            Job.BLM => StaticJobs.BLM,
            Job.ACN => StaticJobs.ACN,
            Job.SMN => StaticJobs.SMN,
            Job.SCH => StaticJobs.SCH,
            Job.ROG => StaticJobs.ROG,
            Job.NIN => StaticJobs.NIN,
            Job.MCH => StaticJobs.MCH,
            Job.DRK => StaticJobs.DRK,
            Job.AST => StaticJobs.AST,
            Job.SAM => StaticJobs.SAM,
            Job.RDM => StaticJobs.RDM,
            Job.BLU => StaticJobs.BLU,
            Job.GNB => StaticJobs.GNB,
            Job.DNC => StaticJobs.DNC,
            Job.RPR => StaticJobs.RPR,
            Job.SGE => StaticJobs.SGE,
            Job.VPR => StaticJobs.VPR,
            Job.PCT => StaticJobs.PCT,
            _ => StaticJobs.ADV,
        };

        public IJobModifiers JobModifiers { get; } = GetModifiers(job);
        public int Level => level;
        public int WeaponDamage => weaponDamage;
        public int WeaponDelay => 0;

        private int GetAttr(StatType t) => attrs[(int)t];

        public int Vitality => GetAttr(StatType.Vitality);
        public int Strength => GetAttr(StatType.Strength);
        public int Dexterity => GetAttr(StatType.Dexterity);
        public int Intelligence => GetAttr(StatType.Intelligence);
        public int Mind => GetAttr(StatType.Mind);
        public int PhysicalDefense => GetAttr(StatType.Defense);
        public int MagicalDefense => GetAttr(StatType.MagicResistance);
        public int AttackPower => GetAttr(StatType.AttackPower);
        public int AttackMagicPotency => GetAttr(StatType.AttackMagicPotency);
        public int HealingMagicPotency => GetAttr(StatType.HealingMagicPotency);

        public int DirectHit => dhOverride ?? GetAttr(StatType.DirectHitRate);
        public int CriticalHit => critOverride ?? GetAttr(StatType.CriticalHit);
        public int Determination => detOverride ?? GetAttr(StatType.Determination);
        public int SkillSpeed => GetAttr(StatType.SkillSpeed);
        public int SpellSpeed => GetAttr(StatType.SpellSpeed);

        public int Piety => GetAttr(StatType.Piety);
        public int Tenacity => tenOverride ?? GetAttr(StatType.Tenacity);
    }
}