using System.Text.Json;

namespace NshmCalculator.Shared.Research.Artifacts;

public sealed class ArtifactDataPack
{
    public ArtifactManifest Manifest { get; set; } = new();
    public ArtifactRules Rules { get; set; } = new();
    public List<ArtifactNode> CommonNodes { get; set; } = new();
    public Dictionary<string, List<ArtifactNode>> ProfessionOverrides { get; set; } = new();
    public List<ArtifactDiagnostic> Diagnostics { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<ArtifactNode> AllNodes => CommonNodes.Concat(ProfessionOverrides.Values.SelectMany(x => x));

    public static ArtifactDataPack Load(string json)
    {
        var pack = JsonSerializer.Deserialize<ArtifactDataPack>(json)
            ?? throw new ArtifactRuleException("invalid-pack", "神器資料包不存在");
        pack.ValidateDefinition();
        return pack;
    }

    public void ValidateDefinition()
    {
        void Require(bool ok, string message)
        {
            if (!ok) throw new ArtifactRuleException("invalid-pack", message);
        }
        Require(Manifest.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(Manifest.Id), "資料包版本無效");
        Require(Manifest.Server == "TW" && Manifest.Platform == "Mobile", "台服手遊資料包範圍不符");
        Require(!Manifest.DamageFormulaAttached, "本階段不允許接入傷害公式");
        var nodes = AllNodes.ToArray();
        Require(nodes.Length == Manifest.NodeCount && nodes.Select(n => n.Id).Distinct().Count() == nodes.Length, "節點數／ID不唯一");
        Require(nodes.Select(n => n.PositionId).Distinct().Count() == nodes.Length, "實體槽位重複");
        Require(CommonNodes.All(n => n.Shared && n.ProfessionId == ""), "共同層混入職業覆寫");
        foreach (var (profession, list) in ProfessionOverrides)
            Require(Manifest.SupportedProfessionOverrides.Contains(profession)
                && list.All(n => !n.Shared && n.ProfessionId == profession), "職業覆寫範圍不符");
        var byId = nodes.ToDictionary(n => n.Id);
        var unitByKey = new Dictionary<string, string>();
        foreach (var n in nodes)
        {
            Require(!string.IsNullOrWhiteSpace(n.Id) && n.MaxLevel > 0 && n.CostPerLevel > 0, "節點上限／成本無效");
            Require(n.UnlockType is "root" or "questItem" or "parentLevel", "未知解鎖類型");
            if (n.UnlockType == "parentLevel")
            {
                Require(n.ParentId is not null && byId.ContainsKey(n.ParentId), $"父節點不存在：{n.Id}");
                var p = byId[n.ParentId!];
                var required = Rules.SpecialParentLevels.GetValueOrDefault(p.PositionId, Rules.GeneralParentLevel);
                Require(n.RequiredParentLevel == required && required <= p.MaxLevel, "父等級規則不符");
            }
            else Require(n.ParentId is null && n.RequiredParentLevel == 0, "根／任務道具節點不得有父鏈");
            var seen = new HashSet<string> { n.Id };
            var current = n;
            while (current.ParentId is not null)
            {
                Require(seen.Add(current.ParentId), "父鏈循環");
                current = byId[current.ParentId];
            }
            Require(n.SelectionType is "none" or "singleSlot" or "targetProfession" or "skillEnhancementMode" or "independentNode", "未知選擇規則");
            Require(n.Options.Select(o => o.Id).Distinct().Count() == n.Options.Count, "選項ID重複");
            Require(n.Options.SelectMany(o => o.Aliases.Append(o.Id).Append(o.Name)).All(s => !string.IsNullOrWhiteSpace(s)), "空選項名稱");
            Require(n.Options.Count == 0 || n.InheritLevelOnSwitch, "單槽選項必須繼承等級");
            Require(n.Options.Count == 0 || string.IsNullOrEmpty(n.MutualExclusionGroup), "單槽不可誤當雙實體互斥");
            var options = n.Options.Select(o => o.Id).DefaultIfEmpty("").ToArray();
            Require(n.Levels.Count == n.MaxLevel * options.Length, "逐級資料筆數不完整");
            foreach (var option in options)
            {
                var rows = n.Levels.Where(l => l.OptionId == option).OrderBy(l => l.Level).ToArray();
                Require(rows.Select(l => l.Level).SequenceEqual(Enumerable.Range(1, n.MaxLevel)), "逐級重複／缺級");
                foreach (var l in rows)
                {
                    Require(l.Cost == l.Level * n.CostPerLevel, "逐級成本不符");
                    Require(l.Effects.Select(e => (e.Key, e.Scope, e.Gate)).Distinct().Count() == l.Effects.Count, "同選項同作用範圍效果鍵重複");
                    foreach (var e in l.Effects)
                    {
                        Require(e.Unit is "point" or "percentagePoint" or "second" or "stack" or "displayPoint", "效果單位無效");
                        Require(e.Scope is "panel" or "skill" or "mechanic" or "triggeredPanel" or "targetProfession", "效果範圍無效");
                        Require(e.Gate is "" or "enemyPlayersGreaterThan5", "未知條件");
                        Require(e.DurationSeconds >= 0, "負持續時間");
                        Require(!unitByKey.TryGetValue(e.Key, out var unit) || unit == e.Unit, "同鍵混用單位");
                        unitByKey[e.Key] = e.Unit;
                    }
                }
            }
            Require(n.SpecialEffects.All(e => e.Kind is "coreSuppressionAtMax" or "interactionStacks"), "未知動態效果");
        }
        Require(nodes.Sum(n => n.MaxLevel) == Manifest.LevelRecordCount, "原逐級筆數不符");
        foreach (var pair in Rules.NonExclusiveGroups)
        {
            Require(pair.Length == 2, "非互斥組結構無效");
            foreach (var position in pair)
                Require(nodes.Any(n => n.PositionId == position && string.IsNullOrEmpty(n.MutualExclusionGroup)), "非互斥節點誤設互斥");
        }
    }
}

public sealed class ArtifactManifest
{
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string Server { get; set; } = "";
    public string Platform { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string FrozenDate { get; set; } = "";
    public string SourceSnapshotSha256 { get; set; } = "";
    public List<string> SupportedProfessionOverrides { get; set; } = new();
    public int NodeCount { get; set; }
    public int LevelRecordCount { get; set; }
    public bool DamageFormulaAttached { get; set; }
}
public sealed class ArtifactRules
{
    public int GeneralParentLevel { get; set; }
    public Dictionary<string, int> SpecialParentLevels { get; set; } = new();
    public bool QuestDefaultUnlocked { get; set; }
    public List<string[]> NonExclusiveGroups { get; set; } = new();
}
public sealed class ArtifactNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PositionId { get; set; } = "";
    public string NodeClass { get; set; } = "";
    public bool Shared { get; set; }
    public string ProfessionId { get; set; } = "";
    public string? ParentId { get; set; }
    public int RequiredParentLevel { get; set; }
    public int MaxLevel { get; set; }
    public int CostPerLevel { get; set; }
    public string UnlockType { get; set; } = "";
    public string SelectionType { get; set; } = "none";
    public string? SelectionGroup { get; set; }
    public string? MutualExclusionGroup { get; set; }
    public bool InheritLevelOnSwitch { get; set; }
    public List<ArtifactOption> Options { get; set; } = new();
    public List<ArtifactLevel> Levels { get; set; } = new();
    public List<ArtifactSpecialEffect> SpecialEffects { get; set; } = new();
    public JsonElement Model { get; set; }
    public string TriggerMode { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public int SpecRow { get; set; }
    public List<string> SourceIds { get; set; } = new();
    public ArtifactOption ResolveOption(string value) => Options.SingleOrDefault(o =>
        o.Id == value || o.Name == value || o.Aliases.Contains(value))
        ?? throw new ArtifactRuleException("invalid-option", $"{Name}沒有此選項：{value}");
}
public sealed class ArtifactOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public string Description { get; set; } = "";
}
public sealed class ArtifactLevel
{
    public int Level { get; set; }
    public string OptionId { get; set; } = "";
    public int Cost { get; set; }
    public List<ArtifactEffect> Effects { get; set; } = new();
    public string Source { get; set; } = "";
    public string LevelRecordId { get; set; } = "";
}
public sealed class ArtifactEffect
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public string Unit { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Gate { get; set; } = "";
    public int DurationSeconds { get; set; }
}
public sealed class ArtifactSpecialEffect
{
    public string Kind { get; set; } = "";
    public int MinimumLevel { get; set; }
    public decimal Factor { get; set; }
    public Dictionary<string, decimal> PerStack { get; set; } = new();
    public string Note { get; set; } = "";
}
public sealed record ArtifactDiagnostic(string Code, string NodeId, string Source, string Detail);
public sealed class ArtifactRuleException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}
