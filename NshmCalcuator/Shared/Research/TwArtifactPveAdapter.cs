using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
using NshmCalculator.Shared.Models.CalculatorModel.Enums;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Formula;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.SpecialRule;
using NshmCalculator.Shared.Research.Artifacts;

namespace NshmCalculator.Shared.Research;

public sealed record TwLegacyIsolationProof(string Id, string[] RemovedFrontInputs, string[] RemovedInternalFormulas,
    string[] RemovedResultFormulas, string ReplacedFormula, string OriginalFormula, string TwFormula, string CoefficientSource);
public sealed class TwArtifactPreparedInputs
{
    public TwArtifactProjection Artifact { get; init; } = null!;
    public LegacyInputSnapshot Mapping { get; init; } = null!;
    public Dictionary<string, LegacyInputValue> Inputs { get; init; } = new();
    public TwLegacyIsolationProof Isolation { get; init; } = null!;
    public string[] Missing { get; init; } = [];
    public string Status { get; init; } = "TW神器來源已隔離；局部候選模型，未校準DPS";
}
public sealed record TwArtifactCandidateResult(double ModelValue, string Status, string[] Limitations, string PolicyId, string CoreRuleId);

/// <summary>Detached TW config overlay. Original config, PveUtility, and Legacy mode remain intact.</summary>
public sealed class TwArtifactPveAdapter
{
    private readonly string overlayJson;
    private readonly LegacyStaticInputAdapter mapper;
    private readonly TwArtifactEffectAdapter effects;
    private readonly TwLegacyIsolationProof proof;
    private static bool OldArtifactCode(string code) => code.StartsWith("SQ_", StringComparison.Ordinal)
        || code.StartsWith("RF_SQ_", StringComparison.Ordinal) || code.StartsWith("FSQ_", StringComparison.Ordinal);

