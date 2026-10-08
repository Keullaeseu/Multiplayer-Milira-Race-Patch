using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Finds gizmo/FloatMenu lambdas by scanning IL instead of guessing ordinal numbers.
///     <para />
///     Lambda ordinals depend on compiler-generated display-class layout and shift between
///     Milira versions (ordinal guesses produced "Couldn't find lambda" warnings). Worse,
///     Milira's lambdas that capture <c>this</c> live directly on the parent type
///     (e.g. <c>CompSwitchResonate.&lt;GetGizmos&gt;b__17_1</c>), where ordinal lookup never
///     searches. This scanner checks the parent type itself plus every nested type, picks
///     methods named <c>&lt;ParentMethod&gt;b__*</c> whose IL touches known state-changing
///     fields or methods, and registers each: nested display-class lambdas via
///     <c>MP.RegisterSyncDelegate</c> (the same call <c>MpCompat.RegisterLambdaDelegate</c>
///     makes after ordinal resolution), parent-type instance lambdas via
///     <c>MP.RegisterSyncMethod</c> (their instance is the syncable comp, same as named
///     comp methods). UI-only lambdas (FloatMenu openers, info-card buttons) never touch
///     the listed members and stay local.
/// </summary>
internal static class MiliraRaceLambdaSync
{
    private const string LogPrefix = "[Multiplayer Milira Race Lambda Sync]";

    public static int SyncStateChangingLambdas(
        string typeName,
        string parentMethodName,
        string[] stateFieldNames,
        string[] stateMethodNames,
        SyncContext? context = null)
    {
        var parentType = AccessTools.TypeByName(typeName);

        if (parentType == null)
        {
            Log.Warning($"{LogPrefix} Type not found: {typeName}.");
            return 0;
        }

        var synced = 0;
        var prefix = $"<{parentMethodName}>b__";

        synced += SyncMatching(parentType, parentType, prefix, stateFieldNames, stateMethodNames, context);

        foreach (var nested in AllNestedTypes(parentType))
            synced += SyncMatching(parentType, nested, prefix, stateFieldNames, stateMethodNames, context);

        if (synced == 0)
            Log.Warning($"{LogPrefix} No state-changing lambdas found in {typeName}.{parentMethodName}.");

        return synced;
    }

    private static int SyncMatching(
        Type parentType,
        Type declaringType,
        string prefix,
        string[] stateFieldNames,
        string[] stateMethodNames,
        SyncContext? context)
    {
        List<MethodInfo> methods;

        try
        {
            methods = AccessTools.GetDeclaredMethods(declaringType);
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not list methods of {declaringType.FullName}: {exception.Message}");
            return 0;
        }

        var synced = 0;

        foreach (var method in methods)
        {
            if (!method.Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            if (!TouchesState(method, stateFieldNames, stateMethodNames))
                continue;

            try
            {
                if (declaringType == parentType)
                {
                    if (IsUiOwned(parentType))
                    {
                        Log.Message(
                            $"{LogPrefix} Skipped UI-owned lambda {parentType.FullName}.{method.Name} (needs manual reroute).");
                        continue;
                    }

                    var sync = MP.RegisterSyncMethod(method);

                    if (context.HasValue)
                        sync.SetContext(context.Value);
                }
                else
                {
                    var sync = MP.RegisterSyncDelegate(parentType, declaringType.Name, method.Name, null);

                    if (context.HasValue)
                        sync.SetContext(context.Value);
                }

                synced++;
                Log.Message($"{LogPrefix} Synced {parentType.FullName} lambda {declaringType.Name}.{method.Name}.");
            }
            catch (Exception exception)
            {
                Log.Warning(
                    $"{LogPrefix} Could not sync {parentType.FullName} lambda {declaringType.Name}.{method.Name}: {exception.Message}");
            }
        }

        return synced;
    }

    private static bool TouchesState(MethodInfo method, string[] stateFieldNames, string[] stateMethodNames)
    {
        List<CodeInstruction> instructions;

        try
        {
            instructions = PatchProcessor.GetOriginalInstructions(method);
        }
        catch
        {
            return false;
        }

        foreach (var code in instructions)
        {
            // Writes only: getters read the same fields and must stay local.
            if ((code.opcode == OpCodes.Stfld || code.opcode == OpCodes.Stsfld)
                && code.operand is FieldInfo field
                && Array.IndexOf(stateFieldNames, field.Name) >= 0)
                return true;

            if (code.operand is MethodInfo called && Array.IndexOf(stateMethodNames, called.Name) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsUiOwned(Type type)
    {
        foreach (var uiTypeName in new[] { "Verse.Gizmo", "Verse.Window", "Verse.Command" })
        {
            var uiType = AccessTools.TypeByName(uiTypeName);

            if (uiType != null && uiType.IsAssignableFrom(type))
                return true;
        }

        return false;
    }

    private static IEnumerable<Type> AllNestedTypes(Type type)
    {
        foreach (var nested in type.GetNestedTypes(AccessTools.all))
        {
            yield return nested;

            foreach (var deeper in AllNestedTypes(nested))
                yield return deeper;
        }
    }
}