# TagAPI Setup Instructions

## What is TagAPI?
TagAPI is a plugin that allows other plugins to set custom chat and scoreboard tags for players. This is what enables rank prefixes in chat messages like `[Silver] PlayerName: message`.

## Installation Steps:

### 1. Copy TagAPI Plugin
Copy the TagAPI plugin from SharpTimer:
```
From: poor-sharptimer/src/API/TagsApi.dll
To: /game/csgo/addons/counterstrikesharp/plugins/TagAPI/TagsApi.dll
```

### 2. Server Folder Structure
Your server should have this structure:
```
/game/csgo/addons/counterstrikesharp/plugins/
├── CombatSurf/
│   └── combatsurf.dll
└── TagAPI/
    └── TagsApi.dll
```

### 3. Restart Server
Restart your CounterStrikeSharp server to load both plugins.

### 4. Verify Loading
Check server logs for:
```
[TagAPI] Plugin loaded successfully
[CombatSurf] Set TagAPI rank for PlayerName: [Silver]
```

### 5. Test Chat
Players should now see rank prefixes in chat:
- Before: `[ALL] Crizer: hello world`
- After: `[Silver] Crizer: hello world`

## Troubleshooting:

### If TagAPI doesn't load:
- Make sure TagsApi.dll is in its own folder: `plugins/TagAPI/TagsApi.dll`
- Check CounterStrikeSharp version compatibility

### If ranks don't show in chat:
- Check logs for "TagAPI not available" message
- Ensure TagAPI loaded before CombatSurf
- Restart server if hot-reloading

### If you see duplicates:
- This shouldn't happen with TagAPI - it properly blocks original messages
- Check that no other chat plugins are conflicting

## How It Works:
1. **CombatSurf** sets player tags using TagAPI interface
2. **TagAPI** intercepts all chat messages automatically
3. **TagAPI** adds the set tags before player names
4. **TagAPI** blocks original messages to prevent duplicates

This is exactly how SharpTimer implements chat rank prefixes!
