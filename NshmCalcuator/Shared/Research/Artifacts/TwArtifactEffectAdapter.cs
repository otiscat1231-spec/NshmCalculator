using System.Text.Json;

namespace NshmCalculator.Shared.Research.Artifacts;

public enum TwArtifactEffectCategory { StaticPanel, TaggedSkillDamage, ConditionalAverage, RotationEffect, Unmodeled }
public sealed class TwArtifactRoute
{
    public string Category { get; init; } = "";
    public string[] Targets { get; init; } = [];
    public string Domain { get; init; } = "";
    public string LegacyShareCode { get; init; } = "";
    public string RequiredRootOption { get; init; } = "";
    public string Note { get; init; } = "";
}
public sealed class TwCoreRule
{
    public string Id { get; init; } = "";
    public string Status { get; init; } = "";
    public string Algorithm { get; init; } = "";
    public Dictionary<string, string> DimensionNames { get; init; } = new();
    public decimal IncrementPerLevel { get; init; }
    public int BonusLevel { get; init; }
    public decimal BossFactor { get; init; }
    public string InputBasis { get; init; } = "";
    public string Source { get; init; } = "";
}
public sealed class TwArtifactEffectPolicy
{
    public string Id { get; init; } = "";
    public int SchemaVersion { get; init; }
    public string PackId { get; init; } = "";
    public string PackSourceSnapshotSha256 { get; init; } = "";
    public string SupportedProfession { get; init; } = "";
    public TwCoreRule CoreRule { get; init; } = new();
    public Dictionary<string, TwArtifactRoute> Routes { get; init; } = new();
    public Dictionary<string, TwArtifactRoute> SpecialRoutes { get; init; } = new();
    public Dictionary<string, string> NodeRuntimeNotes { get; init; } = new();
    public string ProfessionDamageKey { get; init; } = "";
    public string ProfessionDamageInput { get; init; } = "";
    public Dictionary<string, string> EquivalentFunctions { get; init; } = new();
}
public sealed record ArtifactStackState(int Stacks, decimal Probability);
public sealed class TwArtifactAverageControl
{
    public ArtifactTriggerMode Mode { get; init; } = ArtifactTriggerMode.Auto;
    public decimal? ManualUptime { get; init; }
    public decimal? ObservedUptime { get; init; }
    public decimal? ManualMeanStacks { get; init; }
    public decimal? ObservedMeanStacks { get; init; }
    public decimal? DurationSeconds { get; init; }
    public decimal? CooldownSeconds { get; init; }
    public ArtifactStackState[]? StackDistribution { get; init; }
}
public sealed class TwArtifactEffectContext
{
    public ArtifactTriggerMode DefaultMode { get; init; } = ArtifactTriggerMode.Auto;
    public Dictionary<string, TwArtifactAverageControl> Controls { get; init; } = new();
}
public sealed record TwFiveDimensionRow(string Key, string Name, decimal? Base, decimal Artifact, decimal? Final);
public sealed record TwFiveDimensionPanel(string RuleId, string Status, string InputBasis, int CoreLevel,
    TwFiveDimensionRow[] Dimensions, decimal? CandidateBossSuppression, string Reason);
public sealed record TwArtifactEffectView(string NodeId, string PositionId, string Name, string OptionId,
    string Key, decimal Amount, string Unit, TwArtifactEffectCategory Category, string[] Targets,
    string Domain, string LegacyShareCode, ArtifactTriggerMode Mode, decimal? AverageValue, string Status, string Note, string Source);
public sealed class TwArtifactProjection
{
    public string PolicyId { get; init; } = "";
    public string ProfessionId { get; init; } = "";
    public string CoreRuleId { get; init; } = "";
    public TwFiveDimensionPanel FiveDimensions { get; init; } = null!;
    public Dictionary<string, decimal> StaticAttributes { get; init; } = new();
    public Dictionary<string, decimal> ConditionalMeanAttributes { get; init; } = new();
    public BuildAttributeRow[] Panel { get; init; } = [];
    public TwArtifactEffectView[] Effects { get; init; } = [];
    public string[] Pending { get; init; } = [];
    public string[] ModelLimitations { get; init; } = [];
    public decimal? ProfessionDamageRatio { get; init; }
}

