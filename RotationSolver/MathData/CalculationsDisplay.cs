using FFXIVClientStructs.FFXIV.Client.Game.UI;
using XIVCalc;
using XIVCalc.Calculations;

namespace RotationSolver.MathData
{
    public static unsafe class CalculationsDisplay
    {
        public static void ShowCalculationsTable()
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
            // Call CalcExpectedOutput primarily for its side effects (updates HealingCalc.Last* values).
            _ = HealingCalc.CalcExpectedOutput(uiState, jobId, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
            // Update HealerData caches once per UI update so other code can reuse deterministic base heal values
            HealerData.UpdateScholarCaches(uiState, jobId, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
            // Call above ensures stat block and accessors are up to date and caches are populated
            
            if (ImGui.TreeNodeEx("Basic Info", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.BeginGroup();
                Row("Current Level", lvl.ToString());
                Row("Current Job", jobId.ToString());
                Row("iLvl Sync", ilvlSync.HasValue ? $"{ilvlSync.Value} ({ilvlSyncType})" : "None", null, "If an item level sync is active this shows the synced item level and type.");
                ImGui.EndGroup();
                ImGui.TreePop();
            }

           
            if (ImGui.TreeNodeEx("Stats", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.BeginGroup();
                Row("Determination", det.ToString());
                Row("Critical Hit", crit.ToString());
                Row("Crit Hit Multiplier", critMult.ToString("N2"), GetGradientColor((float)Math.Clamp(critRate, 0.0, 1.0)), "Effective crit damage multiplier.");
                Row("Crit Hit Chance", critRate.ToString("P1"), GetGradientColor((float)critRate), "Chance to land a critical hit.");
                Row("Direct Hit Rate", dh.ToString());
                Row("Tenacity", ten.ToString());
                ImGui.EndGroup();
                ImGui.TreePop();
            }

            // Damage outputs group as collapsible tree node
            if (ImGui.TreeNodeEx("Damage Output", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.BeginGroup();
                Row("Avg Damage", HealingCalc.LastAvgDamage.ToString("N2"));
                Row("Normal Damage", HealingCalc.LastNormalDamage.ToString("N2"));
                Row("Crit Damage", HealingCalc.LastCritDamage.ToString("N2"));
                ImGui.EndGroup();
                ImGui.TreePop();
            }

            // Healing outputs group as collapsible tree node
            if (ImGui.TreeNodeEx("Healing Output", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.BeginGroup();
                ImGui.TextUnformatted("Scholar Healing Actions:");
                var schPotencies = HealerData.GetScholarHealingPotencies();
                var schShieldStrengths = HealerData.LastScholarShieldStrengths; // cached by UpdateScholarCaches
                foreach (var (actionName, potency) in schPotencies)
                {
                    // Retrieve deterministic base heal from the cache; fall back to calc if missing
                    if (!HealerData.LastScholarBaseHeals.TryGetValue(actionName, out var baseHealForAction))
                    {
                        baseHealForAction = HealingCalc.BaseHealCalculation(uiState, jobId, potency, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                    }
                    if (schShieldStrengths != null && schShieldStrengths.TryGetValue(actionName, out var shieldStrength))
                        Row($"{actionName}", $"Potency: {potency} | Base Heal: {baseHealForAction:N0} | Shield: {shieldStrength:N0}");
                    else
                        Row($"{actionName}", $"Potency: {potency} | Base Heal: {baseHealForAction:N0}");
                }
                Row("Avg Heal (100 Potency)", HealingCalc.LastAvgHeal.ToString("N2"));
                Row("Normal Heal (100 Potency)", HealingCalc.LastNormalHeal.ToString("N2"));
                Row("Crit Heal (100 Potency)", HealingCalc.LastCritHeal.ToString("N2"));
                ImGui.EndGroup();
                ImGui.TreePop();
            }

            // HoT outputs group as collapsible tree node
            if (ImGui.TreeNodeEx("HoT Output", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.BeginGroup();
                var hotCol = GetGradientColor((float)Math.Clamp(HealingCalc.LastAvgHot / Math.Max(1.0f, (float)HealingCalc.LastAvgHeal), 0f, 1f));
                Row("Avg HoT (tick)", HealingCalc.LastAvgHot.ToString("N2"), hotCol, "Average heal per HoT tick of 100 potency.");
                Row("Normal HoT (tick)", HealingCalc.LastNormalHot.ToString("N2"));
                Row("Crit HoT (tick)", HealingCalc.LastCritHot.ToString("N2"));

                // Show per-action Scholar HoT base values
                ImGui.Separator();
                ImGui.TextUnformatted("Scholar HoT Actions:");
                var schHotPerTick = HealerData.GetScholarHotPotenciesPerTick();
                var schHotTotal = HealerData.GetScholarHotTotalPotencies();
                foreach (var (actionName, perTickPotency) in schHotPerTick)
                {
                    // Use cached HoT base per tick if available
                    if (!HealerData.LastScholarHotBasePerTick.TryGetValue(actionName, out var baseHotPerTick))
                    {
                        baseHotPerTick = HealingCalc.BaseHealCalculation(uiState, jobId, perTickPotency, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                    }
                    // Calculate average (expected) heal per tick for the per-tick potency (no cache for average)
                    var avgHotPerTick = HealingCalc.CalcAverageHealForPotency(uiState, jobId, perTickPotency, det, critMult, critRate, dh, ten, ilvlSync, ilvlSyncType);
                    if (HealerData.LastScholarHotTotalBase.TryGetValue(actionName, out var totalBaseFromCache))
                    {
                        var ticks = perTickPotency > 0 ? (int)(HealerData.LastScholarHotTotalBase[actionName] / baseHotPerTick) : 0;
                        var totalAvgBase = avgHotPerTick * ticks;
                        Row(actionName, $"Potency/tick: {perTickPotency} | Base/tick: {baseHotPerTick:N0} | Avg/tick: {avgHotPerTick:N1} | Ticks: {ticks} | Total Base: {totalBaseFromCache:N0} | Total Avg: {totalAvgBase:N1}");
                    }
                    else if (schHotTotal.TryGetValue(actionName, out var totalPotency))
                    {
                        var ticks = perTickPotency > 0 ? (totalPotency / perTickPotency) : 0;
                        var totalBase = baseHotPerTick * ticks;
                        var totalAvgBase = avgHotPerTick * ticks;
                        Row(actionName, $"Potency/tick: {perTickPotency} | Base/tick: {baseHotPerTick:N0} | Avg/tick: {avgHotPerTick:N1} | Ticks: {ticks} | Total Base: {totalBase:N0} | Total Avg: {totalAvgBase:N1}");
                    }
                    else
                    {
                        Row(actionName, $"Potency/tick: {perTickPotency} | Base/tick: {baseHotPerTick:N0} | Avg/tick: {avgHotPerTick:N1} | Ticks: N/A | Total Base: N/A | Total Avg: N/A");
                    }
                }

                ImGui.EndGroup();
                ImGui.TreePop();
            }

            return;

            // Helper to render a two-column labeled row using ImGui.Columns
            void Row(string label, string value, Vector4? color = null, string? tooltip = null)
            {
                ImGui.Columns(2);
                ImGui.SetColumnWidth(0, 200);
                ImGui.TextUnformatted(label);
                ImGui.NextColumn();
                if (color is not null)
                    ImGui.TextColored(color.Value, value);
                else
                    ImGui.TextUnformatted(value);
                ImGui.NextColumn();

                if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
                    ImGui.SetTooltip(tooltip);
                ImGui.Columns();
            }
        }

        public static Vector4 GetGradientColor(float value) { var t = Math.Clamp(value, 0f, 1f); return new Vector4(1 - t, t, 0, 1); }
    }
}