using System.Text.Json;
using NshmCalculator.Shared.Research.Artifacts;

namespace NshmCalculator.Test.Research;

/// <summary>Independent assertion against verbatim source cumulative JSON, not regenerated expectations.</summary>
[TestFixture]
public class ArtifactSourceGoldenTest
{
    private static string TestFile(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", name);
    private static string PackFile => Path.Combine(TestContext.CurrentContext.TestDirectory, "data/artifacts/TW-Mobile-2.3.3/artifact-pack.json");
    private static readonly Dictionary<string, string> SourceKeys = new() {
        ["攻擊"] = "attack", ["攻擊力"] = "attack", ["元素攻擊"] = "elementAttack", ["命中"] = "hit",
        ["會心"] = "critical", ["破防"] = "defensePenetration", ["氣血上限"] = "maxHealth", ["格擋"] = "block",
        ["防禦"] = "defense", ["全元素抗性"] = "allElementResistance", ["忽視元素抗性"] = "ignoreElementResistance",
        ["會心抗性"] = "criticalResistance", ["流派克制"] = "professionSuppression", ["首領克制"] = "bossSuppression",
        ["流派抵禦"] = "professionResistance", ["首領抵禦"] = "bossResistance",
        ["會心傷害百分比"] = "criticalDamage", ["全技能增強"] = "skillEnhancement.all",
        ["單體技能增強"] = "skillEnhancement.single", ["群體技能增強"] = "skillEnhancement.group",
        ["爆發技能增強"] = "skillEnhancement.burst", ["持續技能增強"] = "skillEnhancement.sustained",
        ["指定流派克制百分比"] = "targetProfessionSuppression", ["指定流派抵禦百分比"] = "targetProfessionResistance",
        ["條件額外流派克制"] = "professionSuppression", ["條件額外流派抵禦"] = "professionResistance",
        ["五維"] = "fiveDimensions.displayIncrement", ["流派技能傷害提升"] = "professionSkill.damageIncrease",
        ["受到流派技能傷害降低"] = "professionSkill.damageReduction", ["吟風傷害提升"] = "yinFeng.damageIncrease",
        ["劍氣對怪物額外傷害"] = "swordQi.monsterExtraDamage", ["誅邪傷害提升"] = "zhuXie.damageIncrease",
        ["劍意追擊傷害提升"] = "swordIntent.pursuitDamageIncrease", ["劍意追擊傷害提升百分比"] = "swordIntent.pursuitDamageIncrease",
        ["怪物額外傷害"] = "leiLong.monsterExtraDamage", ["護盾血量百分比"] = "leiLong.shieldHealth",
        ["輕功值削減"] = "yinFeng.lightnessReduction", ["輕功值回復"] = "yinFeng.lightnessRecovery",
        ["減傷比例"] = "longFei.damageReduction", ["冷卻時間降低秒"] = "jingLei.cooldownReduction",
        ["互動增益最大層數"] = "jingLei.interactionMaxStacks", ["攻速提升百分比"] = "jianDang.attackSpeedIncrease"
    };

    public static IEnumerable<TestCaseData> SourceRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TestFile("artifact_233_source_golden.json")));
        foreach (var row in doc.RootElement.EnumerateArray())
            yield return new TestCaseData(row.GetRawText()).SetName($"Source_{row.GetProperty("LevelRecordId").GetString()}");
    }

    private static string SourceScope(string label)
    {
        if (label.StartsWith("條件額外")) return "triggeredPanel";
        if (label.StartsWith("指定流派")) return "targetProfession";
        if (label is "五維" or "互動增益最大層數") return "mechanic";
        if (label is "流派技能傷害提升" or "受到流派技能傷害降低" or "吟風傷害提升"
            or "劍氣對怪物額外傷害" or "誅邪傷害提升" or "劍意追擊傷害提升"
            or "劍意追擊傷害提升百分比" or "怪物額外傷害" or "護盾血量百分比"
            or "輕功值削減" or "輕功值回復" or "減傷比例" or "冷卻時間降低秒"
            or "攻速提升百分比") return "skill";
        return "panel";
    }

    private static Dictionary<string, decimal> Expected(JsonElement raw, ArtifactOption? option)
    {
        var result = new Dictionary<string, decimal>();
        void Collect(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (!item.TryGetProperty("value", out var amount)) amount = item.GetProperty("cumulativeIncrement");
                    var label = item.GetProperty("type").GetString()!;
                    if (amount.ValueKind == JsonValueKind.Number)
                        result[$"{SourceScope(label)}:{SourceKeys[label]}"] = amount.GetDecimal();
                }
            }
            else
            {
                foreach (var prop in value.EnumerateObject())
                {
                    if (prop.Name is "每層命中" or "每層首領克制" or "targetProfession") continue;
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        result[$"{SourceScope(prop.Name)}:{SourceKeys[prop.Name]}"] = prop.Value.GetDecimal();
                    else if (prop.Value.ValueKind == JsonValueKind.Object && option?.Name == prop.Name) Collect(prop.Value);
                }
            }
        }
        Collect(raw);
        return result;
    }

    [TestCaseSource(nameof(SourceRows))]
    public void EverySourceLevelAndEveryOptionHasExactSignedCumulativeValues(string sourceJson)
    {
        using var row = JsonDocument.Parse(sourceJson);
        var pack = ArtifactDataPack.Load(File.ReadAllText(PackFile));
        var node = pack.AllNodes.Single(n => n.Id == row.RootElement.GetProperty("NodeId").GetString());
        var level = row.RootElement.GetProperty("Level").GetInt32();
        var raw = row.RootElement.GetProperty("RawCumulative");
        foreach (var actual in node.Levels.Where(l => l.Level == level))
        {
            var option = node.Options.SingleOrDefault(o => o.Id == actual.OptionId);
            var expected = Expected(raw, option);
            Assert.That(actual.Effects.ToDictionary(e => $"{e.Scope}:{e.Key}", e => e.Amount), Is.EquivalentTo(expected), actual.OptionId);
            Assert.That(actual.Cost, Is.EqualTo(level * node.CostPerLevel));
            Assert.That(actual.LevelRecordId, Is.EqualTo(row.RootElement.GetProperty("LevelRecordId").GetString()));
            if (raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("每層命中", out var hit))
            {
                Assert.That(node.SpecialEffects.Single().PerStack["hit"], Is.EqualTo(hit.GetDecimal()));
                Assert.That(node.SpecialEffects.Single().PerStack["bossSuppression"], Is.EqualTo(raw.GetProperty("每層首領克制").GetDecimal()));
            }
        }
    }
}
