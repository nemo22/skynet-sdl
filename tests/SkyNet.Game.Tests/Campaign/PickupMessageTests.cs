using SkyNet.Formats;
using SkyNet.Game.Campaign;
using SkyNet.Game.Tests.Behaviour;

namespace SkyNet.Game.Tests.Campaign;

/// <summary>Issue #14: a pickup's status line is its STRINGS.PRS text, never the raw key.</summary>
public class PickupMessageTests
{
    /// <summary>A fact skipped unless Future Shock's data sits beside SKYNET_DATA's.</summary>
    private sealed class ShockDataFactAttribute : FactAttribute
    {
        public ShockDataFactAttribute()
        {
            if (BehaviourData.Dir is not { } dir || GameDataLocator.OtherGameDir(dir) == null)
            {
                Skip = "no Future Shock data beside SKYNET_DATA's SkyNET folder";
            }
        }
    }

    [BehaviourDataFact]
    public void SkyNetItemKeysResolve() => Assert.Empty(Unresolved(BehaviourData.Data));

    [ShockDataFact]
    public void FutureShockItemKeysResolve() =>
        Assert.Empty(Unresolved(GameData.Open(GameDataLocator.OtherGameDir(BehaviourData.Dir!)!, BehaviourData.Dir)!));

    // Each DOS item's key read as the pickup reads it; a miss falls back to the key itself.
    private static List<string> Unresolved(GameData data)
    {
        var session = new GameSession(data);
        return [.. PickupTable.Dos.Values.Select(i => i.MessageKey).Distinct().Where(k => session.Text(k, k) == k)];
    }
}
