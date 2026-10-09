using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;
using NshmCalculator.Shared.Research.Artifacts;

namespace NshmCalculator.Shared.Research;

public sealed record LegacyFieldMapping(string SourceKey, string Label, string SourceUnit,
    string LegacyCode, string LegacyUnit, decimal Divisor, string DuplicateRisk);
public sealed record LegacyUnmappedField(string SourceKey, string Reason);
public sealed record LegacyDisabledInput(string Code, bool NumberMode, double NumberValue, string? StringValue);
public sealed record LegacyFormulaConflict(string Code, string Name, string Term, string[] InputCodes, string EvidenceCode, string Reason);
public sealed class LegacyMappingSpec
{
    public string Id { get; init; } = "";
    public string ArtifactPackId { get; init; } = "";
    public string LegacyConfigSha256 { get; init; } = "";
    public string LegacyVersion { get; init; } = "";
    public long LegacyInternalVersion { get; init; }
    public LegacyFieldMapping[] Mappings { get; init; } = [];
    public LegacyUnmappedField[] Unmapped { get; init; } = [];
    public LegacyDisabledInput[] DisabledInputs { get; init; } = [];
    public string[] ExcludedResultCodes { get; init; } = [];
    public LegacyFormulaConflict[] EmbeddedFormulaConflicts { get; init; } = [];
}
public sealed record LegacyInputValue(bool NumberMode, double NumberValue, string? StringValue)
{
    public static LegacyInputValue From(ParamValue p) => new(p.NumberMode, p.NumberValue, p.StringValue);
    public ParamValue ToParamValue() => new() { NumberMode = NumberMode, NumberValue = NumberValue, StringValue = StringValue };
}
public sealed record LegacyFieldSnapshot(string LegacyCode, string Label, string? SourceKey,
    string SourceUnit, string LegacyUnit, decimal? Base, decimal? Artifact, decimal? Final,
    LegacyInputValue? Input, LegacyInputValue? ReplacedLegacyInput, string SourceKind, string Note);
public sealed record LegacyAttributeSource(string LegacyCode, string SourceKind, string SourceId);
public sealed record LegacyOmission(string Key, decimal? Final, decimal Artifact, string Reason);
public sealed record LegacyInputDifference(string LegacyCode, LegacyFieldSnapshot A, LegacyFieldSnapshot B, double? Difference);
public sealed record LegacyInputPair(LegacyInputSnapshot A, LegacyInputSnapshot B,
    LegacyInputDifference[] Differences, bool CrossProfession, string? Warning);
public sealed class LegacyInputSnapshot
{
    public string AdapterId { get; init; } = "";
    public string ArtifactPackId { get; init; } = "";
    public string LegacyConfigSha256 { get; init; } = "";
    public string BuildId { get; init; } = "";
    public string ProfessionId { get; init; } = "";
    public string LegacyMode { get; init; } = "";
    public string RotationId { get; init; } = "";
    public string Status { get; init; } = "candidate-input-only; uncalibrated";
    // Detached, serializable values. Validate again before any future handoff to an engine.
    public Dictionary<string, LegacyInputValue> Inputs { get; init; } = new();
    public LegacyFieldSnapshot[] Fields { get; init; } = [];
    public LegacyAttributeSource[] InjectionSources { get; init; } = [];
    public LegacyOmission[] Omissions { get; init; } = [];
    public ArtifactContribution[] ExcludedArtifactEffects { get; init; } = [];
    public ArtifactPendingEffect[] UnmodeledMechanics { get; init; } = [];
    public Dictionary<string, decimal?> TaggedSkillEnhancement { get; init; } = new();
    public Dictionary<string, decimal?> EffectiveTaggedSkillEnhancement { get; init; } = new();
    public string[] MissingInputs { get; init; } = [];
    public string[] ExcludedResultCodes { get; init; } = [];
    public LegacyFormulaConflict[] EmbeddedFormulaConflicts { get; init; } = [];
    // Phase 2C deliberately supplies no engine execution permission, even with all fields filled.
    public bool EngineEvaluationAllowed { get; init; }
    public string[] Warnings { get; init; } = [];
    public bool HasCompleteInputSet => MissingInputs.Length == 0;
}

