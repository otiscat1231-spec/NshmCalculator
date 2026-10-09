using System.Text.Json;
using NshmCalculator.Shared.Research;
using NshmCalculator.Shared.Research.Artifacts;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;

namespace NshmCalculator.Test.Research;

[TestFixture]
public class ArtifactPhase2BBuildTest
{
    private ArtifactBoardEngine engine = null!;
    private ArtifactBoardLayout layout = null!;
    private string DataFile(string file) => Path.Combine(TestContext.CurrentContext.TestDirectory,
        "data/artifacts/TW-Mobile-2.3.3", file);
    private string Id(string position) => engine.Pack.AllNodes.Single(n => n.PositionId == position).Id;
    private static string Encode(object obj) => JsonSerializer.Serialize(obj);
    private static ResearchBuild Build() => new() { ProfessionId = "LY", LegacyMode = "龙吟",
        BaseAttributes = new() { ["attack"] = 100, ["hit"] = 1000, ["criticalDamage"] = 175 },
        Parameters = new() { ["ST_001"] = new ParamValue { NumberMode = true, NumberValue = 7654 } } };
    private BuildAttributeRow Panel(ResearchBuild b, string key) => BuildAttributeLayer.Project(b, engine).Single(r => r.Key == key);
    [SetUp] public void Setup()
    {
        var pack = ArtifactDataPack.Load(File.ReadAllText(DataFile("artifact-pack.json")));
        engine = new(pack);
        layout = ArtifactBoardLayout.Load(File.ReadAllText(DataFile("board-layout.json")), pack);
    }

    [Test] public void ProjectionRepeatedSaveReloadNeverWritesFinalBackOrChangesLegacy()
    {
        var b = Build(); var s = new ArtifactBoardSession(b, engine);
        s.SetLevel(Id("BLUE-N02"), 3);
        var before = Encode(b);
        for (var i = 0; i < 5; i++) Assert.That(Panel(b, "hit").Final, Is.EqualTo(880));
        Assert.That(Encode(b), Is.EqualTo(before));
        var restored = JsonSerializer.Deserialize<ResearchBuild>(before)!;
        new ArtifactBoardSession(restored, engine);
        Assert.That(Panel(restored, "hit").Final, Is.EqualTo(880));
        Assert.That(restored.BaseAttributes["hit"], Is.EqualTo(1000));
        Assert.That(restored.Parameters["ST_001"].NumberValue, Is.EqualTo(7654));
        Assert.That(before, Does.Not.Contain("Final"));
    }

    [Test] public void MissingBaseIsUnknownButSignedArtifactIncrementRemainsAvailable()
    {
        var b = new ResearchBuild { ProfessionId = "LY" };
        var s = new ArtifactBoardSession(b, engine); s.SetLevel(Id("BLUE-N02"), 10);
        var hit = Panel(b, "hit");
        Assert.That(hit.Base, Is.Null); Assert.That(hit.Final, Is.Null); Assert.That(hit.Artifact, Is.EqualTo(-400));
        b.BaseAttributes["hit"] = 0;
        Assert.That(Panel(b, "hit").Final, Is.EqualTo(-400));
    }

    [Test] public void SwitchingChoiceReplacesIncrementAndNeverAccumulatesOldOption()
    {
        var b = Build(); b.BaseAttributes["elementAttack"] = 200;
        var s = new ArtifactBoardSession(b, engine);
        s.SwitchOption(Id("COMMON-SEL-OFFELE01"), "OFFELE01-ATK");
        s.SetLevel(Id("COMMON-SEL-OFFELE01"), 3);
        Assert.That(Panel(b, "attack").Final, Is.EqualTo(610));
        s.SwitchOption(Id("COMMON-SEL-OFFELE01"), "OFFELE01-ELE");
        Assert.That(Panel(b, "attack").Final, Is.EqualTo(-110));
        s.SwitchOption(Id("COMMON-SEL-OFFELE01"), "OFFELE01-ATK");
        Assert.That(Panel(b, "attack").Final, Is.EqualTo(610));
        Assert.That(s.Level(Id("COMMON-SEL-OFFELE01")), Is.EqualTo(3));
    }

    [Test] public void PercentagePointsStayPercentagePointsAndAreNotRatios()
    {
        var b = Build(); var s = new ArtifactBoardSession(b, engine);
        s.SwitchOption(Id("COMMON-SEL-CRIT01"), "刃影摧風");
        s.SetLevel(Id("COMMON-SEL-CRIT01"), 7);
        Assert.That(Panel(b, "criticalDamage").Final, Is.EqualTo(182));
        Assert.That(BuildAttributeLayer.Definitions(engine.Pack).Single(d => d.Key == "criticalDamage").Unit, Is.EqualTo("percentagePoint"));
    }

