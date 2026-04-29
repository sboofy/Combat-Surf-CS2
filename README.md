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

# CombatSurf + TagAPI Deployment Instructions

## Problem: Chat ranks aren't working
This means TagAPI plugin isn't loaded. You need BOTH plugins running.

## File Structure on Server:

```
/game/csgo/addons/counterstrikesharp/plugins/
├── CombatSurf/
│   └── combatsurf.dll
└── TagAPI/
    └── TagAPI.dll
```

## Step-by-Step Deployment:

### 1. Deploy CombatSurf Plugin
```
Copy: bin/Release/net8.0/combatsurf.dll
To: /game/csgo/addons/counterstrikesharp/plugins/CombatSurf/combatsurf.dll
```

### 2. Deploy TagAPI Plugin  
```
Copy: TagAPI.dll (from this folder)
To: /game/csgo/addons/counterstrikesharp/plugins/TagAPI/TagAPI.dll
```

### 3. Restart Server
Both plugins must load on server startup.

### 4. Check Logs
Look for these messages:
```
[TagAPI] Plugin loaded successfully
[CombatSurf] Set TagAPI rank for PlayerName: [Silver]
```

### 5. Test Chat
- Type a message in chat
- Should see: `[Silver] PlayerName: message`
- NOT: `[ALL] PlayerName: message`

## If TagAPI Doesn't Load:

### Option 1: Use SharpTimer's TagAPI
If you have SharpTimer working, copy their TagAPI:
```
From: SharpTimer plugin folder
To: plugins/TagAPI/TagAPI.dll
```

### Option 2: Alternative TagAPI
Download TagAPI from CSS community:
- Search for "CounterStrikeSharp TagAPI plugin"
- Install as separate plugin

## Troubleshooting:

### "TagAPI not available" message:
- TagAPI plugin isn't loaded
- Check plugin folder structure
- Restart server

### Still shows [ALL]:
- TagAPI plugin loaded but not working
- Check TagAPI version compatibility
- Try different TagAPI plugin

### Duplicates:
- Multiple chat plugins conflicting
- Disable other chat plugins

## Expected Result:
✅ Chat: `[Silver] Crizer: hello world` (with rank color)
✅ Scoreboard: `[Silver]` as clan tag
✅ No duplicates or [ALL] messages

