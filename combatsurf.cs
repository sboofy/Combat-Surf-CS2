using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Listeners;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CombatSurf.Config;
using CombatSurf.Data;
using CombatSurf.Utils;
using TagsApi;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace CombatSurf;

public partial class CombatSurf : BasePlugin, IPluginConfig<PluginConfig>
{
	public override string ModuleName => "combatsurf";
    public override string ModuleVersion => "1.0.1";
	public override string ModuleAuthor => "Crizer + spooky";

	private bool _roundGodmodeActive = false;
	private SqlDataManager _dataManager = null!;
	private Dictionary<int, PlayerStats> _playerStatsCache = new();

	// Spawn mode state (controlled by !spawn admin command)
	private bool _spawnModeActive = false;

	// Event tracking
	private bool _eventActive = false;
	private DateTime _eventStartTime = DateTime.MinValue;
	private CounterStrikeSharp.API.Modules.Timers.Timer? _eventTimer;
	public ITagApi? TagApi { get; set; }
	public PluginConfig Config { get; set; } = new();
	
	// Speed HUD System
	private Dictionary<int, PlayerHudInfo> _playerHudInfo = new();
	private bool _speedHudEnabled = true;
	private float _hudTickrate = 64.0f;
	
	// Damage hook tracking
	private bool _damageHookRegistered = false;
	private object? _takeDamageHook = null;
	
