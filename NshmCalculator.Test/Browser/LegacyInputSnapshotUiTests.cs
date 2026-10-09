using System.Text.Json;
using Microsoft.Playwright.NUnit;

namespace NshmCalculator.Test.Browser;

[NonParallelizable]
public class LegacyInputSnapshotUiTests : PageTest
{
    private async Task Open()
    {
        await Page.GotoAsync(Environment.GetEnvironmentVariable("BASE_URL") ?? "http://localhost:5218");
        await Expect(Page.GetByTestId("generate-mapped-inputs")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
    private async Task Fill(string key, string value, string build = "A")
    {
        await Page.GetByTestId($"observed-{build}-{key}").FillAsync(value);
        await Page.GetByTestId($"observed-{build}-{key}").PressAsync("Tab");
    }
    private async Task<JsonDocument> SnapshotJson()
    {
        await Page.GetByText("完整 A/B 輸入、逐欄来源及排除清單 JSON", new() { Exact = true }).ClickAsync();
        return JsonDocument.Parse(await Page.GetByTestId("mapped-input-json").InputValueAsync());
    }

    [Test] public async Task MissingTwBaseShowsUnknownAndNeverUsesLegacyDefaults()
    {
        await Open(); await Page.GetByTestId("capture-anchor").ClickAsync(); await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_001'] [data-mapped-build='A']")).ToHaveTextAsync("未知（不寫入）");
        using var json = await SnapshotJson(); var a = json.RootElement.GetProperty("A");
        Assert.That(a.GetProperty("Inputs").TryGetProperty("ST_001", out _), Is.False);
        Assert.That(a.GetProperty("MissingInputs").EnumerateArray().Select(v => v.GetString()), Contains.Item("ST_001"));
        Assert.That(a.GetProperty("Inputs").GetProperty("SQ_004").GetProperty("NumberValue").GetDouble(), Is.Zero);
        Assert.That(a.GetProperty("EngineEvaluationAllowed").GetBoolean(), Is.False);
        Assert.That(a.GetProperty("EmbeddedFormulaConflicts")[0].GetProperty("Code").GetString(), Is.EqualTo("FZJ_010"));
        Assert.That(a.TryGetProperty("Dps", out _), Is.False);
        await Page.GetByText("完整 TW A/B 執行輸入與隔離證據 JSON", new() { Exact = true }).ClickAsync();
        using var tw = JsonDocument.Parse(await Page.GetByTestId("tw-execution-json").InputValueAsync());
        var execution = tw.RootElement.GetProperty("A");
        Assert.That(execution.GetProperty("Inputs").TryGetProperty("SQ_004", out _), Is.False);
        Assert.That(execution.GetProperty("Inputs").GetProperty("TW_ART_PROF_DAMAGE_RATIO").GetProperty("NumberValue").GetDouble(), Is.Zero);
        Assert.That(execution.GetProperty("Isolation").GetProperty("TwFormula").GetString(), Does.Not.Contain("0.05*[SH_001]"));
        await Expect(Page.GetByText("比較 Legacy 模型值",new() { Exact=true })).ToHaveCountAsync(0);
        Assert.That(await Page.GetByText("Build 比較結果", new() { Exact = true }).CountAsync(), Is.Zero);
    }

    [Test] public async Task ABMappingShowsSourceUnitsSignedArtifactAndInvalidatesAfterEdit()
    {
        await Open(); await Fill("hit", "1000");
        await Fill("criticalDamage", "175"); await Fill("bossSuppressionPercent", "5");
        var node = Page.Locator("[data-position='BLUE-N02']"); await node.Locator(".node-controls button").Last.ClickAsync();
        await Page.GetByTestId("capture-anchor").ClickAsync();
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button,new(){Name="編輯 Build B 神器",Exact=true}).ClickAsync();
        await node.Locator(".node-controls button").First.ClickAsync();
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='A']")).ToHaveTextAsync("1000");
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-field='difference']")).ToHaveTextAsync("40");
        await Expect(Page.Locator("[data-mapped-code='ST_009'] [data-mapped-build='A']")).ToHaveTextAsync("1.75");
        await Expect(Page.Locator("[data-mapped-code='ST_010'] [data-mapped-build='A']")).ToHaveTextAsync("0.05");
        await Expect(Page.Locator("[data-mapped-code='ST_008']")).ToContainTextAsync("hit");
        await Fill("hit", "1200"); await Expect(Page.GetByTestId("legacy-input-snapshots")).ToHaveCountAsync(0);
        await Page.GetByTestId("capture-anchor").ClickAsync();
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='A']")).ToHaveTextAsync("1200");
    }

    [Test] public async Task FullJsonContainsAllSourceRowsAndPreservesSeparateTags()
    {
        await Open(); await Fill("skillEnhancement.all", "5600"); await Fill("skillEnhancement.burst", "200");
        await Page.GetByTestId("capture-anchor").ClickAsync();
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        using var json = await SnapshotJson(); var a = json.RootElement.GetProperty("A");
        Assert.That(a.GetProperty("Fields").GetArrayLength(), Is.EqualTo(163));
        Assert.That(a.GetProperty("Inputs").GetProperty("ST_015").GetProperty("NumberValue").GetDouble(), Is.EqualTo(5600));
        Assert.That(a.GetProperty("TaggedSkillEnhancement").GetProperty("burst").GetDecimal(), Is.EqualTo(200));
        Assert.That(a.GetProperty("EffectiveTaggedSkillEnhancement").GetProperty("burst").GetDecimal(), Is.EqualTo(5800));
        Assert.That(a.GetProperty("ExcludedResultCodes").GetArrayLength(), Is.EqualTo(28));
    }
    [Test] public async Task NewProfessionNodeCoefficientAndFiveInputsUseTwPipeline()
    {
        await Open();
        await Page.GetByText("目前遊戲實際五維（已含目前眾法歸一）",new() { Exact=true }).First.ClickAsync();
        foreach(var key in new[]{"constitution","strength","spirit","agility","endurance"}) {
            await Page.GetByTestId($"five-A-{key}").FillAsync("104");
            await Page.GetByTestId($"five-A-{key}").PressAsync("Tab");
        }
        var node = Page.Locator("[data-position='M07']");
        await node.Locator(".node-controls button").Last.ClickAsync();
        await Page.GetByTestId("capture-anchor").ClickAsync();
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Page.GetByText("完整 TW A/B 執行輸入與隔離證據 JSON",new() { Exact=true }).ClickAsync();
        using var json=JsonDocument.Parse(await Page.GetByTestId("tw-execution-json").InputValueAsync());
        var a=json.RootElement.GetProperty("A");
        Assert.That(a.GetProperty("Inputs").GetProperty("TW_ART_PROF_DAMAGE_RATIO").GetProperty("NumberValue").GetDouble(),Is.EqualTo(.01));
        Assert.That(a.GetProperty("Artifact").GetProperty("FiveDimensions").GetProperty("Dimensions")[0].GetProperty("Base").GetDecimal(),Is.EqualTo(104));
        Assert.That(a.GetProperty("Isolation").GetProperty("RemovedResultFormulas").GetArrayLength(),Is.EqualTo(28));
    }

    [Test] public async Task HeroHonorShowsVerifiedDisplaysWithoutChangingAnchor()
    {
        await Open(); await Fill("defensePenetration","16057");await Fill("ignoreElementResistance","4051");
        await Page.GetByTestId("capture-anchor").ClickAsync();
        var before=await Page.GetByTestId("anchor-status").TextContentAsync();
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.GetByTestId("target-ratios-a")).ToContainTextAsync("54.4%");
        await Expect(Page.GetByTestId("target-ratios-a")).ToContainTextAsync("77.1%");
        await Page.GetByTestId("tw-target-select").SelectOptionAsync("Honor");
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.GetByTestId("target-ratios-a")).ToContainTextAsync("55.9%");
        await Expect(Page.GetByTestId("target-ratios-a")).ToContainTextAsync("79.2%");
        await Expect(Page.GetByTestId("anchor-status")).ToHaveTextAsync(before!);
        await Expect(Page.GetByTestId("tw-target-model")).ToContainTextAsync("研究候選");
        await Expect(Page.GetByTestId("observed-B-attack")).ToHaveCountAsync(0);
    }
    [Test] public async Task CurrentEditRequiresExplicitRecaptureButCandidateEditDoesNot()
    {
        await Open();await Fill("hit","1000");await Page.GetByTestId("capture-anchor").ClickAsync();
        var before=await Page.GetByTestId("anchor-status").TextContentAsync();
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button,new(){Name="編輯 Build B 神器",Exact=true}).ClickAsync();
        await Page.Locator("[data-position='BLUE-N02'] .node-controls button").Last.ClickAsync();
        await Expect(Page.GetByTestId("anchor-status")).ToHaveTextAsync(before!);
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='A']")).ToHaveTextAsync("1000");
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='B']")).ToHaveTextAsync("960");
        await Fill("hit","1200");await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.GetByText("目前實際資料已變更，請明確更新錨點；不自動覆寫",new(){Exact=true})).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("tw-artifact-execution")).ToHaveCountAsync(0);
        await Page.GetByTestId("capture-anchor").ClickAsync();await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='B']")).ToHaveTextAsync("1160");
    }

}