    [Test] public void TriggeredScopedCoreAndRootMechanicsNeverLeakIntoFinalPanel()
    {
        var b = Build(); var s = new ArtifactBoardSession(b, engine);
        s.SetLevel(Id("CLASS-LY-UP02"), 5); s.SetLevel(Id("CORE"), 5);
        s.SetLevel(Id("COMMON-SEL-PVP01"), 7); s.SetLevel(Id("CORE-D03-L"), 5);
        var summary = new ArtifactAggregator(engine).Summarize(s.State, new() {
            NearbyEnemyPlayers = 6, InteractionStacks = 5, VerifiedFiveDimensionSum = 1234,
            CoreRoundingPolicy = ArtifactRoundingPolicy.UnroundedCandidate });
        Assert.That(summary.TriggeredAttributes["hit"], Is.EqualTo(200));
        Assert.That(summary.UnmodeledMechanics.Count, Is.GreaterThan(0));
        var panel = BuildAttributeLayer.Project(b, engine);
        Assert.That(panel.Single(r => r.Key == "hit").Artifact, Is.EqualTo(summary.StaticAttributes.GetValueOrDefault("hit")));
        Assert.That(panel.Single(r => r.Key == "bossSuppression").Artifact, Is.EqualTo(summary.StaticAttributes.GetValueOrDefault("bossSuppression")));
        Assert.That(panel.Select(r => r.Key), Does.Not.Contain("longFei.damageReduction"));
        Assert.That(panel.Select(r => r.Key), Does.Not.Contain("jingLei.interactionMaxStacks"));
        Assert.That(panel.Select(r => r.Key), Does.Not.Contain("fiveDimensions.displayIncrement"));
    }

    [Test] public void ComparisonShowsBaseAndArtifactDifferencesAndKeepsASeparateFromB()
    {
        var a = Build(); var b = Build(); b.BaseAttributes["hit"] = 1100;
        var sa = new ArtifactBoardSession(a, engine); var sb = new ArtifactBoardSession(b, engine);
        sa.SetLevel(Id("BLUE-N02"), 2); sb.SetLevel(Id("BLUE-N02"), 5);
        var row = BuildAttributeLayer.Compare(a, b, engine).Single(r => r.Key == "hit");
        Assert.That(row.A.Final, Is.EqualTo(920)); Assert.That(row.B.Final, Is.EqualTo(900));
        Assert.That(row.FinalDifference, Is.EqualTo(-20)); Assert.That(row.ArtifactDifference, Is.EqualTo(-120));
        Assert.That(sa.Level(Id("BLUE-N02")), Is.EqualTo(2));
        b.BaseAttributes.Remove("hit");
        Assert.That(BuildAttributeLayer.Compare(a, b, engine).Single(r => r.Key == "hit").FinalDifference, Is.Null);
    }

    [Test] public void AutoPrerequisiteProvenanceSurvivesReloadAndFailedOperation()
    {
        var b = Build(); var s = new ArtifactBoardSession(b, engine);
        s.SetLevel(Id("CORE-U02"), 1);
        Assert.That(s.TotalCost, Is.EqualTo(20)); Assert.That(s.AutoLevel(Id("ROOT-L")), Is.EqualTo(1));
        Assert.That(s.AutoLevel(Id("CORE")), Is.EqualTo(1)); Assert.That(s.AutoLevel(Id("CORE-U01")), Is.EqualTo(5));
        Assert.That(s.AutoLevel(Id("CORE-U02")), Is.Zero);
        var before = Encode(b);
        Assert.Throws<ArtifactRuleException>(() => s.SetLevel(Id("CORE-U01"), 4));
        Assert.That(Encode(b), Is.EqualTo(before));
        var restored = JsonSerializer.Deserialize<ResearchBuild>(before)!;
        var rs = new ArtifactBoardSession(restored, engine);
        Assert.That(rs.AutoLevel(Id("CORE-U01")), Is.EqualTo(5));
        rs.SetLevel(Id("CORE-U01"), 6);
        Assert.That(rs.AutoLevel(Id("CORE-U01")), Is.EqualTo(5));
        rs.SetLevel(Id("CORE-U02"), 0); rs.SetLevel(Id("CORE-U01"), 4);
        Assert.That(rs.AutoLevel(Id("CORE-U01")), Is.EqualTo(4));
    }

    [Test] public void PartialExistingParentMarksOnlyNewAutoLevelsRatherThanManualLevels()
    {
        var s = new ArtifactBoardSession(Build(), engine);
        s.SetLevel(Id("M02"), 3);
        Assert.That(s.AutoLevel(Id("M02")), Is.Zero);
        s.SetLevel(Id("M03"), 1);
        Assert.That(s.Level(Id("M02")), Is.EqualTo(5));
        Assert.That(s.AutoLevel(Id("M02")), Is.EqualTo(2));
        s.SwitchOption(Id("ROOT-L"), "LY-ROOT-B");
        Assert.That(s.AutoLevel(Id("M02")), Is.EqualTo(2));
    }

