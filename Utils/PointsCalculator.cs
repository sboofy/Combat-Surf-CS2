using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System.Numerics;

namespace CombatSurf.Utils;

public static class PointsCalculator
{
    // Base points for a kill
    private const int BASE_KILL_POINTS = 10;
    
    // Speed calculation constants
    private const float SPEED_BASE_THRESHOLD = 250f; // Minimum speed to start getting bonuses
    private const float SPEED_MULTIPLIER_SCALE = 1000f; // Every 1000 u/s adds significant multiplier
    
    // Victim speed constants
    private const float VICTIM_SPEED_THRESHOLD = 300f; // Minimum victim speed for bonus
    private const float VICTIM_SPEED_SCALE = 800f; // Scaling factor for victim speed bonus
    private const float MAX_VICTIM_SPEED_MULTIPLIER = 2.0f; // Cap the victim speed multiplier
    
    // Other multipliers
    private const float AIRBORNE_MULTIPLIER = 1.5f;
    private const float NOSCOPE_MULTIPLIER = 2.0f;
    private const float HEADSHOT_MULTIPLIER = 1.4f;
    private const float LONG_DISTANCE_MULTIPLIER = 1.2f; // 1000+ units
    
    public static KillAnalysis AnalyzeKill(CCSPlayerController killer, CCSPlayerController victim, EventPlayerDeath deathEvent)
    {
        var analysis = new KillAnalysis();
        
        if (!PlayerUtils.IsValidPlayer(killer) || !PlayerUtils.IsValidPlayer(victim))
            return analysis;

        var killerPawn = killer.PlayerPawn.Value!;
        var victimPawn = victim.PlayerPawn.Value!;
        
        // Get killer's movement data
        analysis.KillerSpeed = GetPlayerSpeed(killer);
        analysis.IsAirborne = !IsPlayerOnGround(killer);
        analysis.IsHeadshot = deathEvent.Headshot;
        analysis.WeaponName = deathEvent.Weapon;
        analysis.Distance = GetDistance(killerPawn, victimPawn);
        
        // Get victim's movement data
        analysis.VictimSpeed = GetPlayerSpeed(victim);
        
        // Check if it's a noscope using the event data
        analysis.IsNoscope = IsNoscope(killer, deathEvent);
        
        // Calculate points
        analysis.PointsAwarded = CalculatePoints(analysis);
        
        return analysis;
    }
    
    private static int CalculatePoints(KillAnalysis analysis)
    {
        float points = BASE_KILL_POINTS;
        var multipliers = new List<string>();
        
        // Killer speed multiplier - dynamic based on actual speed
        float killerSpeedMultiplier = GetSpeedMultiplier(analysis.KillerSpeed);
        if (killerSpeedMultiplier > 1.0f)
        {
            points *= killerSpeedMultiplier;
            string speedDescription = GetSpeedDescription(analysis.KillerSpeed);
            multipliers.Add($"Killer {speedDescription} ({killerSpeedMultiplier:F2}x)");
        }
        
        // Victim speed multiplier - bonus for hitting fast-moving targets
        float victimSpeedMultiplier = GetVictimSpeedMultiplier(analysis.VictimSpeed);
        if (victimSpeedMultiplier > 1.0f)
        {
            points *= victimSpeedMultiplier;
            string victimSpeedDescription = GetVictimSpeedDescription(analysis.VictimSpeed);
            multipliers.Add($"Target {victimSpeedDescription} ({victimSpeedMultiplier:F2}x)");
        }
        
        // Airborne bonus
        if (analysis.IsAirborne)
        {
            points *= AIRBORNE_MULTIPLIER;
            multipliers.Add($"Airborne ({AIRBORNE_MULTIPLIER:F1}x)");
        }
        
        // Noscope bonus
        if (analysis.IsNoscope)
        {
            points *= NOSCOPE_MULTIPLIER;
            multipliers.Add($"Noscope ({NOSCOPE_MULTIPLIER:F1}x)");
        }
        
        // Headshot bonus
        if (analysis.IsHeadshot)
        {
            points *= HEADSHOT_MULTIPLIER;
            multipliers.Add($"Headshot ({HEADSHOT_MULTIPLIER:F1}x)");
        }
        
        // Distance bonus
        if (analysis.Distance > 1000f)
        {
            points *= LONG_DISTANCE_MULTIPLIER;
            multipliers.Add($"Long Range ({LONG_DISTANCE_MULTIPLIER:F1}x)");
        }
        
        analysis.Multipliers = multipliers;
        return (int)Math.Round(points);
    }
    
