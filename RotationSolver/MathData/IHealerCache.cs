namespace RotationSolver.MathData
{
    public interface IHealerCache
    {
        Dictionary<string, double> LastScholarBaseHeals { get; }
        Dictionary<string, double> LastScholarHotBasePerTick { get; }
        Dictionary<string, double> LastScholarHotTotalBase { get; }
        Dictionary<string, double> LastScholarShieldStrengths { get; }

        void SetCaches(Dictionary<string, double> baseHeals,
                       Dictionary<string, double> hotPerTick,
                       Dictionary<string, double> hotTotal,
                       Dictionary<string, double> shields);
    }
}