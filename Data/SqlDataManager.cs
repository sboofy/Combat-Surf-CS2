using CounterStrikeSharp.API;
using Microsoft.Data.Sqlite;
using CombatSurf.Config;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace CombatSurf.Data;

public class SqlDataManager
{
    private readonly string _connectionString;
    private readonly object _dbLock = new object();
    private string _eventConnectionString = "";
    
    private static DateTime SafeParseDateTime(string dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;
        
        try
        {
            return DateTime.Parse(dateString);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    public SqlDataManager(PluginConfig config)
    {
        // Copy SQLite native library to where .NET can find it
        try
        {
            CopySqliteNativeLibrary();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] Warning: Could not copy SQLite native library: {ex.Message}");
        }
        
        // Initialize SQLite raw provider for Linux compatibility
        try
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] Warning: Could not set SQLite provider: {ex.Message}");
        }
        
        // Create database in the same location as the JSON file was
        // Based on working directory: /home/container/game/bin/linuxsteamrt64
        // Navigate to the CounterStrikeSharp plugins directory
        var dbPath = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "player_data.db");
        _connectionString = $"Data Source={dbPath}";
        
        Console.WriteLine($"[CombatSurf] Using SQLite database at: {dbPath}");
        Console.WriteLine($"[CombatSurf] Current working directory: {Directory.GetCurrentDirectory()}");
        Console.WriteLine($"[CombatSurf] Runtime: {RuntimeInformation.OSDescription}");
        
        InitializeDatabase();
        MigrateFromJsonIfExists();

        // Attempt to auto-load the most recent event database if it's from the past week
        try
        {
            LoadMostRecentEventDbIfFresh();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] Warning: Failed to load recent event DB: {ex.Message}");
        }
        
        // Debug: Check database after initialization
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM PlayerStats";
            var totalRecords = countCommand.ExecuteScalar();
            Console.WriteLine($"[CombatSurf] Database loaded with {totalRecords} player records");
            
            // Show a sample record if any exist
            if (Convert.ToInt32(totalRecords) > 0)
            {
                var sampleCommand = connection.CreateCommand();
                sampleCommand.CommandText = "SELECT PlayerName, Points, Kills FROM PlayerStats LIMIT 1";
                using var reader = sampleCommand.ExecuteReader();
                if (reader.Read())
                {
                    Console.WriteLine($"[CombatSurf] Sample record: {reader.GetString(0)} - {reader.GetInt32(1)} points, {reader.GetInt32(2)} kills");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] Error checking database contents: {ex.Message}");
        }
    }

    private void CopySqliteNativeLibrary()
    {
        try
        {
            // Based on working directory: /home/container/game/bin/linuxsteamrt64
            // We need to copy the SQLite library to where .NET can find it
            var pluginDir = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf");
            var runtimesDir = Path.Combine(pluginDir, "runtimes", "linux-x64", "native");
            var sqliteLibPath = Path.Combine(runtimesDir, "libe_sqlite3.so");
            
            Console.WriteLine($"[CombatSurf] Looking for SQLite library at: {sqliteLibPath}");
            Console.WriteLine($"[CombatSurf] SQLite library exists: {File.Exists(sqliteLibPath)}");
            
            if (File.Exists(sqliteLibPath))
            {
                // Copy to multiple locations where .NET might look
                var targetPaths = new[]
                {
                    Path.Combine(pluginDir, "libe_sqlite3.so"),
                    Path.Combine(pluginDir, "e_sqlite3.so"),
                    Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "libe_sqlite3.so"),
                    Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "e_sqlite3.so")
                };
                
                foreach (var targetPath in targetPaths)
                {
                    try
                    {
                        File.Copy(sqliteLibPath, targetPath, true);
                        Console.WriteLine($"[CombatSurf] ✅ Copied SQLite library to: {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CombatSurf] ❌ Failed to copy to {targetPath}: {ex.Message}");
                    }
                }
            }
            else
            {
                // Try alternative paths
                var altPaths = new[]
                {
                    Path.Combine(pluginDir, "2.1.6", "runtimes", "linux-x64", "native", "libe_sqlite3.so"),
                    Path.Combine(pluginDir, "runtimes", "linux-musl-x64", "native", "libe_sqlite3.so")
                };
                
                foreach (var altPath in altPaths)
                {
                    Console.WriteLine($"[CombatSurf] Trying alternative path: {altPath}");
                    if (File.Exists(altPath))
                    {
                        Console.WriteLine($"[CombatSurf] ✅ Found SQLite library at: {altPath}");
                        File.Copy(altPath, Path.Combine(pluginDir, "libe_sqlite3.so"), true);
                        File.Copy(altPath, Path.Combine(pluginDir, "e_sqlite3.so"), true);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] Error in CopySqliteNativeLibrary: {ex.Message}");
        }
    }

    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Create base table first (without new columns for compatibility)
            var createTableCommand = connection.CreateCommand();
            createTableCommand.CommandText = @"
                CREATE TABLE IF NOT EXISTS PlayerStats (
                    SteamId TEXT PRIMARY KEY,
                    PlayerName TEXT NOT NULL,
                    Kills INTEGER DEFAULT 0,
                    Deaths INTEGER DEFAULT 0,
                    RoundsPlayed INTEGER DEFAULT 0,
                    Points INTEGER DEFAULT 0,
                    TotalPointsEarned INTEGER DEFAULT 0,
                    NoscopeKills INTEGER DEFAULT 0,
                    AirborneKills INTEGER DEFAULT 0,
                    HeadshotKills INTEGER DEFAULT 0,
                    KillNotifications INTEGER DEFAULT 1,
                    LastUpdated TEXT NOT NULL
                );
            ";
            createTableCommand.ExecuteNonQuery();
            
            // Create basic indexes first
            var createIndexCommand = connection.CreateCommand();
            createIndexCommand.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_points ON PlayerStats(Points DESC);
                CREATE INDEX IF NOT EXISTS idx_kills ON PlayerStats(Kills DESC);
                CREATE INDEX IF NOT EXISTS idx_total_points ON PlayerStats(TotalPointsEarned DESC);
                CREATE INDEX IF NOT EXISTS idx_rounds ON PlayerStats(RoundsPlayed DESC);
            ";
            createIndexCommand.ExecuteNonQuery();

            // Add new columns if they don't exist (for existing databases)
            // SQLite doesn't support multiple ALTER TABLE commands in one statement
            try
            {
                // Check if columns exist first
                var checkCommand = connection.CreateCommand();
                checkCommand.CommandText = "PRAGMA table_info(PlayerStats)";
                
                var existingColumns = new HashSet<string>();
                using (var reader = checkCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1)); // Column name is at index 1
                    }
                }
                
                // Add missing columns one by one
                if (!existingColumns.Contains("BestKillPoints"))
                {
                    var addCommand1 = connection.CreateCommand();
                    addCommand1.CommandText = "ALTER TABLE PlayerStats ADD COLUMN BestKillPoints INTEGER DEFAULT 0";
                    addCommand1.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added BestKillPoints column");
                }
                
                if (!existingColumns.Contains("BestKillDescription"))
                {
                    var addCommand2 = connection.CreateCommand();
                    addCommand2.CommandText = "ALTER TABLE PlayerStats ADD COLUMN BestKillDescription TEXT DEFAULT ''";
                    addCommand2.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added BestKillDescription column");
                }
                
                if (!existingColumns.Contains("BestKillDate"))
                {
                    var addCommand3 = connection.CreateCommand();
                    addCommand3.CommandText = "ALTER TABLE PlayerStats ADD COLUMN BestKillDate TEXT DEFAULT ''";
                    addCommand3.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added BestKillDate column");
                }

                if (!existingColumns.Contains("EventPoints"))
                {
                    var addCommand4 = connection.CreateCommand();
                    addCommand4.CommandText = "ALTER TABLE PlayerStats ADD COLUMN EventPoints INTEGER DEFAULT 0";
                    addCommand4.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added EventPoints column");
                }

                if (!existingColumns.Contains("EventBestKillPoints"))
                {
                    var addCommand5 = connection.CreateCommand();
                    addCommand5.CommandText = "ALTER TABLE PlayerStats ADD COLUMN EventBestKillPoints INTEGER DEFAULT 0";
                    addCommand5.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added EventBestKillPoints column");
                }

                if (!existingColumns.Contains("EventBestKillDescription"))
                {
                    var addCommand6 = connection.CreateCommand();
                    addCommand6.CommandText = "ALTER TABLE PlayerStats ADD COLUMN EventBestKillDescription TEXT DEFAULT ''";
                    addCommand6.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added EventBestKillDescription column");
                }

                if (!existingColumns.Contains("EventStartDate"))
                {
                    var addCommand7 = connection.CreateCommand();
                    addCommand7.CommandText = "ALTER TABLE PlayerStats ADD COLUMN EventStartDate TEXT DEFAULT ''";
                    addCommand7.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Added EventStartDate column");
                }
                
                // Create index for BestKill after columns are added
                try
                {
                    var bestKillIndexCommand = connection.CreateCommand();
                    bestKillIndexCommand.CommandText = "CREATE INDEX IF NOT EXISTS idx_best_kill ON PlayerStats(BestKillPoints DESC)";
                    bestKillIndexCommand.ExecuteNonQuery();
                    Console.WriteLine("[CombatSurf] ✅ Created BestKill index");
                }
                catch (Exception indexEx)
                {
                    Console.WriteLine($"[CombatSurf] ❌ Could not create BestKill index: {indexEx.Message}");
                }
                
                Console.WriteLine("[CombatSurf] ✅ Database migration completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] ❌ Error during database migration: {ex.Message}");
                Console.WriteLine("[CombatSurf] BestKill columns may already exist or migration failed");
            }

            Console.WriteLine("[CombatSurf] ✅ SQLite database initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] ❌ Error initializing database: {ex.Message}");
            throw;
        }
    }

    private void MigrateFromJsonIfExists()
    {
        try
        {
            var jsonPath = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "player_data.json");
            
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine("[CombatSurf] No JSON file found for migration");
                return;
            }

            Console.WriteLine("[CombatSurf] Found existing JSON data, starting migration...");
            
            var json = File.ReadAllText(jsonPath);
            var data = JsonSerializer.Deserialize<Dictionary<string, PlayerStats>>(json);
            
            if (data == null || data.Count == 0)
            {
                Console.WriteLine("[CombatSurf] JSON file is empty, skipping migration");
                return;
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var kvp in data)
                {
                    var stats = kvp.Value;
                    InsertOrUpdatePlayerStats(connection, stats);
                }

                transaction.Commit();
                Console.WriteLine($"[CombatSurf] ✅ Successfully migrated {data.Count} player records from JSON to SQLite");
                
                // Backup the JSON file instead of deleting it
                var backupPath = jsonPath + ".backup";
                File.Move(jsonPath, backupPath);
                Console.WriteLine($"[CombatSurf] JSON file backed up to: {backupPath}");
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CombatSurf] ❌ Error during JSON migration: {ex.Message}");
        }
    }

    private void InsertOrUpdatePlayerStats(SqliteConnection connection, PlayerStats stats)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO PlayerStats (
                SteamId, PlayerName, Kills, Deaths, RoundsPlayed, Points,
                TotalPointsEarned, NoscopeKills, AirborneKills, HeadshotKills,
                KillNotifications, LastUpdated, BestKillPoints, BestKillDescription, BestKillDate,
                EventPoints, EventBestKillPoints, EventBestKillDescription, EventStartDate
            ) VALUES (
                @steamId, @playerName, @kills, @deaths, @roundsPlayed, @points,
                @totalPointsEarned, @noscopeKills, @airborneKills, @headshotKills,
                @killNotifications, @lastUpdated, @bestKillPoints, @bestKillDescription, @bestKillDate,
                @eventPoints, @eventBestKillPoints, @eventBestKillDescription, @eventStartDate
            )";

        command.Parameters.AddWithValue("@steamId", stats.SteamId);
        command.Parameters.AddWithValue("@playerName", stats.PlayerName);
        command.Parameters.AddWithValue("@kills", stats.Kills);
        command.Parameters.AddWithValue("@deaths", stats.Deaths);
        command.Parameters.AddWithValue("@roundsPlayed", stats.RoundsPlayed);
        command.Parameters.AddWithValue("@points", stats.Points);
        command.Parameters.AddWithValue("@totalPointsEarned", stats.TotalPointsEarned);
        command.Parameters.AddWithValue("@noscopeKills", stats.NoscopeKills);
        command.Parameters.AddWithValue("@airborneKills", stats.AirborneKills);
        command.Parameters.AddWithValue("@headshotKills", stats.HeadshotKills);
        command.Parameters.AddWithValue("@killNotifications", stats.KillNotifications ? 1 : 0);
        command.Parameters.AddWithValue("@lastUpdated", stats.LastUpdated.ToString("O"));
        command.Parameters.AddWithValue("@bestKillPoints", stats.BestKillPoints);
        command.Parameters.AddWithValue("@bestKillDescription", stats.BestKillDescription);
        command.Parameters.AddWithValue("@bestKillDate", stats.BestKillDate.ToString("O"));
        command.Parameters.AddWithValue("@eventPoints", stats.EventPoints);
        command.Parameters.AddWithValue("@eventBestKillPoints", stats.EventBestKillPoints);
        command.Parameters.AddWithValue("@eventBestKillDescription", stats.EventBestKillDescription);
        command.Parameters.AddWithValue("@eventStartDate", stats.EventStartDate.ToString("O"));

        command.ExecuteNonQuery();
    }

    public string StartEvent()
    {
        lock (_dbLock)
        {
            try
            {
                // Create event database under Events/ directory to reduce clutter
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
                var pluginDir = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf");
                var eventsDir = Path.Combine(pluginDir, "Events");
                Directory.CreateDirectory(eventsDir);
                var eventDbPath = Path.Combine(eventsDir, $"event_{timestamp}.db");
                _eventConnectionString = $"Data Source={eventDbPath}";

                // Create and initialize the event database
                using var connection = new SqliteConnection(_eventConnectionString);
                connection.Open();

                // Create event stats table
                var createTableCommand = connection.CreateCommand();
                createTableCommand.CommandText = @"
                    CREATE TABLE IF NOT EXISTS EventStats (
                        SteamId TEXT PRIMARY KEY,
                        PlayerName TEXT NOT NULL,
                        EventPoints INTEGER DEFAULT 0,
                        EventBestKillPoints INTEGER DEFAULT 0,
                        EventBestKillDescription TEXT DEFAULT '',
                        EventStartDate TEXT DEFAULT ''
                    )
                ";
                createTableCommand.ExecuteNonQuery();

                // Create indexes for performance
                var createIndexCommand = connection.CreateCommand();
                createIndexCommand.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_event_points ON EventStats(EventPoints DESC);
                    CREATE INDEX IF NOT EXISTS idx_event_best_kill ON EventStats(EventBestKillPoints DESC);
                ";
                createIndexCommand.ExecuteNonQuery();

                Console.WriteLine($"[CombatSurf] ✅ Event started - created database: {eventDbPath}");
                return eventDbPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] ❌ Error starting event: {ex.Message}");
                _eventConnectionString = "";
                return "";
            }
        }
    }

    private void LoadMostRecentEventDbIfFresh()
    {
        var pluginDir = Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf");
        var eventsDir = Path.Combine(pluginDir, "Events");

        if (!Directory.Exists(eventsDir))
        {
            Console.WriteLine("[CombatSurf] Events directory not found; no recent event DB to load");
            return;
        }

        var files = Directory.GetFiles(eventsDir, "event_*.db");
        if (files.Length == 0)
        {
            Console.WriteLine("[CombatSurf] No event databases found in Events directory");
            return;
        }

        // Try parse timestamps from filenames and select newest
        DateTime newestTime = DateTime.MinValue;
        string newestPath = string.Empty;

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file); // event_YYYY-MM-DD_HH-mm-ss
            if (name.StartsWith("event_"))
            {
                var ts = name.Substring("event_".Length);
                if (DateTime.TryParse(ts.Replace('-', ':').Replace('_', ' '), out var parsed))
                {
                    // We replaced '-' with ':' which breaks date portion; instead parse exact using known format
                }
                else
                {
                    // Fallback to exact format parsing
                    if (DateTime.TryParseExact(ts, "yyyy-MM-dd_HH-mm-ss", null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var exact))
                    {
                        if (exact > newestTime)
                        {
                            newestTime = exact;
                            newestPath = file;
                        }
                    }
                }
            }
        }

        // If failed to parse from names, fall back to file last write time
        if (string.IsNullOrEmpty(newestPath))
        {
            foreach (var file in files)
            {
                var write = File.GetLastWriteTimeUtc(file);
                if (write > newestTime)
                {
                    newestTime = write;
                    newestPath = file;
                }
            }
        }

        if (!string.IsNullOrEmpty(newestPath))
        {
            var age = DateTime.UtcNow - newestTime;
            if (age <= TimeSpan.FromDays(7))
            {
                _eventConnectionString = $"Data Source={newestPath}";
                Console.WriteLine($"[CombatSurf] Loaded recent event DB: {newestPath} (age {age.TotalHours:F1}h)");
            }
            else
            {
                Console.WriteLine("[CombatSurf] Most recent event DB is older than a week; not loading");
            }
        }
    }

    public void AddEventPoints(string steamId, int points, int killPoints, string killDescription = "")
    {
        if (string.IsNullOrEmpty(_eventConnectionString))
            return;

        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_eventConnectionString);
                connection.Open();

                // Check if player already exists in event database
                var checkCommand = connection.CreateCommand();
                checkCommand.CommandText = "SELECT COUNT(*) FROM EventStats WHERE SteamId = @steamId";
                checkCommand.Parameters.AddWithValue("@steamId", steamId);
                var exists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;

                if (exists)
                {
                    // Update existing record
                    var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = @"
                        UPDATE EventStats SET
                            EventPoints = EventPoints + @points,
                            EventBestKillPoints = MAX(EventBestKillPoints, @killPoints),
                            EventBestKillDescription = CASE
                                WHEN @killPoints > EventBestKillPoints THEN @killDescription
                                ELSE EventBestKillDescription
                            END
                        WHERE SteamId = @steamId
                    ";
                    updateCommand.Parameters.AddWithValue("@points", points);
                    updateCommand.Parameters.AddWithValue("@killPoints", killPoints);
                    updateCommand.Parameters.AddWithValue("@killDescription", killDescription);
                    updateCommand.Parameters.AddWithValue("@steamId", steamId);
                    updateCommand.ExecuteNonQuery();
                }
                else
                {
                    // Insert new record
                    var insertCommand = connection.CreateCommand();
                    insertCommand.CommandText = @"
                        INSERT INTO EventStats (SteamId, PlayerName, EventPoints, EventBestKillPoints, EventBestKillDescription, EventStartDate)
                        VALUES (@steamId, @playerName, @points, @killPoints, @killDescription, @eventStartDate)
                    ";
                    insertCommand.Parameters.AddWithValue("@steamId", steamId);
                    insertCommand.Parameters.AddWithValue("@playerName", ""); // Will be updated when we get player name
                    insertCommand.Parameters.AddWithValue("@points", points);
                    insertCommand.Parameters.AddWithValue("@killPoints", killPoints);
                    insertCommand.Parameters.AddWithValue("@killDescription", killDescription);
                    insertCommand.Parameters.AddWithValue("@eventStartDate", DateTime.UtcNow.ToString("O"));
                    insertCommand.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] ❌ Error adding event points: {ex.Message}");
            }
        }
    }

    public List<PlayerStats> GetEventLeaderboard()
    {
        if (string.IsNullOrEmpty(_eventConnectionString))
            return new List<PlayerStats>();

        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_eventConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM EventStats
                    WHERE EventPoints > 0
                    ORDER BY EventPoints DESC, EventBestKillPoints DESC
                    LIMIT 10
                ";

                var results = new List<PlayerStats>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new PlayerStats
                    {
                        SteamId = reader.GetString(0),
                        PlayerName = reader.GetString(1),
                        EventPoints = reader.GetInt32(2),
                        EventBestKillPoints = reader.GetInt32(3),
                        EventBestKillDescription = reader.GetString(4),
                        EventStartDate = SafeParseDateTime(reader.GetString(5))
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] ❌ Error getting event leaderboard: {ex.Message}");
                return new List<PlayerStats>();
            }
        }
    }

    public List<PlayerStats> GetEventBestKills()
    {
        if (string.IsNullOrEmpty(_eventConnectionString))
            return new List<PlayerStats>();

        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_eventConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM EventStats
                    WHERE EventBestKillPoints > 0
                    ORDER BY EventBestKillPoints DESC
                    LIMIT 3
                ";

                var results = new List<PlayerStats>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new PlayerStats
                    {
                        SteamId = reader.GetString(0),
                        PlayerName = reader.GetString(1),
                        EventBestKillPoints = reader.GetInt32(3),
                        EventBestKillDescription = reader.GetString(4)
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] ❌ Error getting event best kills: {ex.Message}");
                return new List<PlayerStats>();
            }
        }
    }

    public void UpdateEventPlayerName(string steamId, string playerName)
    {
        if (string.IsNullOrEmpty(_eventConnectionString))
            return;

        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_eventConnectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "UPDATE EventStats SET PlayerName = @playerName WHERE SteamId = @steamId";
                command.Parameters.AddWithValue("@playerName", playerName);
                command.Parameters.AddWithValue("@steamId", steamId);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] ❌ Error updating event player name: {ex.Message}");
            }
        }
    }

    public string GetEventDatabasePath()
    {
        return _eventConnectionString.Replace("Data Source=", "");
    }

    public PlayerStats GetPlayerStats(string steamId)
    {
        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM PlayerStats WHERE SteamId = @steamId";
                command.Parameters.AddWithValue("@steamId", steamId);

                Console.WriteLine($"[CombatSurf] Looking up player: {steamId}");
                
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    Console.WriteLine($"[CombatSurf] Found player {steamId} with {reader.GetInt32(5)} points");
                    return new PlayerStats
                    {
                        SteamId = reader.GetString(0),
                        PlayerName = reader.GetString(1),
                        Kills = reader.GetInt32(2),
                        Deaths = reader.GetInt32(3),
                        RoundsPlayed = reader.GetInt32(4),
                        Points = reader.GetInt32(5),
                        TotalPointsEarned = reader.GetInt32(6),
                        NoscopeKills = reader.GetInt32(7),
                        AirborneKills = reader.GetInt32(8),
                        HeadshotKills = reader.GetInt32(9),
                        KillNotifications = reader.GetInt32(10) == 1,
                        LastUpdated = DateTime.Parse(reader.GetString(11)),
                        BestKillPoints = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                        BestKillDescription = reader.IsDBNull(13) ? "" : reader.GetString(13),
                        BestKillDate = reader.IsDBNull(14) ? DateTime.MinValue : SafeParseDateTime(reader.GetString(14)),
                        EventPoints = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                        EventBestKillPoints = reader.IsDBNull(16) ? 0 : reader.GetInt32(16),
                        EventBestKillDescription = reader.IsDBNull(17) ? "" : reader.GetString(17),
                        EventStartDate = reader.IsDBNull(18) ? DateTime.MinValue : SafeParseDateTime(reader.GetString(18))
                    };
                }

                // Create new player stats if not found
                Console.WriteLine($"[CombatSurf] Player {steamId} not found in database, creating new record");
                var newStats = new PlayerStats { SteamId = steamId };
                InsertOrUpdatePlayerStats(connection, newStats);
                return newStats;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] Error getting player stats: {ex.Message}");
                return new PlayerStats { SteamId = steamId };
            }
        }
    }

    public void UpdatePlayerStats(string steamId, string playerName, 
        int kills = 0, int deaths = 0, int roundsPlayed = 0, int points = 0, 
        int totalPointsEarned = 0, int noscopeKills = 0, int airborneKills = 0, 
        int headshotKills = 0, bool? killNotifications = null, int bestKillPoints = 0, 
        string bestKillDescription = "", DateTime? bestKillDate = null)
    {
        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Get current stats
                var currentStats = GetPlayerStatsFromDb(connection, steamId);
                if (currentStats == null)
                {
                    currentStats = new PlayerStats { SteamId = steamId };
                }

                // Update values
                currentStats.PlayerName = playerName;
                currentStats.Kills += kills;
                currentStats.Deaths += deaths;
                currentStats.RoundsPlayed += roundsPlayed;
                currentStats.Points += points;
                currentStats.TotalPointsEarned += totalPointsEarned;
                currentStats.NoscopeKills += noscopeKills;
                currentStats.AirborneKills += airborneKills;
                currentStats.HeadshotKills += headshotKills;
                currentStats.LastUpdated = DateTime.UtcNow;

                if (killNotifications.HasValue)
                    currentStats.KillNotifications = killNotifications.Value;

                // Update best kill if this kill is better
                if (bestKillPoints > 0 && bestKillPoints > currentStats.BestKillPoints)
                {
                    currentStats.BestKillPoints = bestKillPoints;
                    currentStats.BestKillDescription = bestKillDescription;
                    currentStats.BestKillDate = bestKillDate ?? DateTime.UtcNow;
                }

                // Save to database
                InsertOrUpdatePlayerStats(connection, currentStats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] Error updating player stats: {ex.Message}");
            }
        }
    }

    private PlayerStats? GetPlayerStatsFromDb(SqliteConnection connection, string steamId)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM PlayerStats WHERE SteamId = @steamId";
        command.Parameters.AddWithValue("@steamId", steamId);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new PlayerStats
            {
                SteamId = reader.GetString(0),
                PlayerName = reader.GetString(1),
                Kills = reader.GetInt32(2),
                Deaths = reader.GetInt32(3),
                RoundsPlayed = reader.GetInt32(4),
                Points = reader.GetInt32(5),
                TotalPointsEarned = reader.GetInt32(6),
                NoscopeKills = reader.GetInt32(7),
                AirborneKills = reader.GetInt32(8),
                HeadshotKills = reader.GetInt32(9),
                KillNotifications = reader.GetInt32(10) == 1,
                LastUpdated = DateTime.Parse(reader.GetString(11)),
                BestKillPoints = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                BestKillDescription = reader.IsDBNull(13) ? "" : reader.GetString(13),
                BestKillDate = reader.IsDBNull(14) ? DateTime.MinValue : SafeParseDateTime(reader.GetString(14))
            };
        }

        return null;
    }

    public List<PlayerStats> GetTopPlayers(string orderBy = "points", int limit = 10)
    {
        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                var orderColumn = orderBy.ToLower() switch
                {
                    "kills" => "Kills",
                    "total_points_earned" => "TotalPointsEarned",
                    "rounds_played" => "RoundsPlayed",
                    _ => "Points"
                };

                command.CommandText = $"SELECT * FROM PlayerStats ORDER BY {orderColumn} DESC LIMIT @limit";
                command.Parameters.AddWithValue("@limit", limit);

                var players = new List<PlayerStats>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    players.Add(new PlayerStats
                    {
                        SteamId = reader.GetString(0),
                        PlayerName = reader.GetString(1),
                        Kills = reader.GetInt32(2),
                        Deaths = reader.GetInt32(3),
                        RoundsPlayed = reader.GetInt32(4),
                        Points = reader.GetInt32(5),
                        TotalPointsEarned = reader.GetInt32(6),
                        NoscopeKills = reader.GetInt32(7),
                        AirborneKills = reader.GetInt32(8),
                        HeadshotKills = reader.GetInt32(9),
                        KillNotifications = reader.GetInt32(10) == 1,
                        LastUpdated = DateTime.Parse(reader.GetString(11)),
                        BestKillPoints = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                        BestKillDescription = reader.IsDBNull(13) ? "" : reader.GetString(13),
                        BestKillDate = reader.IsDBNull(14) ? DateTime.MinValue : SafeParseDateTime(reader.GetString(14))
                    });
                }

                return players;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] Error getting top players: {ex.Message}");
                return new List<PlayerStats>();
            }
        }
    }

    public int GetPlayerRank(string steamId, string orderBy = "points")
    {
        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                var orderColumn = orderBy.ToLower() switch
                {
                    "kills" => "Kills",
                    "total_points_earned" => "TotalPointsEarned",
                    _ => "Points"
                };

                command.CommandText = $@"
                    SELECT COUNT(*) + 1 as Rank
                    FROM PlayerStats p1
                    CROSS JOIN PlayerStats p2
                    WHERE p1.SteamId = @steamId AND p2.{orderColumn} > p1.{orderColumn}";
                
                command.Parameters.AddWithValue("@steamId", steamId);

                var result = command.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] Error getting player rank: {ex.Message}");
                return 1;
            }
        }
    }

    public void RemovePlayer(string steamId)
    {
        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM PlayerStats WHERE SteamId = @steamId";
                command.Parameters.AddWithValue("@steamId", steamId);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] Error removing player: {ex.Message}");
            }
        }
    }

    public int GetTotalPlayers()
    {
        lock (_dbLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM PlayerStats";
                var result = command.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatSurf] Error getting total players: {ex.Message}");
                return 0;
            }
        }
    }

    public string GetDataFilePath()
    {
        return _connectionString;
    }
}