/// <summary>Legacy-style weighting/coverage methods with TW-owned amounts and explicit observations.</summary>
public static class TwArtifactAveraging
{
    public static decimal CooldownReadyUptime(decimal duration, decimal cooldown)
    {
        if (duration < 0 || cooldown <= 0) throw new ArgumentException("持續時間／冷卻無效");
        return Math.Min(1, duration / cooldown); // user's immediate-retrigger estimate, not a verified game rule
    }
    public static decimal WeightedSkillGain(TwArtifactEffectView effect, decimal verifiedShare, decimal uptime)
    {
        if (effect.Category != TwArtifactEffectCategory.TaggedSkillDamage || effect.Domain != "PvE"
            || effect.Unit != "percentagePoint" || verifiedShare is < 0 or > 1 || uptime is < 0 or > 1)
            throw new ArgumentException("只有指定技能增傷可用技能占比平均；攻速/CD不得作總DPS倍率");
        if (effect.Mode == ArtifactTriggerMode.Off || effect.AverageValue == 0) return 0;
        return effect.Amount / 100 * verifiedShare * uptime;
    }
    public static double ConditionalExpectation(IEnumerable<(decimal Probability, double Value)> states)
    {
        var rows = states.ToArray();
        if (rows.Length == 0 || rows.Any(r => r.Probability < 0 || !double.IsFinite(r.Value)) || rows.Sum(r => r.Probability) != 1)
            throw new ArgumentException("條件狀態機率須非負、總和1，模型值須有限");
        return rows.Sum(r => (double)r.Probability * r.Value);
    }
}

public sealed class TwArtifactEffectAdapter
{
    private readonly ArtifactBoardEngine engine;
    private readonly TwArtifactEffectPolicy policy;
    private readonly Func<IReadOnlyDictionary<string, decimal>, decimal, decimal> coreAlgorithm;
    public string PolicyId => policy.Id;
    public string ProfessionDamageInput => policy.ProfessionDamageInput;
    public IReadOnlyDictionary<string, string> DimensionNames => new Dictionary<string, string>(policy.CoreRule.DimensionNames);
    internal Dictionary<string, string> EquivalentFunctions => new(policy.EquivalentFunctions);

    public TwArtifactEffectAdapter(ArtifactBoardEngine engine, string policyJson,
        IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, decimal>, decimal, decimal>>? replacementAlgorithms = null)
    {
        this.engine = engine;
        policy = JsonSerializer.Deserialize<TwArtifactEffectPolicy>(policyJson) ?? throw new ArgumentException("效果規格為空");
        if (policy.SchemaVersion != 1 || policy.PackId != engine.Pack.Manifest.Id
            || policy.PackSourceSnapshotSha256 != engine.Pack.Manifest.SourceSnapshotSha256
            || !engine.Pack.ProfessionOverrides.ContainsKey(policy.SupportedProfession))
            throw new ArgumentException("TW神器效果規格與資料包版本／流派不符");
        var expected = engine.Pack.AllNodes.SelectMany(n => n.Levels).SelectMany(l => l.Effects)
            .Select(e => $"{e.Scope}:{e.Key}").ToHashSet();
        if (!expected.SetEquals(policy.Routes.Keys) || policy.Routes.Values.Concat(policy.SpecialRoutes.Values)
            .Any(r => !Enum.TryParse<TwArtifactEffectCategory>(r.Category, out var category) || !Enum.IsDefined(category) || r.Domain is not ("PvE" or "PvP")))
            throw new ArgumentException("效果分類缺漏／未知類別");
        if (!engine.Pack.AllNodes.SelectMany(n => n.SpecialEffects).Select(e => e.Kind).ToHashSet().SetEquals(policy.SpecialRoutes.Keys)
            || !policy.CoreRule.DimensionNames.Keys.ToHashSet().SetEquals(new[] { "constitution", "strength", "spirit", "agility", "endurance" })
            || policy.CoreRule.BonusLevel != engine.Pack.AllNodes.Single(n => n.PositionId == "CORE").MaxLevel
            || policy.CoreRule.IncrementPerLevel <= 0 || policy.CoreRule.BossFactor < 0 || string.IsNullOrWhiteSpace(policy.CoreRule.Id))
            throw new ArgumentException("五維／特殊效果規格不完整");
        var algorithms = new Dictionary<string, Func<IReadOnlyDictionary<string, decimal>, decimal, decimal>> {
            ["floor-each-v1"] = (dimensions, factor) => dimensions.Values.Sum(v => decimal.Floor(v * factor))
        };
        foreach (var (key, algorithm) in replacementAlgorithms ?? new Dictionary<string, Func<IReadOnlyDictionary<string, decimal>, decimal, decimal>>())
            if (!algorithms.TryAdd(key, algorithm)) throw new ArgumentException("替代算法須有不同版本ID");
        coreAlgorithm = algorithms.GetValueOrDefault(policy.CoreRule.Algorithm) ?? throw new ArgumentException("候選五維算法未註冊");
    }

