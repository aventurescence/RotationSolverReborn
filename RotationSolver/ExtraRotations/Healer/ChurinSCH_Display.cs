using RotationSolver.MathData;

namespace RotationSolver.ExtraRotations.Healer;

public sealed partial class ChurinSCH
{
    #region Tracking Properties
    public override void DisplayRotationStatus()
    {
        Vector4 green = new(0, 1, 0, 1);
        Vector4 red = new(1, 0, 0, 1);

        if (ImGui.CollapsingHeader("General Rotation Info"))
        {
            if (ImGui.BeginTable("GeneralTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Label");
                ImGui.TableSetupColumn("Value");
                ImGui.TableHeadersRow();

                var maxTargets = GetAoWBreakevenTargets() - 1;
                var maxTargetsCol = maxTargets > 0 ? green : red;
                DisplayTableRow("Max Targets to apply Bio to rather than spamming AoW", $"{maxTargets}", maxTargetsCol);

                var isBurstCol = IsBurst ? green : red;
                DisplayTableRow("Is Burst", $"{IsBurst}", isBurstCol);

                var hasChainCol = HasChainStratagem ? green : red;
                DisplayTableRow("Has Chain Stratagem", $"{HasChainStratagem}", hasChainCol);

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("Mitigation/Party Stats"))
        {
            if (ImGui.BeginTable("MitigationTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {

                var mitigation = GetCurrentMitigationPercent();
                var currMitCol = CalculationsDisplay.GetGradientColor(mitigation);
                DisplayTableRow("Current Mitigation", $"{mitigation:P1}", currMitCol);

                var enoughMitCol = EnoughMitigation ? green : red;
                DisplayTableRow("Enough Mitigation?", $"{EnoughMitigation}", enoughMitCol);

                var partyInRangeCol = FairyHelper.PartyInRange ? green : red;
                DisplayTableRow("Party In Fairy Range", $"{FairyHelper.PartyInRange}", partyInRangeCol);

                var avgHp = PartyMembersAverHP;
                var avgHpCol = CalculationsDisplay.GetGradientColor(avgHp);
                DisplayTableRow("Party Average HP", $"{avgHp:P1}", avgHpCol);

                var minHp = PartyMembersMinHP;
                var minHpCol = CalculationsDisplay.GetGradientColor(minHp);
                DisplayTableRow("Lowest Party HP", $"{LowestHealthPartyMember?.Name ?? "None"} {minHp:P1}", minHpCol);

                var noStCol = NoSingleTargetOGCD ? green : red;
                DisplayTableRow("No Single Target OGCD Heals", $"{NoSingleTargetOGCD}", noStCol);

                var noAOECol = NoAreaOGCD ? green : red;
                DisplayTableRow("No AoE OGCD Heals", $"{NoAreaOGCD}", noAOECol);

                var contDmgCol = TakingContinuousDamage()? green : red;
                DisplayTableRow("Taking Continuous Damage?", $"{TakingContinuousDamage()}", contDmgCol);

                // Recompute the best-heal candidate here to ensure the UI reflects the current state
                try { _ = TryUseOptimizedHeal(out _); } catch { /* safeguard: ignore errors from selection */ }
                // Show the last ability chosen by the automatic best-heal selector, but only if it's a single-target action
                var singleActions = HealerData.GetScholarSingleActionNames();
                var lastBest = "None";
                var lastBestCol = red;
                if (OptimizedHealChoice != null && singleActions.Contains(OptimizedHealChoice))
                {
                    lastBest = OptimizedHealChoice;
                    lastBestCol = green;
                }
                DisplayTableRow("Last Best Heal (single)", lastBest, lastBestCol);

                ImGui.EndTable();
            }
        }

        // --- Stat Breakdown Section ---
        if (ImGui.CollapsingHeader("Calculations", ImGuiTreeNodeFlags.DefaultOpen))
        {
            CalculationsDisplay.ShowCalculationsTable();
        }
    }

    private static void DisplayTableRow(string label, string value, Vector4 color)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(label);
        ImGui.TableNextColumn();
        ImGui.TextColored(color, value);
    }
    #endregion
}