using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace CombatSurf.Utils;

public static class PlayerUtils
{
    public static bool IsValidPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid && player.PlayerPawn != null && player.PlayerPawn.IsValid;
    }

    public static bool IsValidHumanPlayer(CCSPlayerController? player)
    {
        return player != null && player.IsValid && !player.IsBot && player.PlayerPawn != null && player.PlayerPawn.IsValid;
    }

    public static bool IsPlayerAlive(CCSPlayerController? player)
    {
        return IsValidPlayer(player) && player!.PawnIsAlive;
    }

    public static void PrintToChatCustom(this CCSPlayerController player, string message)
    {
        if (IsValidPlayer(player))
        {
            player.PrintToChat($" {ChatColors.Red}[IG] {ChatColors.Default}{message}");
        }
    }

    public static void PrintToChatAllCustom(string message)
    {
        Server.PrintToChatAll($" {ChatColors.Red}[IG] {ChatColors.Default}{message}");
    }

    public static List<CCSPlayerController> GetValidPlayers()
    {
        return Utilities.GetPlayers().Where(IsValidPlayer).ToList();
    }

    public static void GiveGodmode(CCSPlayerController player)
    {
        if (IsValidPlayer(player))
        {
            player.PlayerPawn.Value!.TakesDamage = false;
        }
    }

    public static void RemoveGodmode(CCSPlayerController player)
    {
        if (IsValidPlayer(player))
        {
            player.PlayerPawn.Value!.TakesDamage = true;
        }
    }

    public static void GiveGodmodeToAll()
    {
        foreach (var player in GetValidPlayers())
        {
            GiveGodmode(player);
        }
    }

    public static void RemoveGodmodeFromAll()
    {
        foreach (var player in GetValidPlayers())
        {
            RemoveGodmode(player);
        }
    }
}
