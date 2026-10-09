using Multiplayer.Compat;
using Verse;

namespace MultiplayerMiliraRacePatch.Source.Mods;

/// <summary>
///     Multiplayer Patch for Milira Race by Ancot,
///     Last Update: 20 Jun @ 5:02am 2026
///     <see href="https://steamcommunity.com/sharedfiles/filedetails/?id=3256974620" />
/// </summary>
[MpCompatFor("Ancot.MiliraRace")]
public class MiliraRacePatch
{
    private const string LogPrefix = "[Multiplayer Milira Race Patch]";

    public MiliraRacePatch(ModContentPack content)
    {
        LongEventHandler.ExecuteWhenFinished(LatePatch);
    }

    private static void LatePatch()
    {
        Log.Message($"{LogPrefix} Initializing...");

        MiliraRaceAbilityPatch.Patch();
        MiliraRaceGameComponent.Patch();
        MiliraRaceGizmos.Patch();
        MiliraRaceHair.Patch();
        MiliraRaceJobs.Patch();

        Log.Message($"{LogPrefix} Initialized.");
    }
}