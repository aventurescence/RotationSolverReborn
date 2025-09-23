namespace RotationSolver.MathData
{
    public static class RecitationAdjuster
    {
        // Adjusts a base heal or shield value to include recitation-driven crit expectation.
        // isRecitationTarget should be computed by caller (whether the action is one of the 4 Scholar recitation targets).
        public static double MaybeApplyRecitationCrit(string name, double baseValue, bool isShield, bool isRecitationTarget,
            bool recitationEffectivelyAvailable, double critRate, double critMult)
        {
            if (!recitationEffectivelyAvailable || !isRecitationTarget || baseValue <= 0) return baseValue;
            if (isShield)
            {
                // On crit, shield value doubles -> expected shield = base*(1 - p) + (2*base)*p = base*(1 + p)
                return baseValue * (1.0 + critRate);
            }

            // expected heal considering crit chance and crit multiplier
            return baseValue * ((1.0 - critRate) + critRate * critMult);
        }
    }
}