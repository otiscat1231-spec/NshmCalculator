using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using NshmCalculator.Shared;
using NshmCalculator.Shared.Research;
using NshmCalculator.Shared.Research.Artifacts;
using NshmCalculator.Shared.Models.CalculatorModel.CalculatorConfig;
using NshmCalculator.Shared.Models.CalculatorModel.FormulaParam.Params;

namespace NshmCalculator.Test.Research;

[TestFixture, NonParallelizable]
public class TwArtifactEffectAdapterTest
{
    private string original = "", policyJson = "";
    private PveConfig config = null!;
    private ArtifactBoardEngine engine = null!;
    private LegacyStaticInputAdapter mapper = null!;
    private TwArtifactEffectAdapter effects = null!;
    private TwArtifactPveAdapter adapter = null!;
    private static ProfessionProfile Profile => new() { Id = "LY", LegacyModes = new() { ["龙吟"] = "龍吟" } };
    private static string Data(string path) => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "data", path));
    private string Id(string pos) => engine.Pack.AllNodes.Single(n => n.PositionId == pos).Id;
    [SetUp] public void Setup()
    {
        original = Data("config_pve.json"); config = JsonSerializer.Deserialize<PveConfig>(original)!;
        engine = new(ArtifactDataPack.Load(Data("artifacts/TW-Mobile-2.3.3/artifact-pack.json")));
        mapper = new(original, Data("pve/adapters/tw-static-v1.json"), engine);
        policyJson = Data("artifacts/TW-Mobile-2.3.3/effect-policy-v1.json");
        effects = new(engine, policyJson); adapter = new(original, mapper, effects);
    }
    // All numbers in this fixture are synthetic controls, not user observations.
    private ResearchBuild Build()
    {
        var b = PveComparison.CreateLegacyExample(config, Profile, "龙吟");
        b.BuildId = "TEST-SYNTHETIC-2DA";
        foreach (var m in mapper.Mappings) b.BaseAttributes[m.SourceKey] = 1000;
        b.BaseAttributes["attack"] = 10000; b.BaseAttributes["criticalDamage"] = 175;
        b.BaseAttributes["bossSuppressionPercent"] = 5; b.BaseAttributes["skillEnhancement.all"] = 5600;
        foreach (var key in effects.DimensionNames.Keys) b.BaseFiveDimensions[key] = 104;
        new ArtifactBoardSession(b, engine);
        return b;
    }
    private void Level(ResearchBuild b, string pos, int level) => new ArtifactBoardSession(b, engine).SetLevel(Id(pos), level);
    private TwArtifactEffectContext Control(string pos, TwArtifactAverageControl control) => new() { Controls = new() { [Id(pos)] = control } };

    [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
    public void CoreEachDimensionIncrementAndLevelFiveOnly(int level)
    {
        var b = Build(); Level(b, "CORE", level); var p = effects.Project(b);
        Assert.That(p.FiveDimensions.Dimensions.Length, Is.EqualTo(5));
        Assert.That(p.FiveDimensions.Dimensions.All(d => d.Artifact == 9*level && d.Final == 104+9*level), Is.True);
        Assert.That(p.FiveDimensions.CandidateBossSuppression, Is.EqualTo(level == 5 ? 70 : 0));
        Assert.That(p.FiveDimensions.Status, Is.EqualTo("暫定候選"));
        Assert.That(p.ConditionalMeanAttributes.ContainsKey("professionSuppression"), Is.False);
        foreach (var key in new[] { "attack", "elementAttack", "critical", "hit", "defensePenetration" })
            Assert.That(p.Panel.Single(r => r.Key == key).Final, Is.EqualTo(BuildAttributeLayer.Project(b,engine).Single(r=>r.Key==key).Final));
        Assert.That(adapter.Prepare(b, Profile).Inputs["ST_002"].NumberValue, Is.EqualTo((double)(1000 + p.StaticAttributes.GetValueOrDefault("bossSuppression") + (level == 5 ? 70 : 0))));
    }
    [Test] public void CoreUsesFloorEachNotFloorOfTotalAndMissingNeverUsesMergedLegacy()
    {
        var b = Build(); Level(b, "CORE", 5);
        Assert.That(effects.Project(b).FiveDimensions.CandidateBossSuppression, Is.EqualTo(70));
        Assert.That(decimal.Floor(5*149*.1m), Is.EqualTo(74));
        b.BaseFiveDimensions.Remove("strength");
        Assert.That(effects.Project(b).FiveDimensions.CandidateBossSuppression, Is.Null);
        Assert.That(adapter.Prepare(b, Profile).Missing.Any(s => s.StartsWith("CORE")), Is.True);
        Assert.Throws<ArgumentException>(() => adapter.EvaluateCandidate(b, Profile));
        var off = effects.Project(b, Control("CORE", new() { Mode = ArtifactTriggerMode.Off }));
        Assert.That(off.Pending, Is.Empty); Assert.That(off.FiveDimensions.CandidateBossSuppression, Is.Zero);
    }
    [Test] public void CoreCandidateAlgorithmIsVersionReplaceable()
    {
        var json = JsonNode.Parse(policyJson)!;
        json["CoreRule"]!["Algorithm"] = "alternative-test-v2"; json["CoreRule"]!["Id"] = "TEST-only-v2";
        var replacement = new TwArtifactEffectAdapter(engine, json.ToJsonString(), new Dictionary<string, Func<IReadOnlyDictionary<string, decimal>, decimal, decimal>> {
            ["alternative-test-v2"] = (dims, factor) => decimal.Floor(dims.Values.Sum()*factor) });
        var b = Build(); Level(b, "CORE", 5);
        Assert.That(replacement.Project(b).FiveDimensions.CandidateBossSuppression, Is.EqualTo(74));
        Assert.That(replacement.Project(b).CoreRuleId, Is.EqualTo("TEST-only-v2"));
        Assert.That(effects.Project(b).FiveDimensions.CandidateBossSuppression, Is.EqualTo(70));
        Assert.Throws<ArgumentException>(() => new TwArtifactEffectAdapter(engine, json.ToJsonString()));
    }
    [Test] public void NoGenericDimensionConversionsOrUnknownKeys()
    {
        var b = Build(); b.BaseFiveDimensions["combinedStrengthSpirit"] = 100;
        Assert.Throws<ArgumentException>(() => effects.Project(b));
        foreach (var key in effects.DimensionNames.Keys)
            Assert.Throws<ArgumentException>(() => adapter.EquivalentPanelGain(Build(), Profile, key, 1, false));
        Assert.Throws<ArgumentException>(() => adapter.EquivalentPanelGain(Build(), Profile, "attackSpeed", 1, false));
    }
    [Test] public void ProjectionIsIdempotentDetachedAndPersistable()
    {
        var b = Build(); Level(b, "CORE", 5); Level(b, "BLUE-N02", 10);
        var before = JsonSerializer.Serialize(b); var p = adapter.Prepare(b, Profile);
        for (int i=0;i<3;i++) Assert.That(JsonSerializer.Serialize(adapter.Prepare(b, Profile)), Is.EqualTo(JsonSerializer.Serialize(p)));
        Assert.That(JsonSerializer.Serialize(b), Is.EqualTo(before));
        var restored = JsonSerializer.Deserialize<ResearchBuild>(before)!;
        Assert.That(JsonSerializer.Serialize(adapter.Prepare(restored, Profile)), Is.EqualTo(JsonSerializer.Serialize(p)));
        Assert.That(p.Inputs["ST_008"].NumberValue, Is.EqualTo(600));
    }
    [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
    public void ProfessionDamageComesFromActualNewNodeLevel(int level)
    {
        var b = Build(); Level(b, "M07", level); var p = adapter.Prepare(b, Profile);
        Assert.That(p.Artifact.ProfessionDamageRatio, Is.EqualTo(level*.01m));
        Assert.That(p.Inputs["TW_ART_PROF_DAMAGE_RATIO"].NumberValue, Is.EqualTo(level*.01).Within(1e-12));
        Assert.That(p.Inputs["SH_001"], Is.EqualTo(p.Mapping.Inputs["SH_001"]));
        var c = Control("M07", new() { Mode = ArtifactTriggerMode.Off });
        Assert.That(effects.Project(b, c).ProfessionDamageRatio, Is.Zero);
    }
    [Test] public void ManualProfessionDutyAndMissingAreExplicit()
    {
        var b = Build(); Level(b, "M07", 5);
        Assert.That(effects.Project(b, Control("M07", new() { Mode=ArtifactTriggerMode.Manual, ManualUptime=.5m })).ProfessionDamageRatio, Is.EqualTo(.025m));
        Assert.That(effects.Project(b, Control("M07", new() { Mode=ArtifactTriggerMode.Manual })).Pending, Is.Not.Empty);
        Assert.Throws<ArgumentException>(() => effects.Project(b, Control("M07", new() { ManualUptime=1.1m })));
    }
    [Test] public void OverlayIsolatesAllOldEntrancesAndPreservesOtherFormulas()
    {
        var before = JsonSerializer.Serialize(config); var isolated = adapter.InspectIsolatedConfig();
        var prepared = adapter.Prepare(Build(), Profile);
        Assert.That(prepared.Isolation.RemovedFrontInputs, Is.EquivalentTo(new[] { "SQ_003", "SQ_004" }));
        Assert.That(prepared.Isolation.RemovedInternalFormulas, Is.EquivalentTo(new[] { "FSQ_001" }));
        Assert.That(prepared.Isolation.RemovedResultFormulas.Length, Is.EqualTo(28));
        Assert.That(isolated.InternalFormulas.Single(f=>f.Code=="FZJ_010").Formula, Does.Not.Contain("0.05*[SH_001]"));
        Assert.That(isolated.InternalFormulas.Single(f=>f.Code=="FZJ_010").Formula, Does.Contain("[TW_ART_PROF_DAMAGE_RATIO]*[SH_001]"));
        foreach(var f in isolated.InternalFormulas.Concat(isolated.ResultFormulas).Where(f=>f.Code!="FZJ_010")) {
            var old = config.InternalFormulas.Concat(config.ResultFormulas).Single(x=>x.Code==f.Code);
            Assert.That(JsonSerializer.Serialize(f), Is.EqualTo(JsonSerializer.Serialize(old)), f.Code);
        }
        Assert.That(isolated.FrontParamInfoArray.Any(f=>f.Code.StartsWith("SQ_") || f.GroupName=="神器"), Is.False);
        Assert.That(prepared.Inputs.Keys.Any(c=>c.StartsWith("SQ_") || c.StartsWith("RF_SQ_")), Is.False);
        isolated.InternalFormulas[0].Formula="0";
        Assert.That(adapter.InspectIsolatedConfig().InternalFormulas[0].Formula, Is.Not.EqualTo("0"));
        Assert.That(JsonSerializer.Serialize(config), Is.EqualTo(before)); Assert.That(Data("config_pve.json"), Is.EqualTo(original));
    }
    [Test] public void OldArtifactChoicesDoNotAffectTwCandidateButLegacyRemainsUsable()
    {
        var b = Build(); var baseline = adapter.EvaluateCandidate(b, Profile).ModelValue;
        b.Parameters["SQ_003"] = new() { NumberMode=false, StringValue="不應讀取的舊神器選項" };
        b.Parameters["SQ_004"].NumberValue=999;
        Assert.That(adapter.EvaluateCandidate(b, Profile).ModelValue, Is.EqualTo(baseline));
        var legacy = PveComparison.Evaluate(original, Build(), Profile);
        adapter.EvaluateCandidate(Build(), Profile);
        Assert.That(PveComparison.Evaluate(original, Build(), Profile), Is.EqualTo(legacy));
    }
    [Test] public void NewProfessionGainUsesExistingShareAndNoSecondFivePercent()
    {
        var b = Build(); Level(b, "M07", 5); b.Parameters["SH_001"].NumberValue = .3;
        var off = Control("M07", new() { Mode=ArtifactTriggerMode.Off });
        var prepared = adapter.Prepare(b, Profile, off);
        var p = prepared.Inputs.ToDictionary(x=>x.Key,x=>x.Value.ToParamValue());
        PveUtility.InitUtilityFromConfig(adapter.InspectIsolatedConfig());
        var values = PveUtility.Calculate(new() { "FZJ_010" }, p);
        double z = values["FZJ_010"]!.Value;
        Assert.That(adapter.EvaluateCandidate(b,Profile).ModelValue / adapter.EvaluateCandidate(b,Profile,off).ModelValue,
            Is.EqualTo((z+.05*.3)/z).Within(1e-10));
        b.Parameters["SH_001"].NumberValue=0;
        Assert.That(adapter.EvaluateCandidate(b,Profile).ModelValue, Is.EqualTo(adapter.EvaluateCandidate(b,Profile,off).ModelValue));
    }
    [Test] public void OverlayVersionAndRoutingFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new TwArtifactPveAdapter(original+" ",mapper,effects));
        var node = JsonNode.Parse(policyJson)!; node["Routes"]!["panel:attack"]!["Category"]="99";
        Assert.Throws<ArgumentException>(()=>new TwArtifactEffectAdapter(engine,node.ToJsonString()));
        node = JsonNode.Parse(policyJson)!; node["Routes"]!.AsObject().Remove("panel:attack");
        Assert.Throws<ArgumentException>(()=>new TwArtifactEffectAdapter(engine,node.ToJsonString()));
        var b=Build(); b.ProfessionId="TY";
        Assert.Throws<ArgumentException>(()=>effects.Project(b));
    }
    [Test] public void DuplicateInjectionAndUnknownInputFail()
    {
        var b=Build(); b.Parameters["TW_ART_PROF_DAMAGE_RATIO"]=new() { NumberMode=true,NumberValue=.99 };
        Assert.Throws<ArgumentException>(()=>adapter.Prepare(b,Profile));
        Assert.Throws<InvalidOperationException>(()=>mapper.Create(Build(),Profile,new[] {
            new LegacyAttributeSource("ST_001","LegacyOldArtifact","RF_SQ_001") }));
    }
    [Test] public void InteractionRequiresObservedOrManualMeanAndNeverAssumesCap()
    {
        var b=Build(); Level(b,"CLASS-LY-UP02",5);
        Assert.That(effects.Project(b).Pending.Any(s=>s.Contains("驚雷")), Is.True);
        var p=effects.Project(b,Control("CLASS-LY-UP02",new() { ObservedMeanStacks=1.5m }));
        Assert.That(p.ConditionalMeanAttributes["hit"],Is.EqualTo(60));
        // CORE auto-prerequisite only Lv1, no derived Lv5 boss bonus here.
        Assert.That(p.ConditionalMeanAttributes["bossSuppression"],Is.EqualTo(180));
        Assert.That(effects.Project(b,Control("CLASS-LY-UP02",new() { Mode=ArtifactTriggerMode.Manual,ManualMeanStacks=2 })).ConditionalMeanAttributes["hit"],Is.EqualTo(80));
        Assert.That(effects.Project(b,Control("CLASS-LY-UP02",new() { Mode=ArtifactTriggerMode.Off })).ConditionalMeanAttributes["hit"],Is.Zero);
        Assert.Throws<ArgumentException>(()=>effects.Project(b,Control("CLASS-LY-UP02",new() { ObservedMeanStacks=6 })));
    }
    [Test] public void ExplicitStackDistributionProducesMeanButPreservesExpectationInterface()
    {
        var b=Build(); Level(b,"CLASS-LY-UP02",5);
        var states=new[] { new ArtifactStackState(0,.5m),new ArtifactStackState(4,.5m) };
        var c=Control("CLASS-LY-UP02",new() { StackDistribution=states });
        Assert.That(effects.Project(b,c).ConditionalMeanAttributes["hit"],Is.EqualTo(80));
        var low=adapter.EvaluateCandidate(b,Profile,Control("CLASS-LY-UP02",new() { Mode=ArtifactTriggerMode.Manual,ManualMeanStacks=0 })).ModelValue;
        var high=adapter.EvaluateCandidate(b,Profile,Control("CLASS-LY-UP02",new() { Mode=ArtifactTriggerMode.Manual,ManualMeanStacks=4 })).ModelValue;
        Assert.That(adapter.InteractionStateExpectation(b,Profile,Id("CLASS-LY-UP02"),states),Is.EqualTo((low+high)/2).Within(1e-8));
        Assert.Throws<ArgumentException>(()=>adapter.InteractionStateExpectation(b,Profile,Id("M07"),states));
        Assert.Throws<ArgumentException>(()=>effects.Project(b,Control("CLASS-LY-UP02",new() { StackDistribution=[new(6,1)] })));
    }
    [TestCase("CLASS-LY-DN02","jianDang.attackSpeedIncrease",15,20,25,30,35)]
    [TestCase("CLASS-LY-UP02","jingLei.cooldownReduction",1,1.5,2,2.5,3)]
    [TestCase("CORE-D03-L","longFei.damageReduction",20,30,40,50,60)]
    public void NonlinearSourceLevelsArePreservedAndNeverMultipliedIntoTotal(string position,string key,double l1,double l2,double l3,double l4,double l5)
    {
        var expected=new[]{l1,l2,l3,l4,l5};
        for(int i=1;i<=5;i++) {
            var b=Build(); Level(b,position,i); var p=effects.Project(b,new() { DefaultMode=ArtifactTriggerMode.Off });
            var e=p.Effects.Single(e=>e.Key==key);
            Assert.That((double)e.Amount,Is.EqualTo(expected[i-1]));
            Assert.That(e.Category,Is.EqualTo(position=="CORE-D03-L" ? TwArtifactEffectCategory.Unmodeled : TwArtifactEffectCategory.RotationEffect));
            Assert.Throws<ArgumentException>(()=>TwArtifactAveraging.WeightedSkillGain(e,.5m,1));
        }
    }
    [Test] public void SpecificSkillIdentityAndRootBranchesRemainSeparate()
    {
        var b=Build(); Level(b,"CORE-U03-L",5);
        var first=effects.Project(b).Effects.Single(e=>e.Key=="swordQi.monsterExtraDamage");
        Assert.That(first.Targets,Is.EquivalentTo(new[]{"LY.swordQi.monster"}));
        Assert.That(first.LegacyShareCode,Is.Empty);
        Assert.That(TwArtifactAveraging.WeightedSkillGain(first,.2m,1),Is.EqualTo(.07m));
        new ArtifactBoardSession(b,engine).SwitchOption(Id("ROOT-L"),"LY-ROOT-B");
        var disabled=effects.Project(b).Effects.Single(e=>e.Key==first.Key);
        Assert.That(disabled.AverageValue,Is.Zero);
        Assert.That(TwArtifactAveraging.WeightedSkillGain(disabled,.2m,1),Is.Zero);
    }
    [TestCase(12,20,.6)][TestCase(8,6,1)][TestCase(0,20,0)]
    public void UserCooldownReadyCoverageIsEstimate(decimal duration,decimal cooldown,decimal expected)
        =>Assert.That(TwArtifactAveraging.CooldownReadyUptime(duration,cooldown),Is.EqualTo(expected));
    [Test] public void InvalidAverageInputsReject()
    {
        Assert.Throws<ArgumentException>(()=>TwArtifactAveraging.CooldownReadyUptime(1,0));
        Assert.Throws<ArgumentException>(()=>TwArtifactAveraging.ConditionalExpectation(new[]{(.5m,1d)}));
        Assert.Throws<ArgumentException>(()=>TwArtifactAveraging.ConditionalExpectation(new[]{(1m,double.NaN)}));
    }
    [TestCase("attack")][TestCase("elementAttack")][TestCase("critical")][TestCase("hit")]
    [TestCase("bossSuppression")][TestCase("defensePenetration")][TestCase("ignoreElementResistance")][TestCase("criticalDamage")]
    [TestCase("technicalSuppression")][TestCase("skillEnhancement.all")]
    public void EquivalentMethodsReuseOriginalLambdaAndAcceptSignedNewPanelAmounts(string key)
    {
        var b=Build();
        Assert.That(double.IsFinite(adapter.EquivalentPanelGain(b,Profile,key,1,false)),Is.True);
        Assert.That(double.IsFinite(adapter.EquivalentPanelGain(b,Profile,key,-1,false)),Is.True);
        var policy=JsonSerializer.Deserialize<TwArtifactEffectPolicy>(policyJson)!;
        var function=policy.EquivalentFunctions[key];
        Assert.That(adapter.InspectIsolatedConfig().InternalFormulas.Single(f=>f.Code==function).Formula,
            Is.EqualTo(config.InternalFormulas.Single(f=>f.Code==function).Formula));
    }
    [Test] public void SameProfessionABMappingIncludesCoreDeltaNoCrossProfessionGuess()
    {
        var a=Build();var b=Build();Level(b,"CORE",5);var pair=adapter.CompareInputs(a,Profile,b,Profile);
        Assert.That(pair.Mapping.Differences.Single(d=>d.LegacyCode=="ST_002").Difference,Is.EqualTo(70));
        b.ProfessionId="TY";Assert.Throws<ArgumentException>(()=>adapter.CompareInputs(a,Profile,b,new(){Id="TY"}));
    }
    [Test] public void FrozenGameDataAndLegacyGoldenAreUntouched()
    {
        // Git normalizes LF; Windows checkouts may use CRLF. Compare content, not checkout newline policy.
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Data(path).Replace("\r\n","\n")))).ToLowerInvariant();
        Assert.That(Hash("artifacts/TW-Mobile-2.3.3/artifact-pack.json"),Is.EqualTo("8188fd2b45b25cdb315d6373ee688d7db42ee54f8c319dd40983c93c8aa8cbf7"));
        Assert.That(Hash("config_pve.json"),Is.EqualTo("b291e208e17102b369adaf5f10bb38d70b0b528b13722dc857f36b2b3d2941e4"));
    }
}
