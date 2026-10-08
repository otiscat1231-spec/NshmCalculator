using System.Text.Json;
using NshmCalculator.Shared.Research.Artifacts;

namespace NshmCalculator.Test.Research;

[TestFixture]
public class ArtifactPhase2ATest
{
    private string Json => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,
        "data/artifacts/TW-Mobile-2.3.3/artifact-pack.json"));
    private ArtifactDataPack pack = null!;
    private ArtifactBoardEngine engine = null!;
    private string Id(string position) => pack.AllNodes.Single(n => n.PositionId == position).Id;
    private ArtifactBoardState State(int? budget = null) => engine.CreateState("LY", budget,
        new Dictionary<string, string> {
            [Id("ROOT-L")] = "LY-ROOT-A", [Id("COMMON-SEL-OFFELE01")] = "OFFELE01-ATK",
            [Id("COMMON-SEL-PVP01")] = "PVP01-DEF", [Id("COMMON-SEL-MINGGE")] = "碎夢",
            [Id("COMMON-SEL-ZHIBI")] = "碎夢", [Id("COMMON-SEL-XINGZHAO")] = "全技能",
            [Id("COMMON-SEL-CRIT01")] = "絕電誅鋒", [Id("BLUE-SEL01")] = "擊連鋒" });
    private ArtifactSummary Summary(ArtifactBoardState s, ArtifactTriggerContext? c = null) => new ArtifactAggregator(engine).Summarize(s, c);
    private static void Error(string code, TestDelegate action) => Assert.That(Assert.Throws<ArtifactRuleException>(action)!.Code, Is.EqualTo(code));
    private static string Encode(ArtifactBoardState state) => JsonSerializer.Serialize(state);

    [SetUp] public void Setup() { pack = ArtifactDataPack.Load(Json); engine = new(pack); }

    [Test] public void PackCountsScopeAndUnknownsArePreserved()
    {
        Assert.Multiple(() => {
            Assert.That(pack.Manifest.Id, Is.EqualTo("TW-Mobile-2.3.3"));
            Assert.That(pack.Manifest.GameVersion, Is.EqualTo("2.3.3"));
            Assert.That(pack.CommonNodes.Count, Is.EqualTo(28));
            Assert.That(pack.ProfessionOverrides["LY"].Count, Is.EqualTo(10));
            Assert.That(pack.Manifest.LevelRecordCount, Is.EqualTo(303));
            Assert.That(pack.Manifest.DamageFormulaAttached, Is.False);
            Assert.That(pack.ProfessionOverrides.Keys, Is.EquivalentTo(new[] { "LY" }));
            Assert.That(pack.Diagnostics.Select(d => d.Code), Does.Contain("STALE_MAX_LEVEL"));
            Assert.That(pack.Diagnostics.Select(d => d.Code), Does.Contain("DUPLICATE_PROFESSION_WRAPPER"));
            Assert.That(pack.AllNodes.All(n => n.Levels.All(l => !string.IsNullOrEmpty(l.LevelRecordId))), Is.True);
        });
    }

    [Test] public void UniqueMinimumChainUsesRootOneCoreOneAndNormalFive()
    {
        var start = State(); var before = Encode(start);
        var result = engine.SetLevel(start, Id("CORE-U02"), 1);
        Assert.That(result.TotalCost, Is.EqualTo(20));
        var expected = new Dictionary<string, int> { [Id("ROOT-L")] = 1, [Id("M02")] = 5,
            [Id("M03")] = 5, [Id("CORE")] = 1, [Id("CORE-U01")] = 5, [Id("CORE-U02")] = 1 };
        Assert.That(result.State.Levels, Is.EquivalentTo(expected));
        Assert.That(Encode(start), Is.EqualTo(before), "caller state mutated");
        Assert.That(result.Changes.Sum(c => c.PointDelta), Is.EqualTo(result.PointDelta));
        Assert.That(result.State.Levels.Keys, Does.Not.Contain(Id("M05")), "cross-branch allocation");
        Assert.That(engine.SetLevel(result.State, Id("CORE-U02"), 1).PointDelta, Is.Zero);
        var raised = engine.SetLevel(result.State, Id("M02"), 8).State;
        Assert.That(engine.SetLevel(raised, Id("CORE-U02"), 2).State.Levels[Id("M02")], Is.EqualTo(8));
    }

    [Test] public void ParentSlotRequiresExplicitSelectionAndNeverInventsChoice()
    {
        var blank = engine.CreateState("LY"); var before = Encode(blank);
        Error("selection-required", () => engine.SetLevel(blank, Id("M07"), 1));
        Assert.That(Encode(blank), Is.EqualTo(before));
        var rootChosen = engine.SwitchOption(blank, Id("ROOT-L"), "LY-ROOT-B").State;
        var result = engine.SetLevel(rootChosen, Id("M07"), 1);
        Assert.That(result.TotalCost, Is.EqualTo(27));
        Assert.That(result.State.Levels[Id("CORE")], Is.EqualTo(1));
        Error("selection-required", () => engine.SetLevel(rootChosen, Id("COMMON-PATH-TIANRUI"), 1));
        var leaf = engine.SetLevel(rootChosen, Id("COMMON-PATH-TIANRUI"), 1,
            new Dictionary<string, string> { [Id("COMMON-SEL-OFFELE01")] = "OFFELE01-ELE" });
        Assert.That(leaf.State.Selections[Id("COMMON-SEL-OFFELE01")], Is.EqualTo("OFFELE01-ELE"));
    }

    [TestCase("BLUE-N01", 20)] [TestCase("BLUE-N02", 20)] [TestCase("BLUE-SEL01", 20)]
    public void QuestNodesHaveNoParentsAndTenLevels(string position, int expectedCost)
    {
        var id = Id(position); var n = engine.Node(id);
        Assert.That(n.ParentId, Is.Null); Assert.That(n.MaxLevel, Is.EqualTo(10));
        var start = State(); var result = engine.SetLevel(start, id, 10);
        Assert.That(result.State.Levels.Keys, Is.EquivalentTo(new[] { id }));
        Assert.That(result.TotalCost, Is.EqualTo(expectedCost));
        var locked = engine.SetQuestUnlocked(start, id, false);
        Error("quest-locked", () => engine.SetLevel(locked, id, 1));
        Error("quest-locked", () => engine.SetQuestUnlocked(result.State, id, false));
    }

    [TestCase("CORE-U03-L", "CORE-U03-R")] [TestCase("CORE-D03-L", "CORE-D03-R")]
    public void TwoPhysicalMutexPairsRejectAndExplicitlyRefundWithoutInheriting(string left, string right)
    {
        var initial = engine.SetLevel(State(), Id(left), 5).State; var before = Encode(initial);
        Error("mutual-exclusion", () => engine.SetLevel(initial, Id(right), 1));
        Assert.That(Encode(initial), Is.EqualTo(before));
        var replace = engine.ReplaceExclusive(initial, Id(right), 1);
        Assert.That(replace.State.Levels[Id(left)], Is.Zero);
        Assert.That(replace.State.Levels[Id(right)], Is.EqualTo(1));
        Assert.That(replace.PointDelta, Is.EqualTo(-8));
        Error("not-selectable", () => engine.SwitchOption(initial, Id(left), "anything"));
    }

    [TestCase("CLASS-LY-UP01", "CLASS-LY-UP02")] [TestCase("CLASS-LY-DN01", "CLASS-LY-DN02")]
    public void NonExclusiveClassPairsCoexist(string a, string b)
    {
        var s = engine.SetLevel(State(), Id(a), 5).State;
        s = engine.SetLevel(s, Id(b), 5).State;
        Assert.That(s.Levels[Id(a)], Is.EqualTo(5)); Assert.That(s.Levels[Id(b)], Is.EqualTo(5));
        Assert.That(engine.TotalCost(s), Is.EqualTo(31));
    }

    [Test] public void EverySingleSlotOptionCanSwitchAtZeroAndAtMaxWithoutCostOrLevelChange()
    {
        foreach (var node in pack.AllNodes.Where(n => n.Options.Count > 0))
        {
            var state = State();
            foreach (var option in node.Options)
            {
                state = engine.SwitchOption(state, node.Id, option.Id).State;
                Assert.That(state.Levels.GetValueOrDefault(node.Id), Is.Zero);
            }
            state = engine.SetLevel(state, node.Id, node.MaxLevel).State;
            foreach (var option in node.Options)
            {
                var r = engine.SwitchOption(state, node.Id, option.Id);
                Assert.That(r.State.Levels[node.Id], Is.EqualTo(node.MaxLevel)); Assert.That(r.PointDelta, Is.Zero);
                var summary = Summary(r.State, new() { Mode = ArtifactTriggerMode.Off });
                Assert.That(summary.Contributions.Where(c => c.NodeId == node.Id).Select(c => c.OptionId).Distinct(),
                    Is.SubsetOf(new[] { option.Id }));
                state = r.State;
            }
        }
    }

    [Test] public void StarAliasesAndTagEnhancementRemainSeparate()
    {
        var state = engine.SetLevel(State(), Id("BLUE-N02"), 10).State;
        state = engine.SetLevel(state, Id("COMMON-SEL-XINGZHAO"), 7).State;
        var burst = engine.SwitchOption(state, Id("COMMON-SEL-XINGZHAO"), "星照·碎瓊").State;
        Assert.That(burst.Selections[Id("COMMON-SEL-XINGZHAO")], Is.EqualTo("爆發"));
        var summary = Summary(burst);
        // Core-U/D paths are not allocated. 墨攻2500 all + burst1400; generic panel5600 never leaks here.
        Assert.That(summary.SkillEnhancementByTag["burst"], Is.EqualTo(3900));
        Assert.That(summary.SkillEnhancementByTag["single"], Is.EqualTo(2500));
        var all = Summary(engine.SwitchOption(burst, Id("COMMON-SEL-XINGZHAO"), "全技能").State);
        Assert.That(all.SkillEnhancementByTag.Values, Is.All.EqualTo(3200));
        Assert.That(all.StaticAttributes.ContainsKey("skillEnhancement.burst"), Is.False);
        Assert.That(summary.StaticAttributes["bossSuppression"], Is.EqualTo(385));
    }

    [Test] public void SignedNegativeValuesAreNotClampedAndOldChoiceDoesNotAccumulate()
    {
        var mogong = Summary(engine.SetLevel(State(), Id("BLUE-N02"), 10).State);
        Assert.That(mogong.StaticAttributes["hit"], Is.EqualTo(-400));
        var s = engine.SetLevel(State(), Id("COMMON-SEL-OFFELE01"), 7).State;
        var atk = Summary(s);
        Assert.That(atk.Contributions.Single(c => c.NodeId == Id("COMMON-SEL-OFFELE01") && c.Key == "elementAttack").Amount, Is.EqualTo(-315));
        var ele = Summary(engine.SwitchOption(s, Id("COMMON-SEL-OFFELE01"), "OFFELE01-ELE").State);
        Assert.That(ele.StaticAttributes["attack"], Is.EqualTo(-490));
        Assert.That(ele.Contributions.Single(c => c.NodeId == Id("COMMON-SEL-OFFELE01") && c.Key == "elementAttack").Amount, Is.EqualTo(770));
    }

    [TestCase("CLASS-LY-UP02", "jingLei.cooldownReduction", new double[] { 1, 1.5, 2, 2.5, 3 })]
    [TestCase("CLASS-LY-DN02", "jianDang.attackSpeedIncrease", new double[] { 15, 20, 25, 30, 35 })]
    [TestCase("CORE-D03-L", "longFei.damageReduction", new double[] { 20, 30, 40, 50, 60 })]
    public void NonlinearGrowthUsesEveryExactLevelAndIsNotGlobal(string position, string key, double[] expected)
    {
        for (var level = 1; level <= 5; level++)
        {
            var result = Summary(engine.SetLevel(State(), Id(position), level).State);
            Assert.That(result.ScopedEffects.Single(c => c.Key == key).Amount, Is.EqualTo((decimal)expected[level - 1]));
            Assert.That(result.CombinedAttributes.ContainsKey(key), Is.False);
        }
    }

    [Test] public void TargetProfessionCanSwitchAcrossAllNineButNeverAddsToGenericSuppression()
    {
        var s = engine.SetLevel(State(), Id("COMMON-SEL-MINGGE"), 7).State;
        s = engine.SetLevel(s, Id("COMMON-SEL-ZHIBI"), 7).State;
        s = engine.SwitchOption(s, Id("COMMON-SEL-MINGGE"), "玄機").State;
        var summary = Summary(s);
        Assert.That(summary.TargetProfessionModifiers["玄機"]["targetProfessionSuppression"], Is.EqualTo(10.5m));
        Assert.That(summary.TargetProfessionModifiers["碎夢"]["targetProfessionResistance"], Is.EqualTo(10.5m));
        Assert.That(summary.StaticAttributes.ContainsKey("targetProfessionSuppression"), Is.False);
        Error("invalid-option", () => engine.SwitchOption(s, Id("COMMON-SEL-MINGGE"), "滄瀾"));
    }

    [Test] public void FailedBudgetInvalidLevelVersionAndParentDowngradeAreAtomic()
    {
        var s = State(19); var before = Encode(s);
        Error("insufficient-points", () => engine.SetLevel(s, Id("CORE-U02"), 1));
        Assert.That(Encode(s), Is.EqualTo(before));
        Error("invalid-level", () => engine.SetLevel(s, Id("ROOT-L"), 2));
        Error("invalid-level", () => engine.SetLevel(s, Id("BLUE-N01"), -1));
        Error("unknown-node", () => engine.SetLevel(s, "invented", 1));
        var raised = engine.SetLevel(State(), Id("M07"), 1).State;
        Error("parent-level", () => engine.SetLevel(raised, Id("M06"), 4));
        var badVersion = raised.Copy(); badVersion.PackId = "CN-PC";
        Error("version-mismatch", () => Summary(badVersion));
    }

    [Test] public void OtherProfessionMayUseSharedQuestButNeverBorrowLongYinRoot()
    {
        var ty = engine.CreateState("TY");
        Assert.That(engine.SetLevel(ty, Id("BLUE-N02"), 3).TotalCost, Is.EqualTo(6));
        Error("unsupported-profession", () => engine.SetLevel(ty, Id("CLASS-LY-UP02"), 1));
        Error("unsupported-profession", () => engine.SetLevel(ty, Id("M03"), 1));
    }

    [Test] public void PvpConditionalBuffHonorsStrictBoundaryTimerManualAndOff()
    {
        var s = engine.SetLevel(State(), Id("COMMON-SEL-PVP01"), 7).State;
        Assert.That(Summary(s).CompleteForRequestedContext, Is.False);
        Assert.That(Summary(s, new() { NearbyEnemyPlayers = 5 }).TriggeredAttributes.GetValueOrDefault("professionResistance"), Is.Zero);
        Assert.That(Summary(s, new() { NearbyEnemyPlayers = 6 }).TriggeredAttributes["professionResistance"], Is.EqualTo(315));
        Assert.That(Summary(s, new() { NearbyEnemyPlayers = 0, PvpBuffRemainingSeconds = 1 }).TriggeredAttributes["professionResistance"], Is.EqualTo(315));
        Assert.That(Summary(s, new() { NearbyEnemyPlayers = 0, PvpBuffRemainingSeconds = 0 }).TriggeredAttributes.GetValueOrDefault("professionResistance"), Is.Zero);
        Assert.That(Summary(s, new() { Mode = ArtifactTriggerMode.Off, NearbyEnemyPlayers = 6 }).TriggeredAttributes.Count, Is.Zero);
        var manual = new ArtifactTriggerContext { Mode = ArtifactTriggerMode.Manual, ManualActive = new() { [Id("COMMON-SEL-PVP01")] = true } };
        Assert.That(Summary(s, manual).TriggeredAttributes["professionResistance"], Is.EqualTo(315));
        Assert.That(Summary(s, new() { Mode = ArtifactTriggerMode.Manual }).PendingEffects.Count, Is.EqualTo(1));
    }

    [Test] public void CoreMaxBonusRequiresExplicitSumAndRoundingRatherThanInventedFiveDimensions()
    {
        var four = engine.SetLevel(State(), Id("CORE"), 4).State;
        Assert.That(Summary(four).TriggeredAttributes.Count, Is.Zero);
        var five = engine.SetLevel(four, Id("CORE"), 5).State;
        Assert.That(Summary(five).PendingEffects.Single().Kind, Is.EqualTo("coreSuppressionAtMax"));
        Assert.That(Summary(five).ScopedEffects.Single(c => c.Key == "fiveDimensions.displayIncrement").Amount, Is.EqualTo(45));
        var c = new ArtifactTriggerContext { VerifiedFiveDimensionSum = 1234, CoreRoundingPolicy = ArtifactRoundingPolicy.UnroundedCandidate };
        Assert.That(Summary(five, c).TriggeredAttributes["bossSuppression"], Is.EqualTo(123.4m));
        var floor = new ArtifactTriggerContext { VerifiedFiveDimensionSum = 1234, CoreRoundingPolicy = ArtifactRoundingPolicy.Floor };
        Assert.That(Summary(five, floor).TriggeredAttributes["professionSuppression"], Is.EqualTo(123));
        Assert.That(Summary(five, new() { VerifiedFiveDimensionSum = 1234 }).PendingEffects.Count, Is.EqualTo(1));
        Assert.That(Summary(five, new() { Mode = ArtifactTriggerMode.Off }).TriggeredAttributes.Count, Is.Zero);
    }

    [Test] public void InteractionBuffUsesExplicitActualStacksNotMaximumCoverage()
    {
        var s = engine.SetLevel(State(), Id("CLASS-LY-UP02"), 5).State;
        Assert.That(Summary(s).PendingEffects.Single().Kind, Is.EqualTo("interactionStacks"));
        Assert.That(Summary(s, new() { InteractionStacks = 2 }).TriggeredAttributes["hit"], Is.EqualTo(80));
        Assert.That(Summary(s, new() { InteractionStacks = 2 }).TriggeredAttributes["bossSuppression"], Is.EqualTo(240));
        Assert.That(Summary(s, new() { InteractionStacks = 0 }).TriggeredAttributes["hit"], Is.Zero);
        Error("invalid-stacks", () => Summary(s, new() { InteractionStacks = 6 }));
        Error("invalid-context", () => Summary(s, new() { InteractionStacks = -1 }));
        Assert.That(Summary(s, new() { Mode = ArtifactTriggerMode.Off, InteractionStacks = 5 }).TriggeredAttributes.Count, Is.Zero);
    }

    [Test] public void DefinitionRejectsCyclesMissingParentsMissingLevelsBadUnitsAndMutexOnSingleSlot()
    {
        ArtifactDataPack Fresh() => ArtifactDataPack.Load(Json);
        var p = Fresh(); p.AllNodes.Single(n => n.PositionId == "M02").ParentId = Id("M03");
        p.AllNodes.Single(n => n.PositionId == "M02").RequiredParentLevel = 5;
        Assert.That(Assert.Throws<ArtifactRuleException>(p.ValidateDefinition)!.Message, Does.Contain("父鏈循環"));
        p = Fresh(); p.AllNodes.Single(n => n.PositionId == "M02").ParentId = "missing";
        Error("invalid-pack", p.ValidateDefinition);
        p = Fresh(); p.AllNodes.First().Levels.RemoveAt(0); Error("invalid-pack", p.ValidateDefinition);
        p = Fresh(); p.AllNodes.First().Levels[0].Effects[0].Unit = "unknown"; Error("invalid-pack", p.ValidateDefinition);
        p = Fresh(); p.AllNodes.Single(n => n.PositionId == "COMMON-SEL-OFFELE01").MutualExclusionGroup = "bad";
        Error("invalid-pack", p.ValidateDefinition);
    }

    [Test] public void SerializationRoundTripAndRepeatedSummaryAreDeterministic()
    {
        var s = engine.SetLevel(State(), Id("BLUE-N02"), 4).State;
        var restored = JsonSerializer.Deserialize<ArtifactBoardState>(Encode(s))!;
        Assert.That(Encode(restored), Is.EqualTo(Encode(s)));
        var a = JsonSerializer.Serialize(Summary(restored));
        Assert.That(JsonSerializer.Serialize(Summary(restored)), Is.EqualTo(a));
        Assert.That(engine.SetLevel(restored, Id("BLUE-N02"), 0).PointDelta, Is.EqualTo(-8));
    }

    [Test] public void SwitchingParentOptionsPreservesAllocatedDescendants()
    {
        var s = engine.SetLevel(State(), Id("COMMON-PATH-TIANRUI"), 4).State;
        var levels = new Dictionary<string, int>(s.Levels);
        var cost = engine.TotalCost(s);
        s = engine.SwitchOption(s, Id("ROOT-L"), "LY-ROOT-B").State;
        s = engine.SwitchOption(s, Id("COMMON-SEL-OFFELE01"), "OFFELE01-ELE").State;
        Assert.That(s.Levels, Is.EquivalentTo(levels));
        Assert.That(engine.TotalCost(s), Is.EqualTo(cost));
        Assert.That(s.Levels[Id("COMMON-PATH-TIANRUI")], Is.EqualTo(4));
        engine.Validate(s);
    }
}
