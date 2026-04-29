using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CombatSurf.Data;
using CombatSurf.Utils;

namespace CombatSurf;

public partial class CombatSurf
{
    // Helper method to check admin permissions
    private bool IsPlayerAdmin(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return false;
            
        return AdminManager.PlayerHasPermissions(player, "@css/generic");
    }
    [ConsoleCommand("css_killbot", "Kill all bots")]
    [ConsoleCommand("killbot", "Kill all bots")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnKillBotCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            Server.ExecuteCommand("bot_kill");
            player.PrintToChatCustom($"{ChatColors.Green}All bots have been killed.");
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in killbot command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_stats", "View your stats")]
    [ConsoleCommand("stats", "View your stats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            var steamId = player.SteamID.ToString();
            var stats = _dataManager.GetPlayerStats(steamId);
            
            if (stats == null)
            {
                player.PrintToChatCustom($"{ChatColors.Red}No stats found. Play some rounds first!");
                return;
            }

            var rank = RankSystem.GetRank(stats.Points);
            var nextRank = RankSystem.GetNextRank(stats.Points);
            var playerRank = _dataManager.GetPlayerRank(steamId);

            // Calculate skill percentages
            var totalShots = stats.Kills + stats.Deaths; // Simplified calculation
            var noscopePercent = totalShots > 0 ? Math.Round((double)stats.NoscopeKills / totalShots * 100, 1) : 0;
            var airbornePercent = totalShots > 0 ? Math.Round((double)stats.AirborneKills / totalShots * 100, 1) : 0;
            var headshotPercent = totalShots > 0 ? Math.Round((double)stats.HeadshotKills / totalShots * 100, 1) : 0;

            SendChatMessage(player, $"{ChatColors.Green}═══ YOUR STATS ═══");
            player.PrintToChatCustom($"{ChatColors.White}Rank: {rank.Color}{rank.Name} {ChatColors.Grey}(#{playerRank})");
            player.PrintToChatCustom($"{ChatColors.White}Points: {ChatColors.Green}{stats.Points:N0} {ChatColors.Grey}(Total Earned: {stats.TotalPointsEarned:N0})");
            player.PrintToChatCustom($"{ChatColors.White}K/D: {ChatColors.Green}{stats.Kills}{ChatColors.White}/{ChatColors.Red}{stats.Deaths} {ChatColors.Grey}({(stats.Deaths > 0 ? (double)stats.Kills / stats.Deaths : stats.Kills):F2} ratio)");
            player.PrintToChatCustom($"{ChatColors.White}Rounds: {ChatColors.Yellow}{stats.RoundsPlayed}");
            player.PrintToChatCustom($"{ChatColors.White}Skills: {ChatColors.Purple}Noscope {noscopePercent}% {ChatColors.Blue}Airborne {airbornePercent}% {ChatColors.Red}Headshot {headshotPercent}%");
            
            // Show best kill if they have one
            if (stats.BestKillPoints > 0)
            {
                var timeSinceBest = DateTime.UtcNow - stats.BestKillDate;
                var timeAgo = timeSinceBest.Days > 0 
                    ? $"{timeSinceBest.Days}d ago" 
                    : timeSinceBest.Hours > 0 
                        ? $"{timeSinceBest.Hours}h ago"
                        : $"{timeSinceBest.Minutes}m ago";
                        
                player.PrintToChatCustom($"{ChatColors.White}Best Kill: {ChatColors.Gold}{stats.BestKillPoints} points {ChatColors.White}{stats.BestKillDescription} {ChatColors.Grey}({timeAgo})");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in stats command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_top", "View top players")]
    [ConsoleCommand("top", "View top players")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnTopCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            var orderBy = command.ArgCount > 1 ? command.GetArg(1).ToLower() : "points";
            var validOrderBys = new[] { "points", "kills", "total_points_earned" };
            
            if (!validOrderBys.Contains(orderBy))
                orderBy = "points";

            var topPlayers = _dataManager.GetTopPlayers(orderBy, 10);
            
            if (topPlayers.Count == 0)
            {
                if (player != null)
                    player.PrintToChatCustom($"{ChatColors.Red}No player data available yet!");
                else
                    Server.PrintToConsole("No player data available yet!");
                return;
            }

            var title = orderBy switch
            {
                "kills" => "TOP KILLERS",
                "total_points_earned" => "TOP POINT EARNERS",
                _ => "TOP PLAYERS"
            };

            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Green}═══ {title} ═══");
                for (int i = 0; i < topPlayers.Count; i++)
                {
                    var p = topPlayers[i];
                    var rank = RankSystem.GetRank(p.Points);
                    var value = orderBy switch
                    {
                        "kills" => p.Kills.ToString("N0"),
                        "total_points_earned" => p.TotalPointsEarned.ToString("N0"),
                        _ => p.Points.ToString("N0")
                    };
                    
                    player.PrintToChatCustom($"{ChatColors.Yellow}{i + 1}. {rank.Color}{p.PlayerName} {ChatColors.White}- {ChatColors.Green}{value}");
                }
            }
            else
            {
                Server.PrintToConsole($"═══ {title} ═══");
                for (int i = 0; i < topPlayers.Count; i++)
                {
                    var p = topPlayers[i];
                    var value = orderBy switch
                    {
                        "kills" => p.Kills.ToString("N0"),
                        "total_points_earned" => p.TotalPointsEarned.ToString("N0"),
                        _ => p.Points.ToString("N0")
                    };
                    Server.PrintToConsole($"{i + 1}. {p.PlayerName} - {value}");
                }
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in top command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_rank", "View your rank progress")]
    [ConsoleCommand("rank", "View your rank progress")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRankCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            var steamId = player.SteamID.ToString();
            var stats = _dataManager.GetPlayerStats(steamId);
            
            if (stats == null)
            {
                player.PrintToChatCustom($"{ChatColors.Red}No stats found. Play some rounds first!");
                return;
            }

            var currentRank = RankSystem.GetRank(stats.Points);
            var nextRank = RankSystem.GetNextRank(stats.Points);
            var playerPosition = _dataManager.GetPlayerRank(steamId);

            player.PrintToChatCustom($"{ChatColors.Green}═══ RANK PROGRESS ═══");
            player.PrintToChatCustom($"{ChatColors.White}Current Rank: {currentRank.Color}{currentRank.Name}");
            player.PrintToChatCustom($"{ChatColors.White}Current Points: {ChatColors.Green}{stats.Points:N0}");
            player.PrintToChatCustom($"{ChatColors.White}Server Position: {ChatColors.Yellow}#{playerPosition}");

            if (nextRank != null)
            {
                var pointsNeeded = nextRank.MinPoints - stats.Points;
                
                player.PrintToChatCustom($"{ChatColors.White}Next Rank: {nextRank.Color}{nextRank.Name}");
                player.PrintToChatCustom($"{ChatColors.White}Points Needed: {ChatColors.Red}{pointsNeeded:N0}");
            }
            else
            {
                player.PrintToChatCustom($"{ChatColors.Gold}🏆 You've reached the maximum rank! 🏆");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in rank command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_ranks", "View all available ranks")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnRanksCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            var ranks = RankSystem.GetAllRanks();
            
            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Green}═══ AVAILABLE RANKS ═══");
                for (int i = 0; i < ranks.Count; i++)
                {
                    var rank = ranks[i];
                    var nextRank = i < ranks.Count - 1 ? ranks[i + 1] : null;
                    
                    // Format point range
                    string pointRange;
                    if (nextRank != null)
                    {
                        pointRange = $"{rank.MinPoints:N0} - {(nextRank.MinPoints - 1):N0} points";
                    }
                    else
                    {
                        pointRange = $"{rank.MinPoints:N0}+ points (MAX RANK)";
                    }
                    
                    // Send properly formatted rank info - each rank on its own line
                    player.PrintToChatCustom($"{rank.Color}{rank.Name} {ChatColors.Default}- {pointRange}");
                }
            }
            else
            {
                Server.PrintToConsole("═══ AVAILABLE RANKS ═══");
                for (int i = 0; i < ranks.Count; i++)
                {
                    var rank = ranks[i];
                    var nextRank = i < ranks.Count - 1 ? ranks[i + 1] : null;
                    
                    string pointRange;
                    if (nextRank != null)
                    {
                        pointRange = $"{rank.MinPoints:N0} - {(nextRank.MinPoints - 1):N0} points";
                    }
                    else
                    {
                        pointRange = $"{rank.MinPoints:N0}+ points (MAX RANK)";
                    }
                    
                    Server.PrintToConsole($"{rank.Name} - {pointRange}");
                }
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in ranks command: {ex.Message}");
        }
    }


    [ConsoleCommand("css_speedhud", "Toggle speed HUD display")]
    [ConsoleCommand("speedhud", "Toggle speed HUD display")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSpeedHudCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            var slot = player.Slot;
            
            // Initialize HUD info if needed
            if (!_playerHudInfo.ContainsKey(slot))
            {
                _playerHudInfo[slot] = new PlayerHudInfo();
            }
            
            var hudInfo = _playerHudInfo[slot];
            
            // Toggle HUD state
            hudInfo.ShowSpeedHud = !hudInfo.ShowSpeedHud;
            
            var statusText = hudInfo.ShowSpeedHud ? $"{ChatColors.Green}enabled" : $"{ChatColors.Red}disabled";
            player.PrintToChatCustom($"Speed HUD {statusText}{ChatColors.White}!");
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in speedhud command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_killnotifications", "Toggle kill point notifications")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnKillNotificationsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            var steamId = player.SteamID.ToString();
            var stats = _dataManager.GetPlayerStats(steamId);
            
            var newNotificationState = !stats.KillNotifications;
            _dataManager.UpdatePlayerStats(steamId, player.PlayerName, killNotifications: newNotificationState);
            
            var statusText = newNotificationState ? $"{ChatColors.Green}enabled" : $"{ChatColors.Red}disabled";
            player.PrintToChatCustom($"Kill notifications {statusText}{ChatColors.White}!");
            
            // Update cache
            if (_playerStatsCache.ContainsKey(player.Slot))
            {
                _playerStatsCache[player.Slot].KillNotifications = newNotificationState;
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in kill notifications command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_path", "Show JSON data file path")]
    [ConsoleCommand("path", "Show JSON data file path")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnPathCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            var dataFilePath = _dataManager.GetDataFilePath();
            var fullPath = Path.GetFullPath(dataFilePath);
            var directory = Path.GetDirectoryName(fullPath);
            
            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Green}═══ DATA FILE INFO ═══");
                player.PrintToChatCustom($"{ChatColors.White}File: {ChatColors.Yellow}{Path.GetFileName(dataFilePath)}");
                player.PrintToChatCustom($"{ChatColors.White}Directory: {ChatColors.Lime}{directory}");
                player.PrintToChatCustom($"{ChatColors.White}Full Path: {ChatColors.LightBlue}{fullPath}");
            }
            else
            {
                Server.PrintToConsole("═══ DATA FILE INFO ═══");
                Server.PrintToConsole($"File: {Path.GetFileName(dataFilePath)}");
                Server.PrintToConsole($"Directory: {directory}");
                Server.PrintToConsole($"Full Path: {fullPath}");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in path command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_myrank", "View your rank position with nearby players")]
    [ConsoleCommand("myrank", "View your rank position with nearby players")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnMyRankCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            var steamId = player.SteamID.ToString();
            var playerStats = _dataManager.GetPlayerStats(steamId);
            
            if (playerStats == null)
            {
                player.PrintToChatCustom($"{ChatColors.Red}No stats found. Play some rounds first!");
                return;
            }

            // Get all players sorted by points
            var allPlayers = _dataManager.GetTopPlayers("points", int.MaxValue);
            var playerPosition = allPlayers.FindIndex(p => p.SteamId == steamId);
            
            if (playerPosition == -1)
            {
                player.PrintToChatCustom($"{ChatColors.Red}Could not find your position on the leaderboard!");
                return;
            }

            var currentRank = RankSystem.GetRank(playerStats.Points);
            var actualPosition = playerPosition + 1; // Convert to 1-based

            player.PrintToChatCustom($"{ChatColors.Green}═══ YOUR RANK POSITION ═══");
            player.PrintToChatCustom($"{ChatColors.White}Your Rank: {currentRank.Color}{currentRank.Name} {ChatColors.Grey}(#{actualPosition} of {allPlayers.Count})");
            player.PrintToChatCustom($"{ChatColors.White}Your Points: {ChatColors.Green}{playerStats.Points:N0}");
            
            // Show players around the current player (3 above, current player, 3 below)
            var startIndex = Math.Max(0, playerPosition - 3);
            var endIndex = Math.Min(allPlayers.Count - 1, playerPosition + 3);
            
            player.PrintToChatCustom($"{ChatColors.Blue}═══ NEARBY PLAYERS ═══");
            
            for (int i = startIndex; i <= endIndex; i++)
            {
                var p = allPlayers[i];
                var rank = RankSystem.GetRank(p.Points);
                var position = i + 1;
                var isCurrentPlayer = i == playerPosition;
                
                // Special formatting for the current player
                if (isCurrentPlayer)
                {
                    player.PrintToChatCustom($"{ChatColors.Yellow}► {position}. {rank.Color}{p.PlayerName} {ChatColors.White}- {ChatColors.Green}{p.Points:N0} pts {ChatColors.Yellow}(YOU)");
                }
                else
                {
                    // Show point difference
                    var pointDiff = Math.Abs(p.Points - playerStats.Points);
                    var diffText = p.Points > playerStats.Points 
                        ? $"{ChatColors.Red}+{pointDiff:N0}" 
                        : $"{ChatColors.Green}-{pointDiff:N0}";
                    
                    player.PrintToChatCustom($"{ChatColors.White}{position}. {rank.Color}{p.PlayerName} {ChatColors.White}- {ChatColors.Green}{p.Points:N0} pts {ChatColors.Grey}({diffText})");
                }
            }
            
            // Show how many points needed to move up or down
            if (playerPosition > 0)
            {
                var playerAbove = allPlayers[playerPosition - 1];
                var pointsNeeded = playerAbove.Points - playerStats.Points + 1;
                player.PrintToChatCustom($"{ChatColors.White}Points to rank up: {ChatColors.Yellow}{pointsNeeded:N0}");
            }
            
            if (playerPosition < allPlayers.Count - 1)
            {
                var playerBelow = allPlayers[playerPosition + 1];
                var pointsAhead = playerStats.Points - playerBelow.Points;
                player.PrintToChatCustom($"{ChatColors.White}Points ahead of next player: {ChatColors.Green}{pointsAhead:N0}");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in myrank command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_leaderboard", "View top 10 players leaderboard")]
    [ConsoleCommand("leaderboard", "View top 10 players leaderboard")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnLeaderboardCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            var topPlayers = _dataManager.GetTopPlayers("points", 10);
            
            if (topPlayers.Count == 0)
            {
                if (player != null)
                    player.PrintToChatCustom($"{ChatColors.Red}No player data available yet!");
                else
                    Server.PrintToConsole("No player data available yet!");
                return;
            }

            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Gold}🏆 TOP 10 LEADERBOARD 🏆");
                for (int i = 0; i < topPlayers.Count; i++)
                {
                    var p = topPlayers[i];
                    var rank = RankSystem.GetRank(p.Points);
                    
                    // Special formatting for top 3
                    var position = i + 1;
                    var medal = position switch
                    {
                        1 => "🥇",
                        2 => "🥈", 
                        3 => "🥉",
                        _ => $"{position}."
                    };
                    
                    player.PrintToChatCustom($"{ChatColors.White}{medal} {rank.Color}{p.PlayerName} {ChatColors.White}- {ChatColors.Green}{p.Points:N0} pts");
                }
            }
            else
            {
                Server.PrintToConsole("🏆 TOP 10 LEADERBOARD 🏆");
                for (int i = 0; i < topPlayers.Count; i++)
                {
                    var p = topPlayers[i];
                    var position = i + 1;
                    var medal = position switch
                    {
                        1 => "🥇",
                        2 => "🥈",
                        3 => "🥉", 
                        _ => $"{position}."
                    };
                    
                    Server.PrintToConsole($"{medal} {p.PlayerName} - {p.Points:N0} pts");
                }
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in leaderboard command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_serverstats", "View server statistics (Admin only)")]
    [ConsoleCommand("serverstats", "View server statistics (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnServerStatsCommand(CCSPlayerController? player, CommandInfo command)
    {
        // Check admin permissions
        if (player != null && !IsPlayerAdmin(player))
        {
            player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
            return;
        }

        try
        {
            var totalPlayers = _dataManager.GetTotalPlayers();
            var topPlayers = _dataManager.GetTopPlayers("points", int.MaxValue);
            
            // Calculate various statistics
            var totalKills = topPlayers.Sum(p => p.Kills);
            var totalDeaths = topPlayers.Sum(p => p.Deaths);
            var totalRounds = topPlayers.Sum(p => p.RoundsPlayed);
            var totalPointsEarned = topPlayers.Sum(p => p.TotalPointsEarned);
            var totalNoscopeKills = topPlayers.Sum(p => p.NoscopeKills);
            var totalAirborneKills = topPlayers.Sum(p => p.AirborneKills);
            var totalHeadshotKills = topPlayers.Sum(p => p.HeadshotKills);
            
            // Get top player info
            var topPlayer = topPlayers.FirstOrDefault();
            var activePlayers = topPlayers.Where(p => p.RoundsPlayed > 0).Count();
            
            // Get players with data from last 7 days
            var recentPlayers = topPlayers.Where(p => 
                (DateTime.UtcNow - p.LastUpdated).TotalDays <= 7).Count();

            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Green}═══ SERVER STATISTICS ═══");
                player.PrintToChatCustom($"{ChatColors.White}Total Players: {ChatColors.Yellow}{totalPlayers}");
                player.PrintToChatCustom($"{ChatColors.White}Active Players: {ChatColors.Yellow}{activePlayers} {ChatColors.Grey}(played at least 1 round)");
                player.PrintToChatCustom($"{ChatColors.White}Recent Players: {ChatColors.Yellow}{recentPlayers} {ChatColors.Grey}(last 7 days)");
                
                if (topPlayer != null)
                {
                    var topRank = RankSystem.GetRank(topPlayer.Points);
                    player.PrintToChatCustom($"{ChatColors.White}Top Player: {topRank.Color}{topPlayer.PlayerName} {ChatColors.White}({ChatColors.Green}{topPlayer.Points:N0} pts{ChatColors.White})");
                }
                
                player.PrintToChatCustom($"{ChatColors.Blue}═══ GLOBAL ACTIVITY ═══");
                player.PrintToChatCustom($"{ChatColors.White}Total Kills: {ChatColors.Green}{totalKills:N0}");
                player.PrintToChatCustom($"{ChatColors.White}Total Deaths: {ChatColors.Red}{totalDeaths:N0}");
                player.PrintToChatCustom($"{ChatColors.White}Total Rounds: {ChatColors.Yellow}{totalRounds:N0}");
                player.PrintToChatCustom($"{ChatColors.White}Total Points Earned: {ChatColors.Gold}{totalPointsEarned:N0}");
                
                player.PrintToChatCustom($"{ChatColors.Purple}═══ SKILL BREAKDOWN ═══");
                player.PrintToChatCustom($"{ChatColors.White}Noscope Kills: {ChatColors.Purple}{totalNoscopeKills:N0}");
                player.PrintToChatCustom($"{ChatColors.White}Airborne Kills: {ChatColors.Blue}{totalAirborneKills:N0}");
                player.PrintToChatCustom($"{ChatColors.White}Headshot Kills: {ChatColors.Red}{totalHeadshotKills:N0}");
                
                // Calculate percentages
                if (totalKills > 0)
                {
                    var noscopePercent = Math.Round((double)totalNoscopeKills / totalKills * 100, 1);
                    var airbornePercent = Math.Round((double)totalAirborneKills / totalKills * 100, 1);
                    var headshotPercent = Math.Round((double)totalHeadshotKills / totalKills * 100, 1);
                    
                    player.PrintToChatCustom($"{ChatColors.Grey}Skill Rates: {ChatColors.Purple}{noscopePercent}% {ChatColors.Blue}{airbornePercent}% {ChatColors.Red}{headshotPercent}%");
                }
            }
            else
            {
                Server.PrintToConsole("═══ SERVER STATISTICS ═══");
                Server.PrintToConsole($"Total Players: {totalPlayers}");
                Server.PrintToConsole($"Active Players: {activePlayers} (played at least 1 round)");
                Server.PrintToConsole($"Recent Players: {recentPlayers} (last 7 days)");
                
                if (topPlayer != null)
                {
                    Server.PrintToConsole($"Top Player: {topPlayer.PlayerName} ({topPlayer.Points:N0} pts)");
                }
                
                Server.PrintToConsole("═══ GLOBAL ACTIVITY ═══");
                Server.PrintToConsole($"Total Kills: {totalKills:N0}");
                Server.PrintToConsole($"Total Deaths: {totalDeaths:N0}");
                Server.PrintToConsole($"Total Rounds: {totalRounds:N0}");
                Server.PrintToConsole($"Total Points Earned: {totalPointsEarned:N0}");
                
                Server.PrintToConsole("═══ SKILL BREAKDOWN ═══");
                Server.PrintToConsole($"Noscope Kills: {totalNoscopeKills:N0}");
                Server.PrintToConsole($"Airborne Kills: {totalAirborneKills:N0}");
                Server.PrintToConsole($"Headshot Kills: {totalHeadshotKills:N0}");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in serverstats command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_recordpath", "Record your movement next round (Admin only)")]
    [ConsoleCommand("recordpath", "Record your movement next round (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnRecordPathCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            if (!IsPlayerAdmin(player))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
                return;
            }

            _pendingRecordNextRound = true;
            _pendingRecordSlot = player.Slot;

            var humans = Utilities.GetPlayers().Count(PlayerUtils.IsValidHumanPlayer);
            player.PrintToChatCustom($"{ChatColors.Green}RecordPath armed.{ChatColors.White} Recording will start at the next round start for you.");
            if (humans == 1)
            {
                player.PrintToChatCustom($"{ChatColors.Grey}Tip:{ChatColors.White} Only one human detected. A bot will replay your path each round.");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in recordpath command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_botlist", "List saved bot replays (Admin only)")]
    [ConsoleCommand("botlist", "List saved bot replays (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnBotListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            if (!IsPlayerAdmin(player))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
                return;
            }

            var botsDir = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "Bots");
            if (!Directory.Exists(botsDir))
            {
                player.PrintToChatCustom($"{ChatColors.Grey}No Bots directory found.");
                return;
            }

            var files = Directory.GetFiles(botsDir, "*.json");
            if (files.Length == 0)
            {
                player.PrintToChatCustom($"{ChatColors.Grey}No saved bot replays.");
                return;
            }

            player.PrintToChatCustom($"{ChatColors.Green}Saved bot replays:");
            foreach (var f in files.OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var json = File.ReadAllText(f);
                    string label = Path.GetFileNameWithoutExtension(f);
                    try
                    {
                        var blob = System.Text.Json.JsonSerializer.Deserialize<RecordingData>(json);
                        if (blob != null && !string.IsNullOrWhiteSpace(blob.RecorderName))
                        {
                            label = $"{blob.RecorderName} ({Path.GetFileNameWithoutExtension(f)})";
                        }
                    }
                    catch { }
                    player.PrintToChatCustom($"{ChatColors.Yellow}- {label}");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in botlist command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_deletebot", "Delete saved bot replay by name (Admin only)")]
    [ConsoleCommand("deletebot", "Delete saved bot replay by name (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnDeleteBotCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            if (!IsPlayerAdmin(player))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
                return;
            }

            if (command.ArgCount < 2)
            {
                player.PrintToChatCustom($"{ChatColors.Red}Usage: !deletebot <name>");
                return;
            }

            var name = command.GetArg(1);
            if (string.IsNullOrWhiteSpace(name))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Invalid name.");
                return;
            }

            var botsDir = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "Bots");
            var file = Path.Combine(botsDir, $"{name}.json");

            if (!File.Exists(file))
            {
                player.PrintToChatCustom($"{ChatColors.Grey}Replay not found: {ChatColors.Yellow}{name}");
                return;
            }

            File.Delete(file);
            player.PrintToChatCustom($"{ChatColors.Green}Deleted replay: {ChatColors.Yellow}{name}");
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in deletebot command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_adminhelp", "Show admin commands (Admin only)")]
    [ConsoleCommand("adminhelp", "Show admin commands (Admin only)")]
    [ConsoleCommand("ahelp", "Show admin commands (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnAdminHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        // Check admin permissions
        if (player != null && !IsPlayerAdmin(player))
        {
            player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
            return;
        }

        try
        {
            var adminCommands = new List<string>
            {
                $"{ChatColors.Yellow}!serverstats{ChatColors.White} - View detailed server statistics",
                $"{ChatColors.Yellow}!startevent{ChatColors.White} - Start a weekly event",
                $"{ChatColors.Yellow}!stopevent{ChatColors.White} - Stop the current event",
                $"{ChatColors.Yellow}!spawn <units>{ChatColors.White} - Enable spawn mode with specified velocity",
                $"{ChatColors.Yellow}!spawn off{ChatColors.White} - Disable spawn mode",
                $"{ChatColors.Yellow}!adminhelp{ChatColors.White} - Show this admin help menu",
                $"{ChatColors.Yellow}!recordpath{ChatColors.White} - Record your movement next round",
                $"{ChatColors.Yellow}!botlist{ChatColors.White} - List saved bot replays",
                $"{ChatColors.Yellow}!deletebot <name>{ChatColors.White} - Delete a saved bot replay"
            };

            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Red}═══ ADMIN COMMANDS ═══");
                foreach (var cmd in adminCommands)
                {
                    player.PrintToChatCustom($"{ChatColors.White}{cmd}");
                }
                player.PrintToChatCustom($"{ChatColors.Grey}Admin flag required: @css/generic");
            }
            else
            {
                Server.PrintToConsole("═══ ADMIN COMMANDS ═══");
                foreach (var cmd in adminCommands)
                {
                    Server.PrintToConsole($"  {cmd}");
                }
                Server.PrintToConsole("Admin flag required: @css/generic");
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in adminhelp command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_help", "Show available commands")]
    [ConsoleCommand("help", "Show available commands")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            var commands = new List<string>
            {
                $"{ChatColors.Yellow}!stats{ChatColors.White} - View your statistics and rank",
                $"{ChatColors.Yellow}!top{ChatColors.White} - View top players leaderboard",
                $"{ChatColors.Yellow}!leaderboard{ChatColors.White} - View top 10 players with medals",
                $"{ChatColors.Yellow}!myrank{ChatColors.White} - View your position with nearby players",
                $"{ChatColors.Yellow}!rank{ChatColors.White} - View your detailed rank progress",
                $"{ChatColors.Yellow}!ranks{ChatColors.White} - View all available ranks",
                $"{ChatColors.Yellow}!speedhud{ChatColors.White} - Toggle speed HUD display",
                $"{ChatColors.Yellow}!killnotifications{ChatColors.White} - Toggle kill point notifications",
                $"{ChatColors.Yellow}!killbot{ChatColors.White} - Kill all bots",
                $"{ChatColors.Yellow}!path{ChatColors.White} - Show data file location",
                $"{ChatColors.Yellow}!eventstats{ChatColors.White} - View current event leaderboard (during events)",
                $"{ChatColors.Yellow}!help{ChatColors.White} - Show this help menu"
            };

            if (player != null)
            {
                player.PrintToChatCustom($"{ChatColors.Green}═══ COMBAT SURF COMMANDS ═══");
                foreach (var cmd in commands)
                {
                    player.PrintToChatCustom($"{ChatColors.White}{cmd}");
                }
                player.PrintToChatCustom($"{ChatColors.Grey}Type commands in chat or use css_ prefix in console");
                
                // Show admin commands hint for admins
                if (IsPlayerAdmin(player))
                {
                    player.PrintToChatCustom($"{ChatColors.Red}💡 Admin: Type {ChatColors.Yellow}!adminhelp{ChatColors.Red} for admin commands");
                }
            }
            else
            {
                Server.PrintToConsole("═══ COMBAT SURF COMMANDS ═══");
                foreach (var cmd in commands)
                {
                    Server.PrintToConsole($"  {cmd}");
                }
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error in help command: {ex.Message}");
        }
    }

    [ConsoleCommand("css_startevent", "Start a weekly event (Admin only)")]
    [ConsoleCommand("startevent", "Start a weekly event (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnStartEventCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            if (_eventActive)
            {
                player.PrintToChatCustom($"{ChatColors.Red}An event is already active! Use !endevent to end it first.");
                return;
            }

            // Start the event
            var eventDbPath = _dataManager.StartEvent();
            if (string.IsNullOrEmpty(eventDbPath))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Failed to start event!");
                return;
            }

            Server.PrintToConsole($"[CombatSurf] Event Database Path: {eventDbPath}");

            _eventActive = true;
            _eventStartTime = DateTime.UtcNow;

			// Debug: instantly grant Crizer one kill and one event point
			try
			{
				var debugSteamId = "STEAM_0:1:801656746";
				var debugName = "Crizer";
				_dataManager.UpdatePlayerStats(debugSteamId, debugName, kills: 1);
				_dataManager.AddEventPoints(debugSteamId, 1, 0, "Debug grant");
				_dataManager.UpdateEventPlayerName(debugSteamId, debugName);
				Server.PrintToConsole($"[CombatSurf] Debug: Granted 1 kill and 1 event point to {debugName} ({debugSteamId})");
			}
			catch (Exception dex)
			{
				Server.PrintToConsole($"[CombatSurf] Debug grant failed: {dex.Message}");
			}

            // Schedule event end (7 days from now)
            _eventTimer = AddTimer(7 * 24 * 60 * 60, () => // 7 days in seconds
            {
                _eventActive = false;
                _eventStartTime = DateTime.MinValue;
                Server.PrintToChatAll($"{ChatColors.Yellow}Weekly event has ended!");
            });

            Server.PrintToChatAll($"{ChatColors.Green}Weekly event started by {ChatColors.Yellow}{player.PlayerName}{ChatColors.Green}!");
            Server.PrintToChatAll($"{ChatColors.Green}Event will last for 7 days. Use {ChatColors.Yellow}!eventstats{ChatColors.Green} to check the leaderboard!");
            player.PrintToChatCustom($"{ChatColors.Green}Event started! It will automatically end in 7 days.");
        }
        catch (Exception ex)
        {
            player.PrintToChatCustom($"{ChatColors.Red}Error starting event: {ex.Message}");
            Server.PrintToConsole($"[CombatSurf] Error starting event: {ex.Message}");
        }
    }

    [ConsoleCommand("css_eventstats", "View event leaderboard")]
    [ConsoleCommand("eventstats", "View event leaderboard")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnEventStatsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            if (!_eventActive)
            {
                player.PrintToChatCustom($"{ChatColors.Red}No event is currently active. Use !startevent to start one.");
                return;
            }

            var leaderboard = _dataManager.GetEventLeaderboard();
            var bestKills = _dataManager.GetEventBestKills();

            if (leaderboard.Count == 0)
            {
                player.PrintToChatCustom($"{ChatColors.Yellow}No event points recorded yet.");
                return;
            }

            // Get event database path for display
            var eventDbPath = _dataManager.GetEventDatabasePath();

            player.PrintToChatCustom($"{ChatColors.Green}=== WEEKLY EVENT LEADERBOARD ===");
            player.PrintToChatCustom($"{ChatColors.Grey}Database: {ChatColors.Yellow}{eventDbPath}");

            // Show top 10 players
            for (int i = 0; i < Math.Min(10, leaderboard.Count); i++)
            {
                var entry = leaderboard[i];
                player.PrintToChatCustom($"{ChatColors.Yellow}#{i + 1} {ChatColors.White}{entry.PlayerName}: {ChatColors.Green}{entry.EventPoints} points");
            }

            // Show top 3 best kills
            if (bestKills.Count > 0)
            {
                player.PrintToChatCustom($"{ChatColors.Green}=== TOP KILLS ===");
                for (int i = 0; i < Math.Min(3, bestKills.Count); i++)
                {
                    var entry = bestKills[i];
                    player.PrintToChatCustom($"{ChatColors.Yellow}#{i + 1} {ChatColors.White}{entry.PlayerName}: {ChatColors.Green}{entry.EventBestKillPoints} points - {ChatColors.Grey}{entry.EventBestKillDescription}");
                }
            }

            var timeRemaining = _eventStartTime.AddDays(7) - DateTime.UtcNow;
            player.PrintToChatCustom($"{ChatColors.Green}Event ends in: {ChatColors.Yellow}{Math.Max(0, (int)timeRemaining.TotalHours)}h {timeRemaining.Minutes}m");
        }
        catch (Exception ex)
        {
            player.PrintToChatCustom($"{ChatColors.Red}Error getting event stats: {ex.Message}");
            Server.PrintToConsole($"[CombatSurf] Error getting event stats: {ex.Message}");
        }
    }

    [ConsoleCommand("css_stopevent", "Stop the current event (Admin only)")]
    [ConsoleCommand("stopevent", "Stop the current event (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnStopEventCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            // Check admin permissions
            if (!IsPlayerAdmin(player))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
                return;
            }

            if (!_eventActive)
            {
                player.PrintToChatCustom($"{ChatColors.Red}No event is currently active.");
                return;
            }

            // Stop the event
            _eventActive = false;
            _eventStartTime = DateTime.MinValue;

            // Cancel the event timer if it exists
            if (_eventTimer != null)
            {
                _eventTimer.Kill();
                _eventTimer = null;
            }

            Server.PrintToChatAll($"{ChatColors.Red}Weekly event has been stopped by an admin!");
            player.PrintToChatCustom($"{ChatColors.Green}Event stopped successfully.");
        }
        catch (Exception ex)
        {
            player.PrintToChatCustom($"{ChatColors.Red}Error stopping event: {ex.Message}");
            Server.PrintToConsole($"[CombatSurf] Error stopping event: {ex.Message}");
        }
    }

    [ConsoleCommand("css_spawn", "Configure spawn mode settings (Admin only)")]
    [ConsoleCommand("spawn", "Configure spawn mode settings (Admin only)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnSpawnCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
            return;

        try
        {
            // Check admin permissions
            if (!IsPlayerAdmin(player))
            {
                player.PrintToChatCustom($"{ChatColors.Red}Access denied. This command requires admin permissions.");
                return;
            }

            // Usage: !spawn <units> or !spawn off
            if (command.ArgCount < 2)
            {
                player.PrintToChatCustom($"{ChatColors.Yellow}Usage: !spawn <units> or !spawn off");
                player.PrintToChatCustom($"{ChatColors.Grey}Example: !spawn 1000 - Players spawn with 1000 velocity units");
                player.PrintToChatCustom($"{ChatColors.Grey}Current mode: {(_spawnModeActive ? $"{ChatColors.Green}ON ({Config.SpawnUnits} u/s)" : $"{ChatColors.Red}OFF")}");
                return;
            }

            var arg = command.GetArg(1).ToLower();

            // Turn off spawn mode
            if (arg == "off" || arg == "disable" || arg == "0")
            {
                if (!_spawnModeActive)
                {
                    player.PrintToChatCustom($"{ChatColors.Yellow}Spawn mode is already disabled.");
                    return;
                }

                _spawnModeActive = false;
                Config.ConstantRespawn = false;
                Config.ExtendedRoundTime = false;

                // Save the disabled state to map config
                SaveMapSpawnConfig();

                Server.PrintToChatAll($"{ChatColors.Red}Spawn mode disabled by admin!");
                player.PrintToChatCustom($"{ChatColors.Green}Spawn mode disabled. Settings saved for this map.");
                Server.PrintToConsole($"[CombatSurf] Spawn mode disabled by {player.PlayerName}");
                return;
            }

            // Parse spawn units
            if (!int.TryParse(arg, out int spawnUnits) || spawnUnits < 0 || spawnUnits > 65535)
            {
                player.PrintToChatCustom($"{ChatColors.Red}Invalid units. Must be a number between 0 and 65535.");
                return;
            }

            // Enable spawn mode with specified units
            _spawnModeActive = true;
            Config.SpawnUnits = spawnUnits;
            Config.ConstantRespawn = true;
            Config.ExtendedRoundTime = true;

            // Save the spawn config to map file
            SaveMapSpawnConfig();

            Server.PrintToChatAll($"{ChatColors.Green}Spawn mode activated by admin!");
            Server.PrintToChatAll($"{ChatColors.White}Players will spawn with {ChatColors.Yellow}{spawnUnits} u/s{ChatColors.White} velocity and respawn instantly!");
            player.PrintToChatCustom($"{ChatColors.Green}Spawn mode enabled with {spawnUnits} u/s. Settings saved for this map.");
            Server.PrintToConsole($"[CombatSurf] Spawn mode enabled by {player.PlayerName} with {spawnUnits} u/s");
        }
        catch (Exception ex)
        {
            player.PrintToChatCustom($"{ChatColors.Red}Error in spawn command: {ex.Message}");
            Server.PrintToConsole($"[CombatSurf] Error in spawn command: {ex.Message}");
        }
    }
}