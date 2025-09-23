using XIVCalc;
using XIVCalc.Calculations;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace RotationSolver.MathData;

public class HealerData
{
    public enum HealType { GCD, OGCD }
    public enum HealTargetType { Single, Area }
    public enum HealEffectType { None, Instant, HealOverTime }
    public enum AdditionalEffectType { HealBuff, Mitigation, Shield }

    public record HealEntry
    {
        public HealType HealType { get; private init; }
        public HealTargetType HealTargetType { get; private init; }
        public HealEffectType[] HealEffects { get; private init; } = [];
        public AdditionalEffectType[] AdditionalEffects { get; private init; } = [];
        public int? CurePotency { get; private init; }
        public int? HoTPotency { get; private init; }
        public int? HoTDuration { get; private init; }
        public float? Mitigation { get; private init; }

        public float? ShieldValue { get; private init; }

        // GCD heal with optional additional effects (including HoT/effect-only entries)
        public static HealEntry HealGCD(int curePotency,
                                       HealTargetType target = HealTargetType.Single,
                                       HealEffectType[]? healEffects = null,
                                       AdditionalEffectType[]? additional = null,
                                       int? hoTPotency = null,
                                       int? hoTDuration = null,
                                       float? mitigation = null,
                                       float? shieldValue = null)
            => new()
            {
                HealType = HealType.GCD,
                HealTargetType = target,
                HealEffects = healEffects ?? [HealEffectType.Instant],
                AdditionalEffects = additional ?? [],
                CurePotency = curePotency,
                HoTPotency = hoTPotency,
                HoTDuration = hoTDuration,
                Mitigation = mitigation,
                ShieldValue = shieldValue
            };

        // OGCD heal (curePotency optional to allow pure HoT/effect OGCDs)
        public static HealEntry HealOgcd(int? curePotency = null,
                                         HealTargetType target = HealTargetType.Single,
                                         HealEffectType[]? healEffects = null,
                                         AdditionalEffectType[]? additional = null,
                                         int? hoTPotency = null,
                                         int? hoTDuration = null,
                                         float? mitigation = null,
                                         float? shieldValue = null)
            => new()
            {
                HealType = HealType.OGCD,
                HealTargetType = target,
                HealEffects = healEffects ?? [HealEffectType.Instant],
                AdditionalEffects = additional ?? [],
                CurePotency = curePotency,
                HoTPotency = hoTPotency,
                HoTDuration = hoTDuration,
                Mitigation = mitigation,
                ShieldValue = shieldValue
            };
    }

    // Pluggable data provider for heal tables (default builder)
    private static HealTableBuilder DataProvider { get; } = new();

    // Helper to get the canonical scholar heal table from the provider
    private static Dictionary<string, HealEntry> GetScholarTable() => DataProvider.GetScholarTable();

    public static Dictionary<string, int> GetScholarHealingPotencies()
    {
        var table = GetScholarTable();
        return table.Where(x => x.Value.CurePotency is > 0)
                    .ToDictionary(x => x.Key, x => x.Value.CurePotency!.Value);
    }

    // Returns mapping of a scholar action name to its HoT potency per tick (if defined)
    public static Dictionary<string, int> GetScholarHotPotenciesPerTick()
    {
        var table = GetScholarTable();
        return table.Where(kv => kv.Value.HoTPotency is > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.HoTPotency!.Value);
    }

