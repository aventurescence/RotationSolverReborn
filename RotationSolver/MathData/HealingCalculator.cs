using FFXIVClientStructs.FFXIV.Client.Game.UI;
using XIVCalc;

namespace RotationSolver.MathData
{
    public class HealingCalculator : IHealingCalculator
    {

        private static HealingCalculator Default { get; } = new();

        public unsafe double CalcBaseHealForPotency(UIState* uiState, Job jobId, int potency, double det, double critMult, double critRate, double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
        {
            return HealingCalc.BaseHealCalculation(uiState, jobId, potency, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
        }

        public static unsafe double BaseHealCalculation(UIState* uiState, Job jobId, int potency, double det, double critMult, double critRate, double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType)
        {
            return Default.CalcBaseHealForPotency(uiState, jobId, potency, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
        }
    }
}