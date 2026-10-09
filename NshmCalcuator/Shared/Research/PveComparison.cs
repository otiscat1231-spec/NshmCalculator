using System.Text.Json;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;

namespace NshmCalculator.Shared.Research;

public sealed record ComparisonResult(double ModelA, double ModelB, double? Ratio,
    bool CrossProfession, string? Warning, double? DpsA, double? DpsB);

public static class PveComparison
{
    internal static readonly object EngineGate = new();
    public const string CrossProfessionWarning = "跨流派比較，需各自完成模型校準";

    public static Dictionary<string, ParamValue> CopyParameters(Dictionary<string, ParamValue> source) =>
        source.ToDictionary(x => x.Key, x => new ParamValue {
            NumberMode = x.Value.NumberMode, NumberValue = x.Value.NumberValue,
            StringValue = x.Value.StringValue });

    public static ResearchBuild CreateLegacyExample(PveConfig config, ProfessionProfile profile, string mode)
    {
        if (!profile.LegacyModes.ContainsKey(mode)) throw new ArgumentException("流派套路不符");
        var parameters = CopyParameters(config.DefaultParamValues);
        foreach (var (code, value) in profile.ParameterOverrides)
            parameters[code] = new ParamValue { NumberMode = value.NumberMode,
                NumberValue = value.NumberValue, StringValue = value.StringValue };
        parameters["KG_001"] = new ParamValue { NumberMode = false, StringValue = mode };
        return new ResearchBuild { BuildId = $"EXAMPLE-{profile.Id}", ProfessionId = profile.Id,
            LegacyMode = mode, RotationId = "legacy-unverified", Parameters = parameters };
    }

    public static double Evaluate(string configJson, ResearchBuild build, ProfessionProfile profile)
    {
        if (build.ProfessionId != profile.Id || !profile.LegacyModes.ContainsKey(build.LegacyMode))
            throw new ArgumentException("Build與流派／套路不符");
        var config = JsonSerializer.Deserialize<PveConfig>(configJson) ?? throw new ArgumentException("資料包無效");
        var values = CopyParameters(build.Parameters);
        var missing = config.DefaultParamValues.Keys.Except(values.Keys).ToArray();
        if (missing.Length > 0) throw new ArgumentException($"缺少模型輸入：{string.Join(", ", missing)}");
        if (!config.FrontParamInfoArray.Single(p => p.Code == "KG_001").Options.Contains(build.LegacyMode))
            throw new ArgumentException("這個資料包未支援所選流派");
        values["KG_001"] = new ParamValue { NumberMode = false, StringValue = build.LegacyMode };
        // The upstream engine uses static mutable dictionaries. Keep A/B evaluation serial.
        lock (EngineGate)
        {
            PveUtility.InitUtilityFromConfig(config);
            var result = PveUtility.Calculate(new List<string> { "FBL_06" }, values)["FBL_06"];
            if (result is not double value || !double.IsFinite(value) || value <= 0)
                throw new InvalidOperationException("模型值無效，請檢查完整面板與進階設定");
            return value;
        }
    }

    public static CalibrationRecord? FindCalibration(IEnumerable<CalibrationRecord> records, CalibrationKey key) =>
        records.SingleOrDefault(r => r.Key == key);

    public static double? PredictDps(double relativeValue, CalibrationKey key, CalibrationRecord? record)
    {
        if (record is null || record.Key != key || !record.ExternallyValidated) return null;
        if (!double.IsFinite(relativeValue) || relativeValue <= 0 || !double.IsFinite(record.Lambda)
            || record.Lambda <= 0 || !double.IsFinite(record.BaselineDps) || record.BaselineDps <= 0)
            throw new ArgumentException("校準數值無效");
        return record.BaselineDps * Math.Pow(relativeValue, record.Lambda);
    }

    public static ComparisonResult Compare(string configJson, ResearchBuild a, ProfessionProfile pa,
        ResearchBuild b, ProfessionProfile pb, bool allowCrossProfession = false)
    {
        var cross = a.ProfessionId != b.ProfessionId;
        if (cross && !allowCrossProfession) throw new ArgumentException("預設只比較同一流派");
        var va = Evaluate(configJson, a, pa);
        var vb = Evaluate(configJson, b, pb);
        // Raw per-coefficient model values have no calibrated common DPS scale across professions.
        return new(va, vb, cross ? null : vb / va, cross,
            cross ? CrossProfessionWarning : null, null, null);
    }

    public static string StorageKey(PveDataPack pack, string professionId) =>
        $"tw-pve:{pack.Id}:{pack.GameVersion}:{pack.DataVersion}:{pack.ModelVersion}:{professionId}:ab:v1";
}
