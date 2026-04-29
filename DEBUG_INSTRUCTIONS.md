# CombatSurf Debug Instructions

## Where to Find Logs
All plugin logs will appear in:
```
/game/csgo/addons/counterstrikesharp/logs/
```

Look for the most recent log file, usually named like:
```
counterstrikesharp-YYYY-MM-DD.log
```

## What to Look For

### 1. Plugin Loading
When the server starts, you should see:
```
[CombatSurf] ============ PLUGIN LOADING START ============
[CombatSurf] Version: 1.0, Author: Crizer
[CombatSurf] Hot reload: False
[CombatSurf] Registering event handlers...
[CombatSurf] - EventRoundFreezeEnd registered
[CombatSurf] - EventPlayerSpawn registered
[CombatSurf] - EventPlayerDeath registered
[CombatSurf] - EventRoundEnd registered
[CombatSurf] - EventPlayerConnectFull registered
[CombatSurf] - EventPlayerDisconnect registered
[CombatSurf] Starting HUD update timer...
[CombatSurf] HUD timer started successfully
[CombatSurf] ============ PLUGIN LOADING SUCCESS ============
```

### 2. Config Parsing
After loading, you should see:
```
[CombatSurf] ============ CONFIG PARSING START ============
[CombatSurf] Database Type: sqlite
[CombatSurf] Connection String: Data Source=combatsurf.db
[CombatSurf] Godmode Enabled: True
[CombatSurf] Godmode Time: 9s
[CombatSurf] Config assigned successfully
[CombatSurf] Creating DatabaseManager...
[CombatSurf] DatabaseManager constructor - Type: sqlite, IsMySQL: False
[CombatSurf] DatabaseManager created successfully
[CombatSurf] Creating PlayerStatsService...
[CombatSurf] PlayerStatsService created successfully
[CombatSurf] Initializing database...
[CombatSurf] ============ CONFIG PARSING SUCCESS ============
```

### 3. Database Initialization
Shortly after config parsing:
```
[CombatSurf] Starting database table creation...
[CombatSurf] Creating tables with prefix: cs_
[CombatSurf] Executing table creation query...
[CombatSurf] Player stats table created/verified successfully
[CombatSurf] Database tables created successfully
[CombatSurf] Database initialized successfully!
```

## Common Issues & Solutions

### If Plugin Doesn't Load:
- Check if you see the "PLUGIN LOADING START" message at all
- If not, there's likely a dependency issue or DLL problem

### If Config Parsing Fails:
- Look for "CONFIG PARSING FAILED" message
- Check if combatsurf.json exists and has valid JSON

### If Database Fails:
- Look for "Database initialization failed" message
- Check file permissions for SQLite database creation
- Verify MySQL connection if using MySQL

### If Events Don't Work:
- Look for event handler registration messages
- If missing, there may be a CounterStrikeSharp API version mismatch

## Force Debug Mode
To get even more verbose output, you can temporarily add this to the plugin Load method:
```csharp
Server.PrintToConsole("[CombatSurf] Debug: This is a test message");
```

## Quick Commands to Test
Once loaded, test these commands in server console:
- `css_plugins` - Should show CombatSurf in the list
- `css_reloadplugins` - Should reload and show all the loading messages again
