using System.Collections.Generic;

namespace RotationSolver.MathData
{
    public class HealerCache : IHealerCache
    {
        public Dictionary<string, double> LastScholarBaseHeals { get; private set; } = new();

        public Dictionary<string, double> LastScholarHotBasePerTick { get; private set; } = new();

        public Dictionary<string, double> LastScholarHotTotalBase { get; private set; } = new();

        public Dictionary<string, double> LastScholarShieldStrengths { get; private set; } = new();

        public void SetCaches(Dictionary<string, double> baseHeals, Dictionary<string, double> hotPerTick, Dictionary<string, double> hotTotal, Dictionary<string, double> shields)
        {
            LastScholarBaseHeals = baseHeals ?? new Dictionary<string, double>();
            LastScholarHotBasePerTick = hotPerTick ?? new Dictionary<string, double>();
            LastScholarHotTotalBase = hotTotal ?? new Dictionary<string, double>();
            LastScholarShieldStrengths = shields ?? new Dictionary<string, double>();
        }
    }
}