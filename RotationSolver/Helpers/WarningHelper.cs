namespace RotationSolver.Helpers;

public static class WarningHelper
{
    public static void AddSystemWarning(string message)
    {
        DataCenter.SystemWarnings ??= [];
        var dict = DataCenter.SystemWarnings;
        lock (dict)
        {
            if (!dict.ContainsKey(message))
            {
                dict[message] = DateTime.Now;
            }
        }
    }
}
