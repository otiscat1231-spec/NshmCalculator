using System.Text.Json;
using NUnit.Framework;
using NshmCalculator.Shared;
using NshmCalculator.Shared.Research;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;

namespace NshmCalculator.Test.Research;

[TestFixture, NonParallelizable]
public class PveResearchTest
{
    private string Config => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "data/config_pve.json"));
    private static ProfessionProfile Profile(string id, string mode) => new() { Id = id, LegacyModes = new() { [mode] = mode } };

    [Test]
    public void LegacyAllOutputsRemainIdentical()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata/pve_legacy_golden.json")));
        foreach (var sample in fixture.RootElement.GetProperty("Cases").EnumerateArray())
        {
            var config = JsonSerializer.Deserialize<PveConfig>(Config)!;
            var inputs = sample.GetProperty("Inputs").Deserialize<Dictionary<string, ParamValue>>()!;
            var expected = sample.GetProperty("Expected");
            PveUtility.InitUtilityFromConfig(config);
            var actual = PveUtility.Calculate(expected.EnumerateObject().Select(p => p.Name).ToList(), inputs);
            foreach (var value in expected.EnumerateObject())
            {
                if (value.Value.ValueKind == JsonValueKind.Number)
                    Assert.That(Convert.ToDouble(actual[value.Name]), Is.EqualTo(value.Value.GetDouble()).Within(Math.Max(1e-8, Math.Abs(value.Value.GetDouble()) * 1e-12)), value.Name);
                else Assert.That(JsonSerializer.Serialize(actual[value.Name]), Is.EqualTo(value.Value.GetRawText()), value.Name);
            }
        }
    }

    [Test]
    public void ProfessionIsolationAndCrossComparison()
    {
        var config = JsonSerializer.Deserialize<PveConfig>(Config)!;
        var ly = Profile("LY", "龙吟"); var ty = Profile("TY", "破铁衣");
        var a = PveComparison.CreateLegacyExample(config, ly, "龙吟");
        var b = PveComparison.CreateLegacyExample(config, ly, "龙吟");
        b.Parameters["ST_001"].NumberValue += 480;
        Assert.That(a.Parameters["ST_001"].NumberValue, Is.EqualTo(config.DefaultParamValues["ST_001"].NumberValue));
        Assert.That(PveComparison.Compare(Config, a, ly, a, ly).Ratio, Is.EqualTo(1));
        var c = PveComparison.CreateLegacyExample(config, ty, "破铁衣");
        Assert.Throws<ArgumentException>(() => PveComparison.Compare(Config, a, ly, c, ty));
        var cross = PveComparison.Compare(Config, a, ly, c, ty, true);
        Assert.That(cross.Warning, Is.EqualTo("跨流派比較，需各自完成模型校準"));
        Assert.That(cross.Ratio, Is.Null); Assert.That(cross.DpsB, Is.Null);
        Assert.Throws<ArgumentException>(() => PveComparison.Evaluate(Config, a, ty));
    }

    [Test]
    public void CalibrationRequiresExactProfessionAndValidation()
    {
        var key = new CalibrationKey("TW", "mobile", "unknown", "d1", "m1", "t1", "c1", "TY", "B-TY", "normal", "r1");
        var record = new CalibrationRecord(key, 100000, 1, new[] { "T1" }, new[] { "T2" }, true);
        Assert.That(PveComparison.PredictDps(1.1, key, record), Is.EqualTo(110000).Within(1e-8));
        Assert.That(PveComparison.PredictDps(1.1, key with { ProfessionId = "LY" }, record), Is.Null);
        Assert.That(PveComparison.PredictDps(1.1, key with { TargetId = "heroic" }, record), Is.Null);
        Assert.That(PveComparison.PredictDps(1.1, key, record with { ExternallyValidated = false }), Is.Null);
        Assert.That(PveComparison.FindCalibration(new[] { record }, key with { RotationId = "other" }), Is.Null);
    }

    [Test]
    public void ManifestSeparatesDataVersionsAndFutureProfession()
    {
        var manifest = JsonSerializer.Deserialize<ResearchManifest>(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "data/pve/manifest.json")))!;
        Assert.That(manifest.DefaultProfessionId, Is.EqualTo("LY"));
        var pack = manifest.DataPacks[0];
        Assert.That(PveComparison.StorageKey(pack, "LY"), Is.Not.EqualTo(PveComparison.StorageKey(pack, "TY")));
        Assert.That(PveComparison.StorageKey(pack, "LY"), Is.Not.EqualTo(PveComparison.StorageKey(manifest.DataPacks[1], "LY")));
        Assert.That(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "data/pve/legacy/config_pve_original.json")), Is.EqualTo(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "data/config_pve.json"))));
        var future = Profile("XJ", "玄机");
        Assert.That(PveComparison.Evaluate(Config, PveComparison.CreateLegacyExample(JsonSerializer.Deserialize<PveConfig>(Config)!, future, "玄机"), future), Is.GreaterThan(0));
    }
}
