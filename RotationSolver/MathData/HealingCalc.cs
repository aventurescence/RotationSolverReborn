using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using XIVCalc;
using XIVCalc.Calculations;
using XIVCalc.Interfaces;
using XIVCalc.Jobs;
using System;

namespace RotationSolver.MathData;

public class HealingCalc
{
    private static (uint Ilvl, int Dmg)? _cachedIlvl;

    // Return tuple: AvgDamage, NormalDamage, CritDamage, AvgHeal, NormalHeal, CritHeal,
    // AvgHot, NormalHot, HotMin, HotMid, HotMax, CritHot
    public static unsafe (double AvgDamage, double NormalDamage, double CritDamage, double AvgHeal, double NormalHeal,
        double CritHeal, double AvgHot, double NormalHot, double HotMin, double HotMid, double HotMax, double CritHot)
        CalcExpectedOutput(UIState* uiState, Job jobId, double det, double critMult, double critRate,
            double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
    {
        try
        {
            var lvl = uiState->PlayerState.CurrentLevel;
            var attrs = uiState->PlayerState.Attributes;

            var inventoryExcelData = (ushort*)((IntPtr)InventoryManager.Instance() + 9360);
            var weaponBaseDamage = inventoryExcelData[jobId.IsCaster() ? 21 : 20] + inventoryExcelData[33];
            if (ilvlSync != null && (inventoryExcelData[39] > lvl || ilvlSyncType == IlvlSyncType.Strict))
            {
                if (_cachedIlvl?.Ilvl != ilvlSync)
                    _cachedIlvl = (ilvlSync.Value, Svc.Data.GetExcelSheet<ItemLevel>().GetRow(ilvlSync.Value).PhysicalDamage);
                weaponBaseDamage = Math.Min(_cachedIlvl.Value.Dmg, weaponBaseDamage);
            }
            var weaponDamageInt = weaponBaseDamage;

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
                catch
                {
                    /* fall back to using UI stat if inversion fails */
                }
            }

            int? detOverride = double.IsNaN(det) ? null : Convert.ToInt32(det);
            int? dhOverride = double.IsNaN(dh) ? null : Convert.ToInt32(dh);
            int? tenOverride = double.IsNaN(ten) ? null : Convert.ToInt32(ten);

            var attrsArr = attrs.ToArray();
            var statBlock = new UiStateStatBlock(lvl, weaponDamageInt, jobId, attrsArr, detOverride, critOverride, dhOverride, tenOverride);
            var eq = new StatBlockEquations(statBlock);

            try
            {
                PluginLog.Information(
                    $"[CalcExpectedOutput] Level={lvl} Job={jobId} MainStat={statBlock.Mind} WeaponDamage={statBlock.WeaponDamage}");
                PluginLog.Information(
                    $"[CalcExpectedOutput] CriticalHitStat={statBlock.CriticalHit} CritChance={eq.CritChance():P2} CritDamage={eq.CritDamage():P0}");
                PluginLog.Information(
                    $"[CalcExpectedOutput] DeterminationStat={statBlock.Determination} DeterminationMultiplier={eq.DeterminationMultiplier():P4}");
                PluginLog.Information(
                    $"[CalcExpectedOutput] DirectHitStat={statBlock.DirectHit} DirectHitChance={eq.DirectHitChance():P2}");
                PluginLog.Information(
                    $"[CalcExpectedOutput] TenacityStat={statBlock.Tenacity} TenacityOffensive={eq.TenacityOffensiveModifier():P4}");
                PluginLog.Information(
                    $"[CalcExpectedOutput] WeaponDamageMultiplier={eq.WeaponDamageMultiplier():F4} MainStatMultiplier={eq.MainStatMultiplier():F4} Trait={eq.GetTraitModifier():F4}");
            }
            catch
            {
                // ignored
            }

            const int potency = 100;
            var normalDamage = Math.Floor(eq.BaseDamage(potency));
            var avgDamage = Math.Floor(eq.AverageSkillDamage(potency));
            var critMultiplier = (critMult > 0) ? critMult : eq.CritDamage();
            var critDamage = Math.Floor(normalDamage * critMultiplier);

            // For healing
            var normalHeal = Math.Floor(BaseHeal());
            var avgHeal = Math.Floor(AverageHeal());
            var critHeal = Math.Floor(normalHeal * critMultiplier);

            // HoT calculations (deterministic min/mid/max)
            double ComputeHotForRoll(int roll)
            {
                var hmp = eq.MainStatMultiplier();
                var detm = eq.DeterminationMultiplier();
                var tnc = eq.TenacityOffensiveModifier();
                var spd = StatEquations.HotMultiplier(statBlock.SpellSpeed, statBlock.Level);
                var wd = statBlock.WeaponDamage;
                var trait = eq.GetTraitModifier();

                var h1 = Math.Floor(potency * hmp * detm);
                h1 = Math.Floor(h1 / 100.0);

                var h2 = Math.Floor(h1 * tnc);
                h2 = Math.Floor(h2 / 100.0);

                h2 = Math.Floor(h2 * spd);
                h2 = Math.Floor(h2 / 100.0);

                h2 = Math.Floor(h2 * wd);
                h2 = Math.Floor(h2 / 100.0);

                h2 = Math.Floor(h2 * trait);
                h2 = Math.Floor(h2 / 100.0);

                var outv = Math.Floor(h2 * roll / 100.0);
                return outv;
            }

            double baseHotMin = 0, baseHotMid = 0, baseHotMax = 0;
            double normalHot = 0, avgHot = 0, critHot = 0;
            try
            {
                baseHotMin = ComputeHotForRoll(97);
                baseHotMid = ComputeHotForRoll(100);
                baseHotMax = ComputeHotForRoll(103);

                normalHot = Math.Floor(baseHotMid);
                var critPre = Math.Floor(baseHotMid * eq.CritDamage());
                critHot = Math.Floor(critPre / 1000.0);
                var cr = (critRate is > 0 and <= 1) ? critRate : eq.CritChance();
                avgHot = Math.Floor(10 * ((1 - cr) * normalHot + cr * critHot)) / 10.0;

                PluginLog.Information(
                    $"[HealingCalc.HoT] min={baseHotMin} mid={baseHotMid} max={baseHotMax} normalMid={normalHot} avgHot={avgHot} critMid={critHot}");
            }
            catch
            {
                // ignore logging failures
            }

            try
            {
                PluginLog.Information(
                    $"[HealingCalc.Result] normalHeal={normalHeal} avgHeal={avgHeal} critHeal={critHeal} critMultiplier={critMultiplier:F4}");
                if (normalHeal == 0)
                {
                    PluginLog.Warning(
                        $"[HealingCalc.ZeroHeal] normalHeal==0. Stats: Mind={statBlock.Mind} HealingMagicPotency={statBlock.HealingMagicPotency} Determination={statBlock.Determination} WeaponDamage={statBlock.WeaponDamage}");
                    PluginLog.Warning(
                        $"[HealingCalc.ZeroHeal] Multipliers: MainStat={eq.MainStatMultiplier():F6} Determination={eq.DeterminationMultiplier():F6} WeaponDamageMult={eq.WeaponDamageMultiplier():F6} Trait={eq.GetTraitModifier():F6}");
                }
            }
            catch
            {
                // ignore logging failures
            }

            // Return tuple including HoT min/mid/max
            return (avgDamage, normalDamage, critDamage, avgHeal, normalHeal, critHeal, avgHot, normalHot, baseHotMin, baseHotMid, baseHotMax, critHot);

            // Local helpers
            double AverageHeal()
            {
                var baseHeal = BaseHeal();
                return Math.Floor(10 * baseHeal * (1 + (critMult - 1) * critRate)) / 10;
            }

            double BaseHeal()
            {
                var hmpMulti = eq.MainStatMultiplier();
                var detMulti = eq.DeterminationMultiplier();

                var h1 = Math.Floor(potency * hmpMulti * detMulti);
                h1 = Math.Floor(h1 / 100.0);
                var h2 = Math.Floor(h1 * statBlock.WeaponDamage);
                var h3 = Math.Floor(h2 * eq.GetTraitModifier());

                // Deterministic midpoint roll
                const int rand = 100;
                var h = Math.Floor(h3 * rand / 100.0);

                try
                {
                    PluginLog.Information(
                        $"[HealingCalc.BaseHeal] int H1={h1} H2={h2} H3={h3} deterministicRoll={rand} final={h} (trait={eq.GetTraitModifier():F4})");
                }
                catch
                {
                    // ignore
                }

                return h;
            }
        }
        catch (Exception e)
        {
            PluginLog.Warning($"Failed to calculate raw damage:{e}");
            return (0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d);
        }
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