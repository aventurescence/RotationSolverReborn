using FFXIVClientStructs.FFXIV.Client.Game.UI;
using XIVCalc;

namespace RotationSolver.MathData
{
    public interface IHealingCalculator
    {
        unsafe double CalcBaseHealForPotency(UIState* uiState, Job jobId, int potency, double det, double critMult, double critRate, double dh, double ten, uint? ilvlSync, IlvlSyncType ilvlSyncType);
    }
}