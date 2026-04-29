# CombatSurf Plugin Deployment Guide

## What You Need to Deploy
Your plugin only needs **one file** to work:
```
combatsurf.dll
```

## Deployment Steps

### 1. Build the Plugin
```bash
dotnet build -c Release
```

### 2. Copy the DLL
Copy `bin\Release\net8.0\combatsurf.dll` to:
```
/game/csgo/addons/counterstrikesharp/plugins/CombatSurf/combatsurf.dll
```

### 3. Start the Server
The plugin will:
- ✅ **Auto-create** the config file (`combatsurf.json`) with defaults
- ✅ **Auto-create** the SQLite database (`combatsurf.db`)
- ✅ **Auto-create** all necessary database tables
- ✅ **Work immediately** with default settings

## Default Configuration
The plugin automatically creates this config if none exists:
```json
{
  "DatabaseType": "sqlite",
  "DatabaseConnectionString": "Data Source=combatsurf.db",
  "GodmodeTime": 9.0,
  "EnableGodmode": true,
  "ShowGodmodeMessages": true,
  "DatabaseTablePrefix": "cs_",
  "SpeedBonusThreshold": 250.0,
  "SpeedMultiplierScale": 1000.0,
  "ConfigVersion": 2
}
```

## Crash-Proof Features
- ✅ **No config file needed** - uses defaults
- ✅ **Database auto-creation** - creates SQLite DB automatically  
- ✅ **Null-safe event handlers** - won't crash on missing data
- ✅ **Comprehensive error logging** - shows exactly what went wrong
- ✅ **Graceful fallbacks** - continues working even if parts fail

## Verification
After deployment, check:

1. **Plugin loaded**: `css_plugins` should show "combatsurf"
2. **Config created**: Look for `combatsurf.json` in plugins folder
3. **Database created**: Look for `combatsurf.db` file
4. **Logs clean**: Check CounterStrikeSharp logs for SUCCESS messages

## Quick Test Commands
- `css_stats` - View your stats
- `css_rank` - View your rank
- `css_top` - View leaderboard
- `css_hud` - Toggle HUD display

## Files Created Automatically
```
/plugins/CombatSurf/
├── combatsurf.dll          (you place this)
├── combatsurf.json         (auto-created)
└── combatsurf.db           (auto-created)
```

**That's it!** The plugin is completely self-contained and auto-configuring.
