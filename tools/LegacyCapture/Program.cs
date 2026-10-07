using System.Text.Json;
using NshmCalculator.Shared;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;

var configPath = args[0];
var output = args[1];
var json = File.ReadAllText(configPath);
var cases = new List<object>();
foreach (var occupation in new[] { "龙吟", "破铁衣", "御铁衣", "玄机" })
foreach (var delta in new[] { 0, 480 })
{
    var config = JsonSerializer.Deserialize<PveConfig>(json)!;
    var inputs = config.DefaultParamValues;
    inputs["KG_001"] = new ParamValue { NumberMode = false, StringValue = occupation };
    inputs["ST_001"].NumberValue += delta;
    PveUtility.InitUtilityFromConfig(config);
    var results = PveUtility.Calculate(config.ResultFormulas.Select(f => f.Code).ToList(), inputs);
    cases.Add(new { Name = $"{occupation}-attack-{delta}", Inputs = inputs, Expected = results });
}
File.WriteAllText(output, JsonSerializer.Serialize(new {
    UpstreamCommit = "d831d871f3d05894afd645da640a5ce17051316c",
    Purpose = "Legacy code-output regression; not Taiwan empirical validation",
    Cases = cases
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Captured {cases.Count} cases, all original result formulas.");