    public TwArtifactPveAdapter(string legacyJson, LegacyStaticInputAdapter mapper, TwArtifactEffectAdapter effects)
    {
        this.mapper = mapper; this.effects = effects;
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson))).ToLowerInvariant() != mapper.OriginalConfigSha256)
            throw new ArgumentException("TW overlay與映射層的Legacy來源不一致");
        var config = JsonSerializer.Deserialize<PveConfig>(legacyJson) ?? throw new ArgumentException("Legacy設定為空");
        // Fail closed against an unreviewed formula source; the mapper checks the original file hash too.
        var formula = config.InternalFormulas.Single(f => f.Code == "FZJ_010");
        const string oldTerm = "0.05*[SH_001]";
        if (formula.Formula.Split(oldTerm).Length != 2) throw new ArgumentException("舊流派神器5%來源已變動，须重新盤點");
        var original = formula.Formula;
        var removedFront = config.FrontParamInfoArray.Where(f => OldArtifactCode(f.Code) || f.GroupName == "神器").Select(f => f.Code).ToArray();
        var removedInternal = config.InternalFormulas.Where(f => OldArtifactCode(f.Code)).Select(f => f.Code).ToArray();
        var removedResults = config.ResultFormulas.Where(f => OldArtifactCode(f.Code)).Select(f => f.Code).ToArray();
        config.FrontParamInfoArray = config.FrontParamInfoArray.Where(f => !removedFront.Contains(f.Code)).ToArray();
        config.DefaultParamValues = config.DefaultParamValues.Where(v => !removedFront.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
        config.InternalFormulas = config.InternalFormulas.Where(f => !OldArtifactCode(f.Code)).ToArray();
        config.ResultFormulas = config.ResultFormulas.Where(f => !OldArtifactCode(f.Code)).ToArray();
        config.ResultGroups = []; // no old artifact UI/output references in this execution-only overlay
        formula.Formula = formula.Formula.Replace(oldTerm, $"[{effects.ProfessionDamageInput}]*[SH_001]", StringComparison.Ordinal);
        formula.FormulaParam = formula.FormulaParam.Append(effects.ProfessionDamageInput).Distinct().ToArray();
        config.FrontParamInfoArray = config.FrontParamInfoArray.Append(new FrontParamInfo {
            Code = effects.ProfessionDamageInput, Name = "TW新盤流派技能增傷係數", Mode = ParamMode.Percent }).ToArray();
        config.DefaultParamValues[effects.ProfessionDamageInput] = new() { NumberMode = true, NumberValue = 0 };
        foreach (var f in config.InternalFormulas.Concat(config.ResultFormulas))
        {
            var dependencies = Regex.Matches(f.Formula, @"\b(?:SQ_|RF_SQ_|FSQ_)[A-Za-z0-9_]*").Select(m => m.Value)
                .Concat(f.FormulaParam).Concat((f.Rule ?? []).SelectMany(r => r.LambdaParam ?? []));
            if (dependencies.Any(OldArtifactCode)) throw new ArgumentException($"TW overlay仍有舊神器引用：{f.Code}");
        }
        proof = new("TW-Mobile-2.3.3-legacy-isolation-v1", removedFront, removedInternal, removedResults,
            "FZJ_010", original, formula.Formula, $"{effects.PolicyId}:professionSkill.damageIncrease；使用新盤M07實際等級，保留SH_001技能占比");
        overlayJson = JsonSerializer.Serialize(config);
    }

    public PveConfig InspectIsolatedConfig() => JsonSerializer.Deserialize<PveConfig>(overlayJson)!;
    public (TwArtifactPreparedInputs A, TwArtifactPreparedInputs B, LegacyInputPair Mapping) CompareInputs(
        ResearchBuild a, ProfessionProfile pa, ResearchBuild b, ProfessionProfile pb)
    {
        if (a.ProfessionId != b.ProfessionId) throw new ArgumentException(PveComparison.CrossProfessionWarning);
        var sa = Prepare(a, pa); var sb = Prepare(b, pb);
        var fields = sb.Mapping.Fields.ToDictionary(f => f.LegacyCode);
        var rows = sa.Mapping.Fields.Select(f => {
            var other = fields[f.LegacyCode];
            double? delta = f.Input?.NumberMode == true && other.Input?.NumberMode == true
                ? other.Input.NumberValue - f.Input.NumberValue : null;
            return new LegacyInputDifference(f.LegacyCode, f, other, delta);
        }).ToArray();
        return (sa, sb, new(sa.Mapping, sb.Mapping, rows, false, null));
    }
    public TwArtifactPreparedInputs Prepare(ResearchBuild build, ProfessionProfile profile, TwArtifactEffectContext? context = null)
    {
        var projection = effects.Project(build, context);
        var mapping = mapper.CreateFromTwArtifact(build, profile, projection);
        var inputs = mapping.Inputs.Where(v => !OldArtifactCode(v.Key)).ToDictionary(v => v.Key, v => v.Value);
        if (projection.ProfessionDamageRatio is { } ratio) inputs.Add(effects.ProfessionDamageInput, new(true, (double)ratio, null));
        var missing = InspectIsolatedConfig().FrontParamInfoArray.Select(f => f.Code).Except(inputs.Keys)
            .Concat(projection.Pending).Distinct().ToArray();
        return new() { Artifact = projection, Mapping = mapping, Inputs = inputs, Isolation = proof, Missing = missing };
    }

    private Dictionary<string, ParamValue> ExecutableInputs(TwArtifactPreparedInputs prepared)
    {
        mapper.Validate(prepared.Mapping);
        if (prepared.Missing.Length > 0) throw new ArgumentException($"缺少候選模型輸入／情境：{string.Join("、", prepared.Missing)}");
        var expected = prepared.Mapping.Inputs.Where(v => !OldArtifactCode(v.Key)).ToDictionary(v => v.Key, v => v.Value);
        expected.Add(effects.ProfessionDamageInput, new(true, (double)prepared.Artifact.ProfessionDamageRatio!.Value, null));
        if (prepared.Inputs.Count != expected.Count || expected.Any(v => prepared.Inputs.GetValueOrDefault(v.Key) != v.Value)
            || prepared.Inputs.Keys.Any(OldArtifactCode)) throw new InvalidOperationException("TW輸入含重複／舊神器來源或與新盤projection不一致");
        return prepared.Inputs.ToDictionary(v => v.Key, v => v.Value.ToParamValue());
    }

    private static Dictionary<string, double?> Run(PveConfig config, Dictionary<string, ParamValue> inputs, params string[] requested)
    {
        // Same gate as independent Legacy comparison. Every call reinitializes its own config.
        lock (PveComparison.EngineGate)
        {
            PveUtility.InitUtilityFromConfig(config);
            return PveUtility.Calculate(requested.ToList(), inputs);
        }
    }
    public TwArtifactCandidateResult EvaluateCandidate(ResearchBuild build, ProfessionProfile profile, TwArtifactEffectContext? context = null)
    {
        var prepared = Prepare(build, profile, context);
        var value = Run(InspectIsolatedConfig(), ExecutableInputs(prepared), "FBL_06")["FBL_06"];
        if (value is not double numeric || !double.IsFinite(numeric) || numeric <= 0) throw new InvalidOperationException("局部候選模型值無效");
        return new(numeric, "僅靜態／明確平均屬性與破空·威霆的局部候選模型值，不是DPS",
            prepared.Artifact.ModelLimitations, prepared.Artifact.PolicyId, prepared.Artifact.CoreRuleId);
    }

    // Reuses original FHX lambdas. No old RF_SQ node amount/level/options or five-dimension conversion exposed.
    public double EquivalentPanelGain(ResearchBuild build, ProfessionProfile profile, string attributeKey, decimal amount,
        bool alreadyIncluded, TwArtifactEffectContext? context = null)
    {
        if (!effects.EquivalentFunctions.TryGetValue(attributeKey, out var function))
            throw new ArgumentException("只允許已盤點的面板等效收益；五維／攻速／CD／技能分支不得作通用轉換");
        var prepared = Prepare(build, profile, context);
        var inputs = ExecutableInputs(prepared);
        var config = InspectIsolatedConfig();
        var source = prepared.Mapping.Fields.Single(f => f.SourceKey == attributeKey);
        var converted = amount / (source.SourceUnit == "percentagePoint" ? 100 : 1);
        if (attributeKey == "skillEnhancement.all") converted *= (decimal)inputs["SH_005"].NumberValue;
        config.FrontParamInfoArray = config.FrontParamInfoArray.Concat(new[] {
            new FrontParamInfo { Code = "TW_EQ_AMOUNT", Name = "TW面板情境增量", Mode = ParamMode.Number },
            new FrontParamInfo { Code = "TW_EQ_PRESENT", Name = "TW面板增量是否已含", Mode = ParamMode.Number } }).ToArray();
        inputs.Add("TW_EQ_AMOUNT", new() { NumberMode = true, NumberValue = (double)converted });
        inputs.Add("TW_EQ_PRESENT", new() { NumberMode = true, NumberValue = alreadyIncluded ? 1 : 0 });
        config.ResultFormulas = config.ResultFormulas.Append(new PveFormula {
            Code = "TW_EQ_RESULT", Name = "TW情境等效收益", Formula = $"{function}([TW_EQ_AMOUNT],[TW_EQ_PRESENT])",
            FormulaParam = ["TW_EQ_AMOUNT", "TW_EQ_PRESENT"], Rule = [new SpecialFormulaRule { Mode = FormulaMode.LinkLambda, LambdaParam = [function] }] }).ToArray();
        var result = Run(config, inputs, "TW_EQ_RESULT")["TW_EQ_RESULT"];
        if (result is not double value || !double.IsFinite(value)) throw new InvalidOperationException("等效收益無效");
        return value;
    }

    // A verified state distribution can be evaluated without the nonlinear D(mean attribute) shortcut.
    // One node at a time; no unverified independence assumption between multiple conditional nodes.
    public double InteractionStateExpectation(ResearchBuild build, ProfessionProfile profile, string nodeId,
        ArtifactStackState[] states, TwArtifactEffectContext? context = null)
    {
        context ??= new();
        var values = new List<(decimal Probability, double Value)>();
        if (states.Length == 0 || states.Sum(s => s.Probability) != 1 || states.Any(s => s.Probability < 0))
            throw new ArgumentException("需完整、明確的層數狀態機率");
        // Ensure this is an active interaction node, not an arbitrary source of extra panel values.
        var projected = effects.Project(build, context);
        if (!projected.Effects.Any(e => e.NodeId == nodeId && e.Key == "interactionStacks"))
            throw new ArgumentException("節點不是已啟用的互動層數效果");
        foreach (var state in states)
        {
            var controls = new Dictionary<string, TwArtifactAverageControl>(context.Controls) {
                [nodeId] = new() { Mode = ArtifactTriggerMode.Manual, ManualMeanStacks = state.Stacks } };
            var result = EvaluateCandidate(build, profile, new() { DefaultMode = context.DefaultMode, Controls = controls });
            values.Add((state.Probability, result.ModelValue));
        }
        return TwArtifactAveraging.ConditionalExpectation(values);
    }
}
