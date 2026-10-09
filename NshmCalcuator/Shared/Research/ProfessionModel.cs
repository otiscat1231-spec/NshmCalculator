using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;

namespace NshmCalculator.Shared.Research;

public sealed class ProfessionProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string> LegacyModes { get; set; } = new();
    public Dictionary<string, ParamValue> ParameterOverrides { get; set; } = new();
    public Dictionary<string, double> Coefficients { get; set; } = new();
    public List<SkillDefinition> Skills { get; set; } = new();
    public Dictionary<string, double> SkillShares { get; set; } = new();
    public List<string> Artifacts { get; set; } = new();
    public List<string> Traits { get; set; } = new();
    public List<RotationDefinition> Rotations { get; set; } = new();
    public List<string> GoldenDatasetIds { get; set; } = new();
    public string ValidationStatus { get; set; } = "待驗證";
}
public sealed record SkillDefinition(string Id, string Name, string[] Tags, string Status);
public sealed record RotationDefinition(string Id, string Name, string[] SkillIds, string Stance);
public sealed class PveDataPack
{
    public string Id { get; set; } = "";
    public string Server { get; set; } = "";
    public string Platform { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string DataVersion { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string TargetDataVersion { get; set; } = "";
    public string CalibrationVersion { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class ResearchManifest
{
    public string DefaultProfessionId { get; set; } = "";
    public string MasterSheetUrl { get; set; } = "";
    public List<ProfessionProfile> Professions { get; set; } = new();
    public List<PveDataPack> DataPacks { get; set; } = new();
}
public sealed class ResearchBuild
{
    public string BuildId { get; set; } = "";
    public string ProfessionId { get; set; } = "";
    public string LegacyMode { get; set; } = "";
    public string RotationId { get; set; } = "";
    public Dictionary<string, ParamValue> Parameters { get; set; } = new();
    // Store the base only. Final panels are projections and must never be written back here.
    public Dictionary<string, decimal> BaseAttributes { get; set; } = new();
    public Artifacts.ArtifactBoardState? ArtifactState { get; set; }
    public Dictionary<string, int> ArtifactAutoLevels { get; set; } = new();
}
// Build and target belong to the calibration identity; missing data never falls back to another profession.
public sealed record CalibrationKey(string Server, string Platform, string GameVersion,
    string DataVersion, string ModelVersion, string TargetDataVersion, string CalibrationVersion,
    string ProfessionId, string BaselineBuildId, string TargetId, string RotationId);
public sealed record CalibrationRecord(CalibrationKey Key, double BaselineDps, double Lambda,
    string[] TrainingTestIds, string[] HoldoutTestIds, bool ExternallyValidated);
public sealed record GoldenDataset(string Id, string ProfessionId, string Server, string GameVersion,
    string[] TestIds, string[] TrainingTestIds, string[] HoldoutTestIds, string Purpose);