    private static float GetSpeedMultiplier(float speed)
    {
        if (speed <= SPEED_BASE_THRESHOLD)
            return 1.0f; // No bonus for slow speeds
            
        // Dynamic multiplier: 1.0 + (speed - threshold) / scale
        // Examples:
        // 500 u/s: 1.0 + (500-250)/1000 = 1.25x
        // 1000 u/s: 1.0 + (1000-250)/1000 = 1.75x  
        // 2000 u/s: 1.0 + (2000-250)/1000 = 2.75x
        // 3000 u/s: 1.0 + (3000-250)/1000 = 3.75x
        
        float speedBonus = (speed - SPEED_BASE_THRESHOLD) / SPEED_MULTIPLIER_SCALE;
        return 1.0f + speedBonus;
    }
    
    private static string GetSpeedDescription(float speed)
    {
        return speed switch
        {
            >= 3000f => "Godlike Speed",
            >= 2500f => "Insane Speed", 
            >= 2000f => "Extreme Speed",
            >= 1500f => "Very High Speed",
            >= 1000f => "High Speed",
            >= 750f => "Fast Speed",
            >= 500f => "Medium Speed",
            >= 350f => "Decent Speed",
            _ => "Low Speed"
        };
    }
    
    private static float GetVictimSpeedMultiplier(float victimSpeed)
    {
        if (victimSpeed <= VICTIM_SPEED_THRESHOLD)
            return 1.0f; // No bonus for slow targets
            
        // Dynamic multiplier for victim speed: 1.0 + (speed - threshold) / scale
        // Examples:
        // 500 u/s: 1.0 + (500-300)/800 = 1.25x
        // 800 u/s: 1.0 + (800-300)/800 = 1.625x
        // 1200 u/s: 1.0 + (1200-300)/800 = 2.125x -> capped at 2.0x
        // 2000 u/s: 1.0 + (2000-300)/800 = 3.125x -> capped at 2.0x
        
        float speedBonus = (victimSpeed - VICTIM_SPEED_THRESHOLD) / VICTIM_SPEED_SCALE;
        float multiplier = 1.0f + speedBonus;
        
        // Cap the multiplier to prevent excessive points
        return Math.Min(multiplier, MAX_VICTIM_SPEED_MULTIPLIER);
    }
    
    private static string GetVictimSpeedDescription(float speed)
    {
        return speed switch
        {
            >= 2000f => "Lightning Target",
            >= 1500f => "Very Fast Target",
            >= 1200f => "Fast Target", 
            >= 800f => "Moving Target",
            >= 500f => "Quick Target",
            _ => "Slow Target"
        };
    }
    
    private static float GetPlayerSpeed(CCSPlayerController player)
    {
        if (!PlayerUtils.IsValidPlayer(player) || player.PlayerPawn.Value?.AbsVelocity == null)
            return 0f;
            
        var velocity = player.PlayerPawn.Value.AbsVelocity;
        // Calculate 2D speed (ignore vertical component for surf)
        return (float)Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
    }
    
    private static bool IsPlayerOnGround(CCSPlayerController player)
    {
        if (!PlayerUtils.IsValidPlayer(player))
            return true;
            
        var pawn = player.PlayerPawn.Value!;
        return (pawn.Flags & (1 << 0)) != 0; // FL_ONGROUND flag
    }
    