	// Replay recording/playback state
	private bool _pendingRecordNextRound = false;
	private int _pendingRecordSlot = -1;
	private bool _isRecordingPath = false;
	private int _recordingSlot = -1;
	private List<ReplayFrame> _currentRecording = new();
	private class BotReplay
	{
		public string RecorderName { get; set; } = "Replay Bot";
		public List<ReplayFrame> Frames { get; set; } = new();
		public CCSPlayerController? Controller { get; set; }
		public int Index { get; set; } = 0;
		public string FilePath { get; set; } = string.Empty;
	}
	private List<BotReplay> _loadedReplays = new();
	private List<ReplayFrame> _loadedReplay = new();
	private int _replayIndex = 0;
	private CCSPlayerController? _replayBotController = null;
	private string _replayRecorderName = "Replay Bot";
	private bool _botSpawnAttemptedThisRound = false;
	// Match SqlDataManager path scheme: ../../csgo/addons/counterstrikesharp/plugins/combatsurf/
	private string _botsDir => Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "Bots");
	private string _mapsDir => Path.Combine("..", "..", "csgo", "addons", "counterstrikesharp", "plugins", "combatsurf", "Maps");

	private class ReplayFrame
	{
		public float PX { get; set; }
		public float PY { get; set; }
		public float PZ { get; set; }
		public float AX { get; set; }
		public float AY { get; set; }
		public float AZ { get; set; }
		public float VX { get; set; }
		public float VY { get; set; }
		public float VZ { get; set; }
		public long Buttons { get; set; }
		public uint Flags { get; set; }
	}

	private class RecordingData
	{
		public string RecorderName { get; set; } = "Replay Bot";
		public List<ReplayFrame> Frames { get; set; } = new();
	}

	private class MapSpawnConfig
	{
		public bool SpawnModeEnabled { get; set; } = false;
		public int SpawnUnits { get; set; } = 0;
	}

	private void LoadMapSpawnConfig()
	{
		try
		{
			Directory.CreateDirectory(_mapsDir);
			var map = Server.MapName ?? "unknown";
			var configPath = Path.Combine(_mapsDir, $"{map}.json");

			if (File.Exists(configPath))
			{
				var json = File.ReadAllText(configPath);
				var config = System.Text.Json.JsonSerializer.Deserialize<MapSpawnConfig>(json);

				if (config != null)
				{
					_spawnModeActive = config.SpawnModeEnabled;
					if (config.SpawnModeEnabled)
					{
						Config.SpawnUnits = config.SpawnUnits;
						Config.ConstantRespawn = true;
						Config.ExtendedRoundTime = true;
						Server.PrintToConsole($"[CombatSurf] Loaded spawn config for {map}: {config.SpawnUnits} u/s");
						Server.PrintToChatAll($"{ChatColors.Green}Spawn mode loaded: {ChatColors.Yellow}{config.SpawnUnits} u/s");
					}
					else
					{
						Server.PrintToConsole($"[CombatSurf] Spawn mode disabled for {map}");
					}
				}
			}
			else
			{
				Server.PrintToConsole($"[CombatSurf] No spawn config found for {map}");
			}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error loading map spawn config: {ex.Message}");
		}
	}

	private void SaveMapSpawnConfig()
	{
		try
		{
			Directory.CreateDirectory(_mapsDir);
			var map = Server.MapName ?? "unknown";
			var configPath = Path.Combine(_mapsDir, $"{map}.json");

			var config = new MapSpawnConfig
			{
				SpawnModeEnabled = _spawnModeActive,
				SpawnUnits = _spawnModeActive ? Config.SpawnUnits : 0
			};

			var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(configPath, json);
			Server.PrintToConsole($"[CombatSurf] Saved spawn config for {map}: SpawnMode={config.SpawnModeEnabled}, Units={config.SpawnUnits}");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error saving map spawn config: {ex.Message}");
		}
	}


    public override void Load(bool hotReload)
    {
            Logger.LogInformation("[CombatSurf] ============ PLUGIN LOADING START ============");
            Logger.LogInformation("[CombatSurf] Version: {Version}, Author: {Author}", ModuleVersion, ModuleAuthor);
            Logger.LogInformation("[CombatSurf] Hot reload: {HotReload}", hotReload);
            Logger.LogInformation("[CombatSurf] Load time: {Now}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            // Log server information
            LogServerInfo();

            // Log current players
            LogCurrentPlayers();

            // Log team information
            LogTeamInfo();

            // Log plugin configuration
            LogPluginConfig();

            Logger.LogInformation("[CombatSurf] Registering event handlers...");
            RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventRoundFreezeEnd registered");

            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventPlayerSpawn registered");

            RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventPlayerDeath registered");

            RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventRoundEnd registered");

            RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventRoundStart registered");

            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventPlayerConnectFull registered");

            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventPlayerDisconnect registered");

            RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
            Logger.LogInformation("[CombatSurf] - EventWeaponFire registered for infinite ammo");

            // Block chat using command listener (more reliable than events)
            AddCommandListener("say", OnSayCommand, HookMode.Pre);
            AddCommandListener("say_team", OnSayCommand, HookMode.Pre);
            Logger.LogInformation("[CombatSurf] - Chat command listeners registered for chat blocking");

            // Register tick system for Speed HUD (similar to SharpTimer)
            RegisterListener<Listeners.OnTick>(OnTick);
            Logger.LogInformation("[CombatSurf] - Speed HUD tick system registered");

            // Hook damage to enforce AWP one-shot kills
            // Use reflection to safely check for the damage hook API (handles API changes after CS2 updates)
            try
            {
                // Try the old API first: CBaseEntity_TakeDamageOldFunc
                var takeDamageField = typeof(VirtualFunctions).GetField("CBaseEntity_TakeDamageOldFunc", BindingFlags.Public | BindingFlags.Static);
                if (takeDamageField == null)
                {
                    // Try alternative: CBaseEntity_TakeDamageFunc (without "Old")
                    takeDamageField = typeof(VirtualFunctions).GetField("CBaseEntity_TakeDamageFunc", BindingFlags.Public | BindingFlags.Static);
                }
                
                if (takeDamageField != null)
                {
                    var takeDamageFunc = takeDamageField.GetValue(null);
                    if (takeDamageFunc != null)
                    {
                        var hookMethod = takeDamageFunc.GetType().GetMethod("Hook", BindingFlags.Public | BindingFlags.Instance);
                        if (hookMethod != null)
                        {
                            // Get the OnTakeDamage method and create a delegate
                            var onTakeDamageMethod = GetType().GetMethod(nameof(OnTakeDamage), BindingFlags.NonPublic | BindingFlags.Instance);
                            if (onTakeDamageMethod != null)
                            {
                                // Create a delegate for the hook callback
                                var delegateType = typeof(Func<,>).MakeGenericType(typeof(DynamicHook), typeof(HookResult));
                                var hookDelegate = Delegate.CreateDelegate(delegateType, this, onTakeDamageMethod);
                                
                                hookMethod.Invoke(takeDamageFunc, new object[] { hookDelegate, HookMode.Pre });
                                _damageHookRegistered = true;
                                _takeDamageHook = takeDamageFunc;
                                Logger.LogInformation("[CombatSurf] - Damage hook registered for AWP 1-shot");
                            }
                        }
                    }
                }
                
                if (!_damageHookRegistered)
                {
                    Logger.LogWarning("[CombatSurf] - Damage hook API not available. AWP one-shot feature disabled. Please update CounterStrikeSharp or use a compatible version.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[CombatSurf] - Failed to register damage hook: {ex.Message}. AWP one-shot feature disabled.");
                _damageHookRegistered = false;
            }

            // Log final status
            LogFinalStatus();

            Logger.LogInformation("[CombatSurf] ============ PLUGIN LOADING SUCCESS ============");
    }

    public override void Unload(bool hotReload)
    {
        if (_damageHookRegistered && _takeDamageHook != null)
        {
            try
            {
                var unhookMethod = _takeDamageHook.GetType().GetMethod("Unhook", BindingFlags.Public | BindingFlags.Instance);
                if (unhookMethod != null)
                {
                    // Get the OnTakeDamage method and create a delegate
                    var onTakeDamageMethod = GetType().GetMethod(nameof(OnTakeDamage), BindingFlags.NonPublic | BindingFlags.Instance);
                    if (onTakeDamageMethod != null)
                    {
                        // Create a delegate for the unhook callback
                        var delegateType = typeof(Func<,>).MakeGenericType(typeof(DynamicHook), typeof(HookResult));
                        var hookDelegate = Delegate.CreateDelegate(delegateType, this, onTakeDamageMethod);
                        
                        unhookMethod.Invoke(_takeDamageHook, new object[] { hookDelegate, HookMode.Pre });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[CombatSurf] - Failed to unhook damage handler: {ex.Message}");
            }
        }
    }

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		try
		{
			Server.PrintToConsole("[CombatSurf] ============ ALL PLUGINS LOADED ============");
			Server.PrintToConsole("[CombatSurf] OnAllPluginsLoaded called");
			
			// Log updated player information after all plugins are loaded
			LogCurrentPlayers();
			LogTeamInfo();
			
			// Log database status if available
			if (_dataManager != null)
			{
				Server.PrintToConsole("[CombatSurf] Database Manager: Ready");
				Server.PrintToConsole($"[CombatSurf] Database Path: {_dataManager.GetDataFilePath()}");

				// Try to resume an active event if a recent event DB exists
				try
				{
					var eventDbPath = _dataManager.GetEventDatabasePath();
					if (!string.IsNullOrEmpty(eventDbPath) && File.Exists(eventDbPath))
					{
						var fileName = Path.GetFileNameWithoutExtension(eventDbPath); // event_yyyy-MM-dd_HH-mm-ss
						if (fileName.StartsWith("event_"))
						{
							var ts = fileName.Substring("event_".Length);
							if (DateTime.TryParseExact(ts, "yyyy-MM-dd_HH-mm-ss", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var startUtc))
							{
								var age = DateTime.UtcNow - startUtc;
								if (age <= TimeSpan.FromDays(7))
								{
									_eventActive = true;
									_eventStartTime = startUtc;
									Server.PrintToConsole($"[CombatSurf] Resumed active event from DB: {eventDbPath} (age {age.TotalHours:F1}h)");
								}
								else
								{
									Server.PrintToConsole("[CombatSurf] Found event DB but it's older than a week; not resuming");
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					Server.PrintToConsole($"[CombatSurf] Error attempting to resume event: {ex.Message}");
				}
			}
			else
			{
				Server.PrintToConsole("[CombatSurf] Database Manager: NOT READY");
			}
			
			File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] All plugins loaded successfully\n");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnAllPluginsLoaded: {ex.Message}");
			File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Error in OnAllPluginsLoaded: {ex.Message}\n");
		}
	}

	public void OnConfigParsed(PluginConfig config)
	{
		try
		{
			Server.PrintToConsole("[CombatSurf] ============ CONFIG PARSING START ============");
			
			// Use default config if parsing failed or file doesn't exist
			if (config == null)
			{
				Server.PrintToConsole("[CombatSurf] Config is null, using defaults");
				config = new PluginConfig();
			}
			
			Server.PrintToConsole($"[CombatSurf] Godmode Enabled: {config.EnableGodmode}");
			Server.PrintToConsole($"[CombatSurf] Godmode Time: {config.GodmodeTime}s");
			
			Config = config;
			Server.PrintToConsole("[CombatSurf] Config assigned successfully");
			
			Server.PrintToConsole("[CombatSurf] Creating SqlDataManager...");
			_dataManager = new SqlDataManager(config);
			Server.PrintToConsole("[CombatSurf] SqlDataManager created successfully");
			Server.PrintToConsole($"[CombatSurf] Database Path: {_dataManager.GetDataFilePath()}");
			
			Server.PrintToConsole("[CombatSurf] ============ CONFIG PARSING SUCCESS ============");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] ============ CONFIG PARSING FAILED ============");
			Server.PrintToConsole($"[CombatSurf] Config ERROR: {ex.Message}");
			Server.PrintToConsole($"[CombatSurf] Config STACK: {ex.StackTrace}");
			
			// Don't crash the plugin, use defaults
			Server.PrintToConsole("[CombatSurf] Using default configuration to prevent crash");
			Config = new PluginConfig();
			_dataManager = new SqlDataManager(Config);
		}
	}

	private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
	{
		try
		{
			Server.PrintToChatAll($"{ChatColors.Green}Round started");
			_botSpawnAttemptedThisRound = false;

			// Load map-specific spawn config
			LoadMapSpawnConfig();

			// Ensure server cvars won't auto-kick bots
			Server.ExecuteCommand("mp_autoteambalance 0");
			Server.ExecuteCommand("mp_limitteams 30");
			Server.ExecuteCommand("bot_join_after_player 0");
			Server.ExecuteCommand("bot_auto_vacate 0");
			Server.ExecuteCommand("bot_quota_mode normal");

			// Allow players to spawn instantly when joining teams from spectator
			Server.ExecuteCommand("mp_respawn_on_death_ct 1");
			Server.ExecuteCommand("mp_respawn_on_death_t 1");
			Server.ExecuteCommand("mp_force_pick_time 99999");

			// Prevent round end if spawn mode is active - SIMPLE approach
			if (_spawnModeActive && Config != null && Config.ExtendedRoundTime)
			{
				// Only set this ONE command to prevent round ending
				Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
				Server.PrintToConsole("[CombatSurf] Spawn mode active - round win conditions disabled");
			}
			else
			{
				// Re-enable round win conditions when spawn mode is off
				Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
			}

			// If we have a saved replay for this map, ensure it's loaded
			TryLoadReplayForCurrentMap();

			// If replays exist, spawn replay bot and start playback
			if (_loadedReplays.Count > 0 && !_botSpawnAttemptedThisRound)
			{
				EnsureReplayBotsSpawned();
			}

			// Arm recording if requested by admin last round
			if (_pendingRecordNextRound && _pendingRecordSlot >= 0)
			{
				var recorder = Utilities.GetPlayers().FirstOrDefault(p => p.Slot == _pendingRecordSlot);
				if (PlayerUtils.IsValidHumanPlayer(recorder))
				{
					_isRecordingPath = true;
					_recordingSlot = _pendingRecordSlot;
					_currentRecording.Clear();
					SendChatMessage(recorder!, $"{ChatColors.Green}Recording started. Your movement this round will be saved.");
				}
				else
				{
					SendChatMessage($"{ChatColors.Red}RecordPath: requesting admin not found; recording cancelled.");
				}
				_pendingRecordNextRound = false;
				_pendingRecordSlot = -1;
			}
			// Enable speed HUD for all human players at round start
			foreach (var player in Utilities.GetPlayers().Where(PlayerUtils.IsValidHumanPlayer))
			{
				EnableSpeedHudForPlayer(player);
			}

			return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnRoundStart: {ex.Message}");
			return HookResult.Continue;
		}
	}

	private HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
	{
		try
		{
			// Match started (freeze time ended) - players can move
			if (_loadedReplays.Count > 0 && !_botSpawnAttemptedThisRound)
			{
				EnsureReplayBotsSpawned();
			}
			// Enable godmode at freeze end if configured
			if (Config != null && Config.EnableGodmode)
			{
				_roundGodmodeActive = true;
				PlayerUtils.GiveGodmodeToAll();
				Server.PrintToConsole($"[CombatSurf] Godmode activated for {Config.GodmodeTime}s");

				// Disable godmode after configured time
				AddTimer(Config.GodmodeTime, () =>
				{
					_roundGodmodeActive = false;
					PlayerUtils.RemoveGodmodeFromAll();

					if (Config != null && Config.ShowGodmodeMessages)
						SendChatMessage("Godmode disabled! Fight!");
				});
			}

			return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnRoundFreezeEnd: {ex.Message}");
			return HookResult.Continue;
		}
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		try
		{
			var player = @event.Userid;

			if (!PlayerUtils.IsValidPlayer(player))
				return HookResult.Continue;
		//test
		// Apply godmode if round godmode is active (for players who spawn during godmode period)
		if (_roundGodmodeActive && Config != null && Config.EnableGodmode)
		{
			AddTimer(0.1f, () =>
			{
				if (PlayerUtils.IsValidPlayer(player))
				{
					PlayerUtils.GiveGodmode(player!);
				}
			});
		}

		// Give infinite ammo when player spawns (with delay to ensure weapons are loaded)
		AddTimer(0.2f, () =>
		{
			if (PlayerUtils.IsValidPlayer(player))
			{
				GiveInfiniteAmmo(player!);
			}
		});

		// Enable speed HUD for spawning human player
		AddTimer(0.3f, () =>
		{
			if (PlayerUtils.IsValidHumanPlayer(player))
			{
				EnableSpeedHudForPlayer(player!);
			}
		});

		return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnPlayerSpawn: {ex.Message}");
			return HookResult.Continue;
		}
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		try
		{
			var victim = @event.Userid;
			var attacker = @event.Attacker;

		if (!PlayerUtils.IsValidPlayer(victim) || _dataManager == null)
			return HookResult.Continue;

			try
			{
				// Update victim death count (no point penalty for dying)
				var victimSteamId = victim!.SteamID.ToString();
				var victimName = victim.PlayerName;
				_dataManager.UpdatePlayerStats(victimSteamId, victimName, deaths: 1);
				
				// Update victim cache with latest stats
				var victimStats = _dataManager.GetPlayerStats(victimSteamId);
				if (victimStats != null)
				{
					_playerStatsCache[victim.Slot] = victimStats;
				}

				// Update attacker kill count and calculate advanced points if valid
				if (PlayerUtils.IsValidPlayer(attacker) && attacker!.SteamID != victim.SteamID)
				{
					var attackerSteamId = attacker.SteamID.ToString();
					var attackerName = attacker.PlayerName;
					
					// Analyze the kill for advanced points calculation
					var killAnalysis = PointsCalculator.AnalyzeKill(attacker, victim, @event);

					// Create best kill description
					var bestKillDesc = PointsCalculator.GetKillDescription(killAnalysis);

					// Update stats with detailed breakdown
					_dataManager.UpdatePlayerStats(
						attackerSteamId,
						attackerName,
						kills: 1,
						points: killAnalysis.PointsAwarded,
						totalPointsEarned: killAnalysis.PointsAwarded,
						noscopeKills: killAnalysis.IsNoscope ? 1 : 0,
						airborneKills: killAnalysis.IsAirborne ? 1 : 0,
						headshotKills: killAnalysis.IsHeadshot ? 1 : 0,
						bestKillPoints: killAnalysis.PointsAwarded,
						bestKillDescription: bestKillDesc,
						bestKillDate: DateTime.UtcNow
					);

					// Track event points if event is active
					if (_eventActive && _eventStartTime != DateTime.MinValue)
					{
						_dataManager.AddEventPoints(attackerSteamId, killAnalysis.PointsAwarded, killAnalysis.PointsAwarded, bestKillDesc);
						// Update player name in event database
						_dataManager.UpdateEventPlayerName(attackerSteamId, attackerName);
					}

					// Get updated stats for notifications and rank check
					var stats = _dataManager.GetPlayerStats(attackerSteamId);
					if (stats != null)
					{
						// Update cache
						_playerStatsCache[attacker.Slot] = stats;
						
						// Show kill notification
						// Kill notifications removed with HUD system
						
						// Also show in chat
						var multiplierText = killAnalysis.Multipliers.Count > 0 
							? $" {ChatColors.Yellow}[{string.Join(", ", killAnalysis.Multipliers)}]" 
							: "";
						
						SendChatMessage(attacker, $"{ChatColors.Green}+{killAnalysis.PointsAwarded} points{ChatColors.White}{multiplierText}");
						
						// If kill is worth more than 100 points, announce to all chat
						if (killAnalysis.PointsAwarded > 100)
						{
							SendChatMessage($"{ChatColors.Yellow}{attackerName}{ChatColors.White} got a {ChatColors.Red}{killAnalysis.PointsAwarded} point{ChatColors.White} kill! {ChatColors.Green}{bestKillDesc}{ChatColors.White}{multiplierText}");
						}
						
						// Check for rank up by comparing old vs new rank
						var oldPoints = stats.Points - killAnalysis.PointsAwarded;
						var oldRank = RankSystem.GetRank(oldPoints);
						var newRank = RankSystem.GetRank(stats.Points);
						
						if (oldRank.Name != newRank.Name)
						{
							// Player ranked up!
							SendChatMessage($"{ChatColors.Yellow}{attackerName}{ChatColors.White} ranked up to {newRank.Color}{newRank.Name}{ChatColors.White}!");
							
							// Update clan tag when they rank up
							SetPlayerClanTag(attacker, stats);
						}
						
					}
				}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error updating player death stats: {ex.Message}");
		}

		// Constant respawn if spawn mode is active
		// ONLY respawn if player is on an actual playing team (CT=2 or T=3), not spectator (0/1)
		if (_spawnModeActive && Config != null && Config.ConstantRespawn)
		{
			if (PlayerUtils.IsValidHumanPlayer(victim))
			{
				// Check if player is on a playing team BEFORE trying to respawn
				if (victim!.TeamNum == 2 || victim.TeamNum == 3)
				{
					// Respawn player after a short delay (1 second)
					AddTimer(1.0f, () =>
					{
						// Double-check player is still on a playing team (they might have gone to spectator)
						if (PlayerUtils.IsValidPlayer(victim) && (victim!.TeamNum == 2 || victim.TeamNum == 3))
						{
							// Respawn the player normally
							victim.Respawn();

							// Apply spawn velocity and make player jump after respawn
							if (Config.SpawnUnits > 0)
							{
								AddTimer(0.2f, () =>
								{
									if (PlayerUtils.IsValidPlayer(victim) && (victim!.TeamNum == 2 || victim.TeamNum == 3))
									{
										var velocityPawn = victim.PlayerPawn.Value;
										if (velocityPawn != null)
										{
											// Set player velocity to spawn units in direction they're looking
											var angles = velocityPawn.EyeAngles;
											var radianYaw = angles!.Y * (Math.PI / 180.0);
											var radianPitch = angles.X * (Math.PI / 180.0);

											var velocityX = (float)(Math.Cos(radianPitch) * Math.Cos(radianYaw) * Config.SpawnUnits);
											var velocityY = (float)(Math.Cos(radianPitch) * Math.Sin(radianYaw) * Config.SpawnUnits);
											var velocityZ = (float)(-Math.Sin(radianPitch) * Config.SpawnUnits);

											velocityPawn.AbsVelocity.X = velocityX;
											velocityPawn.AbsVelocity.Y = velocityY;
											velocityPawn.AbsVelocity.Z = velocityZ;

											// Make player jump by adding upward velocity
											velocityPawn.AbsVelocity.Z += 300f;
										}
									}
								});
							}
						}
					});
				}
			}
		}

			return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnPlayerDeath: {ex.Message}");
			return HookResult.Continue;
		}
	}

	private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
	{
		_roundGodmodeActive = false;

		try
		{
			// If recording, stop and persist to disk under /Bots
			if (_isRecordingPath && _recordingSlot >= 0 && _currentRecording.Count > 0)
			{
				PersistRecordingForCurrentMap();
				_isRecordingPath = false;
				_recordingSlot = -1;
				_currentRecording.Clear();
			}

				// Update rounds played for all players
				foreach (var player in PlayerUtils.GetValidPlayers())
				{
					var steamId = player.SteamID.ToString();
					_dataManager.UpdatePlayerStats(steamId, player.PlayerName, roundsPlayed: 1);
				}
			}
			catch (Exception ex)
			{
				Server.PrintToConsole($"[CombatSurf] Error updating round stats: {ex.Message}");
			}

			return HookResult.Continue;
	}

	private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
	{
		try
		{
			var player = @event.Userid;
			
		if (!PlayerUtils.IsValidHumanPlayer(player) || _dataManager == null)
			return HookResult.Continue;

			try
			{
				var steamId = player!.SteamID.ToString();
				var teamName = GetTeamName(player.TeamNum);
				
				Server.PrintToConsole($"[CombatSurf] ============ PLAYER CONNECTED ============");
				Server.PrintToConsole($"[CombatSurf] Player: {player.PlayerName}");
				Server.PrintToConsole($"[CombatSurf] Steam ID: {steamId}");
				Server.PrintToConsole($"[CombatSurf] Slot: {player.Slot}");
				Server.PrintToConsole($"[CombatSurf] Team: {teamName} ({player.TeamNum})");
				Server.PrintToConsole($"[CombatSurf] Is Bot: {player.IsBot}");
				Server.PrintToConsole($"[CombatSurf] Is Valid: {PlayerUtils.IsValidPlayer(player)}");
				
				// Load player stats into cache
				var stats = _dataManager.GetPlayerStats(steamId);
				// Update player name in case it changed
				_dataManager.UpdatePlayerStats(steamId, player.PlayerName);
				
				// Reload stats after potential update to ensure cache is current
				stats = _dataManager.GetPlayerStats(steamId);
				
				if (stats != null)
				{
					_playerStatsCache[player.Slot] = stats;
					
					// Set clan tag to show rank in TAB menu/scoreboard (doesn't change actual name)
					SetPlayerClanTag(player, stats);
					
				Server.PrintToConsole($"[CombatSurf] Stats Loaded: {stats.Points} points, {stats.Kills} kills, {stats.Deaths} deaths");
				var rank = RankSystem.GetRank(stats.Points);
				Server.PrintToConsole($"[CombatSurf] Rank: {rank?.Name ?? "Unknown"}");
					File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Player connected: {(player?.PlayerName ?? "Unknown Player")} ({steamId}) - {stats.Points} points\n");
				}
				else
				{
					Server.PrintToConsole($"[CombatSurf] No stats found for {player.PlayerName}");
					File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Player connected (no stats): {(player?.PlayerName ?? "Unknown Player")} ({steamId})\n");
				}

				// Players should choose their own team normally - removed auto-team assignment
			}
			catch (Exception ex)
			{
				Server.PrintToConsole($"[CombatSurf] Error loading player stats: {ex.Message}");
				File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Error loading stats for {(player?.PlayerName ?? "Unknown Player")}: {ex.Message}\n");
			}

			return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnPlayerConnectFull: {ex.Message}");
			return HookResult.Continue;
		}
	}

	private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
	{
		try
		{
			var player = @event.Userid;
			
			if (player != null)
			{
				Server.PrintToConsole($"[CombatSurf] ============ PLAYER DISCONNECTED ============");
				Server.PrintToConsole($"[CombatSurf] Player: {player.PlayerName}");
				Server.PrintToConsole($"[CombatSurf] Slot: {player.Slot}");
				Server.PrintToConsole($"[CombatSurf] Steam ID: {player.SteamID}");
				
				// Log stats before removing from cache
				if (_playerStatsCache.TryGetValue(player.Slot, out var stats))
				{
					Server.PrintToConsole($"[CombatSurf] Final Stats: {stats.Points} points, {stats.Kills} kills, {stats.Deaths} deaths");
					File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Player disconnected: {player.PlayerName} ({player.SteamID}) - {stats.Points} points\n");
				}
				else
				{
					File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Player disconnected (no stats): {player.PlayerName} ({player.SteamID})\n");
				}
				
				_playerStatsCache.Remove(player.Slot);
				_playerHudInfo.Remove(player.Slot);
				
				Server.PrintToConsole($"[CombatSurf] Removed from cache and HUD info");
			}

			return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnPlayerDisconnect: {ex.Message}");
			File.AppendAllText("combatsurf.log", $"[{DateTime.Now}] Error in OnPlayerDisconnect: {ex.Message}\n");
			return HookResult.Continue;
		}
	}

	private HookResult OnSayCommand(CCSPlayerController? player, CommandInfo commandInfo)
	{
		try
		{
			if (!PlayerUtils.IsValidPlayer(player))
				return HookResult.Continue;

			// Get the message from the command
			string message = commandInfo.GetArg(1);
			
			// Check if message is empty or just whitespace
			if (string.IsNullOrWhiteSpace(message))
			{
				return HookResult.Stop; // Block empty messages
			}
			
			// Check if this is a command (starts with ! or /)
			if (message.StartsWith("!") || message.StartsWith("/"))
			{
				return HookResult.Continue; // Let commands through to be processed normally
			}
			
			// Get player's rank and color for regular chat messages - use same method as !rank command
			string playerRank = "Unranked";
			string rankColor = ChatColors.Grey.ToString();
			string cleanName = player!.PlayerName;
			
			// Always get fresh data from database (same as !rank command)
			var steamId = player.SteamID.ToString();
			var stats = _dataManager.GetPlayerStats(steamId);
			
			if (stats != null)
			{
				var rank = RankSystem.GetRank(stats.Points);
				playerRank = rank.Name;
				rankColor = rank.Color;
			}
			
			// Send our custom formatted message with colored rank to all players
			string customMessage = $" {rankColor}[{playerRank}] {ChatColors.Default}{cleanName}: {message}";
			
			// Use NextFrame to ensure proper timing after blocking the original message
			Server.NextFrame(() =>
			{
				// Send custom message to all players (no [IG] prefix for player messages)
				Server.PrintToChatAll(customMessage);
			});
			
			// Block the original CS2 message
			return HookResult.Stop;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnSayCommand: {ex.Message}");
			return HookResult.Continue;
		}
	}



	private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
	{
		try
		{
			var player = @event.Userid;
			
			if (!PlayerUtils.IsValidPlayer(player))
				return HookResult.Continue;

			// Check if the weapon fired was an AWP, if so refill its clip immediately (no reload)
			var weaponName = @event.Weapon?.ToLower() ?? "";
			if (weaponName.Contains("awp"))
			{
				Server.NextFrame(() =>
				{
					if (PlayerUtils.IsValidPlayer(player))
					{
						RefillAWPClip(player!);
					}
				});
			}

			return HookResult.Continue;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnWeaponFire: {ex.Message}");
			return HookResult.Continue;
		}
	}

	// Ensure AWP deals lethal damage regardless of hitgroup/armor
	private HookResult OnTakeDamage(DynamicHook hook)
	{
		try
		{
			// Param 0 is victim entity. Only apply to players
			if (hook.GetParam<CEntityInstance>(0).DesignerName is not "player")
				return HookResult.Continue;

			CTakeDamageInfo info = hook.GetParam<CTakeDamageInfo>(1);
			var weapon = info.Ability.Value;
			if (weapon == null)
				return HookResult.Continue;

			// Identify weapon by its designer name
			var weaponEnt = weapon.As<CBasePlayerWeapon>();
			var vdata = weaponEnt?.GetVData<CCSWeaponBaseVData>();
			var designerName = vdata?.Name?.ToLower() ?? string.Empty;
			if (string.IsNullOrEmpty(designerName))
				return HookResult.Continue;

			// AWP one-shot kill
			if (designerName.Contains("awp"))
			{
				// Set very high damage to guarantee kill (health+armor overshoot)
				info.Damage = 1000.0f;
			}

			return HookResult.Continue;
		}
		catch
		{
			return HookResult.Continue;
		}
	}



	/// <summary>
	/// Send a message to all players with the [IG] prefix in red
	/// </summary>
	private void SendChatMessage(string message)
	{
		PlayerUtils.PrintToChatAllCustom(message);
	}

	/// <summary>
	/// Send a message to a specific player with the [IG] prefix in red
	/// </summary>
	private void SendChatMessage(CCSPlayerController player, string message)
	{
		if (PlayerUtils.IsValidPlayer(player))
		{
			player.PrintToChat($" {ChatColors.Red}[IG] {ChatColors.Default}{message}");
		}
	}

	/// <summary>
	/// Set player's clan tag to show rank in TAB menu/scoreboard
	/// This doesn't change their actual name, just the clan tag that appears in the scoreboard
	/// </summary>
	private void SetPlayerClanTag(CCSPlayerController player, PlayerStats stats)
	{
		try
		{
			if (!PlayerUtils.IsValidPlayer(player) || stats == null)
				return;

			var rank = RankSystem.GetRank(stats.Points);
			
			// Set clan tag to show rank in scoreboard
			player.Clan = $"[{rank.Name}]";
			
			Server.PrintToConsole($"[CombatSurf] Set clan tag for {player.PlayerName}: [{rank.Name}]");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error setting player clan tag: {ex.Message}");
		}
	}

	/// <summary>
	/// Give player infinite ammo - all weapons get infinite reserve ammo, only AWP gets no reload
	/// </summary>
	private void GiveInfiniteAmmo(CCSPlayerController player)
	{
		try
		{
			if (!PlayerUtils.IsValidPlayer(player))
				return;

			var pawn = player.PlayerPawn.Value;
			if (pawn?.WeaponServices?.MyWeapons != null)
			{
				foreach (var weapon in pawn.WeaponServices.MyWeapons)
				{
					if (weapon?.Value?.VData != null)
					{
						// Give infinite reserve ammo to ALL weapons
						weapon.Value.ReserveAmmo[0] = 999;
						
						// Only refill clip for AWP (no reload needed for AWP only)
						var weaponDesignerName = weapon.Value.DesignerName?.ToLower() ?? "";
						if (weaponDesignerName.Contains("awp"))
						{
							weapon.Value.Clip1 = weapon.Value.VData.MaxClip1;
						}
						
						// Handle secondary ammo/clip for weapons that have it
						if (weapon.Value.VData.MaxClip2 > 0)
						{
							weapon.Value.ReserveAmmo[1] = 999;
							if (weaponDesignerName.Contains("awp"))
							{
								weapon.Value.Clip2 = weapon.Value.VData.MaxClip2;
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error giving infinite ammo: {ex.Message}");
		}
	}

	/// <summary>
	/// Refill only AWP clips for no-reload functionality
	/// </summary>
	private void RefillAWPClip(CCSPlayerController player)
	{
		try
		{
			if (!PlayerUtils.IsValidPlayer(player))
				return;

			var pawn = player.PlayerPawn.Value;
			if (pawn?.WeaponServices?.MyWeapons != null)
			{
				foreach (var weapon in pawn.WeaponServices.MyWeapons)
				{
					if (weapon?.Value?.VData != null)
					{
						var weaponDesignerName = weapon.Value.DesignerName?.ToLower() ?? "";
						if (weaponDesignerName.Contains("awp"))
						{
							// Refill AWP clip immediately (no reload needed)
							weapon.Value.Clip1 = weapon.Value.VData.MaxClip1;
							
							if (weapon.Value.VData.MaxClip2 > 0)
							{
								weapon.Value.Clip2 = weapon.Value.VData.MaxClip2;
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error refilling AWP clip: {ex.Message}");
		}
	}

	// Speed HUD System Methods
	private void OnTick()
	{
		try
		{
			int currentTick = Server.TickCount;
			
			foreach (var player in Utilities.GetPlayers().Where(PlayerUtils.IsValidHumanPlayer))
			{
				if (player == null || !player.IsValid || !player.PawnIsAlive)
					continue;
					
				int slot = player.Slot;

				// If we are recording this specific player, capture a frame every tick
				if (_isRecordingPath && slot == _recordingSlot)
				{
					CaptureReplayFrame(player);
				}
				
				// Initialize HUD info if needed
				if (!_playerHudInfo.ContainsKey(slot))
				{
					_playerHudInfo[slot] = new PlayerHudInfo();
				}
				
				var hudInfo = _playerHudInfo[slot];
				
				// Only update HUD at specified tickrate to prevent spam
				if (currentTick % (64 / (int)_hudTickrate) != 0)
					continue;
					
				// Only show HUD if enabled and player is in round
				if (_speedHudEnabled && hudInfo.ShowSpeedHud)
				{
					string hudContent = GetSpeedHudContent(player);
					if (!string.IsNullOrEmpty(hudContent))
					{
						player.PrintToCenterHtml(hudContent);
					}
				}
			}

			// Drive bot playback after iterating players
			if (_loadedReplay.Count > 0)
			{
				PlaybackTick();
			}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in OnTick: {ex.Message}");
		}
	}

	// Recording helpers
	private void CaptureReplayFrame(CCSPlayerController player)
	{
		try
		{
			var pawn = player.PlayerPawn?.Value;
			var body = player.Pawn.Value?.CBodyComponent?.SceneNode;
			if (pawn == null || body == null)
				return;

			var pos = body.AbsOrigin;
			var vel = pawn.AbsVelocity;
			var ang = pawn.EyeAngles;
			var buttons = (long)player.Buttons;
			var flags = player.Pawn.Value!.Flags;

			_currentRecording.Add(new ReplayFrame
			{
				PX = pos!.X,
				PY = pos.Y,
				PZ = pos.Z,
				AX = ang!.X,
				AY = ang.Y,
				AZ = ang.Z,
				VX = vel.X,
				VY = vel.Y,
				VZ = vel.Z,
				Buttons = buttons,
				Flags = flags
			});
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error capturing frame: {ex.Message}");
		}
	}

	private void PersistRecordingForCurrentMap()
	{
		try
		{
			Directory.CreateDirectory(_botsDir);
			var map = Server.MapName ?? "unknown";
			// Save unique per recorder to avoid overwriting older bots
			var safeName = GetRecorderNameSafe();
			foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
			var basePath = Path.Combine(_botsDir, $"{map}_{safeName}.json");
			var path = basePath;
			int idx = 2;
			while (File.Exists(path) && idx < 1000)
			{
				path = Path.Combine(_botsDir, $"{map}_{safeName} {idx}.json");
				idx++;
			}
			var blob = new RecordingData { RecorderName = GetRecorderNameSafe(), Frames = new List<ReplayFrame>(_currentRecording) };
			var json = System.Text.Json.JsonSerializer.Serialize(blob);
			File.WriteAllText(path, json);
			Server.PrintToConsole($"[CombatSurf] Saved path recording: {path}");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error saving recording: {ex.Message}");
		}
	}

    private void TryLoadReplayForCurrentMap()
	{
		try
		{
			Directory.CreateDirectory(_botsDir);
			var map = Server.MapName ?? "unknown";
            _loadedReplays.Clear();
            var files = Directory.GetFiles(_botsDir, $"{map}_*.json");
            if (files.Length == 0)
            {
                var fallback = Path.Combine(_botsDir, $"{map}.json");
                if (File.Exists(fallback)) files = new[] { fallback };
            }
            foreach (var path in files.OrderBy(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var blob = System.Text.Json.JsonSerializer.Deserialize<RecordingData>(json);
                    if (blob != null && blob.Frames != null && blob.Frames.Count > 0)
                    {
                        _loadedReplays.Add(new BotReplay { RecorderName = string.IsNullOrWhiteSpace(blob.RecorderName) ? "Replay Bot" : blob.RecorderName, Frames = blob.Frames, FilePath = path });
                    }
                    else
                    {
                        var frames = System.Text.Json.JsonSerializer.Deserialize<List<ReplayFrame>>(json) ?? new List<ReplayFrame>();
                        if (frames.Count > 0) _loadedReplays.Add(new BotReplay { RecorderName = "Replay Bot", Frames = frames, FilePath = path });
                    }
                }
                catch { }
            }
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error loading replay: {ex.Message}");
            _loadedReplays.Clear();
		}
	}

	private void EnsureReplayBotsSpawned()
	{
		try
		{
			// If bot already exists, keep it
			if (_replayBotController != null && _replayBotController.IsValid)
				return;

			if (_loadedReplays.Count == 0)
			{
				Server.PrintToConsole("[CombatSurf] No replay loaded, not spawning bot.");
				return;
			}

			// Use the first replay for now (single bot approach that was working)
			var firstReplay = _loadedReplays[0];
			_loadedReplay = firstReplay.Frames;
			_replayRecorderName = firstReplay.RecorderName;

			// SharpTimer's bot spawn sequence
			Server.NextFrame(() =>
			{
				AddTimer(3.0f, () =>
				{
					Server.ExecuteCommand("bot_quota_mode normal");
					Server.ExecuteCommand("bot_quota 0");
					Server.ExecuteCommand("bot_chatter off");
					Server.ExecuteCommand("bot_controllable 0");
					Server.ExecuteCommand("bot_kick");
					_replayBotController = null;

					AddTimer(3.0f, () =>
					{
						Server.ExecuteCommand("bot_quota 1");
						Server.ExecuteCommand("bot_add_ct");
						Server.ExecuteCommand("bot_quota 1");

						AddTimer(0.0f, () =>
						{
							var bot = Utilities.GetPlayers().Where(b => b.IsBot && !b.IsHLTV).FirstOrDefault();
							if (bot != null)
							{
								_replayBotController = bot;
								var pawn = bot.PlayerPawn.Value;
								if (pawn == null) return;

								bot.RemoveWeapons();
								pawn.Bot!.IsStopping = true;
								pawn.Bot.IsSleeping = true;
								pawn.Bot.AllowActive = true;
								pawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
								pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING;
								Utilities.SetStateChanged(bot, "CCollisionProperty", "m_CollisionGroup");
								Utilities.SetStateChanged(bot, "CCollisionProperty", "m_collisionAttribute");

								SetPlayerVisibleName(bot, GetUniqueBotName(_replayRecorderName));
								Server.PrintToConsole($"[CombatSurf] Replay bot spawned: {bot.PlayerName}");

								// Kick unused bots if there are any
								var botsToKick = Utilities.GetPlayers().Where(b => b.IsBot && !b.IsHLTV && b != _replayBotController);
								foreach (var kicked in botsToKick)
								{
									Server.ExecuteCommand($"kickid {kicked.UserId}");
									Server.PrintToConsole($"[CombatSurf] Kicking unused bot: {kicked.PlayerName}");
								}
							}
							else
							{
								Server.PrintToConsole($"[CombatSurf] Failed to find replay bot after spawn commands.");
							}
						});
					});
				});
			});
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error spawning replay bot: {ex.Message}");
		}
	}

	private void PlaybackTick()
	{
		try
		{
			if (_loadedReplay.Count == 0) return;
			if (_replayBotController == null || !_replayBotController.IsValid) return;

			if (_replayIndex < 0 || _replayIndex >= _loadedReplay.Count)
			{
				_replayIndex = 0;
			}

			var frame = _loadedReplay[_replayIndex];
			var pawn = _replayBotController.PlayerPawn.Value;
			if (pawn == null) return;
			var pos = new Vector(frame.PX, frame.PY, frame.PZ);
			// Use yaw-only to avoid model leaning forward due to pitch roll
			var ang = new QAngle(0, frame.AY, 0);
			var vel = new Vector(frame.VX, frame.VY, frame.VZ);
			_replayBotController.PlayerPawn.Value!.Teleport(pos, ang, vel);
			_replayIndex++;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error in PlaybackTick: {ex.Message}");
		}
	}

	private string GetRecorderNameSafe()
	{
		try
		{
			var recorder = Utilities.GetPlayers().FirstOrDefault(p => p.Slot == _recordingSlot);
			return PlayerUtils.IsValidHumanPlayer(recorder) ? recorder!.PlayerName : _replayRecorderName;
		}
		catch { return _replayRecorderName; }
	}

	private string GetUniqueBotName(string baseName)
	{
		var existing = Utilities.GetPlayers().Select(p => p.PlayerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (!existing.Contains(baseName)) return baseName;
		for (int i = 2; i < 1000; i++)
		{
			var candidate = $"{baseName} {i}";
			if (!existing.Contains(candidate)) return candidate;
		}
		return baseName + " *";
	}

	private void SetPlayerVisibleName(CCSPlayerController player, string name)
	{
		try
		{
			player.PlayerName = name;
			Utilities.SetStateChanged(player, "CBasePlayerController", "m_iszPlayerName");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error setting player name: {ex.Message}");
		}
	}
	
	private string GetSpeedHudContent(CCSPlayerController player)
	{
		try
		{
			var playerPawn = player.PlayerPawn?.Value;
			if (playerPawn?.AbsVelocity == null)
				return "";
				
			// Calculate 2D speed (ignore vertical component for surf)
			var velocity = playerPawn.AbsVelocity;
			float speed2D = (float)Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
			float speed3D = (float)Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);
			
			// Format speed with proper padding
			string formattedSpeed2D = Math.Round(speed2D).ToString("0000");
			string formattedSpeed3D = Math.Round(speed3D).ToString("0000");
			
			// Dynamic color based on speed (similar to SharpTimer)
			string speedColor = GetSpeedColor((int)speed2D);
			
			// Get rank information if available
			string rankInfo = "";
			if (_playerStatsCache.TryGetValue(player.Slot, out var stats))
			{
				var rank = RankSystem.GetRank(stats.Points);
				rankInfo = $"<font class='fontSize-s' color='gray'>{rank.Name} | {stats.Points:N0} pts</font><br>";
			}
			
			// Build HUD content similar to SharpTimer format
			string hudContent = 
				$"{rankInfo}" +
				$"<font class='fontSize-s stratum-bold-italic' color='gray'>Speed:</font> " +
				$"<font class='fontSize-l horizontal-center' color='{speedColor}'>{formattedSpeed2D}</font> " +
				$"<font class='fontSize-s stratum-bold-italic' color='gray'>({formattedSpeed3D})</font>" +
				$"<br>";
				
			return hudContent;
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error getting HUD content: {ex.Message}");
			return "";
		}
	}
	
	private string GetSpeedColor(int speed)
	{
		// Dynamic color system similar to SharpTimer
		return speed switch
		{
			< 350 => "LimeGreen",
			< 700 => "Lime", 
			< 1050 => "GreenYellow",
			< 1400 => "Yellow",
			< 1750 => "Gold",
			< 2100 => "Orange",
			< 2450 => "DarkOrange",
			< 2800 => "Tomato",
			< 3150 => "OrangeRed",
			< 3500 => "Red",
			_ => "Crimson"
		};
	}
	
	private void EnableSpeedHudForPlayer(CCSPlayerController player)
	{
		if (player?.IsValid == true)
		{
			if (!_playerHudInfo.ContainsKey(player.Slot))
			{
				_playerHudInfo[player.Slot] = new PlayerHudInfo();
			}
			_playerHudInfo[player.Slot].ShowSpeedHud = true;
		}
	}
	
	private void DisableSpeedHudForPlayer(CCSPlayerController player)
	{
		if (player?.IsValid == true && _playerHudInfo.ContainsKey(player.Slot))
		{
			_playerHudInfo[player.Slot].ShowSpeedHud = false;
		}
	}

	// Logging Methods
	private void LogServerInfo()
	{
		try
		{
			Server.PrintToConsole("[CombatSurf] ============ SERVER INFORMATION ============");
			Server.PrintToConsole($"[CombatSurf] Map: {Server.MapName}");
			Server.PrintToConsole($"[CombatSurf] Max Players: {Server.MaxPlayers}");
			Server.PrintToConsole($"[CombatSurf] Current Tick: {Server.TickCount}");
			Server.PrintToConsole($"[CombatSurf] Current Players: {Utilities.GetPlayers().Count}");
			Server.PrintToConsole($"[CombatSurf] Valid Players: {Utilities.GetPlayers().Count(p => PlayerUtils.IsValidPlayer(p))}");
			Server.PrintToConsole($"[CombatSurf] Bot Count: {Utilities.GetPlayers().Count(p => p.IsBot)}");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error logging server info: {ex.Message}");
		}
	}

	private void LogCurrentPlayers()
	{
		try
		{
			var players = Utilities.GetPlayers();
			Server.PrintToConsole("[CombatSurf] ============ CURRENT PLAYERS ============");
			Server.PrintToConsole($"[CombatSurf] Total Players: {players.Count}");
			Server.PrintToConsole($"[CombatSurf] Valid Players: {players.Count(p => PlayerUtils.IsValidPlayer(p))}");
			Server.PrintToConsole($"[CombatSurf] Bot Count: {players.Count(p => p.IsBot)}");
			Server.PrintToConsole($"[CombatSurf] Human Players: {players.Count(p => PlayerUtils.IsValidHumanPlayer(p))}");
			
			if (players.Count > 0)
			{
				Server.PrintToConsole("[CombatSurf] Player Details:");
				foreach (var player in players)
				{
					if (PlayerUtils.IsValidPlayer(player))
					{
						var teamName = GetTeamName(player.TeamNum);
						var isAlive = player.PawnIsAlive ? "ALIVE" : "DEAD";
						var isBot = player.IsBot ? "BOT" : "HUMAN";
						var isValid = PlayerUtils.IsValidHumanPlayer(player) ? "VALID" : "BOT";
						Server.PrintToConsole($"[CombatSurf] - Slot {player.Slot}: {player.PlayerName} | Team: {teamName} ({player.TeamNum}) | {isAlive} | {isBot} | {isValid}");
					}
					else
					{
						Server.PrintToConsole($"[CombatSurf] - Slot {player.Slot}: {player.PlayerName} | INVALID");
					}
				}
			}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error logging current players: {ex.Message}");
		}
	}

	private void LogTeamInfo()
	{
		try
		{
			var players = Utilities.GetPlayers().Where(PlayerUtils.IsValidHumanPlayer).ToList();
			var ctPlayers = players.Where(p => p.TeamNum == 3).ToList();
			var tPlayers = players.Where(p => p.TeamNum == 2).ToList();
			var spectatorPlayers = players.Where(p => p.TeamNum == 1).ToList();
			
			Server.PrintToConsole("[CombatSurf] ============ TEAM INFORMATION ============");
			Server.PrintToConsole($"[CombatSurf] Counter-Terrorists (Team 3): {ctPlayers.Count} players");
			foreach (var player in ctPlayers)
			{
				Server.PrintToConsole($"[CombatSurf] - CT: {player.PlayerName} (Slot {player.Slot})");
			}
			
			Server.PrintToConsole($"[CombatSurf] Terrorists (Team 2): {tPlayers.Count} players");
			foreach (var player in tPlayers)
			{
				Server.PrintToConsole($"[CombatSurf] - T: {player.PlayerName} (Slot {player.Slot})");
			}
			
			Server.PrintToConsole($"[CombatSurf] Spectators (Team 1): {spectatorPlayers.Count} players");
			foreach (var player in spectatorPlayers)
			{
				Server.PrintToConsole($"[CombatSurf] - SPEC: {player.PlayerName} (Slot {player.Slot})");
			}
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error logging team info: {ex.Message}");
		}
	}

	private void LogPluginConfig()
	{
		try
		{
			Server.PrintToConsole("[CombatSurf] ============ PLUGIN CONFIGURATION ============");
			Server.PrintToConsole($"[CombatSurf] Godmode Enabled: {Config?.EnableGodmode ?? false}");
			Server.PrintToConsole($"[CombatSurf] Godmode Time: {Config?.GodmodeTime ?? 0}s");
			Server.PrintToConsole($"[CombatSurf] Show Godmode Messages: {Config?.ShowGodmodeMessages ?? false}");
			Server.PrintToConsole($"[CombatSurf] Speed HUD Enabled: {_speedHudEnabled}");
			Server.PrintToConsole($"[CombatSurf] HUD Tick Rate: {_hudTickrate}");
			Server.PrintToConsole($"[CombatSurf] Data Manager: {_dataManager?.GetType().Name ?? "NULL"}");
			Server.PrintToConsole($"[CombatSurf] Player Stats Cache: {_playerStatsCache.Count} entries");
			Server.PrintToConsole($"[CombatSurf] Player HUD Info: {_playerHudInfo.Count} entries");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error logging plugin config: {ex.Message}");
		}
	}

	private void LogFinalStatus()
	{
		try
		{
			Server.PrintToConsole("[CombatSurf] ============ FINAL STATUS ============");
			Server.PrintToConsole($"[CombatSurf] Event Handlers: 7 registered");
			Server.PrintToConsole($"[CombatSurf] Command Listeners: 2 registered (say, say_team)");
			Server.PrintToConsole($"[CombatSurf] Tick Listener: 1 registered (Speed HUD)");
			Server.PrintToConsole($"[CombatSurf] Round Godmode Active: {_roundGodmodeActive}");
			Server.PrintToConsole($"[CombatSurf] Plugin Ready: TRUE");
		}
		catch (Exception ex)
		{
			Server.PrintToConsole($"[CombatSurf] Error logging final status: {ex.Message}");
		}
	}

	private string GetTeamName(int teamNum)
	{
		return teamNum switch
		{
			1 => "SPECTATOR",
			2 => "TERRORIST",
			3 => "COUNTER-TERRORIST",
			_ => "UNKNOWN"
		};
	}

}

// Player HUD tracking class
public class PlayerHudInfo
{
	public bool ShowSpeedHud { get; set; } = false;
}