    [TestCase("CORE-U03-L", "CORE-U03-R")]
    [TestCase("CORE-D03-L", "CORE-D03-R")]
    public void UiMutexCommandRequiresExplicitReplacementAndRefundsOtherSide(string a, string b)
    {
        var build = Build(); var s = new ArtifactBoardSession(build, engine);
        s.SetLevel(Id(a), 5); var cost = s.TotalCost;
        Assert.That(s.BlockedByMutex(Id(b)), Is.True);
        var before = Encode(build);
        Assert.Throws<ArtifactRuleException>(() => s.SetLevel(Id(b), 1));
        Assert.That(Encode(build), Is.EqualTo(before));
        s.ReplaceExclusive(Id(b));
        Assert.That(s.Level(Id(a)), Is.Zero); Assert.That(s.Level(Id(b)), Is.EqualTo(1));
        Assert.That(s.TotalCost, Is.EqualTo(cost - 8));
    }

    [TestCase("CLASS-LY-UP01", "CLASS-LY-UP02")]
    [TestCase("CLASS-LY-DN01", "CLASS-LY-DN02")]
    public void UiNonExclusiveCommandsAllowBothNodes(string a, string b)
    {
        var s = new ArtifactBoardSession(Build(), engine);
        s.SetLevel(Id(a), 1); s.SetLevel(Id(b), 1);
        Assert.That(s.Level(Id(a)), Is.EqualTo(1)); Assert.That(s.Level(Id(b)), Is.EqualTo(1));
        Assert.That(s.BlockedByMutex(Id(b)), Is.False);
    }

    [Test] public void QuestSessionDoesNotAddParentsAndLocksOnlyUnallocatedNodes()
    {
        var s = new ArtifactBoardSession(Build(), engine); s.SetLevel(Id("BLUE-N01"), 1);
        Assert.That(s.State.Levels.Keys, Is.EquivalentTo(new[] { Id("BLUE-N01") }));
        Assert.That(s.Build.ArtifactAutoLevels, Is.Empty);
        Assert.Throws<ArtifactRuleException>(() => s.SetQuestUnlocked(Id("BLUE-N01"), false));
        s.SetLevel(Id("BLUE-N01"), 0); s.SetQuestUnlocked(Id("BLUE-N01"), false);
        Assert.Throws<ArtifactRuleException>(() => s.SetLevel(Id("BLUE-N01"), 1));
    }

    [Test] public void FixedLayoutRetainsSlotsAndOnlyReplacesProfessionContents()
    {
        var ly = layout.ForProfession(engine.Pack, "LY"); var other = layout.ForProfession(engine.Pack, "TY");
        Assert.That(ly.Length, Is.EqualTo(38)); Assert.That(ly.Select(s => s.Slot), Is.EqualTo(other.Select(s => s.Slot)));
        Assert.That(ly.All(s => s.Node is not null), Is.True);
        Assert.That(other.Count(s => s.Node is null), Is.EqualTo(10));
        Assert.That(other.Where(s => s.Node is not null).All(s => s.Node!.Shared), Is.True);
        Assert.That(other.Where(s => s.Node is not null).Select(s => s.Node!.Id), Is.EquivalentTo(engine.Pack.CommonNodes.Select(n => n.Id)));
    }

    [Test] public void InvalidLayoutAndWrongSavedProfessionOrVersionAreRejected()
    {
        layout.Slots[1] = layout.Slots[0];
        Assert.Throws<ArtifactRuleException>(() => ArtifactBoardLayout.Load(Encode(layout), engine.Pack));
        var b = Build(); new ArtifactBoardSession(b, engine); b.ArtifactState!.ProfessionId = "TY";
        Assert.Throws<ArtifactRuleException>(() => BuildAttributeLayer.Project(b, engine));
        b.ArtifactState.ProfessionId = "LY"; b.ArtifactState.PackId = "other-version";
        Assert.Throws<ArtifactRuleException>(() => new ArtifactBoardSession(b, engine));
    }

    [Test] public void OldBuildWithoutArtifactFieldsLoadsWithoutInventedPanel()
    {
        var old = JsonSerializer.Deserialize<ResearchBuild>("{\"ProfessionId\":\"LY\",\"Parameters\":{}}")!;
        Assert.That(old.BaseAttributes, Is.Empty); Assert.That(old.ArtifactState, Is.Null);
        var s = new ArtifactBoardSession(old, engine);
        Assert.That(s.TotalCost, Is.Zero);
        Assert.That(BuildAttributeLayer.Project(old, engine).All(r => r.Final is null), Is.True);
    }
}
