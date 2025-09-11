using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using Lumina.Excel.Sheets;
using RotationSolver.Commands;
using RotationSolver.UI.HighlightTeachingMode;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;

namespace RotationSolver.Updaters;

internal static class MajorUpdater
{
    private static TimeSpan _timeSinceUpdate = TimeSpan.Zero;
    private static bool _rotationsLoaded = false;

    public static bool IsValid
    {
        get
        {
            if (!Player.AvailableThreadSafe)
                return false;

            // Replace Svc.Condition.Any() with manual check
            bool anyCondition = false;
            foreach (var conditionFlag in Svc.Condition.AsReadOnlySet())
            {
                if (conditionFlag == ConditionFlag.BetweenAreas || conditionFlag == ConditionFlag.BetweenAreas51 || conditionFlag == ConditionFlag.LoggingOut)
                {
                    return false;
                }
                anyCondition = true; // foreach doesn't execute on an empty enumerable, so this will only be true if there are conditions
            }

            return anyCondition;
        }
    }

    private static Exception? _threadException;

    public static void Enable()
    {
        ActionSequencerUpdater.Enable(Svc.PluginInterface.ConfigDirectory.FullName + "\\Conditions");
        Svc.Framework.Update += RSRUpdate;

        // Load rotations on enable
        _ = Task.Run(async () =>
        {
            try
            {
                await RotationUpdater.GetAllCustomRotationsAsync();
                _rotationsLoaded = true;
                PluginLog.Information("Rotations loaded successfully on plugin enable");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to load rotations on enable: {ex.Message}");
            }
        });
    }

