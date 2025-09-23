using RotationSolver.RebornRotations.Healer;
using static RotationSolver.MathData.HealerData.HealEntry;
using static RotationSolver.MathData.HealerData.HealTargetType;
using static RotationSolver.MathData.HealerData.HealEffectType;
using static RotationSolver.MathData.HealerData.AdditionalEffectType;

namespace RotationSolver.MathData;

public class HealTableBuilder : IHealerDataProvider
{
    public Dictionary<string, HealerData.HealEntry> GetScholarTable()
    {
        ScholarRotation rotation = new SCH_Reborn();
        var table = new Dictionary<string, HealerData.HealEntry>
        {
            [rotation.AdloquiumPvE.Name] = HealGCD(300, HealerData.HealTargetType.Single, [Instant], [Shield], 0, 0, 0, 1.8f),
            [rotation.ManifestationPvE.Name] = HealGCD(360, HealerData.HealTargetType.Single, [Instant], [Shield], 0, 0, 0, 1.8f),
            [rotation.SuccorPvE.Name] = HealGCD(200, Area, [Instant], [Shield], 0, 0, 0, 1.6f),
            [rotation.ConcitationPvE.Name] = HealGCD(200, Area, [Instant], [Shield], 0, 0, 0, 1.8f),
            [rotation.AccessionPvE.Name] = HealGCD(240, Area, [Instant], [Shield], 0, 0, 0, 1.8f),
            [rotation.PhysickPvE.Name] = HealGCD(450, HealerData.HealTargetType.Single, [Instant]),
            [rotation.LustratePvE.Name] = HealOgcd(600, HealerData.HealTargetType.Single, [Instant]),
            [rotation.ExcogitationPvE.Name] = HealOgcd(800, HealerData.HealTargetType.Single, [Instant]),
            [rotation.WhisperingDawnPvE.Name] = HealOgcd(null, Area, [HealOverTime], null, 80, 21),
            [rotation.SacredSoilPvE.Name] = HealOgcd(null, Area, [HealOverTime], [Mitigation], 100, 15, 0.9f),
            [rotation.FeyIlluminationPvE.Name] = HealOgcd(null, Area, [None], [HealBuff, Mitigation], null, null, 0.9f),
            [rotation.IndomitabilityPvE.Name] = HealOgcd(400, Area),
            [rotation.FeyBlessingPvE.Name] = HealOgcd(320, Area),
            [rotation.ConsolationPvE.Name] = HealOgcd(250, Area, null, [Shield], 0,0,0,1) };
        var aetherpactHotDuration = (ScholarRotation.FairyGauge / 10) * 3;
        table[rotation.AetherpactPvE.Name] = HealOgcd(null, HealerData.HealTargetType.Single, [HealOverTime], null, 300, aetherpactHotDuration);
        table[rotation.SeraphismPvE.Name] = HealOgcd(null, Area, [HealOverTime], null, 100, 20);
        return table;
    }
}