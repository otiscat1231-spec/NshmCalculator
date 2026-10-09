using System.Text.Json;
using System.Text.Json.Nodes;
using NshmCalculator.Shared.Research;
using NshmCalculator.Shared.Research.Artifacts;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
namespace NshmCalculator.Test.Research;

[TestFixture,NonParallelizable]
public class TwTargetAndActualPanelTest
{
    private string original="",targetJson="";
    private TwTargetModel targets=null!;
    private ArtifactBoardEngine engine=null!;
    private TwArtifactEffectAdapter effects=null!;
    private TwActualPanelAdapter anchors=null!;
    private LegacyStaticInputAdapter mapper=null!;
    private TwArtifactPveAdapter adapter=null!;
    private static ProfessionProfile Profile=>new(){Id="LY",LegacyModes=new(){["龙吟"]="龍吟"}};
    private static string Data(string file)=>File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,"data",file));
    private string Id(string position)=>engine.Pack.AllNodes.Single(n=>n.PositionId==position).Id;
    private TwTargetSelection Select(string id="Hero")=>new(targets.Id,id);
    [SetUp]public void Setup()
    {
        original=Data("config_pve.json");targetJson=Data("pve/targets/tw-mobile-2.3.3-v1.json");targets=new(targetJson);
        engine=new(ArtifactDataPack.Load(Data("artifacts/TW-Mobile-2.3.3/artifact-pack.json")));
        effects=new(engine,Data("artifacts/TW-Mobile-2.3.3/effect-policy-v1.json"));anchors=new(engine,effects);
        mapper=new(original,Data("pve/adapters/tw-static-v1.json"),engine);adapter=new(original,mapper,effects,targets,anchors);
    }
    private ResearchBuild Current(int core=0)
    {
        var b=PveComparison.CreateLegacyExample(JsonSerializer.Deserialize<PveConfig>(original)!,Profile,"龙吟");
        b.BuildId="SYNTHETIC-ACTUAL-ONLY";
        // Explicit actual readings; these are synthetic test controls, not user observations.
        foreach(var m in mapper.Mappings)b.ObservedAttributes[m.SourceKey]=1000;
        b.ObservedAttributes["attack"]=10000;b.ObservedAttributes["criticalDamage"]=175;
        b.ObservedAttributes["bossSuppressionPercent"]=5;b.ObservedAttributes["skillEnhancement.all"]=5600;
        foreach(var d in effects.DimensionNames.Keys)b.ObservedFiveDimensions[d]=104+core*9;
        new ArtifactBoardSession(b,engine);Level(b,"CORE",core);return b;
    }
    private void Level(ResearchBuild b,string pos,int level)=>new ArtifactBoardSession(b,engine).SetLevel(Id(pos),level);
    [TestCase("Hero",16057,54.4)][TestCase("Hero",18135,57.6)][TestCase("Hero",18855,58.8)]
    [TestCase("Hero",19635,60.2)][TestCase("Hero",20355,61.5)]
    [TestCase("Honor",16057,55.9)][TestCase("Honor",16647,56.9)][TestCase("Honor",18135,59.5)]
    [TestCase("Honor",18855,60.8)][TestCase("Honor",20355,63.8)]
    public void AllDefenseDisplaysMatchExactly(string id,decimal penetration,decimal expected)
    {
        var r=targets.DefenseRatio(Select(id),penetration);
        Assert.That(targets.DisplayPercent(r),Is.EqualTo(expected));
        Assert.That(Math.Abs(r*100-expected),Is.LessThan(.05m));
    }
    [TestCase(4051,77.1,79.2)][TestCase(2890,68.4,69.6)][TestCase(1696,61.2,61.8)]
    [TestCase(1158,58.5,58.8)][TestCase(4571,81.8,84.5)][TestCase(4746,83.5,86.4)]
    public void AllElementDisplaysMatchExactly(decimal ignore,decimal hero,decimal honor)
    {
        foreach(var pair in new[]{("Hero",hero),("Honor",honor)}) {
            var r=targets.ElementRatio(Select(pair.Item1),ignore);
            Assert.That(targets.DisplayPercent(r),Is.EqualTo(pair.Item2));
            Assert.That(Math.Abs(r*100-pair.Item2),Is.LessThan(.05m));
        }
    }
    [Test]public void RegressionObservationsAndRangesArePreserved()
    {
        Assert.That(targets.Definitions.Sum(t=>t.Observations.Length),Is.EqualTo(22));
        foreach(var t in targets.Definitions)foreach(var o in t.Observations) {
            var ratio=o.Kind=="Defense" ? targets.DefenseRatio(Select(t.Id),o.Input) : targets.ElementRatio(Select(t.Id),o.Input);
            Assert.That(targets.DisplayPercent(ratio),Is.EqualTo(o.DisplayPercent),o.Id);
        }
        var hero=targets.Snapshot(Select(),null,null);
        Assert.That(hero.ElementResistanceRange,Is.EqualTo(new decimal[]{6130,6135}));
        Assert.That(hero.ElementConstantRange,Is.EqualTo(new decimal[]{7004,7020}));
        Assert.That(hero.DefenseRatio,Is.Null);
    }
    [Test]public void TargetBoundariesAndVersionIsolation()
    {
        Assert.That(targets.DefenseRatio(Select(),31555),Is.EqualTo(.9m));
        Assert.That(targets.DefenseRatio(Select(),100000),Is.EqualTo(.9m));
        Assert.That(targets.ElementRatio(Select(),100000),Is.EqualTo(1));
        Assert.Throws<ArgumentException>(()=>targets.DefenseRatio(new("Legacy","Hero"),1000));
        Assert.Throws<ArgumentException>(()=>targets.ElementRatio(new(targets.Id,"normal"),1000));
        Assert.Throws<ArgumentException>(()=>targets.DefenseRatio(Select(),-1));
        Assert.Throws<ArgumentException>(()=>targets.ElementRatio(Select(),-1));
    }
    [Test]public void RatioCandidatesNeverSupplyConstantsAndReturnedDataIsDetached()
    {
        var node=JsonNode.Parse(targetJson)!;node["RatioCandidates"]!["K_D/D"]="999";node["RatioCandidates"]!["K_E/E"]="999";
        var alternative=new TwTargetModel(node.ToJsonString());
        Assert.That(alternative.DefenseRatio(Select(),16057),Is.EqualTo(targets.DefenseRatio(Select(),16057)));
        var def=targets.Definitions;def[0].ElementResistanceRange[0]=1;
        Assert.That(targets.Definitions[0].ElementResistanceRange[0],Is.EqualTo(6130));
        node["DefenseAlgorithm"]="other";Assert.Throws<ArgumentException>(()=>new TwTargetModel(node.ToJsonString()));
    }
    [Test]public void ActualIdentityUsesObservedReadingsNotBareOrLegacyDefaults()
    {
        var current=Current(5);current.BaseAttributes["attack"]=999999;current.BaseFiveDimensions["strength"]=999999;
        var anchor=anchors.Capture(current);var before=anchor.SnapshotJson;
        var p=anchors.Project(anchor,current);
        foreach(var row in p.Attributes.Where(r=>r.Observed is not null))Assert.That(row.CandidatePanel,Is.EqualTo(row.Observed),row.Key);
        Assert.That(p.CoreBossDelta,Is.Zero);Assert.That(p.CurrentCoreBoss,Is.EqualTo(70));
        Assert.That(p.FiveDimensions.All(d=>d.Base==149 && d.Artifact==0 && d.Final==149),Is.True);
        Assert.That(anchor.SnapshotJson,Is.EqualTo(before));
    }
    [Test]public void CandidateSignedStaticDifferenceNeverMutatesAnchorOrAddsCurrentTwice()
    {
        var c=Current();var board=new ArtifactBoardSession(c,engine);board.SwitchOption(Id("COMMON-SEL-OFFELE01"),"OFFELE01-ATK");Level(c,"COMMON-SEL-OFFELE01",1);
        var anchor=anchors.Capture(c);var n=anchors.Current(anchor);new ArtifactBoardSession(n,engine).SwitchOption(Id("COMMON-SEL-OFFELE01"),"OFFELE01-ELE");
        var before=JsonSerializer.Serialize(anchor);var p=anchors.Project(anchor,n);
        var attack=p.Attributes.Single(r=>r.Key=="attack");
        Assert.That(attack.Delta,Is.EqualTo(-240));Assert.That(attack.CandidatePanel,Is.EqualTo(9760));
        Assert.That(anchors.Project(anchor,c).Attributes.Single(r=>r.Key=="attack").CandidatePanel,Is.EqualTo(10000));
        for(int i=0;i<3;i++)Assert.That(anchors.Project(anchor,n).Attributes.Single(r=>r.Key=="attack"),Is.EqualTo(attack));
        n.ObservedAttributes["attack"]=999999;n.BaseAttributes["attack"]=999999;
        Assert.That(anchors.Project(anchor,n).Attributes.Single(r=>r.Key=="attack").CandidatePanel,Is.EqualTo(9760));
        Assert.That(JsonSerializer.Serialize(anchor),Is.EqualTo(before));
    }
    [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
    public void CoreFiveDimensionsUseCandidateMinusCurrentAtEveryLevel(int level)
    {
        var c=Current(5);var anchor=anchors.Capture(c);var n=anchors.Current(anchor);Level(n,"CORE",level);
        var p=anchors.Project(anchor,n);
        Assert.That(p.FiveDimensions.All(d=>d.Base==149 && d.Artifact==(level-5)*9 && d.Final==104+level*9),Is.True);
        Assert.That(p.CoreBossDelta,Is.EqualTo(level==5 ? 0 : -70));
        Assert.That(p.Attributes.Single(r=>r.Key=="bossSuppression").CandidatePanel,Is.EqualTo(level==5 ? 1000 : 930));
        Assert.That(p.Limitations.Any(x=>x.Contains("間接影響未建模")),Is.True);
    }
    [Test]public void NewLevelFiveBonusUsesCandidateFiveAfterDelta()
    {
        var c=Current(4);var anchor=anchors.Capture(c);var n=anchors.Current(anchor);Level(n,"CORE",5);
        var p=anchors.Project(anchor,n);
        Assert.That(p.FiveDimensions.All(d=>d.Base==140 && d.Artifact==9 && d.Final==149),Is.True);
        Assert.That(p.CurrentCoreBoss,Is.Zero);Assert.That(p.CandidateCoreBoss,Is.EqualTo(70));Assert.That(p.CoreBossDelta,Is.EqualTo(70));
        Assert.That(p.Attributes.Single(r=>r.Key=="bossSuppression").CandidatePanel,Is.EqualTo(1070));
    }
    [Test]public void MissingFiveCancelsForSameCoreButChangingMaxBoundaryStaysUnknown()
    {
        var c=Current(5);c.ObservedFiveDimensions.Clear();var anchor=anchors.Capture(c);
        var same=anchors.Project(anchor,c);
        Assert.That(same.CoreBossDelta,Is.Zero);Assert.That(same.Artifact.Pending,Is.Empty);
        Assert.That(same.Attributes.Single(r=>r.Key=="bossSuppression").CandidatePanel,Is.EqualTo(1000));
        var n=anchors.Current(anchor);Level(n,"CORE",4);var changed=anchors.Project(anchor,n);
        Assert.That(changed.CoreBossDelta,Is.Null);Assert.That(changed.Attributes.Single(r=>r.Key=="bossSuppression").CandidatePanel,Is.Null);
        Assert.That(adapter.PrepareAnchored(anchor,n,Profile,Select()).Inputs.ContainsKey("ST_002"),Is.False);
    }
    [Test]public void UnknownActualPanelIsNeverZeroOrLegacyFallback()
    {
        var c=Current();c.ObservedAttributes.Remove("hit");var anchor=anchors.Capture(c);
        var n=anchors.Current(anchor);Level(n,"BLUE-N02",1);
        var p=adapter.PrepareAnchored(anchor,n,Profile,Select());
        Assert.That(p.Inputs.ContainsKey("ST_008"),Is.False);Assert.That(p.Missing,Contains.Item("ST_008"));
        Assert.That(p.Anchor!.Attributes.Single(r=>r.Key=="hit").Delta,Is.EqualTo(-40));
    }
    [Test]public void AnchorRequiresCurrentBoardAndDetectsTamperingAndWrongProfession()
    {
        var c=Current();c.ArtifactState=null;Assert.Throws<ArgumentException>(()=>anchors.Capture(c));
        var anchor=anchors.Capture(Current());Assert.Throws<ArgumentException>(()=>anchors.Current(anchor with{SnapshotJson=anchor.SnapshotJson+" "}));
        Assert.Throws<ArgumentException>(()=>anchors.Current(anchor with{PackId="Legacy"}));
        Assert.Throws<ArgumentException>(()=>anchors.Current(anchor with{EffectPolicyId="other"}));
        Assert.Throws<ArgumentException>(()=>anchors.Current(anchor with{CoreRuleId="other"}));
        var wrong=anchors.Current(anchor);wrong.ProfessionId="TY";Assert.Throws<ArgumentException>(()=>anchors.Project(anchor,wrong));
        c=Current(5);c.ObservedFiveDimensions["strength"]=1;Assert.Throws<ArgumentException>(()=>anchors.Capture(c));
    }
    [Test]public void CurrentAndCandidateSaveReloadRemainSeparateAndAnchorMatchesOnlyExplicitCurrent()
    {
        var c=Current(5);var anchor=anchors.Capture(c);var n=anchors.Current(anchor);Level(n,"BLUE-N02",2);
        var session=new TwActualPanelSession{Current=c,Candidate=n,Anchor=anchor,Target=Select("Honor")};
        var restored=JsonSerializer.Deserialize<TwActualPanelSession>(JsonSerializer.Serialize(session))!;
        var before=restored.Anchor!.SnapshotJson;
        Level(restored.Candidate,"BLUE-N02",3);restored.Candidate.ObservedAttributes["attack"]=2;
        Assert.That(restored.Anchor.SnapshotJson,Is.EqualTo(before));Assert.That(anchors.Matches(restored.Anchor,restored.Current),Is.True);
        restored.Current.ObservedAttributes["attack"]+=1;Assert.That(anchors.Matches(restored.Anchor,restored.Current),Is.False);
        Assert.That(anchors.Current(restored.Anchor).ObservedAttributes["attack"],Is.EqualTo(10000));
    }
    [Test]public void TargetInjectionReplacesAllLegacyTargetValuesAndLeavesUnknownsMissing()
    {
        var c=Current();c.ObservedAttributes["defensePenetration"]=16057;c.ObservedAttributes["ignoreElementResistance"]=4051;
        foreach(var p in c.Parameters.Where(p=>p.Key.StartsWith("BO_")))p.Value.NumberValue=999999;
        var anchor=anchors.Capture(c);var prepared=adapter.PrepareAnchored(anchor,c,Profile,Select());
        Assert.That(prepared.Inputs["BO_01"].NumberValue,Is.EqualTo(31555));Assert.That(prepared.Inputs["BO_06"].NumberValue,Is.EqualTo(28060));
        Assert.That(prepared.Inputs["BO_03"].NumberValue,Is.EqualTo(6132.5));Assert.That(prepared.Inputs["BO_07"].NumberValue,Is.EqualTo(7012));
        Assert.That(prepared.Inputs.ContainsKey("BO_02"),Is.False);Assert.That(prepared.Missing.Count(k=>k.StartsWith("BO_")),Is.EqualTo(8));
        Assert.That(prepared.Mapping.Fields.Where(f=>f.LegacyCode.StartsWith("BO_")).All(f=>f.SourceKind=="TWTargetCandidate"),Is.True);
        Assert.That(prepared.Target!.DefenseDisplayPercent,Is.EqualTo(54.4m));Assert.That(prepared.Target.ElementDisplayPercent,Is.EqualTo(77.1m));
        Assert.That(c.Parameters["BO_01"].NumberValue,Is.EqualTo(999999));Assert.That(Data("config_pve.json"),Is.EqualTo(original));
        Assert.Throws<ArgumentException>(()=>adapter.Prepare(c,Profile));
    }
    [Test]public void ABTargetChangesDoNotRecaptureAnchorOrChangeObservedPanel()
    {
        var c=Current();c.ObservedAttributes["defensePenetration"]=16057;var anchor=anchors.Capture(c);
        var n=anchors.Current(anchor);Level(n,"BLUE-N02",1);
        var hero=adapter.CompareAnchoredInputs(anchor,n,Profile,Select());var honor=adapter.CompareAnchoredInputs(anchor,n,Profile,Select("Honor"));
        Assert.That(hero.A.Target!.DefenseDisplayPercent,Is.EqualTo(54.4m));Assert.That(honor.A.Target!.DefenseDisplayPercent,Is.EqualTo(55.9m));
        Assert.That(hero.A.Anchor!.AnchorSha256,Is.EqualTo(honor.A.Anchor!.AnchorSha256));
        Assert.That(hero.Mapping.Differences.Single(d=>d.LegacyCode=="ST_008").Difference,Is.EqualTo(-40));
    }
}
