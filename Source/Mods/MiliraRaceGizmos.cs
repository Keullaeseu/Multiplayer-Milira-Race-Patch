using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Syncs Milira gizmo actions that mutate sim state.
///     <para />
///     <c>CompFlightControl.CompGetGizmosExtra</c> opens a FloatMenu with options toggling
///     <c>switchOn</c>/<c>onlyForMove</c> (+ ForceLand/Notify_JobStarted). <c>CompSwitchResonate.GetGizmos</c>
///     opens a FloatMenu with options setting <c>resonateNum</c>/<c>resonateClass</c> (+ hediff updates).
///     Without sync each client toggles locally and the sim diverges.
///     <para />
///     Lambdas are discovered by IL scan (<see cref="MiliraRaceLambdaSync" />) instead of ordinal
///     guesses, so compiler layout changes between Milira versions cannot break registration.
///     UI-only lambdas (FloatMenu openers, info-card buttons) stay local.
/// </summary>
public static class MiliraRaceGizmos
{
    private const string LogPrefix = "[Multiplayer Milira Race Gizmos Patch]";

    public static void Patch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        PatchFlightControl();
        PatchSwitchResonate();

        Log.Message($"{LogPrefix} Initialized.");
    }

    private static void PatchFlightControl()
    {
        MiliraRaceLambdaSync.SyncStateChangingLambdas(
            "Milira.CompFlightControl",
            "CompGetGizmosExtra",
            ["switchOn", "onlyForMove"],
            ["Notify_JobStarted", "ForceLand"],
            SyncContext.MapSelected);
    }

    private static void PatchSwitchResonate()
    {
        MiliraRaceLambdaSync.SyncStateChangingLambdas(
            "Milira.CompSwitchResonate",
            "GetGizmos",
            ["resonateNum", "resonateClass"],
            ["SetClassHediffToInitial"],
            SyncContext.MapSelected);

        // Safety net: the hediff update itself. Syncing the method means any local call
        // still broadcasts the hediff change.
        try
        {
            var type = AccessTools.TypeByName("Milira.CompSwitchResonate");

            if (type == null)
            {
                Log.Warning($"{LogPrefix} Type not found: Milira.CompSwitchResonate.");
                return;
            }

            MP.RegisterSyncMethod(type, "SetClassHediffToInitial").SetContext(SyncContext.MapSelected);
            Log.Message($"{LogPrefix} Synced Milira.CompSwitchResonate.SetClassHediffToInitial.");
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not sync SetClassHediffToInitial: {exception.Message}");
        }
    }
}