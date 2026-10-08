using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Syncs Milira job orders, container/toggle gizmos and work UI.
///     <para />
///     Direct method registrations (broadcast by Multiplayer itself, sim-safe because MP only
///     intercepts interface calls while sim-tick calls run locally on every client):
///     <list type="bullet">
///         <item><c>CompTargetEffect_Dress/Equip/HumanizeMilian.DoEffectOn</c> (targeter job orders)</item>
///         <item><c>CompThingContainer_Milian.CancelLoad</c> (gizmo; tick expiry stays local sim)</item>
///     </list>
///     Rerouted UI writes (postfix detects the local change and broadcasts a named
///     synced method instead): Milian work priorities checkbox.
///     PawnTable checkbox columns are synced directly per precedent (GiddyUp2, HuntForMe).
///     FloatMenu/handover lambdas are discovered by IL scan (<see cref="MiliraRaceLambdaSync" />),
///     so compiler layout changes between Milira versions cannot break registration.
/// </summary>
public static class MiliraRaceJobs
{
    private const string LogPrefix = "[Multiplayer Milira Race Jobs Patch]";

    private static bool prioritiesInit;
    private static bool lastSeenPriorities;

    public static void Patch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        PatchTargetEffects();
        PatchCancelLoad();
        PatchCheckboxes();
        PatchFloatMenus();
        PatchHandover();
        PatchWorkPriorities();

        Log.Message($"{LogPrefix} Initialized.");
    }

    #region Target effects (dress / equip / humanize)

    private static void PatchTargetEffects()
    {
        foreach (var typeName in new[]
                 {
                     "Milira.CompTargetEffect_DressMilian",
                     "Milira.CompTargetEffect_EquipMilian",
                     "Milira.CompTargetEffect_HumanizeMilian"
                 })
        {
            var type = AccessTools.TypeByName(typeName);

            if (type == null)
            {
                Log.Warning($"{LogPrefix} Type not found: {typeName}.");
                continue;
            }

            var method = AccessTools.DeclaredMethod(type, "DoEffectOn");

            if (method == null)
            {
                Log.Warning($"{LogPrefix} Could not find {typeName}.DoEffectOn.");
                continue;
            }

            try
            {
                MP.RegisterSyncMethod(method);
                Log.Message($"{LogPrefix} Synced {typeName}.DoEffectOn.");
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix} Failed to sync {typeName}.DoEffectOn: {exception}");
            }
        }
    }

    #endregion

    #region Fortress container CancelLoad

    private static void PatchCancelLoad()
    {
        var type = AccessTools.TypeByName("Milira.CompThingContainer_Milian");

        if (type == null)
        {
            Log.Warning($"{LogPrefix} Type not found: Milira.CompThingContainer_Milian.");
            return;
        }

        var method = AccessTools.DeclaredMethod(type, "CancelLoad");

        if (method == null)
        {
            Log.Warning($"{LogPrefix} Could not find CompThingContainer_Milian.CancelLoad.");
            return;
        }

        try
        {
            MP.RegisterSyncMethod(method);
            Log.Message($"{LogPrefix} Synced CompThingContainer_Milian.CancelLoad.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to sync CancelLoad: {exception}");
        }
    }

    #endregion

    #region FloatMenu job lambdas (strip / equip)

    private static void PatchFloatMenus()
    {
        // Only lambdas issuing/cancelling jobs are synced; submenu openers stay local.
        // Discovery by IL scan, no ordinal guesses.
        MiliraRaceLambdaSync.SyncStateChangingLambdas(
            "Milira.CompStripMilian",
            "CompFloatMenuOptions",
            [],
            ["TryTakeOrderedJob"]);

        MiliraRaceLambdaSync.SyncStateChangingLambdas(
            "Milira.PawnColumnWorker_EquipmentMilian",
            "GetEquipmentFloatMenu",
            [],
            ["TryTakeOrderedJob", "StopAll"]);
    }

    #endregion

    #region Fallen-angel handover (caravan gizmo)

    private static void PatchHandover()
    {
        // Single handover action lambda: RemovePawn + flags + goodwill recalc + SetRelation.
        MiliraRaceLambdaSync.SyncStateChangingLambdas(
            "Milira.WorldObjectCompMiliraSettlement",
            "GetCaravanGizmos",
            [],
            ["RemovePawn", "SetRelation"]);
    }

    #endregion

    #region Checkbox columns (autoSuitUp / displayInBillWorker)

    private static void PatchCheckboxes()
    {
        // Standard pattern (cf. GiddyUp2, HuntForMe, MedicalTab): sync SetValue itself.
        // MP broadcasts the (pawn, value) call; every client runs the original body.
        foreach (var typeName in new[]
                 {
                     "Milira.PawnColumnWorker_AutoSuitMilian",
                     "Milira.PawnColumnWorker_BillWorkerDisplayMilian"
                 })
        {
            var type = AccessTools.TypeByName(typeName);

            if (type == null)
            {
                Log.Warning($"{LogPrefix} Type not found: {typeName}.");
                continue;
            }

            try
            {
                MP.RegisterSyncMethod(type, "SetValue");
                Log.Message($"{LogPrefix} Synced {typeName}.SetValue.");
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix} Failed to sync {typeName}.SetValue: {exception}");
            }
        }
    }

    #endregion

    #region Milian work priorities checkbox

    private static void PatchWorkPriorities()
    {
        var type = AccessTools.TypeByName("Milira.MainTabWindow_MilianWork");

        if (type == null)
        {
            Log.Warning($"{LogPrefix} Type not found: Milira.MainTabWindow_MilianWork.");
            return;
        }

        var method = AccessTools.DeclaredMethod(type, "DoManualPrioritiesCheckbox");

        if (method == null)
        {
            Log.Warning($"{LogPrefix} Could not find MainTabWindow_MilianWork.DoManualPrioritiesCheckbox.");
            return;
        }

        try
        {
            MP.RegisterSyncMethod(typeof(MiliraRaceJobs), nameof(SyncedSetWorkPriorities));
            MpCompat.harmony.Patch(
                method,
                postfix: new HarmonyMethod(typeof(MiliraRaceJobs), nameof(PostWorkPriorities)));
            Log.Message($"{LogPrefix} Watching MainTabWindow_MilianWork priorities checkbox.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to patch work priorities: {exception}");
        }
    }

    private static void PostWorkPriorities()
    {
        if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
            return;

        var current = Current.Game.playSettings.useWorkPriorities;

        if (!prioritiesInit)
        {
            lastSeenPriorities = current;
            prioritiesInit = true;
            return;
        }

        if (current == lastSeenPriorities)
            return;

        lastSeenPriorities = current;
        SyncedSetWorkPriorities(current);
    }

    public static void SyncedSetWorkPriorities(bool value)
    {
        lastSeenPriorities = value;
        Current.Game.playSettings.useWorkPriorities = value;

        foreach (var pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                pawn.workSettings.Notify_UseWorkPrioritiesChanged();
    }

    #endregion
}