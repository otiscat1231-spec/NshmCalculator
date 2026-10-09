using System.Text.Json;

namespace NshmCalculator.Shared.Research.Artifacts;

public sealed record ArtifactSlot(string PositionId, int Column, int Row);
public sealed record ArtifactVisibleSlot(ArtifactSlot Slot, ArtifactNode? Node);

/// <summary>Presentation coordinates only. Game mechanics remain in the versioned pack.</summary>
public sealed class ArtifactBoardLayout
{
    public int SchemaVersion { get; set; }
    public string PackId { get; set; } = "";
    public List<ArtifactSlot> Slots { get; set; } = new();

    public static ArtifactBoardLayout Load(string json, ArtifactDataPack pack)
    {
        var layout = JsonSerializer.Deserialize<ArtifactBoardLayout>(json)
            ?? throw new ArtifactRuleException("invalid-layout", "盤面位置資料不存在");
        if (layout.SchemaVersion != 1 || layout.PackId != pack.Manifest.Id
            || layout.Slots.Count != pack.AllNodes.Select(n => n.PositionId).Distinct().Count()
            || layout.Slots.Select(s => s.PositionId).Distinct().Count() != layout.Slots.Count
            || layout.Slots.Select(s => (s.Column, s.Row)).Distinct().Count() != layout.Slots.Count
            || layout.Slots.Any(s => s.Column < 0 || s.Row < 0)
            || !layout.Slots.Select(s => s.PositionId).ToHashSet().SetEquals(pack.AllNodes.Select(n => n.PositionId)))
            throw new ArtifactRuleException("invalid-layout", "盤面版本、槽位或座標不符");
        return layout;
    }

    public ArtifactVisibleSlot[] ForProfession(ArtifactDataPack pack, string professionId)
    {
        var nodes = pack.CommonNodes.Concat(pack.ProfessionOverrides.GetValueOrDefault(professionId) ?? new())
            .ToDictionary(n => n.PositionId);
        return Slots.Select(s => new ArtifactVisibleSlot(s, nodes.GetValueOrDefault(s.PositionId))).ToArray();
    }
}
