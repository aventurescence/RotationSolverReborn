using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;

namespace RotationSolver.ExtraRotations.Healer;

/// <summary>
/// Utility class for identifying and working with Fairy game objects.
/// </summary>
public static class FairyHelper
{
    /// <summary>
    /// Represents the player's Fairy summon
    /// </summary>
    public static IBattleChara? Fairy
    {
        get
        {
            if (Svc.Buddies.PetBuddy == null)
            {
                return null;
            }
            var pet = Svc.Objects.SearchById(Svc.Buddies.PetBuddy.ObjectId) as IBattleChara;
            return pet?.IsValid() == true && Player.Job == Job.SCH ? pet : null;
        }
    }

    private static bool IsAvailable => Fairy != null;

    /// <summary>
    /// Returns the position of the Fairy
    /// </summary>
    private static Vector3 Position => Fairy?.Position ?? Vector3.Zero;

    /// <summary>
    /// Calculates the distance between the fairy and another point in 3D space.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    private static float DistanceTo(Vector3 other)
    {
        return IsAvailable ? Vector3.Distance(Position, other) : float.NaN;
    }

    /// <summary>
    /// Calculates the distance between the fairy and another object.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    private static float DistanceTo(IGameObject other)
    {
        return IsAvailable ? DistanceTo(other.Position) : float.NaN;
    }

    private static bool IsInRange(IGameObject other, float range)
    {
        return IsAvailable && DistanceTo(other) <= range;
    }

    /// <summary>
    /// The count of party members within the specified range.
    /// </summary>
    /// <param name="range"></param>
    /// <returns></returns>
    private static int GetNumberOfFriendliesInRange(float range)
    {
        // Only count party members that are alive
        return DataCenter.PartyMembers != null ? DataCenter.PartyMembers.Count(member => !member.IsDead && IsInRange(member, range)) : 0;
    }

    /// <summary>
    /// Whether the party (based on current max size) is fully in range of Fairy
    /// </summary>
    public static bool PartyInRange
    {
        get
        {
            if (DataCenter.PartyMembers == null) return false;
            var aliveCount = DataCenter.PartyMembers.Count(member => !member.IsDead);
            if (aliveCount == 0) return false;
            // PartyInRange is true when all alive party members are inside the fairy range (20 yalms)
            return GetNumberOfFriendliesInRange(20) == aliveCount;
        }
    }
}