using CounterStrikeSharp.API.Modules.Utils;

namespace CombatSurf.Utils;

public static class RankSystem
{
    private static readonly List<Rank> Ranks = new()
    {
        new Rank { Name = "Unranked", MinPoints = 0, Color = $"{ChatColors.Grey}" },
        new Rank { Name = "Bronze", MinPoints = 1, Color = $"{ChatColors.Orange}" },
        new Rank { Name = "Silver", MinPoints = 100, Color = $"{ChatColors.LightBlue}" },
        new Rank { Name = "Gold", MinPoints = 300, Color = $"{ChatColors.Yellow}" },
        new Rank { Name = "Platinum", MinPoints = 600, Color = $"{ChatColors.Blue}" },
        new Rank { Name = "Diamond", MinPoints = 1000, Color = $"{ChatColors.Lime}" },
        new Rank { Name = "Master", MinPoints = 1500, Color = $"{ChatColors.Green}" },
        new Rank { Name = "Elite", MinPoints = 2500, Color = $"{ChatColors.Purple}" },
        new Rank { Name = "Champion", MinPoints = 4000, Color = $"{ChatColors.Red}" },
        new Rank { Name = "Grandmaster", MinPoints = 6000, Color = $"{ChatColors.Magenta}" },
        new Rank { Name = "Legend", MinPoints = 8500, Color = $"{ChatColors.Gold}" },
        new Rank { Name = "Godlike", MinPoints = 12000, Color = $"{ChatColors.Red}" } // Hot Pink
    };
    
    public static Rank GetRank(int points)
    {
        // Find the highest rank the player qualifies for
        for (int i = Ranks.Count - 1; i >= 0; i--)
        {
            if (points >= Ranks[i].MinPoints)
                return Ranks[i];
        }
        
        return Ranks[0]; // Return lowest rank if somehow no match
    }
    
    public static Rank? GetNextRank(int points)
    {
        var currentRank = GetRank(points);
        var currentIndex = Ranks.FindIndex(r => r.Name == currentRank.Name);
        
        if (currentIndex >= 0 && currentIndex < Ranks.Count - 1)
            return Ranks[currentIndex + 1];
            
        return null; // Already at max rank
    }
    
    public static int GetPointsToNextRank(int points)
    {
        var nextRank = GetNextRank(points);
        return nextRank?.MinPoints - points ?? 0;
    }
    
    public static string GetRankProgressBar(int points, int barLength = 10)
    {
        var currentRank = GetRank(points);
        var nextRank = GetNextRank(points);
        
        if (nextRank == null)
            return $"{ChatColors.Gold}{"█".PadRight(barLength, '█')}{ChatColors.White} MAX";
        
        var pointsInCurrentRank = points - currentRank.MinPoints;
        var pointsNeededForNext = nextRank.MinPoints - currentRank.MinPoints;
        var progress = (float)pointsInCurrentRank / pointsNeededForNext;
        
        var filledBars = (int)(progress * barLength);
        var emptyBars = barLength - filledBars;
        
        var progressBar = $"{ChatColors.Green}{"█".PadRight(filledBars, '█')}{ChatColors.DarkRed}{"░".PadRight(emptyBars, '░')}{ChatColors.White}";
        
        return $"{progressBar} {(progress * 100):F1}%";
    }
    
    public static List<Rank> GetAllRanks() => Ranks.ToList();
    
    public static string GetFormattedRankName(int points)
    {
        var rank = GetRank(points);
        return $"{rank.Color}[{rank.Name}]{ChatColors.White}";
    }
}

public class Rank
{
    public string Name { get; set; } = "";
    public int MinPoints { get; set; }
    public string Color { get; set; } = "";
}