    private static void RSRUpdate(IFramework framework)
    {
        _timeSinceUpdate += framework.UpdateDelta;
        if (_timeSinceUpdate < TimeSpan.FromSeconds(Service.Config.MinUpdatingTime))
        {
            return;
        }
        else
        {
            _timeSinceUpdate = TimeSpan.Zero;
        }

        // Load rotations if not already loaded and conditions are met
        if (!_rotationsLoaded && IsValid)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RotationUpdater.GetAllCustomRotationsAsync();
                    PluginLog.Information("Rotations loaded successfully");
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Failed to load rotations: {ex.Message}");
                }
            });
        }

        if (Service.Config.TeachingMode)
        {
            try
            {
                HotbarHighlightManager.HotbarIDs.Clear();
            }
            catch (Exception ex)
            {
                if (_threadException != ex)
                {
                    _threadException = ex;
                    PluginLog.Error($"HotbarHighlightManager.HotbarIDs.Clear Exception: {ex.Message}");
                    if (Service.Config.InDebug)
                    {
                        _ = BasicWarningHelper.AddSystemWarning("HotbarHighlightManager.HotbarIDs.Clear Exception");
                    }
                }
            }
        }

        // Transistion safe commands
        if (!IsValid)
        {
            try
            {
                RSCommands.UpdateRotationState();
                ActionUpdater.ClearNextAction();
                MiscUpdater.UpdateEntry();
                ActionUpdater.NextAction = ActionUpdater.NextGCDAction = null;
                return;
            }
            catch (Exception ex)
            {
                if (_threadException != ex)
                {
                    _threadException = ex;
                    PluginLog.Error($"RSRInvalidUpdate Exception: {ex.Message}");
                    if (Service.Config.InDebug)
                    {
                        _ = BasicWarningHelper.AddSystemWarning("RSRInvalidUpdate Exception");
                    }
                }
            }
        }

        if (DataCenter.IsActivated())
        {
            try
            {
                bool canDoAction = ActionUpdater.CanDoAction();
                MovingUpdater.UpdateCanMove(canDoAction);

                if (canDoAction)
                {
                    RSCommands.DoAction();
                }

                MacroUpdater.UpdateMacro();
                TargetUpdater.UpdateTargets();
                StateUpdater.UpdateState();
                ActionUpdater.UpdateNextAction();
                ActionSequencerUpdater.UpdateActionSequencerAction();
            }
            catch (Exception ex)
            {
                if (_threadException != ex)
                {
                    _threadException = ex;
                    PluginLog.Error($"RSRUpdate DC Exception: {ex.Message}");
                    if (Service.Config.InDebug)
                    {
                        _ = BasicWarningHelper.AddSystemWarning("RSRUpdate DC Exception");
                    }
                }
            }

            // Handle Teaching Mode Highlighting
            if (Service.Config.TeachingMode && ActionUpdater.NextAction is not null)
            {
                try
                {
                    IAction nextAction = ActionUpdater.NextAction;
                    HotbarID? hotbar = null;
                    if (nextAction is IBaseItem item)
                    {
                        hotbar = new HotbarID(HotbarSlotType.Item, item.ID);
                    }
                    else if (nextAction is IBaseAction baseAction)
                    {
                        hotbar = baseAction.Action.ActionCategory.RowId is 10 or 11
                                ? GetGeneralActionHotbarID(baseAction)
                                : new HotbarID(HotbarSlotType.Action, baseAction.AdjustedID);
                    }

                    if (hotbar.HasValue)
                    {
                        _ = HotbarHighlightManager.HotbarIDs.Add(hotbar.Value);
                    }
                }
                catch (Exception ex)
                {
                    if (_threadException != ex)
                    {
                        _threadException = ex;
                        PluginLog.Error($"UpdateHighlight Exception: {ex.Message}");
                        if (Service.Config.InDebug)
                        {
                            _ = BasicWarningHelper.AddSystemWarning("UpdateHighlight Exception");
                        }
                    }
                }
            }
        }

        try
        {
            // Update various combat tracking perameters,
            // combat time, blue mage/dutyaction slot info, player movement time, player dead status and MP timer.
            ActionUpdater.UpdateCombatInfo();
            
            // Update timing tweaks
            ActionManagerEx.Instance.UpdateTweaks();

            // Update displaying the additional UI windows
            RotationSolverPlugin.UpdateDisplayWindow();

            // Handle system warnings
            if (DataCenter.SystemWarnings.Count > 0)
            {
                DateTime now = DateTime.Now;
                List<string> keysToRemove = [];

                foreach (KeyValuePair<string, DateTime> kvp in DataCenter.SystemWarnings)
                {
                    if (kvp.Value + TimeSpan.FromMinutes(10) < now)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (string key in keysToRemove)
                {
                    _ = DataCenter.SystemWarnings.Remove(key);
                }
            }

            // Clear old VFX data
            if (DataCenter.VfxDataQueue.Count > 0)
            {
                while (DataCenter.VfxDataQueue.TryPeek(out var vfx) && vfx.TimeDuration > TimeSpan.FromSeconds(6))
                {
                    _ = DataCenter.VfxDataQueue.TryDequeue(out _);
                }
            }

            // Change loaded rotation based on job
            RotationUpdater.UpdateRotation();

            // Change RS state
            RSCommands.UpdateRotationState();

            if (Service.Config.TeachingMode)
            {
                try
                {
                    HotbarHighlightManager.UpdateSettings();
                }
                catch (Exception ex)
                {
                    if (_threadException != ex)
                    {
                        _threadException = ex;
                        PluginLog.Error($"HotbarHighlightManager.UpdateSettings Exception: {ex.Message}");
                        if (Service.Config.InDebug)
                        {
                            _ = BasicWarningHelper.AddSystemWarning("HotbarHighlightManager.UpdateSettings Exception");
                        }
                    }
                }
            }

            MiscUpdater.UpdateMisc();

            if (Service.Config.TargetFreely && !DataCenter.IsPvP)
            {
                IAction? nextAction2 = ActionUpdater.NextAction;
                if (nextAction2 == null)
                {
                    if (Svc.Targets.Target == null)
                    {
                        // Try to find the closest enemy and target it
                        IBattleChara? closestEnemy = null;
                        float minDistance = float.MaxValue;

                        foreach (var enemy in DataCenter.AllHostileTargets)
                        {
                            if (enemy == null || !enemy.IsEnemy() || enemy == Player.Object)
                                continue;

                            float distance = Vector3.Distance(Player.Object.Position, enemy.Position);
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                closestEnemy = enemy;
                            }
                        }

                        if (closestEnemy != null)
                        {
                            Svc.Targets.Target = closestEnemy;
                            PluginLog.Information($"Targeting {closestEnemy}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (_threadException != ex)
            {
                _threadException = ex;
                PluginLog.Error($"Secondary RSRUpdate Exception: {ex.Message}");
                if (Service.Config.InDebug)
                {
                    _ = BasicWarningHelper.AddSystemWarning("Secondary RSRUpdate Exception");
                }
            }
        }
    }

    private static HotbarID? GetGeneralActionHotbarID(IBaseAction baseAction)
    {
        Lumina.Excel.ExcelSheet<GeneralAction> generalActions = Svc.Data.GetExcelSheet<GeneralAction>();
        if (generalActions == null)
        {
            return null;
        }

        foreach (GeneralAction gAct in generalActions)
        {
            if (gAct.Action.RowId == baseAction.ID)
            {
                return new HotbarID(HotbarSlotType.GeneralAction, gAct.RowId);
            }
        }

        return null;
    }

    public static void Dispose()
    {
        Svc.Framework.Update -= RSRUpdate;
        MiscUpdater.Dispose();
        ActionSequencerUpdater.SaveFiles();
        ActionUpdater.ClearNextAction();
    }
}