/// <summary>
/// Projects TW base + static artifacts once. Does not evaluate PveUtility, alter formulas,
/// collapse skill tags, infer unknown base values, or promote candidate inputs to calibrated DPS.
/// </summary>
public sealed class LegacyStaticInputAdapter
{
    private readonly PveConfig config;
    private readonly LegacyMappingSpec spec;
    private readonly ArtifactBoardEngine engine;
    public string OriginalConfigSha256 => spec.LegacyConfigSha256;
    public LegacyFieldMapping[] Mappings => spec.Mappings.ToArray();
    public AttributeDefinition[] AdditionalBaseDefinitions => spec.Mappings
        .Select(m => new AttributeDefinition(m.SourceKey, m.Label, m.SourceUnit))
        .ExceptBy(BuildAttributeLayer.Definitions(engine.Pack).Select(d => d.Key), d => d.Key).ToArray();

    public LegacyStaticInputAdapter(string configJson, string mappingJson, ArtifactBoardEngine engine)
    {
        this.engine = engine;
        config = JsonSerializer.Deserialize<PveConfig>(configJson) ?? throw new ArgumentException("Legacy設定為空");
        spec = JsonSerializer.Deserialize<LegacyMappingSpec>(mappingJson) ?? throw new ArgumentException("映射規格為空");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configJson))).ToLowerInvariant();
        if (hash != spec.LegacyConfigSha256 || config.Version != spec.LegacyVersion
            || config.InternalVersion != spec.LegacyInternalVersion || engine.Pack.Manifest.Id != spec.ArtifactPackId)
            throw new ArgumentException("映射規格與Legacy／神器版本不符，須重新盤點");
        var fronts = config.FrontParamInfoArray.ToDictionary(f => f.Code);
        if (spec.Mappings.Select(m => m.LegacyCode).Distinct().Count() != spec.Mappings.Length
            || spec.Mappings.Select(m => m.SourceKey).Distinct().Count() != spec.Mappings.Length
            || spec.Mappings.Any(m => m.Divisor <= 0 || !fronts.ContainsKey(m.LegacyCode)
                || !config.DefaultParamValues[m.LegacyCode].NumberMode))
            throw new ArgumentException("映射欄位重複、單位或輸入類型不符");
        if (!fronts.Values.Where(f => f.GroupName == "神器").Select(f => f.Code).Order()
            .SequenceEqual(spec.DisabledInputs.Select(d => d.Code).Order()))
            throw new ArgumentException("舊神器停用清單不完整");
        if (!config.ResultFormulas.Where(f => f.Code.StartsWith("RF_SQ_", StringComparison.Ordinal)).Select(f => f.Code).Order()
            .SequenceEqual(spec.ExcludedResultCodes.Order()))
            throw new ArgumentException("舊神器收益排除清單不完整");
        if (spec.EmbeddedFormulaConflicts.Length == 0 || spec.EmbeddedFormulaConflicts.Any(c =>
            !config.InternalFormulas.Single(f => f.Code == c.Code).Formula.Contains(c.Term, StringComparison.Ordinal)
            || !config.ResultFormulas.Single(f => f.Code == c.EvidenceCode).Formula.Contains(c.Term, StringComparison.Ordinal)))
            throw new ArgumentException("Legacy內嵌神器衝突盤點不完整或已變動");
    }

    public LegacyInputSnapshot Create(ResearchBuild build, ProfessionProfile profile,
        IEnumerable<LegacyAttributeSource>? additionalInjections = null)
        => CreateCore(build, profile, additionalInjections, null);

    // Internal bridge accepts only a projection made by the TW effect adapter, not arbitrary Legacy deltas.
    internal LegacyInputSnapshot CreateFromTwArtifact(ResearchBuild build, ProfessionProfile profile, TwArtifactProjection projection)
        => CreateCore(build, profile, null, projection);

    private LegacyInputSnapshot CreateCore(ResearchBuild build, ProfessionProfile profile,
        IEnumerable<LegacyAttributeSource>? additionalInjections, TwArtifactProjection? projection)
    {
        if (build.ProfessionId != profile.Id || !profile.LegacyModes.ContainsKey(build.LegacyMode)
            || !config.FrontParamInfoArray.Single(f => f.Code == "KG_001").Options.Contains(build.LegacyMode))
            throw new ArgumentException("Build與流派／套路不符");
        var fronts = config.FrontParamInfoArray.ToDictionary(f => f.Code);
        if (build.Parameters.Keys.Except(fronts.Keys).Any())
            throw new ArgumentException("TW模式只能接收已盤點的Legacy前端欄位；不得注入內部公式或等效收益");
        var panel = (projection?.Panel ?? BuildAttributeLayer.Project(build, engine)).ToDictionary(r => r.Key);
        var fields = new List<LegacyFieldSnapshot>();
        var inputs = new Dictionary<string, LegacyInputValue>();
        var sources = new List<LegacyAttributeSource>();
        var rules = spec.Mappings.ToDictionary(m => m.LegacyCode);
        var disabled = spec.DisabledInputs.ToDictionary(d => d.Code);
        foreach (var front in config.FrontParamInfoArray.OrderBy(f => f.Code, StringComparer.Ordinal))
        {
            var original = build.Parameters.TryGetValue(front.Code, out var old) ? LegacyInputValue.From(old) : null;
            LegacyFieldSnapshot field;
            if (rules.TryGetValue(front.Code, out var rule))
            {
                var row = panel.GetValueOrDefault(rule.SourceKey) ?? new(rule.SourceKey, null, 0, null);
                var value = row.Final is decimal final ? new LegacyInputValue(true, (double)(final / rule.Divisor), null) : null;
                field = new(front.Code, rule.Label, rule.SourceKey, rule.SourceUnit, rule.LegacyUnit,
                    row.Base, row.Artifact, row.Final, value, original, "TWFinalStaticPanel",
                    value is null ? "基礎值未知；不沿用舊面板、不以0補值" : rule.DuplicateRisk
                        + (projection is null ? "" : $"；TW效果接線 {projection.PolicyId}／{projection.CoreRuleId}；條件增量另見projection追蹤"));
                if (value is not null) sources.Add(new(front.Code, "TWFinalStaticPanel", rule.SourceKey));
            }
            else if (disabled.TryGetValue(front.Code, out var off))
                field = new(front.Code, front.Name, null, "", "", null, null, null,
                    new(off.NumberMode, off.NumberValue, off.StringValue), original, "DisabledLegacyArtifact",
                    "停用舊神器控制；所有RF_SQ等效收益另行排除");
            else if (front.Code == "KG_001")
                field = new(front.Code, front.Name, null, "", "", null, null, null,
                    new(false, 0, build.LegacyMode), original, "BuildProfession", "由Build流派／套路控制");
            else
                field = new(front.Code, front.Name, null, "LegacyContext", "Legacy原單位", null, null, null,
                    original, original, "LegacyContextUnverified",
                    "原樣保留目標／內功／特質／裝備／占比等候選輸入；來源及台服適用性待驗證");
            fields.Add(field);
            if (field.Input is { } input)
            {
                if (input.NumberMode && !double.IsFinite(input.NumberValue)) throw new ArgumentException($"{front.Code}不是有限數值");
                inputs.Add(front.Code, input);
            }
        }
        sources.AddRange(additionalInjections ?? []);
        AssertNoDuplicateSources(sources);
        var mappedKeys = spec.Mappings.Select(m => m.SourceKey).ToHashSet();
        var omittedReasons = spec.Unmapped.ToDictionary(o => o.SourceKey, o => o.Reason);
        var tags = new[] { "all", "single", "group", "burst", "sustained" }.ToDictionary(t => t,
            t => panel.GetValueOrDefault($"skillEnhancement.{t}")?.Final);
        var nonStatic = new List<ArtifactContribution>();
        ArtifactPendingEffect[] unmodeled = [];
        if (build.ArtifactState is { } state)
        {
            unmodeled = new ArtifactAggregator(engine).Summarize(state, new() { Mode = ArtifactTriggerMode.Off }).UnmodeledMechanics.ToArray();
            foreach (var (id, level) in state.Levels.Where(x => x.Value > 0))
            {
                var node = engine.Node(id);
                var option = state.Selections.GetValueOrDefault(id, "");
                var record = node.Levels.Single(l => l.Level == level && l.OptionId == option);
                nonStatic.AddRange(record.Effects.Where(e => e.Scope != "panel").Select(e =>
                    new ArtifactContribution(id, option, e.Key, e.Amount, e.Unit, e.Scope, node.Status, record.Source)));
                unmodeled = unmodeled.Concat(node.SpecialEffects.Where(e => level >= e.MinimumLevel)
                    .Select(e => new ArtifactPendingEffect(id, e.Kind, "Phase2C僅靜態映射；此條件機制未注入"))).ToArray();
            }
        }
        var result = new LegacyInputSnapshot {
            AdapterId = spec.Id, ArtifactPackId = spec.ArtifactPackId, LegacyConfigSha256 = spec.LegacyConfigSha256,
            BuildId = build.BuildId, ProfessionId = build.ProfessionId, LegacyMode = build.LegacyMode, RotationId = build.RotationId,
            Inputs = inputs, Fields = fields.ToArray(), InjectionSources = sources.ToArray(),
            ExcludedArtifactEffects = nonStatic.ToArray(), UnmodeledMechanics = unmodeled,
            TaggedSkillEnhancement = tags,
            EffectiveTaggedSkillEnhancement = tags.Where(t => t.Key != "all").ToDictionary(t => t.Key, t => tags["all"] + t.Value),
            Omissions = panel.Values.Where(r => !mappedKeys.Contains(r.Key)).Select(r => new LegacyOmission(r.Key, r.Final,
                r.Artifact, omittedReasons.GetValueOrDefault(r.Key, "Legacy無對應輸入；保留，不套入DPS"))).ToArray(),
            MissingInputs = fronts.Keys.Except(inputs.Keys).Order().ToArray(), ExcludedResultCodes = spec.ExcludedResultCodes.ToArray(),
            EmbeddedFormulaConflicts = spec.EmbeddedFormulaConflicts.ToArray(), EngineEvaluationAllowed = false,
            Warnings = ["僅映射輸入，未呼叫Legacy engine；不是已校準DPS。",
                "基礎面板契約：不得包含本盤神器；數值本身無法辨識未申報的重複增量。",
                "ST_015只吃全技能；單體／群體／爆發／持續增強保存為標籤，暫不降階成全技能。",
                projection is null ? "TriggeredAttributes／ScopedEffects／UnmodeledMechanics／目標流派限定效果全部未接入。"
                    : "Phase2D-A：面板含新盤明確條件平均與CORE候選首克；指定技能／Rotation／PvP仍獨立保留。",
                "LegacyContext含舊版內功、藥品、裝備、特質及技能占比；不能視為台服已驗證。",
                projection is null ? "FZJ_010內嵌舊神器流派5%未能由前端停用；TW預測執行禁止，SH_001原樣保留。"
                    : "原設定FZJ_010衝突保留作歷史證據；只可交由TW獨立overlay執行，禁止交回原設定。",
                "attack必須由使用者提供代表攻擊值；不推定最小／最大攻擊的平均或其抽樣分布。"]
        };
        Validate(result);
        return result;
    }

    // Single injection is the already-projected final panel, never a second artifact delta.
    public void AssertNoDuplicateSources(IEnumerable<LegacyAttributeSource> injections)
    {
        var rows = injections.ToArray();
        if (rows.GroupBy(r => r.LegacyCode).Any(g => g.Count() > 1))
            throw new InvalidOperationException("duplicate-attribute-source：同一欄位有多個注入來源");
        if (rows.Any(r => r.SourceKind != "TWFinalStaticPanel" || !spec.Mappings.Any(m => m.LegacyCode == r.LegacyCode && m.SourceKey == r.SourceId)))
            throw new InvalidOperationException("unsupported-attribute-source：TW模式禁止舊神器、等效收益與條件效果注入");
    }

    public void Validate(LegacyInputSnapshot snapshot)
    {
        if (snapshot.AdapterId != spec.Id || snapshot.LegacyConfigSha256 != spec.LegacyConfigSha256 || snapshot.ArtifactPackId != spec.ArtifactPackId)
            throw new ArgumentException("快照版本不符");
        AssertNoDuplicateSources(snapshot.InjectionSources);
        var fronts = config.FrontParamInfoArray.Select(f => f.Code).ToHashSet();
        if (snapshot.Inputs.Keys.Except(fronts).Any() || snapshot.Fields.Select(f => f.LegacyCode).Distinct().Count() != snapshot.Fields.Length
            || !snapshot.Fields.Select(f => f.LegacyCode).ToHashSet().SetEquals(fronts))
            throw new ArgumentException("快照含非前端／重複／缺少的來源欄位");
        foreach (var field in snapshot.Fields)
        {
            snapshot.Inputs.TryGetValue(field.LegacyCode, out var actual);
            if (actual != field.Input) throw new InvalidOperationException($"快照輸入遭額外注入／變更：{field.LegacyCode}");
            if (actual?.NumberMode == true && !double.IsFinite(actual.NumberValue)) throw new ArgumentException("快照數值無效");
        }
        foreach (var rule in spec.Mappings)
        {
            var field = snapshot.Fields.Single(f => f.LegacyCode == rule.LegacyCode);
            var expected = field.Final is decimal final ? new LegacyInputValue(true, (double)(final / rule.Divisor), null) : null;
            if (field.SourceKind != "TWFinalStaticPanel" || field.SourceKey != rule.SourceKey || field.Final != field.Base + field.Artifact
                || field.Input != expected || field.SourceUnit != rule.SourceUnit || field.LegacyUnit != rule.LegacyUnit)
                throw new InvalidOperationException($"最終靜態面板來源不一致：{rule.LegacyCode}");
            var source = snapshot.InjectionSources.SingleOrDefault(s => s.LegacyCode == rule.LegacyCode);
            if ((expected is null) != (source is null)) throw new InvalidOperationException("缺少／多餘的注入來源");
        }
        foreach (var off in spec.DisabledInputs)
            if (snapshot.Inputs.GetValueOrDefault(off.Code) != new LegacyInputValue(off.NumberMode, off.NumberValue, off.StringValue))
                throw new InvalidOperationException("duplicate-legacy-artifact：TW模式舊神器控制未停用");
        if (snapshot.Inputs.GetValueOrDefault("KG_001") != new LegacyInputValue(false, 0, snapshot.LegacyMode)
            || !snapshot.ExcludedResultCodes.Order().SequenceEqual(spec.ExcludedResultCodes.Order())
            || snapshot.EngineEvaluationAllowed
            || JsonSerializer.Serialize(snapshot.EmbeddedFormulaConflicts) != JsonSerializer.Serialize(spec.EmbeddedFormulaConflicts)
            || !snapshot.MissingInputs.Order().SequenceEqual(fronts.Except(snapshot.Inputs.Keys).Order()))
            throw new ArgumentException("快照流派、缺值或舊神器輸出排除清單不一致");
    }

    public void ValidatePredictionHandoff(LegacyInputSnapshot snapshot)
    {
        Validate(snapshot);
        throw new InvalidOperationException("TW預測執行禁止：Phase2C僅輸出候選輸入；Legacy內嵌神器公式尚未隔離，標籤及條件效果未建模，DPS未校準");
    }

    public Dictionary<string, ParamValue> ExportMappedInputs(LegacyInputSnapshot snapshot)
    {
        Validate(snapshot);
        // May deliberately be incomplete. This is an input export, not a prediction API.
        return snapshot.Inputs.ToDictionary(x => x.Key, x => x.Value.ToParamValue());
    }

    public LegacyInputPair Compare(ResearchBuild a, ProfessionProfile pa, ResearchBuild b, ProfessionProfile pb, bool allowCrossProfession = false)
    {
        var cross = a.ProfessionId != b.ProfessionId;
        if (cross && !allowCrossProfession) throw new ArgumentException("預設只比較同流派映射輸入");
        var sa = Create(a, pa); var sb = Create(b, pb);
        var fb = sb.Fields.ToDictionary(f => f.LegacyCode);
        return new(sa, sb, sa.Fields.Select(f => new LegacyInputDifference(f.LegacyCode, f, fb[f.LegacyCode],
            f.Input?.NumberMode == true && fb[f.LegacyCode].Input?.NumberMode == true
                ? fb[f.LegacyCode].Input!.NumberValue - f.Input.NumberValue : null)).ToArray(), cross,
            cross ? PveComparison.CrossProfessionWarning : null);
    }
}
