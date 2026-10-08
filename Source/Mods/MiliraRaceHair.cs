using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using RimWorld;
using UnityEngine;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Syncs Milian hair style + color customization.
///     <para />
///     State lives on <c>Milira.CompMilianHairSwitch</c> (
///     <c>
///         num/frontHairPath/behindHairPath/
///         colorOverride
///     </c>
///     , all scribed). Writers: hair dialog thumbnail clicks
///     (<c>ChangeGraphic(index) + num = index</c>) and dialog Accept
///     (<c>action(color) → colorOverride</c>). Dialog openers, Reset, Copy/Paste and the
///     color-wheel preview are local-only and need no sync.
///     <para />
///     Style is synced by registering <c>ChangeGraphic</c> itself; a postfix keeps scribed
///     <c>num</c> in step on every client. Color is assigned through a lambda field, so the
///     dialog constructor is patched to wrap it with the synced <c>SyncedSetHairColor</c>.
/// </summary>
public static class MiliraRaceHair
{
    private const string LogPrefix = "[Multiplayer Milira Race Hair Patch]";
    private const string CompTypeName = "Milira.CompMilianHairSwitch";
    private const string DialogTypeName = "Milira.Dialog_MilianHairStyleConfig";

    private static Type compType;
    private static AccessTools.FieldRef<object, int> numField;
    private static AccessTools.FieldRef<object, Color> colorField;
    private static AccessTools.FieldRef<object, object> dialogActionField;

    public static void Patch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        PatchChangeGraphic();
        PatchDialogAction();

        Log.Message($"{LogPrefix} Initialized.");
    }

    private static void PatchChangeGraphic()
    {
        compType = AccessTools.TypeByName(CompTypeName);

        if (compType == null)
        {
            Log.Warning($"{LogPrefix} Type not found: {CompTypeName}.");
            return;
        }

        numField = AccessTools.FieldRefAccess<int>(compType, "num");
        colorField = AccessTools.FieldRefAccess<Color>(compType, "colorOverride");

        if (numField == null || colorField == null)
            Log.Warning($"{LogPrefix} Could not resolve {CompTypeName} fields (num/colorOverride).");

        var method = AccessTools.DeclaredMethod(compType, "ChangeGraphic", [typeof(int)]);

        if (method == null)
        {
            Log.Warning($"{LogPrefix} Could not find {CompTypeName}.ChangeGraphic(int).");
            return;
        }

        try
        {
            MP.RegisterSyncMethod(method);
            Log.Message($"{LogPrefix} Synced {CompTypeName}.ChangeGraphic.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to sync ChangeGraphic: {exception}");
            return;
        }

        try
        {
            MpCompat.harmony.Patch(
                method,
                postfix: new HarmonyMethod(typeof(MiliraRaceHair), nameof(PostChangeGraphic)));
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to patch ChangeGraphic postfix: {exception}");
        }

        try
        {
            MP.RegisterSyncMethod(typeof(MiliraRaceHair), nameof(SyncedSetHairColor));
            Log.Message($"{LogPrefix} Registered SyncedSetHairColor.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to register SyncedSetHairColor: {exception}");
        }
    }

    /// <summary>Keeps scribed <c>num</c> identical on all clients after a synced style change.</summary>
    private static void PostChangeGraphic(object __instance, int index)
    {
        if (numField != null)
            numField(__instance) = index;
    }

    /// <summary>Applies a hair color on every client (invoked via dialog Accept on all clients).</summary>
    public static void SyncedSetHairColor(Pawn pawn, Color color)
    {
        if (pawn == null || compType == null || colorField == null)
            return;

        foreach (var comp in pawn.AllComps)
        {
            if (!compType.IsInstanceOfType(comp))
                continue;

            colorField(comp) = color;
            pawn.Drawer?.renderer?.renderTree?.SetDirty();
            PortraitsCache.SetDirty(pawn);
            return;
        }
    }

    private static void PatchDialogAction()
    {
        var type = AccessTools.TypeByName(DialogTypeName);

        if (type == null)
        {
            Log.Warning($"{LogPrefix} Type not found: {DialogTypeName}.");
            return;
        }

        dialogActionField = AccessTools.FieldRefAccess<object>(type, "action");

        if (dialogActionField == null)
        {
            Log.Warning($"{LogPrefix} Could not resolve {DialogTypeName}.action field.");
            return;
        }

        foreach (var ctor in AccessTools.GetDeclaredConstructors(type))
            try
            {
                MpCompat.harmony.Patch(
                    ctor,
                    postfix: new HarmonyMethod(typeof(MiliraRaceHair), nameof(PostDialogCtor)));
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix} Failed to patch dialog ctor: {exception}");
            }

        Log.Message($"{LogPrefix} Wrapped {DialogTypeName} color action.");
    }

    private static void PostDialogCtor(object __instance, Pawn milian)
    {
        // Replace the gizmo/column lambda (local-only color assignment) with a broadcast.
        // Fall back to the dialog's own field if parameter injection ever misses.
        try
        {
            var captured = milian
                           ?? (Pawn)AccessTools.Field(__instance.GetType(), "milian")?.GetValue(__instance);

            if (captured == null)
            {
                Log.Warning($"{LogPrefix} Could not resolve dialog pawn, color sync skipped for this dialog.");
                return;
            }

            Action<Color> synced = color => SyncedSetHairColor(captured, color);
            dialogActionField(__instance) = synced;
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Failed to wrap dialog action: {exception}");
        }
    }
}