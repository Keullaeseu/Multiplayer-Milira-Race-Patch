using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

public static class MiliraRaceAbilityPatch
{
    private const string LogPrefix = "[Multiplayer Milira Race Ability Patch]";

    public static void Patch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        MiliraRaceAbilityShortFly.Patch();

        Log.Message($"{LogPrefix} Initialized.");
    }
}