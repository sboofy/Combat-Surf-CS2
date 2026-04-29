using CounterStrikeSharp.API;
using System.Text.Json;
using CombatSurf.Config;
using System.IO;
using System.Threading;

namespace CombatSurf.Data;

public class PlayerStats
{
    public string SteamId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int Kills { get; set; } = 0;
    public int Deaths { get; set; } = 0;
    public int RoundsPlayed { get; set; } = 0;
    public int Points { get; set; } = 0;
    public int TotalPointsEarned { get; set; } = 0;
    public int NoscopeKills { get; set; } = 0;
    public int AirborneKills { get; set; } = 0;
    public int HeadshotKills { get; set; } = 0;
    public bool KillNotifications { get; set; } = true;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    // Best Kill tracking
    public int BestKillPoints { get; set; } = 0;
    public string BestKillDescription { get; set; } = "";
    public DateTime BestKillDate { get; set; } = DateTime.MinValue;

    // Event tracking (weekly events)
    public int EventPoints { get; set; } = 0;
    public int EventBestKillPoints { get; set; } = 0;
    public string EventBestKillDescription { get; set; } = "";
    public DateTime EventStartDate { get; set; } = DateTime.MinValue;
}

public class JsonDataManager
{
    private readonly string _dataFilePath;
    private readonly object _fileLock = new object();
    private Dictionary<string, PlayerStats> _playerData;

    public JsonDataManager(PluginConfig config)
    {
        // Based on working directory: /home/container/game/bin/linuxsteamrt64
        // Navigate to the CounterStrikeSharp plugins directory
        _dataFilePath = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "player_data.json");
        _playerData = new Dictionary<string, PlayerStats>();
        
        Console.WriteLine($"[CombatSurf] Using relative path from working directory: {_dataFilePath}");
        LoadData();
    }

    public void LoadData()
    {
        try
        {
            Console.WriteLine($"[CombatSurf] Attempting to load JSON from: {_dataFilePath}");
            Console.WriteLine($"[CombatSurf] File exists check: {File.Exists(_dataFilePath)}");
            Console.WriteLine($"[CombatSurf] Current working directory: {Directory.GetCurrentDirectory()}");
            
            // Try to get the absolute path
            try
            {
                var absolutePath = Path.GetFullPath(_dataFilePath);
                Console.WriteLine($"[CombatSurf] Absolute path resolved to: {absolutePath}");
                Console.WriteLine($"[CombatSurf] Absolute path exists: {File.Exists(absolutePath)}");
            }
            catch (Exception pathEx)
            {
                Console.WriteLine($"[CombatSurf] Error resolving absolute path: {pathEx.Message}");
            }

            if (File.Exists(_dataFilePath))
            {
                lock (_fileLock)
                {
                    var json = File.ReadAllText(_dataFilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, PlayerStats>>(json);
                    _playerData = data ?? new Dictionary<string, PlayerStats>();
                }
                Console.WriteLine($"[CombatSurf] ✅ Successfully loaded {_playerData.Count} player records from JSON");
            }
            else
            {
                Console.WriteLine($"[CombatSurf] ❌ JSON file not found at {_dataFilePath}");
                _playerData = new Dictionary<string, PlayerStats>();
                
                // Try to create the file anyway
                try
                {
                    SaveData();
                    Console.WriteLine("[CombatSurf] ✅ Created new player data file");
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"[CombatSurf] ❌ Failed to create file: {saveEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] ❌ Error loading player data: {ex.Message}");
            _playerData = new Dictionary<string, PlayerStats>();
        }
    }

    public void SaveData()
    {
        try
        {
            lock (_fileLock)
            {
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                };
                var json = JsonSerializer.Serialize(_playerData, options);
                File.WriteAllText(_dataFilePath, json);
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CombatSurf] Error saving player data: {ex.Message}");
        }
    }

    public PlayerStats GetPlayerStats(string steamId)
    {
        if (_playerData.TryGetValue(steamId, out var stats))
        {
            return stats;
        }

        // Create new player stats
        var newStats = new PlayerStats { SteamId = steamId };
        _playerData[steamId] = newStats;
        return newStats;
    }

    public void UpdatePlayerStats(string steamId, string playerName, 
        int kills = 0, int deaths = 0, int roundsPlayed = 0, int points = 0, 
        int totalPointsEarned = 0, int noscopeKills = 0, int airborneKills = 0, 
        int headshotKills = 0, bool? killNotifications = null)
    {
        var stats = GetPlayerStats(steamId);
        
        stats.PlayerName = playerName;
        stats.Kills += kills;
        stats.Deaths += deaths;
        stats.RoundsPlayed += roundsPlayed;
        stats.Points += points;
        stats.TotalPointsEarned += totalPointsEarned;
        stats.NoscopeKills += noscopeKills;
        stats.AirborneKills += airborneKills;
        stats.HeadshotKills += headshotKills;
        stats.LastUpdated = DateTime.UtcNow;

        if (killNotifications.HasValue)
            stats.KillNotifications = killNotifications.Value;

        _playerData[steamId] = stats;
        SaveData(); // Auto-save after each update
    }

    public List<PlayerStats> GetTopPlayers(string orderBy = "points", int limit = 10)
    {
        var players = _playerData.Values.ToList();
        
        return orderBy.ToLower() switch
        {
            "kills" => players.OrderByDescending(p => p.Kills).Take(limit).ToList(),
            "total_points_earned" => players.OrderByDescending(p => p.TotalPointsEarned).Take(limit).ToList(),
            "rounds_played" => players.OrderByDescending(p => p.RoundsPlayed).Take(limit).ToList(),
            _ => players.OrderByDescending(p => p.Points).Take(limit).ToList()
        };
    }

    public int GetPlayerRank(string steamId, string orderBy = "points")
    {
        var playerStats = GetPlayerStats(steamId);
        var allPlayers = _playerData.Values.ToList();

        var sortedPlayers = orderBy.ToLower() switch
        {
            "kills" => allPlayers.OrderByDescending(p => p.Kills).ToList(),
            "total_points_earned" => allPlayers.OrderByDescending(p => p.TotalPointsEarned).ToList(),
            _ => allPlayers.OrderByDescending(p => p.Points).ToList()
        };

        return sortedPlayers.FindIndex(p => p.SteamId == steamId) + 1;
    }

    public void RemovePlayer(string steamId)
    {
        if (_playerData.ContainsKey(steamId))
        {
            _playerData.Remove(steamId);
            SaveData();
        }
    }

    public int GetTotalPlayers()
    {
        return _playerData.Count;
    }

    public string GetDataFilePath()
    {
        return _dataFilePath;
    }

}