    // Returns mapping of a scholar action name to its shield strength (base heal * shield multiplier).
    // Caller must provide the same stat/context parameters used for HealingCalc methods.
    private static unsafe Dictionary<string, double> GetScholarShieldValue(UIState* uiState, Job jobId, double det, double critMult, double critRate,
        double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
    {
        var table = GetScholarTable();
        var result = new Dictionary<string, double>();
        foreach (var kv in table)
        {
            var name = kv.Key;
            var entry = kv.Value;
            if (entry.CurePotency is > 0 && entry.ShieldValue is not null && entry.ShieldValue > 0f)
            {
                try
                {
                    var potency = entry.CurePotency!.Value;
                    var baseHeal = HealingCalculator.BaseHealCalculation(uiState, jobId, potency, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                    var shieldStrength = baseHeal * entry.ShieldValue.Value;
                    result[name] = shieldStrength;
                }
                catch
                {
                    result[name] = 0d;
                }
            }
        }
        return result;
    }

    // Convenience overload that uses the current UIState and pulls job/stats from it (mirrors how CalculationsDisplay uses HealingCalc).
    public static unsafe Dictionary<string, double> GetScholarShieldStrength(Job jobId, uint? ilvlSync = null, IlvlSyncType ilvlSyncType = IlvlSyncType.Strict)
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
        return GetScholarShieldValue(uiState, jobId, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
    }

    // Returns a mapping of scholar action name to total HoT potency (HoT potency * number of ticks).
    // Assumes HoT ticks occur every 3 seconds (FFXIV standard tick interval for most HoTs).
    public static Dictionary<string, int> GetScholarHotTotalPotencies()
    {
        var table = GetScholarTable();
        const int hotTickInterval = 3; // seconds per HoT tick
        return table.Where(kv => kv.Value.HoTPotency is > 0 && kv.Value.HoTDuration is > 0)
                    .ToDictionary(
                        kv => kv.Key,
                        kv =>
                        {
                            var potency = kv.Value.HoTPotency!.Value;
                            var duration = kv.Value.HoTDuration!.Value;
                            var ticks = Math.Max(0, duration / hotTickInterval);
                            return potency * ticks;
                        });
    }

    // Public helpers to expose heal metadata in a consumable form.
    // Returns mapping action name -> HealType (GCD or OGCD).
    public static Dictionary<string, HealType> GetScholarHealTypeNames()
    {
        var table = GetScholarTable();
        var result = new Dictionary<string, HealType>();
        foreach (var kv in table)
        {
            result[kv.Key] = kv.Value.HealType;
        }
        return result;
    }

    // Returns mapping action name -> "Instant" | "HealOverTime" | "None" (based on HealEffectType)
    public static Dictionary<string, HealEffectType> GetScholarHealEffectTypeNames()
     {
         var table = GetScholarTable();
         var result = new Dictionary<string, HealEffectType>();
         foreach (var kv in table)
         {
             var entry = kv.Value;
             if (entry.HoTPotency is > 0 && entry.HoTDuration is > 0)
                 result[kv.Key] = HealEffectType.HealOverTime;
             else if (entry.CurePotency is > 0)
                 result[kv.Key] = HealEffectType.Instant;
             else
                 result[kv.Key] = HealEffectType.None;
         }
         return result;
     }

    // Returns the set of Scholar action names that are area-target (based on the HealEntry table).
    // This lets callers decide area vs. single based on the canonical healer table instead of hard-coding names.
    public static HashSet<string> GetScholarAreaActionNames()
    {
        var table = GetScholarTable();
        var result = new HashSet<string>();
        foreach (var kv in table)
        {
            // HealEntry and HealTargetType are private to this class but accessible here.
            if (kv.Value is { HealTargetType: HealTargetType.Area })
            {
                result.Add(kv.Key);
            }
        }
        return result;
    }

    // Returns the set of Scholar action names that are single-target (based on the HealEntry table).
    public static HashSet<string> GetScholarSingleActionNames()
    {
        var table = GetScholarTable();
        var result = new HashSet<string>();
        foreach (var kv in table)
        {
            if (kv.Value is { HealTargetType: HealTargetType.Single })
            {
                result.Add(kv.Key);
            }
        }
        return result;
    }

    // --- CACHING API --------------------------------------------------
    // Cache backing (delegates to IHealerCache for testability)
    private static readonly HealerCache Cache = new();

    // Cached deterministic base heals (per-action, deterministic BaseHeal for the action's cure potency)
    public static Dictionary<string, double> LastScholarBaseHeals => Cache.LastScholarBaseHeals;
    // Cached HoT base per tick
    public static Dictionary<string, double> LastScholarHotBasePerTick => Cache.LastScholarHotBasePerTick;
    // Cached HoT total base (perTickBase * ticks)
    public static Dictionary<string, double> LastScholarHotTotalBase => Cache.LastScholarHotTotalBase;
    // Cached shield strengths (base heal * shield multiplier)
    public static Dictionary<string, double> LastScholarShieldStrengths => Cache.LastScholarShieldStrengths;

    public static unsafe void UpdateScholarCaches(UIState* uiState, Job jobId, double det, double critMult, double critRate,
        double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
    {
        var table = GetScholarTable();
        var baseHeals = new Dictionary<string, double>();
        var hotPerTick = new Dictionary<string, double>();
        var hotTotal = new Dictionary<string, double>();
        // Use an existing shield builder which multiplies base heal by shield multiplier
        var shields = GetScholarShieldValue(uiState, jobId, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);

        foreach (var kv in table)
        {
            var name = kv.Key;
            var entry = kv.Value;
            if (entry.CurePotency is > 0)
            {
                // HealingCalc expects ECommons.ExcelServices.Job for jobId; cast from XIVCalc.Job
                var baseVal = HealingCalculator.BaseHealCalculation(uiState, jobId, entry.CurePotency!.Value, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                baseHeals[name] = baseVal;
            }

            if (entry is { HoTPotency: > 0, HoTDuration: > 0 })
            {
                var perTickPot = entry.HoTPotency!.Value;
                var basePerTick = HealingCalculator.BaseHealCalculation(uiState, jobId, perTickPot, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                hotPerTick[name] = basePerTick;
                var ticks = Math.Max(0, entry.HoTDuration!.Value / 3);
                hotTotal[name] = basePerTick * ticks;
            }
        }

        // Swap into public caches (via cache interface)
        Cache.SetCaches(baseHeals, hotPerTick, hotTotal, shields ?? new Dictionary<string, double>());
     }

     // --- end cache API ------------------------------------------------
 }