    private static bool IsNoscope(CCSPlayerController player, EventPlayerDeath deathEvent)
    {
        if (!PlayerUtils.IsValidPlayer(player))
        {
            Console.WriteLine("[CombatSurf] IsNoscope: Invalid player");
            return false;
        }
            
        // Check if weapon is a scoped weapon (AWP, Scout, etc.)
        // Handle both formats: "awp" and "weapon_awp"
        var scopedWeapons = new[] { "weapon_awp", "weapon_ssg08", "weapon_scar20", "weapon_g3sg1", "awp", "ssg08", "scar20", "g3sg1" };
        var weaponName = deathEvent.Weapon?.ToLower() ?? "";
        
        bool isScoped = scopedWeapons.Contains(weaponName);
        Console.WriteLine($"[CombatSurf] IsNoscope check: weapon={weaponName}, isScoped={isScoped}");
        
        if (!isScoped)
            return false;
            
        // Check if the event has a noscope property
        // First try to access common noscope property names
        try
        {
            // Try different possible property names for noscope
            var eventType = deathEvent.GetType();
            
            // Debug: Log all available properties to help identify the correct noscope property (only for scoped weapons)
            var properties = eventType.GetProperties();
            Console.WriteLine($"[CombatSurf] EventPlayerDeath properties for {weaponName}: {string.Join(", ", properties.Select(p => p.Name))}");
            
            // Check for Noscope property (capital N)
            var noscopeProperty = eventType.GetProperty("Noscope");
            if (noscopeProperty != null)
            {
                var value = noscopeProperty.GetValue(deathEvent);
                Console.WriteLine($"[CombatSurf] Found Noscope property, value: {value}");
                if (value is bool noscopeValue)
                    return noscopeValue;
            }
            
            // Check for noscope property (lowercase n)
            var noscopePropertyLower = eventType.GetProperty("noscope");
            if (noscopePropertyLower != null)
            {
                var value = noscopePropertyLower.GetValue(deathEvent);
                Console.WriteLine($"[CombatSurf] Found noscope property, value: {value}");
                if (value is bool noscopeValue)
                    return noscopeValue;
            }
            
            // Check for NoScope property (camelCase)
            var noScopeProperty = eventType.GetProperty("NoScope");
            if (noScopeProperty != null)
            {
                var value = noScopeProperty.GetValue(deathEvent);
                Console.WriteLine($"[CombatSurf] Found NoScope property, value: {value}");
                if (value is bool noscopeValue)
                    return noscopeValue;
            }
            
            Console.WriteLine("[CombatSurf] No noscope property found, checking for other possible names...");
            
            // Look for any property that might contain scope-related information
            foreach (var prop in properties)
            {
                var propName = prop.Name.ToLower();
                if (propName.Contains("scope") || propName.Contains("zoom"))
                {
                    var value = prop.GetValue(deathEvent);
                    Console.WriteLine($"[CombatSurf] Found scope-related property '{prop.Name}': {value} (type: {value?.GetType().Name})");
                }
            }
        }
        catch (Exception ex)
        {
            // Log the error and fall back to false for scoped weapons
            Console.WriteLine($"[CombatSurf] Error checking noscope property: {ex.Message}");
        }
        
        // If we can't determine noscope status from event data, 
        // assume it was scoped (return false for noscope)
        Console.WriteLine("[CombatSurf] Defaulting to scoped kill (not noscope)");
        return false;
    }
    
    private static float GetDistance(CBasePlayerPawn killer, CBasePlayerPawn victim)
    {
        if (killer?.AbsOrigin == null || victim?.AbsOrigin == null)
            return 0f;
            
        var killerPos = killer.AbsOrigin;
        var victimPos = victim.AbsOrigin;
        
        return Vector3.Distance(
            new Vector3(killerPos.X, killerPos.Y, killerPos.Z),
            new Vector3(victimPos.X, victimPos.Y, victimPos.Z)
        );
    }
    
    public static string GetKillDescription(KillAnalysis analysis)
    {
        var parts = new List<string>();
        
        if (analysis.IsAirborne) parts.Add("airborne");
        if (analysis.IsNoscope) parts.Add("noscope");
        if (analysis.IsHeadshot) parts.Add("headshot");
        if (analysis.KillerSpeed >= 350f) parts.Add($"killer {analysis.KillerSpeed:F0} u/s");
        if (analysis.VictimSpeed >= 350f) parts.Add($"target {analysis.VictimSpeed:F0} u/s");
        if (analysis.Distance > 1000f) parts.Add($"{analysis.Distance:F0} units");
        
        return parts.Count > 0 ? $"({string.Join(", ", parts)})" : "";
    }
}

public class KillAnalysis
{
    public float KillerSpeed { get; set; }
    public float VictimSpeed { get; set; }
    public bool IsAirborne { get; set; }
    public bool IsNoscope { get; set; }
    public bool IsHeadshot { get; set; }
    public string WeaponName { get; set; } = "";
    public float Distance { get; set; }
    public int PointsAwarded { get; set; }
    public List<string> Multipliers { get; set; } = new();
}
