using FFXIVClientStructs.FFXIV.Client.Game.UI;
using RotationSolver.MathData;
using XIVCalc;
using XIVCalc.Calculations;
using XIVCalc.Jobs;

namespace RotationSolver.ExtraRotations.Healer;

public sealed partial class ChurinSCH
{
    #region Tracking Properties
    public override unsafe void DisplayRotationStatus()
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

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Max Targets to apply Bio to rather than spamming AoW");
                ImGui.TableNextColumn();
                var maxTargets = GetAoWBreakevenTargets() - 1;
                var maxTargetsCol = maxTargets > 0 ? green : red;
                ImGui.TextColored(maxTargetsCol, $"{maxTargets}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Is Burst");
                ImGui.TableNextColumn();
                var isBurstCol = IsBurst ? green : red;
                ImGui.TextColored(isBurstCol, $"{IsBurst}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Has Chain Stratagem");
                ImGui.TableNextColumn();
                var hasChainCol = HasChainStratagem ? green : red;
                ImGui.TextColored(hasChainCol, $"{HasChainStratagem}");

                ImGui.EndTable();
            }
        }

        if (ImGui.CollapsingHeader("Mitigation/Party Stats"))
        {
            if (ImGui.BeginTable("MitigationTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Label");
                ImGui.TableSetupColumn("Value");
                ImGui.TableHeadersRow();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Current Mitigation");
                ImGui.TableNextColumn();
                var mitigation = GetCurrentMitigationPercent();
                var currMitCol = GetGradientColor(mitigation);
                ImGui.TextColored(currMitCol, $"{mitigation:P1}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Enough Mitigation?");
                ImGui.TableNextColumn();
                var enoughMitCol = EnoughMitigation ? green : red;
                ImGui.TextColored(enoughMitCol, $"{EnoughMitigation}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Party Average HP");
                ImGui.TableNextColumn();
                var avgHp = PartyMembersAverHP;
                var avgHpCol = GetGradientColor(avgHp);
                ImGui.TextColored(avgHpCol, $"{avgHp:P1}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Lowest Party HP");
                ImGui.TableNextColumn();
                var minHp = PartyMembersMinHP;
                var minHpCol = GetGradientColor(minHp);
                ImGui.TextColored(minHpCol, $"{LowestHealthPartyMember?.Name ?? "None"} {minHp:P1}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("No Single Target OGCD Heals");
                ImGui.TableNextColumn();
                var noStCol = NoSingleTargetOGCD ? green : red;
                ImGui.TextColored(noStCol, $"{NoSingleTargetOGCD}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("No AoE OGCD Heals");
                ImGui.TableNextColumn();
                var noAOECol = NoAreaOGCD ? green : red;
                ImGui.TextColored(noAOECol, $"{NoAreaOGCD}");

                ImGui.EndTable();
            }
        }

        // --- Stat Breakdown Section ---
        if (ImGui.CollapsingHeader("Calculations", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var uiState = UIState.Instance();
            var lvl = uiState->PlayerState.CurrentLevel;
            var attr = uiState->PlayerState.Attributes;
            var jobId = (Job)uiState->PlayerState.CurrentClassJobId;
            var det = attr[(int)StatType.Determination];
            var crit = attr[(int)StatType.CriticalHit];
            var critMult = StatEquations.CritDamage(crit, lvl);
            var critRate = StatEquations.CritChance(crit, lvl);
            var dh = attr[(int)StatType.DirectHitRate];
            var ten = attr[(int)StatType.Tenacity];
            var (ilvlSync, ilvlSyncType) = IlvlSync.GetCurrentIlvlSync();
            var (avgDamage, normalDamage, critDamage, avgHeal, normalHeal, critHeal, avgHot, normalHot, hotMin, hotMid, hotMax, critHot) =
                HealingCalc.CalcExpectedOutput(uiState, jobId, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);

            if (ImGui.BeginTable("CalcTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Current Level"); ImGui.TableNextColumn(); ImGui.Text($"{lvl}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Current Job"); ImGui.TableNextColumn(); ImGui.Text($"{jobId}");

                ImGui.Separator();

                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Determination"); ImGui.TableNextColumn(); ImGui.Text($"{det}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Critical Hit"); ImGui.TableNextColumn(); ImGui.Text($"{crit}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Direct Hit Rate"); ImGui.TableNextColumn(); ImGui.Text($"{dh}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Tenacity"); ImGui.TableNextColumn(); ImGui.Text($"{ten}");
                ImGui.TableNextRow(); ImGui.TableNextColumn();ImGui.Text("Crit Multiplier"); ImGui.TableNextColumn();ImGui.Text($"{critMult:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Crit Chance"); ImGui.TableNextColumn(); ImGui.Text($"{critRate:P1}");

                ImGui.Separator();

                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Avg Damage"); ImGui.TableNextColumn(); ImGui.Text($"{avgDamage:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Normal Damage"); ImGui.TableNextColumn(); ImGui.Text($"{normalDamage:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Crit Damage"); ImGui.TableNextColumn(); ImGui.Text($"{critDamage:N2}");

                ImGui.Separator();

                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Avg Heal"); ImGui.TableNextColumn(); ImGui.Text($"{avgHeal:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Normal Heal"); ImGui.TableNextColumn(); ImGui.Text($"{normalHeal:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Crit Heal"); ImGui.TableNextColumn(); ImGui.Text($"{critHeal:N2}");

                ImGui.Separator();

                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Avg HoT (tick)"); ImGui.TableNextColumn(); ImGui.Text($"{avgHot:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Normal HoT (tick)"); ImGui.TableNextColumn(); ImGui.Text($"{normalHot:N2}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Crit HoT (tick)"); ImGui.TableNextColumn(); ImGui.Text($"{critHot:N2}");
                ImGui.Separator();
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("HoT Tick Min (deterministic)"); ImGui.TableNextColumn(); ImGui.Text($"{hotMin:N0}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("HoT Tick Mid (deterministic)"); ImGui.TableNextColumn(); ImGui.Text($"{hotMid:N0}");
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("HoT Tick Max (deterministic)"); ImGui.TableNextColumn(); ImGui.Text($"{hotMax:N0}");

                ImGui.EndTable();
             }
         }

         return;

         static Vector4 GetGradientColor(float value) { var t = Math.Clamp(value, 0f, 1f); return new Vector4(1 - t, t, 0, 1); }
     }
     #endregion
 }