    private static void ValidateControl(TwArtifactAverageControl c)
    {
        if (!Enum.IsDefined(c.Mode) || c.ManualUptime is < 0 or > 1 || c.ObservedUptime is < 0 or > 1
            || c.ManualMeanStacks < 0 || c.ObservedMeanStacks < 0 || c.DurationSeconds < 0 || c.CooldownSeconds <= 0)
            throw new ArgumentException("auto/manual/off平均化輸入無效");
        if (c.StackDistribution is { } states && (states.Length == 0 || states.Any(s => s.Stacks < 0 || s.Probability < 0)
            || states.Sum(s => s.Probability) != 1)) throw new ArgumentException("層數分布機率須總和1");
    }
    private static decimal? Uptime(TwArtifactAverageControl c, bool alwaysActive)
    {
        if (c.Mode == ArtifactTriggerMode.Off) return 0;
        if (c.Mode == ArtifactTriggerMode.Manual) return c.ManualUptime;
        if (alwaysActive) return 1;
        if (c.ObservedUptime is { } observed) return observed;
        return c.DurationSeconds is { } duration && c.CooldownSeconds is { } cooldown
            ? TwArtifactAveraging.CooldownReadyUptime(duration, cooldown) : null;
    }

    public TwArtifactProjection Project(ResearchBuild build, TwArtifactEffectContext? context = null)
    {
        if (build.ProfessionId != policy.SupportedProfession)
            throw new ArgumentException("本效果接線版本只支援已建資料的龍吟；其他流派不套用龍吟規則");
        context ??= new();
        if (!Enum.IsDefined(context.DefaultMode)) throw new ArgumentException("未知預設觸發模式");
        foreach (var (id, c) in context.Controls) { engine.Node(id); ValidateControl(c); }
        var state = build.ArtifactState ?? engine.CreateState(build.ProfessionId);
        if (state.ProfessionId != build.ProfessionId) throw new ArgumentException("神器與Build流派不符");
        engine.Validate(state);
        TwArtifactAverageControl Control(string id) => context.Controls.GetValueOrDefault(id) ?? new() { Mode = context.DefaultMode };
        var statics = new ArtifactAggregator(engine).Summarize(state, new() { Mode = ArtifactTriggerMode.Off }).StaticAttributes;
        var conditional = new Dictionary<string, decimal>();
        var views = new List<TwArtifactEffectView>(); var pending = new List<string>();
        decimal? professionRatio = 0;
        var core = engine.Pack.AllNodes.Single(n => n.PositionId == "CORE");
        var coreLevel = state.Levels.GetValueOrDefault(core.Id);
        if (build.BaseFiveDimensions.Keys.Except(policy.CoreRule.DimensionNames.Keys).Any() || build.BaseFiveDimensions.Values.Any(v => v < 0))
            throw new ArgumentException("五維僅允許根骨、力量、氣海、身法、耐力，數值不可為負");
        var dimensions = policy.CoreRule.DimensionNames.Select(d => {
            decimal? basis = build.BaseFiveDimensions.TryGetValue(d.Key, out var v) ? v : null;
            return new TwFiveDimensionRow(d.Key, d.Value, basis, coreLevel * policy.CoreRule.IncrementPerLevel,
                basis + coreLevel * policy.CoreRule.IncrementPerLevel);
        }).ToArray();
        decimal? boss = 0;
        var coreControl = Control(core.Id);
        var reason = "未達Lv5，無首克候選增量";
        if (coreLevel == policy.CoreRule.BonusLevel)
        {
            var duty = Uptime(coreControl, true);
            boss = duty == 0 ? 0 : dimensions.Any(d => d.Final is null) || duty is null ? null
                : coreAlgorithm(dimensions.ToDictionary(d => d.Key, d => d.Final!.Value), policy.CoreRule.BossFactor) * duty;
            reason = boss is null ? "缺少五個獨立基礎值／manual覆蓋率；不使用Legacy合併值補填" : policy.CoreRule.Status;
            if (boss is null) pending.Add($"CORE：{reason}");
            else conditional["bossSuppression"] = boss.Value;
        }
        var fivePanel = new TwFiveDimensionPanel(policy.CoreRule.Id, policy.CoreRule.Status, policy.CoreRule.InputBasis,
            coreLevel, dimensions, boss, reason);
        foreach (var (id, level) in state.Levels.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var node = engine.Node(id); var option = state.Selections.GetValueOrDefault(id, "");
            var record = node.Levels.Single(l => l.Level == level && l.OptionId == option);
            var c = Control(id);
            foreach (var e in record.Effects)
            {
                var route = policy.Routes[$"{e.Scope}:{e.Key}"];
                var category = Enum.Parse<TwArtifactEffectCategory>(route.Category);
                decimal? mean = category == TwArtifactEffectCategory.StaticPanel ? e.Amount : null;
                var status = route.Domain == "PvP" ? "PVE排除" : category == TwArtifactEffectCategory.StaticPanel ? "靜態面板" : "未接總DPS";
                if (category == TwArtifactEffectCategory.TaggedSkillDamage)
                {
                    var duty = Uptime(c, true);
                    if (route.RequiredRootOption.Length > 0 && state.Selections.GetValueOrDefault(engine.Pack.AllNodes.Single(n => n.PositionId == "ROOT-L").Id) != route.RequiredRootOption)
                        duty = 0;
                    mean = duty * e.Amount;
                    if (e.Key == policy.ProfessionDamageKey)
                    {
                        professionRatio = mean / 100;
                        status = mean is null ? "缺manual覆蓋率" : "新盤係數→Legacy技能占比接口";
                        if (mean is null) pending.Add($"{node.Name}：缺manual覆蓋率");
                    }
                    else status = duty == 0 ? "off／分支不適用" : "保留指定技能群；缺占比／疊加驗證";
                }
                views.Add(new(id, node.PositionId, node.Name, option, e.Key, e.Amount, e.Unit, category, route.Targets,
                    route.Domain, route.LegacyShareCode, c.Mode, mean, status, route.Note, record.Source));
            }
            foreach (var s in node.SpecialEffects.Where(s => level >= s.MinimumLevel))
            {
                var route = policy.SpecialRoutes[s.Kind];
                decimal? mean;
                string note;
                if (s.Kind == "coreSuppressionAtMax") { mean = boss; note = $"{policy.CoreRule.Id}；{reason}；只首領克制"; }
                else
                {
                    var cap = record.Effects.Single(e => e.Key == "jingLei.interactionMaxStacks").Amount;
                    var distribution = c.Mode == ArtifactTriggerMode.Auto ? c.StackDistribution : null;
                    if (distribution?.Any(s => s.Stacks > cap) == true) throw new ArgumentException("層數分布超過本級上限");
                    mean = c.Mode == ArtifactTriggerMode.Off ? 0 : c.Mode == ArtifactTriggerMode.Manual ? c.ManualMeanStacks
                        : distribution?.Sum(s => s.Stacks * s.Probability) ?? c.ObservedMeanStacks;
                    if (mean > cap) throw new ArgumentException("平均層數超過本級上限");
                    note = mean is null ? "缺實測平均層數／分布；不推定滿層" : "平均屬性近似；精確傷害期望需逐狀態加權";
                    if (mean is null) pending.Add($"{node.Name}：{note}");
                    else foreach (var (key, perStack) in s.PerStack) conditional[key] = conditional.GetValueOrDefault(key) + mean.Value * perStack;
                }
                views.Add(new(id, node.PositionId, node.Name, option, s.Kind, 0, "context", TwArtifactEffectCategory.ConditionalAverage,
                    route.Targets, "PvE", "", c.Mode, mean, mean is null ? "待情境資料" : "明確情境候選", note, s.Kind == "coreSuppressionAtMax" ? policy.CoreRule.Source : record.Source));
            }
            if (policy.NodeRuntimeNotes.TryGetValue(id, out var runtime))
                views.Add(new(id, node.PositionId, node.Name, option, "runtime", 0, "mechanic", TwArtifactEffectCategory.Unmodeled,
                    [], "PvE", "", c.Mode, null, "未建模", runtime, record.Source));
        }
        if (coreLevel > 0 && engine.Node(core.Id).Levels.Single(l => l.Level == coreLevel).Effects
            .Single(e => e.Key == "fiveDimensions.displayIncrement").Amount != coreLevel * policy.CoreRule.IncrementPerLevel)
            throw new ArgumentException("版本化五維增量與來源逐級記錄衝突");
        var rows = BuildAttributeLayer.Project(build, engine).ToDictionary(r => r.Key);
        foreach (var (key, amount) in conditional)
        {
            var row = rows.GetValueOrDefault(key) ?? new(key, build.BaseAttributes.TryGetValue(key, out var value) ? value : null, 0, null);
            rows[key] = new(key, row.Base, row.Artifact + amount, row.Base + row.Artifact + amount);
        }
        return new() { PolicyId = policy.Id, ProfessionId = build.ProfessionId, CoreRuleId = policy.CoreRule.Id,
            FiveDimensions = fivePanel, StaticAttributes = statics, ConditionalMeanAttributes = conditional,
            Panel = rows.Values.OrderBy(r => r.Key, StringComparer.Ordinal).ToArray(), Effects = views.ToArray(), Pending = pending.ToArray(),
            ProfessionDamageRatio = professionRatio,
            ModelLimitations = ["候選模型值≠已校準DPS；不自動補技能占比或跨流派校準。",
                "五維逐項增量只保存；不建立五維→攻擊、破防、會心、命中等通用公式，也不覆寫Legacy合併五維欄。",
                "四種標籤增強、指定技能增傷、分支疊加仍需占比與作用邊界資料；目前只有破空·威霆接入Legacy SH_001。",
                "條件平均屬性不是精確傷害期望；非線性／相關狀態需逐狀態條件加權。",
                "RotationEffect、減傷、護盾、輕功、PvP流派克制與未建模連動不乘總DPS。"] };
    }
}
