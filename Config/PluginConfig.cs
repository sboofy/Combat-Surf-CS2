using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace CombatSurf.Config;

public class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("GodmodeTime")]
    public float GodmodeTime { get; set; } = 9.0f;

    [JsonPropertyName("EnableGodmode")]
    public bool EnableGodmode { get; set; } = true;

    [JsonPropertyName("ShowGodmodeMessages")]
    public bool ShowGodmodeMessages { get; set; } = true;

    [JsonPropertyName("SpeedBonusThreshold")]
    public float SpeedBonusThreshold { get; set; } = 250.0f;

    [JsonPropertyName("SpeedMultiplierScale")]
    public float SpeedMultiplierScale { get; set; } = 1000.0f;

    [JsonPropertyName("DisplayChatTags")]
    public bool DisplayChatTags { get; set; } = true;

    [JsonPropertyName("DisplayScoreboardTags")]
    public bool DisplayScoreboardTags { get; set; } = true;

    [JsonPropertyName("BlockChat")]
    public bool BlockChat { get; set; } = false;

    [JsonPropertyName("AllowAdminChat")]
    public bool AllowAdminChat { get; set; } = true;

    [JsonPropertyName("SpawnUnits")]
    public int SpawnUnits { get; set; } = 16000;

    [JsonPropertyName("ConstantRespawn")]
    public bool ConstantRespawn { get; set; } = false;

    [JsonPropertyName("ExtendedRoundTime")]
    public bool ExtendedRoundTime { get; set; } = false;

}