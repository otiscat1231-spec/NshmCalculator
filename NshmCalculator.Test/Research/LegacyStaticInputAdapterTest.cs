using System.Text.Json;
using NshmCalculator.Shared;
using NshmCalculator.Shared.Research;
using NshmCalculator.Shared.Research.Artifacts;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;

namespace NshmCalculator.Test.Research;

[TestFixture, NonParallelizable]
public class LegacyStaticInputAdapterTest
{
    private string configJson = "", mappingJson = "";
    private PveConfig config = null!;
    private ArtifactBoardEngine engine = null!;
    private LegacyStaticInputAdapter adapter = null!;
    private static ProfessionProfile Profile(string id = "LY", string mode = "龙吟") => new() { Id = id, LegacyModes = new() { [mode] = mode } };
    private static string Encode(object o) => JsonSerializer.Serialize(o);
    private string Id(string position) => engine.Pack.AllNodes.Single(n => n.PositionId == position).Id;
    private string Data(string path) => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "data", path));
    [SetUp] public void Setup()
    {
        configJson = Data("config_pve.json"); config = JsonSerializer.Deserialize<PveConfig>(configJson)!;
        mappingJson = Data("pve/adapters/tw-static-v1.json");
        engine = new(ArtifactDataPack.Load(Data("artifacts/TW-Mobile-2.3.3/artifact-pack.json")));
        adapter = new(configJson, mappingJson, engine);
    }
    // Synthetic data only, deliberately differs from Legacy defaults and from user measurements.
    private ResearchBuild Build(string id = "LY", string mode = "龙吟")
    {
        var build = PveComparison.CreateLegacyExample(config, Profile(id, mode), mode);
        build.BuildId = $"TEST-SYNTHETIC-{id}";
        foreach (var m in adapter.Mappings) build.BaseAttributes[m.SourceKey] = 1000;
        build.BaseAttributes["attack"] = 10000;
        build.BaseAttributes["criticalDamage"] = 175;
        build.BaseAttributes["bossSuppressionPercent"] = 5;
        build.BaseAttributes["skillEnhancement.all"] = 5600;
        foreach (var tag in new[] { "single", "group", "burst", "sustained" }) build.BaseAttributes[$"skillEnhancement.{tag}"] = 0;
        if (engine.Pack.ProfessionOverrides.ContainsKey(id)) new ArtifactBoardSession(build, engine);
        return build;
    }
    private LegacyInputSnapshot Snapshot(ResearchBuild b) => adapter.Create(b, Profile(b.ProfessionId, b.LegacyMode));

    [TestCase("attack", "ST_001", 1)]
    [TestCase("bossSuppression", "ST_002", 1)]
    [TestCase("technicalSuppression", "ST_003", 1)]
    [TestCase("defensePenetration", "ST_004", 1)]
    [TestCase("elementAttack", "ST_005", 1)]
    [TestCase("ignoreElementResistance", "ST_006", 1)]
    [TestCase("critical", "ST_007", 1)]
    [TestCase("hit", "ST_008", 1)]
    [TestCase("criticalDamage", "ST_009", 100)]
    [TestCase("bossSuppressionPercent", "ST_010", 100)]
    [TestCase("skillEnhancement.all", "ST_015", 1)]
    public void EveryMappingUsesFinalPanelWithExplicitUnit(string key, string code, int divisor)
    {
        var build = Build(); build.BaseAttributes[key] = 123.45m;
        var snap = Snapshot(build); var field = snap.Fields.Single(f => f.LegacyCode == code);
        Assert.That(snap.Inputs[code].NumberValue, Is.EqualTo(123.45 / divisor).Within(1e-12));
        Assert.That(field.SourceKey, Is.EqualTo(key)); Assert.That(field.Base, Is.EqualTo(123.45m));
        Assert.That(field.Artifact, Is.Zero); Assert.That(field.Final, Is.EqualTo(field.Base));
        Assert.That(snap.InjectionSources.Count(s => s.LegacyCode == code), Is.EqualTo(1));
        Assert.That(snap.HasCompleteInputSet, Is.True);
    }

    [Test] public void StaticSignedIncrementIsAppliedOnceAcrossRepeatedProjectionAndReload()
    {
        var b = Build(); var board = new ArtifactBoardSession(b, engine); board.SetLevel(Id("BLUE-N02"), 10);
        var before = Encode(b); var s = Snapshot(b);
        Assert.That(s.Inputs["ST_008"].NumberValue, Is.EqualTo(600));
        Assert.That(s.Inputs["ST_015"].NumberValue, Is.EqualTo(8100));
        for (var i = 0; i < 5; i++) Assert.That(Encode(Snapshot(b)), Is.EqualTo(Encode(s)));
        Assert.That(Encode(b), Is.EqualTo(before));
        var restored = JsonSerializer.Deserialize<ResearchBuild>(before)!;
        Assert.That(Encode(Snapshot(restored)), Is.EqualTo(Encode(s)));
        var export = adapter.ExportMappedInputs(s); export["ST_008"].NumberValue += 777;
        Assert.That(s.Inputs["ST_008"].NumberValue, Is.EqualTo(600));
        Assert.That(b.Parameters["ST_008"].NumberValue, Is.EqualTo(config.DefaultParamValues["ST_008"].NumberValue));
    }

    [Test] public void UnknownBaseNeverFallsBackToLegacyExampleEvenWhenArtifactIncrementKnown()
    {
        var b = Build(); b.BaseAttributes.Remove("hit"); var board = new ArtifactBoardSession(b, engine);
        board.SetLevel(Id("BLUE-N02"), 2); var s = Snapshot(b);
        Assert.That(s.Inputs.ContainsKey("ST_008"), Is.False);
        Assert.That(s.MissingInputs, Contains.Item("ST_008")); Assert.That(s.HasCompleteInputSet, Is.False);
        var row = s.Fields.Single(f => f.LegacyCode == "ST_008");
        Assert.That(row.Final, Is.Null); Assert.That(row.Artifact, Is.EqualTo(-80));
        Assert.That(row.ReplacedLegacyInput, Is.Not.Null);
        Assert.That(adapter.ExportMappedInputs(s).ContainsKey("ST_008"), Is.False);
    }

    [TestCase("全技能", "all", 700)]
    [TestCase("單體", "single", 1400)]
    [TestCase("群體", "group", 1400)]
    [TestCase("爆發", "burst", 1400)]
    [TestCase("持續", "sustained", 1400)]
    public void StarKeepsEveryTagAndMapsOnlyAllSkill(string option, string tag, int gain)
    {
        var b = Build(); var board = new ArtifactBoardSession(b, engine);
        board.SwitchOption(Id("COMMON-SEL-XINGZHAO"), option); board.SetLevel(Id("COMMON-SEL-XINGZHAO"), 7);
        var s = Snapshot(b);
        // Parent increments are independent, obtained from the authoritative panel.
        var panel = BuildAttributeLayer.Project(b, engine).ToDictionary(r => r.Key);
        Assert.That(s.Inputs["ST_015"].NumberValue, Is.EqualTo((double)panel["skillEnhancement.all"].Final!));
        Assert.That(s.TaggedSkillEnhancement[tag], Is.EqualTo(tag == "all" ? 5600 + gain : gain));
        if (tag != "all") {
            Assert.That(s.Inputs["ST_015"].NumberValue, Is.EqualTo(5600));
            Assert.That(s.EffectiveTaggedSkillEnhancement[tag], Is.EqualTo(7000));
            Assert.That(s.Omissions.Single(o => o.Key == $"skillEnhancement.{tag}").Artifact, Is.EqualTo(gain));
        }
        Assert.That(s.Inputs["SH_004"].NumberValue, Is.EqualTo(b.Parameters["SH_004"].NumberValue));
        Assert.That(s.Inputs["KG_004"].StringValue, Is.EqualTo(b.Parameters["KG_004"].StringValue));
    }

    [Test] public void MissingTagBaseRemainsUnknownInsteadOfAssumingZero()
    {
        var b = Build(); b.BaseAttributes.Remove("skillEnhancement.burst");
        var board = new ArtifactBoardSession(b, engine); board.SwitchOption(Id("COMMON-SEL-XINGZHAO"), "爆發");
        board.SetLevel(Id("COMMON-SEL-XINGZHAO"), 7); var s = Snapshot(b);
        Assert.That(s.TaggedSkillEnhancement["burst"], Is.Null); Assert.That(s.EffectiveTaggedSkillEnhancement["burst"], Is.Null);
        Assert.That(s.Omissions.Single(o => o.Key == "skillEnhancement.burst").Artifact, Is.EqualTo(1400));
    }

    [Test] public void ProfessionSuppressionCannotBecomeTechnicalSuppression()
    {
        var b = Build(); b.BaseAttributes["professionSuppression"] = 20000;
        var board = new ArtifactBoardSession(b, engine); board.SetLevel(Id("COMMON-SEL-MINGGE"), 7);
        var s = Snapshot(b); Assert.That(s.Inputs["ST_003"].NumberValue, Is.EqualTo(1000));
        Assert.That(s.Omissions.Single(o => o.Key == "professionSuppression").Final, Is.EqualTo(21640));
        Assert.That(s.ExcludedArtifactEffects.Any(e => e.Key == "targetProfessionSuppression"), Is.True);
    }

    [Test] public void ConditionalScopedAndCoreMechanicsDoNotEnterInputs()
    {
        var b = Build(); var board = new ArtifactBoardSession(b, engine);
        board.SetLevel(Id("CLASS-LY-UP02"), 5); board.SetLevel(Id("CORE"), 5);
        board.SetLevel(Id("COMMON-SEL-PVP01"), 7); board.SetLevel(Id("CORE-D03-L"), 5);
        var s = Snapshot(b); var panel = BuildAttributeLayer.Project(b, engine).ToDictionary(r => r.Key);
        foreach (var m in adapter.Mappings) Assert.That(s.Inputs[m.LegacyCode].NumberValue, Is.EqualTo((double)(panel[m.SourceKey].Final / m.Divisor)!));
        Assert.That(s.ExcludedArtifactEffects.Any(e => e.Scope == "triggeredPanel"), Is.True);
        Assert.That(s.ExcludedArtifactEffects.Any(e => e.Key == "longFei.damageReduction"), Is.True);
        Assert.That(s.UnmodeledMechanics.Any(e => e.Kind == "coreSuppressionAtMax"), Is.True);
        Assert.That(s.UnmodeledMechanics.Any(e => e.Kind == "interactionStacks"), Is.True);
        Assert.That(s.Inputs.Keys.Any(k => k.Contains("jingLei") || k.Contains("longFei")), Is.False);
    }

    [Test] public void OldArtifactControlsAreDisabledAndAllMarginalOutputsExcluded()
    {
        var b = Build(); var s = Snapshot(b);
        Assert.That(s.Inputs["SQ_004"].NumberValue, Is.Zero);
        Assert.That(b.Parameters["SQ_004"].NumberValue, Is.EqualTo(7));
        Assert.That(s.ExcludedResultCodes, Is.EquivalentTo(config.ResultFormulas.Where(f => f.Code.StartsWith("RF_SQ_")).Select(f => f.Code)));
        Assert.That(s.Inputs.Keys.Any(k => k.StartsWith("RF_SQ_")), Is.False);
    }

    [TestCase("ST_001")][TestCase("ST_004")][TestCase("ST_009")][TestCase("ST_015")]
    public void DuplicateTwAndLegacyArtifactInjectionIsRejected(string code)
    {
        var b = Build(); Assert.Throws<InvalidOperationException>(() => adapter.Create(b, Profile(),
            [new(code, "LegacyOldArtifact", "RF_SQ_012")]));
    }

    [TestCase("TWArtifact")][TestCase("LegacyEquivalentBenefit")][TestCase("TriggeredAttributes")][TestCase("ScopedEffects")]
    public void AnySecondAttributeSourceIsRejected(string source)
    {
        Assert.Throws<InvalidOperationException>(() => adapter.Create(Build(), Profile(), [new("ST_001", source, "extra")]));
    }

    [Test] public void ReenabledOldArtifactOrModifiedInputFailsHandoffValidation()
    {
        var s = Snapshot(Build()); s.Inputs["SQ_004"] = new(true, 7, "7");
        Assert.Throws<InvalidOperationException>(() => adapter.ExportMappedInputs(s));
        s = Snapshot(Build()); s.Inputs["ST_001"] = new(true, s.Inputs["ST_001"].NumberValue + 170, null);
        Assert.Throws<InvalidOperationException>(() => adapter.ExportMappedInputs(s));
    }

    [TestCase("RF_SQ_012")][TestCase("FZJ_016")][TestCase("extra")]
    public void UnknownInternalOrOldBenefitFieldsCannotBeInjected(string code)
    {
        var b = Build(); b.Parameters[code] = new() { NumberMode = true, NumberValue = 170 };
        Assert.Throws<ArgumentException>(() => Snapshot(b));
    }

    [Test] public void AttackNegativeValuesAndPercentagePointsAreNotClampedOrReinterpreted()
    {
        var b = Build(); b.BaseAttributes["attack"] = -25; b.BaseAttributes["criticalDamage"] = 175;
        Assert.That(Snapshot(b).Inputs["ST_001"].NumberValue, Is.EqualTo(-25));
        var board = new ArtifactBoardSession(b, engine); board.SwitchOption(Id("COMMON-SEL-CRIT01"), "刃影摧風");
        board.SetLevel(Id("COMMON-SEL-CRIT01"), 7); var s = Snapshot(b);
        Assert.That(s.Inputs["ST_001"].NumberValue, Is.EqualTo(1065)); // includes auto-added parent static attack
        Assert.That(s.Inputs["ST_009"].NumberValue, Is.EqualTo(1.82).Within(1e-12));
        Assert.That(s.Inputs["ST_010"].NumberValue, Is.EqualTo(.05).Within(1e-12));
    }

    [Test] public void ABInputSnapshotsHaveIndependentSourceTraceAndNumericDifference()
    {
        var a = Build(); var b = Build(); b.BuildId = "TEST-SYNTHETIC-B"; b.BaseAttributes["attack"] += 500;
        new ArtifactBoardSession(b, engine).SetLevel(Id("BLUE-N02"), 3);
        var pair = adapter.Compare(a, Profile(), b, Profile());
        Assert.That(pair.Differences.Single(r => r.LegacyCode == "ST_001").Difference, Is.EqualTo(500));
        Assert.That(pair.Differences.Single(r => r.LegacyCode == "ST_008").Difference, Is.EqualTo(-120));
        Assert.That(pair.Differences.Single(r => r.LegacyCode == "ST_015").Difference, Is.EqualTo(750));
        Assert.That(pair.Differences.Single(r => r.LegacyCode == "ST_008").B.SourceKey, Is.EqualTo("hit"));
        b.BaseAttributes.Remove("hit");
        Assert.That(adapter.Compare(a, Profile(), b, Profile()).Differences.Single(r => r.LegacyCode == "ST_008").Difference, Is.Null);
        var serialized = Encode(pair); var restored = JsonSerializer.Deserialize<LegacyInputPair>(serialized)!;
        adapter.Validate(restored.A); adapter.Validate(restored.B);
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "phase2c-synthetic-ab.json"),
            JsonSerializer.Serialize(pair, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.WriteLine("Synthetic A/B input snapshots; no DPS evaluation");
    }

    [Test] public void CrossProfessionRequiresExplicitPermissionAndNeverSharesCoefficients()
    {
        var a = Build(); var b = Build("TY", "破铁衣");
        Assert.Throws<ArgumentException>(() => adapter.Compare(a, Profile(), b, Profile("TY", "破铁衣")));
        var pair = adapter.Compare(a, Profile(), b, Profile("TY", "破铁衣"), true);
        Assert.That(pair.Warning, Is.EqualTo(PveComparison.CrossProfessionWarning));
        Assert.That(pair.B.Inputs["KG_001"].StringValue, Is.EqualTo("破铁衣"));
        Assert.Throws<ArgumentException>(() => adapter.Create(a, Profile("TY", "破铁衣")));
    }

    [Test] public void ConfigDriftAndMissingOldArtifactDisableRuleFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new LegacyStaticInputAdapter(configJson + " ", mappingJson, engine));
        var obj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(mappingJson)!;
        obj["DisabledInputs"] = JsonSerializer.SerializeToElement(Array.Empty<LegacyDisabledInput>());
        Assert.Throws<ArgumentException>(() => new LegacyStaticInputAdapter(configJson, Encode(obj), engine));
    }

    [Test] public void InputsContainAll163FrontFieldsWithProvenanceButNeverInventMissingContext()
    {
        var b = Build(); b.Parameters.Remove("BO_01"); var s = Snapshot(b);
        Assert.That(s.Fields.Length, Is.EqualTo(163)); Assert.That(s.Inputs.Count, Is.EqualTo(162));
        Assert.That(s.MissingInputs, Is.EquivalentTo(new[] { "BO_01" }));
        Assert.That(s.Fields.Single(f => f.LegacyCode == "BO_01").SourceKind, Is.EqualTo("LegacyContextUnverified"));
        Assert.That(s.Fields.Single(f => f.LegacyCode == "BO_01").Input, Is.Null);
    }

    [Test] public void MappingDoesNotAlterLegacyEngineEvenWhenSnapshotProducedBetweenGoldenEvaluations()
    {
        PveUtility.InitUtilityFromConfig(config);
        var before = PveUtility.Calculate(["FBL_06", "RF_SQ_012"], PveComparison.CopyParameters(config.DefaultParamValues));
        Snapshot(Build());
        PveUtility.InitUtilityFromConfig(config);
        var after = PveUtility.Calculate(["FBL_06", "RF_SQ_012"], PveComparison.CopyParameters(config.DefaultParamValues));
        Assert.That(Encode(after), Is.EqualTo(Encode(before)));
    }

    [Test] public void EmbeddedLegacyArtifactBlocksTwPredictionEvenWithCompleteInputs()
    {
        var b = Build(); var s = Snapshot(b);
        Assert.That(s.HasCompleteInputSet, Is.True);
        Assert.That(s.EngineEvaluationAllowed, Is.False);
        Assert.That(s.EmbeddedFormulaConflicts.Single().Code, Is.EqualTo("FZJ_010"));
        Assert.That(s.EmbeddedFormulaConflicts.Single().EvidenceCode, Is.EqualTo("RF_SQ_030"));
        Assert.That(s.Inputs["SH_001"].NumberValue, Is.EqualTo(b.Parameters["SH_001"].NumberValue));
        Assert.Throws<InvalidOperationException>(() => adapter.ValidatePredictionHandoff(s));
        Assert.That(config.InternalFormulas.Single(f => f.Code == "FZJ_010").Formula, Does.Contain("0.05*[SH_001]"));
    }
}
