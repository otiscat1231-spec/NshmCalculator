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
        await Page.GetByTestId($"base-{build}-{key}").FillAsync(value);
        await Page.GetByTestId($"base-{build}-{key}").PressAsync("Tab");
    }
    private async Task<JsonDocument> SnapshotJson()
    {
        await Page.GetByText("完整 A/B 輸入、逐欄来源及排除清單 JSON", new() { Exact = true }).ClickAsync();
        return JsonDocument.Parse(await Page.GetByTestId("mapped-input-json").InputValueAsync());
    }

    [Test] public async Task MissingTwBaseShowsUnknownAndNeverUsesLegacyDefaults()
    {
        await Open(); await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_001'] [data-mapped-build='A']")).ToHaveTextAsync("未知（不寫入）");
        using var json = await SnapshotJson(); var a = json.RootElement.GetProperty("A");
        Assert.That(a.GetProperty("Inputs").TryGetProperty("ST_001", out _), Is.False);
        Assert.That(a.GetProperty("MissingInputs").EnumerateArray().Select(v => v.GetString()), Contains.Item("ST_001"));
        Assert.That(a.GetProperty("Inputs").GetProperty("SQ_004").GetProperty("NumberValue").GetDouble(), Is.Zero);
        Assert.That(a.GetProperty("EngineEvaluationAllowed").GetBoolean(), Is.False);
        Assert.That(a.GetProperty("EmbeddedFormulaConflicts")[0].GetProperty("Code").GetString(), Is.EqualTo("FZJ_010"));
        Assert.That(a.TryGetProperty("Dps", out _), Is.False);
        Assert.That(await Page.GetByText("Build 比較結果", new() { Exact = true }).CountAsync(), Is.Zero);
    }

    [Test] public async Task ABMappingShowsSourceUnitsSignedArtifactAndInvalidatesAfterEdit()
    {
        await Open(); await Fill("hit", "1000"); await Fill("hit", "1100", "B");
        await Fill("criticalDamage", "175"); await Fill("bossSuppressionPercent", "5");
        var node = Page.Locator("[data-position='BLUE-N02']"); await node.Locator(".node-controls button").Last.ClickAsync();
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='A']")).ToHaveTextAsync("960");
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-field='difference']")).ToHaveTextAsync("140");
        await Expect(Page.Locator("[data-mapped-code='ST_009'] [data-mapped-build='A']")).ToHaveTextAsync("1.75");
        await Expect(Page.Locator("[data-mapped-code='ST_010'] [data-mapped-build='A']")).ToHaveTextAsync("0.05");
        await Expect(Page.Locator("[data-mapped-code='ST_008']")).ToContainTextAsync("hit");
        await Fill("hit", "1200"); await Expect(Page.GetByTestId("legacy-input-snapshots")).ToHaveCountAsync(0);
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        await Expect(Page.Locator("[data-mapped-code='ST_008'] [data-mapped-build='A']")).ToHaveTextAsync("1160");
    }

    [Test] public async Task FullJsonContainsAllSourceRowsAndPreservesSeparateTags()
    {
        await Open(); await Fill("skillEnhancement.all", "5600"); await Fill("skillEnhancement.burst", "200");
        await Page.GetByTestId("generate-mapped-inputs").ClickAsync();
        using var json = await SnapshotJson(); var a = json.RootElement.GetProperty("A");
        Assert.That(a.GetProperty("Fields").GetArrayLength(), Is.EqualTo(163));
        Assert.That(a.GetProperty("Inputs").GetProperty("ST_015").GetProperty("NumberValue").GetDouble(), Is.EqualTo(5600));
        Assert.That(a.GetProperty("TaggedSkillEnhancement").GetProperty("burst").GetDecimal(), Is.EqualTo(200));
        Assert.That(a.GetProperty("EffectiveTaggedSkillEnhancement").GetProperty("burst").GetDecimal(), Is.EqualTo(5800));
        Assert.That(a.GetProperty("ExcludedResultCodes").GetArrayLength(), Is.EqualTo(28));
    }
}
