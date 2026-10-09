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
        PatchDevGizmos();

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

    private static void PatchDevGizmos()
    {
        // DEV: Spawn Milira Feather - resets countdown (Rand) and spawns the item.
        // Debug-only like vanilla dev gizmos: hidden unless god/dev mode is on.
        MiliraRaceLambdaSync.SyncStateChangingLambdas(
            "Milira.CompSpawner_MiliraFeather",
            "CompGetGizmosExtra",
            [],
            ["ResetCountdown", "TryDoSpawn"],
            debugOnly: true);

        // DEV: Spawn (delayed pawn wakeup) - the gizmo calls named Spawn() directly.
        try
        {
            var type = AccessTools.TypeByName("Milira.CompDelayedPawnSpawnOnWakeup");

            if (type == null)
            {
                Log.Warning($"{LogPrefix} Type not found: Milira.CompDelayedPawnSpawnOnWakeup.");
                return;
            }

            var method = AccessTools.DeclaredMethod(type, "Spawn");

            if (method == null)
            {
                Log.Warning($"{LogPrefix} Could not find Milira.CompDelayedPawnSpawnOnWakeup.Spawn.");
                return;
            }

            MP.RegisterSyncMethod(method).SetDebugOnly();
            Log.Message($"{LogPrefix} Synced Milira.CompDelayedPawnSpawnOnWakeup.Spawn.");
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not sync delayed pawn Spawn: {exception.Message}");
        }
    }
}