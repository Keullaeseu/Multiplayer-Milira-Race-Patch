using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Compat;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Syncs Milira fly/jump verbs.
///     <para />
///     Vanilla Multiplayer auto-syncs <c>ITargetingSource.OrderForceTarget</c> only for types
///     in the RimWorld assembly. Milira's verbs live in Milira.dll and override
///     <c>OrderForceTarget</c> to call <c>MiliraFlyUtility*.OrderJump</c> (which issues a
///     <c>JobDefOf.CastJump</c> with <c>verbToUse</c>), so they must be registered explicitly.
///     The rest of the chain (TryCastShot -&gt; DoJump -&gt; PawnFlyer tick) is pure sim and
///     re-executes deterministically on all clients once the order is synced.
/// </summary>
public static class MiliraRaceAbilityShortFly
{
    private const string LogPrefix = "[Multiplayer Milira Race Ability Short Fly Patch]";

    // Every Milira verb type that overrides OrderForceTarget. Verb_CastAbilityMiliraJump
    // intentionally omitted: it doesn't override OrderForceTarget, so vanilla MP already
    // covers it via Verb_CastAbilityJump in the RimWorld assembly.
    private static readonly string[] FlyVerbTypes =
    [
        "Milira.Verb_CastAbilityMiliraFly",
        "Milira.Verb_CastAbilityMiliraFly_Blade",
        "Milira.Verb_CastAbilityMiliraFly_Hammer",
        "Milira.Verb_CastAbilityMiliraFly_Lance",
        "Milira.Verb_CastAbilityMiliraFly_Rook",
        "Milira.Verb_CastAbilityMiliraFly_KnightCharge",
        "Milira.Verb_CastMiliraFly",
        "Milira.Verb_MiliraJump"
    ];

    public static void Patch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        PatchOrderForceTarget();
        PatchVisualRng();

        Log.Message($"{LogPrefix} Initialized.");
    }

    private static void PatchOrderForceTarget()
    {
        foreach (var typeName in FlyVerbTypes)
        {
            var type = AccessTools.TypeByName(typeName);

            if (type == null)
            {
                Log.Warning($"{LogPrefix} Type not found: {typeName} (Milira version mismatch?).");
                continue;
            }

            var method = AccessTools.DeclaredMethod(type, "OrderForceTarget", [typeof(LocalTargetInfo)]);

            if (method == null)
            {
                Log.Warning($"{LogPrefix} Could not find {typeName}.OrderForceTarget(LocalTargetInfo).");
                continue;
            }

            try
            {
                MP.RegisterSyncMethod(method);
                Log.Message($"{LogPrefix} Synced {typeName}.OrderForceTarget.");
            }
            catch (Exception exception)
            {
                Log.Error($"{LogPrefix} Failed to sync {typeName}.OrderForceTarget: {exception}");
            }
        }
    }

    private static void PatchVisualRng()
    {
        // Visual-only RNG after ShouldSpawnMotesAt / camera checks. Isolate it so camera
        // position differences between clients can't perturb gameplay RNG state.
        // Gameplay RNG inside TryCastShot/DoJump/flyer ticks is left alone: it runs in sim
        // on all clients from the same state, so it stays in sync.
        PatchingUtilities.PatchPushPopRand([
            "Milira.MiliraPawnFlyer:LandingEffects",
            "Milira.MiliraPawnFlyer_Blade:LandingEffects",
            "Milira.MiliraPawnFlyer_Hammer:LandingEffects",
            "Milira.MiliraPawnFlyer_Lance:LandingEffects",
            "Milira.MiliraPawnFlyer_Rook:LandingEffects",
            "Milira.MiliraPawnFlyer_KnightCharge:LandingEffects",
            "Milira.MiliraFleckMaker:ThrowPlasmaAirPuffUp",
            "Milira.MiliraFleckMaker:ThrowLineEMP",
            "Milira.Verb_Shoot_Fortress:TryCastShot"
        ]);
    }
}