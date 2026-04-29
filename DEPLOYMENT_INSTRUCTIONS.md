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
