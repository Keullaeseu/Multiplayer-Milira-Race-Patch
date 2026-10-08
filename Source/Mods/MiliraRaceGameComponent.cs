using System.Reflection;
using HarmonyLib;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Fixes <c>MiliraGameComponent_OverallControl</c> desyncs.
///     <para />
///     The component ticks in sim on all clients but reads <c>Find.CurrentMap</c> (the
///     locally selected map, which differs per client) to target incidents, and rolls
///     <c>Rand.Chance</c> for raid/cluster/mining events. With different current maps the
///     incident queue diverges. We replace <c>Find.CurrentMap</c> inside the component's
///     tick/senders with a deterministic map shared by all clients.
/// </summary>
public static class MiliraRaceGameComponent
{
    private const string LogPrefix = "[Multiplayer Milira Race GameComponent Patch]";
    private const string ComponentTypeName = "Milira.MiliraGameComponent_OverallControl";

    private static readonly string[] CurrentMapUsers =
    [
        "GameComponentTick",
        "SendChurchFirstInteract",
        "SendMilianClusterRaid",
        "SendMiliraRaid",
        "SolarCrystalMining"
    ];

    public static void Patch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        var componentType = AccessTools.TypeByName(ComponentTypeName);

        if (componentType == null)
        {
            Log.Warning($"{LogPrefix} Type not found: {ComponentTypeName} (Milira version mismatch?).");
            return;
        }

        var currentMapGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
        var deterministicGetter =
            AccessTools.DeclaredMethod(typeof(MiliraRaceGameComponent), nameof(GetDeterministicMap));

        if (currentMapGetter == null || deterministicGetter == null)
        {
            Log.Error($"{LogPrefix} Could not resolve Find.CurrentMap getter or replacement.");
            return;
        }

        foreach (var methodName in CurrentMapUsers)
        {
            var method = AccessTools.DeclaredMethod(componentType, methodName);

            if (method == null)
            {
                Log.Warning($"{LogPrefix} Could not find {ComponentTypeName}.{methodName}.");
                continue;
            }

            try
            {
                MpCompat.harmony.Patch(
                    method,
                    transpiler: new HarmonyMethod(typeof(MiliraRaceGameComponent),
                        nameof(ReplaceCurrentMapTranspiler)));
                Log.Message($"{LogPrefix} Patched {ComponentTypeName}.{methodName} to use deterministic map.");
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix} Failed to patch {methodName}: {exception}");
            }
        }

        PatchExposeData(componentType);

        Log.Message($"{LogPrefix} Initialized.");
    }

    /// <summary>
    ///     Deterministic map shared by all clients: first player home map, falling back to
    ///     any map. Never returns a client-local selection.
    /// </summary>
    public static Map GetDeterministicMap()
    {
        var home = Find.AnyPlayerHomeMap;
        if (home != null)
            return home;

        var maps = Find.Maps;
        return maps.Count > 0 ? maps[0] : Find.CurrentMap;
    }

    private static IEnumerable<CodeInstruction> ReplaceCurrentMapTranspiler(
        IEnumerable<CodeInstruction> instr,
        MethodBase baseMethod)
    {
        var from = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
        var to = AccessTools.DeclaredMethod(typeof(MiliraRaceGameComponent), nameof(GetDeterministicMap));

        return instr.ReplaceMethod(from, to, baseMethod, expectedReplacements: -2);
    }

    /// <summary>
    ///     Milira only scribes a subset of the component (<c>pawn</c> but not
    ///     <c>pawnInColony</c>/<c>canSendChurchInfo</c>). Without <c>pawnInColony</c>, joiner
    ///     quests resolve differently after save/load and clients fork. The postfix adds the
    ///     missing Scribe calls in the same Scribe context. <c>colorClipboard</c> is an
    ///     intentionally local UI clipboard and is left unscribed.
    /// </summary>
    private static void PatchExposeData(Type componentType)
    {
        var method = AccessTools.DeclaredMethod(componentType, "ExposeData");

        if (method == null)
        {
            Log.Warning($"{LogPrefix} Could not find {ComponentTypeName}.ExposeData.");
            return;
        }

        try
        {
            MpCompat.harmony.Patch(
                method,
                postfix: new HarmonyMethod(typeof(MiliraRaceGameComponent), nameof(PostExposeData)));
            Log.Message($"{LogPrefix} Patched {ComponentTypeName}.ExposeData (extra fields).");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to patch ExposeData: {exception}");
        }
    }

    private static void PostExposeData(object __instance)
    {
        try
        {
            var type = __instance.GetType();
            var pawnField = AccessTools.Field(type, "pawnInColony");
            var infoField = AccessTools.Field(type, "canSendChurchInfo");

            if (pawnField != null)
            {
                var pawn = (Pawn)pawnField.GetValue(__instance);
                Scribe_References.Look(ref pawn, "pawnInColony", true);
                pawnField.SetValue(__instance, pawn);
            }

            if (infoField != null)
            {
                var info = (bool)infoField.GetValue(__instance);
                Scribe_Values.Look(ref info, "canSendChurchInfo");
                infoField.SetValue(__instance, info);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Extra ExposeData failed: {exception}");
        }
    }
}