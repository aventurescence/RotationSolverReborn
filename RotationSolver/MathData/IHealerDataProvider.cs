using System.Collections.Generic;

namespace RotationSolver.MathData
{
    public interface IHealerDataProvider
    {
        Dictionary<string, HealerData.HealEntry> GetScholarTable();
    }
}