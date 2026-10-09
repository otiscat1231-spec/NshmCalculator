using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace NshmCalculator.Test.Browser;

/// <summary>Real WASM rendering, click commands, signed panel projection and local-storage round trip.</summary>
[NonParallelizable]
public class ArtifactBoardUiTests : PageTest
{
    private string BaseUrl => Environment.GetEnvironmentVariable("BASE_URL") ?? "http://localhost:5218";
    private ILocator Slot(string position) => Page.Locator($"[data-position='{position}']");
    private ILocator Add(string position) => Slot(position).Locator(".node-controls button").Last;
    private ILocator Panel(string key, string field) => Page.Locator($"[data-attribute='{key}'] [data-field='{field}']");
    private async Task Open()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("artifact-board")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(Page.Locator(".artifact-node")).ToHaveCountAsync(38);
    }
    private Task Level(string position, int level) => Expect(Slot(position)).ToHaveAttributeAsync("data-level", level.ToString());

    [Test] public async Task RearNodeAutoFillsUniqueChainShowsProvenanceAndRejectsParentDowngrade()
    {
        await Open(); await Add("CORE-U02").ClickAsync();
        await Level("ROOT-L", 1); await Level("M02", 5); await Level("M03", 5);
        await Level("CORE", 1); await Level("CORE-U01", 5); await Level("CORE-U02", 1);
        await Level("M05", 0);
        await Expect(Page.GetByTestId("artifact-total")).ToHaveTextAsync("總神器點數：20 點");
        await Expect(Slot("CORE")).ToHaveAttributeAsync("data-auto-level", "1");
        await Expect(Page.GetByTestId("artifact-autofill")).ToContainTextAsync("Lv0→5");
        await Slot("CORE-U01").Locator(".node-controls button").First.ClickAsync();
        await Expect(Page.GetByTestId("artifact-error")).ToContainTextAsync("父節點Lv5");
        await Level("CORE-U01", 5);
        var screenDir = Environment.GetEnvironmentVariable("ARTIFACT_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenDir))
        {
            Directory.CreateDirectory(screenDir);
            await Page.GetByTestId("artifact-board").ScreenshotAsync(new() { Path = Path.Combine(screenDir, "phase2b-desktop.png") });
        }
    }

    [Test] public async Task PartiallyAllocatedParentShowsOnlyAutoAddedLevelsAndKeepsMarkerAfterManualAddition()
    {
        await Open();
        for (var i = 0; i < 3; i++) await Add("M02").ClickAsync();
        await Level("M02", 3); await Expect(Slot("M02")).ToHaveAttributeAsync("data-auto-level", "0");
        await Add("M03").ClickAsync();
        await Level("M02", 5); await Expect(Slot("M02")).ToHaveAttributeAsync("data-auto-level", "2");
        await Expect(Slot("M02")).ToContainTextAsync("自動補 2 級");
        await Add("M02").ClickAsync();
        await Level("M02", 6); await Expect(Slot("M02")).ToHaveAttributeAsync("data-auto-level", "2");
    }

    [Test] public async Task BlueQuestHasNoParentsAndSupportsAddSubtractAndLock()
    {
        await Open(); await Add("BLUE-N02").ClickAsync(); await Add("BLUE-N02").ClickAsync();
        await Level("BLUE-N02", 2); await Level("ROOT-L", 0); await Level("CORE", 0);
        await Expect(Page.GetByTestId("artifact-total")).ToHaveTextAsync("總神器點數：4 點");
        await Expect(Slot("BLUE-N02")).ToContainTextAsync("/ 10");
        await Slot("BLUE-N02").Locator(".node-controls button").First.ClickAsync();
        await Slot("BLUE-N02").Locator(".node-controls button").First.ClickAsync();
        await Slot("BLUE-N02").Locator("input[type=checkbox]").UncheckAsync();
        await Expect(Add("BLUE-N02")).ToBeDisabledAsync();
        await Slot("BLUE-N02").Locator("input[type=checkbox]").CheckAsync();
        await Expect(Add("BLUE-N02")).ToBeEnabledAsync();
    }

    [TestCase("CORE-U03-L", "CORE-U03-R")]
    [TestCase("CORE-D03-L", "CORE-D03-R")]
    public async Task BothPhysicalMutexPairsRequireExplicitSwap(string left, string right)
    {
        await Open(); await Add(left).ClickAsync();
        await Expect(Add(right)).ToBeDisabledAsync();
        await Slot(right).GetByRole(AriaRole.Button, new() { Name = "改點這邊（退還另一邊）" }).ClickAsync();
        await Level(left, 0); await Level(right, 1);
        await Expect(Add(left)).ToBeDisabledAsync();
    }

    [TestCase("CLASS-LY-UP01", "CLASS-LY-UP02")]
    [TestCase("CLASS-LY-DN01", "CLASS-LY-DN02")]
    public async Task BothNonExclusivePairsCanBeAllocatedTogether(string left, string right)
    {
        await Open(); await Add(left).ClickAsync(); await Add(right).ClickAsync();
        await Level(left, 1); await Level(right, 1);
        await Expect(Add(left)).ToBeEnabledAsync();
    }

    [Test] public async Task SwitchingSignedOptionUpdatesABAndSaveReloadDoesNotApplyTwice()
    {
        await Open();
        await Page.GetByTestId("base-A-attack").FillAsync("100");
        await Page.GetByTestId("base-A-attack").BlurAsync();
        await Page.GetByTestId("base-B-attack").FillAsync("100");
        await Page.GetByTestId("base-B-attack").BlurAsync();
        await Slot("COMMON-SEL-OFFELE01").Locator("select").SelectOptionAsync("OFFELE01-ATK");
        await Add("COMMON-SEL-OFFELE01").ClickAsync();
        await Expect(Panel("attack", "final-a")).ToHaveTextAsync("270");
        await Slot("COMMON-SEL-OFFELE01").Locator("select").SelectOptionAsync("OFFELE01-ELE");
        await Level("COMMON-SEL-OFFELE01", 1);
        await Expect(Panel("attack", "final-a")).ToHaveTextAsync("30");
        await Expect(Panel("attack", "final-b")).ToHaveTextAsync("100");
        await Expect(Panel("attack", "difference")).ToHaveTextAsync("+70");
        await Page.GetByRole(AriaRole.Button, new() { Name = "編輯 Build B 神器", Exact = true }).ClickAsync();
        await Level("COMMON-SEL-OFFELE01", 0);
        await Add("BLUE-N02").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "儲存此流派 A/B", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("已儲存此流派 A/B 基礎面板與神器盤。")).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(Panel("attack", "final-a")).ToHaveTextAsync("30", new() { Timeout = 60_000 });
        await Expect(Panel("attack", "final-b")).ToHaveTextAsync("100");
        await Expect(Slot("CORE")).ToHaveAttributeAsync("data-auto-level", "1");
        await Expect(Panel("hit", "final-a")).ToHaveTextAsync("未知");
    }

    [Test] public async Task EverySingleSlotSwitchesAtZeroAndAfterAllocationWithoutChangingLevelsOrCost()
    {
        await Open();
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/data/artifacts/TW-Mobile-2.3.3/artifact-pack.json");
        using var pack = JsonDocument.Parse(await response.TextAsync());
        var nodes = pack.RootElement.GetProperty("CommonNodes").EnumerateArray()
            .Concat(pack.RootElement.GetProperty("ProfessionOverrides").GetProperty("LY").EnumerateArray())
            .Where(n => n.GetProperty("Options").GetArrayLength() > 0).ToArray();
        foreach (var node in nodes)
        {
            var position = node.GetProperty("PositionId").GetString()!;
            var select = Slot(position).Locator("select");
            foreach (var option in node.GetProperty("Options").EnumerateArray())
            {
                await select.SelectOptionAsync(option.GetProperty("Id").GetString()!);
                await Level(position, 0);
            }
        }
        foreach (var node in nodes)
        {
            var position = node.GetProperty("PositionId").GetString()!;
            if (await Slot(position).GetAttributeAsync("data-level") == "0") await Add(position).ClickAsync();
            var level = await Slot(position).GetAttributeAsync("data-level");
            var total = await Page.GetByTestId("artifact-total").TextContentAsync();
            foreach (var option in node.GetProperty("Options").EnumerateArray())
            {
                await Slot(position).Locator("select").SelectOptionAsync(option.GetProperty("Id").GetString()!);
                await Expect(Slot(position)).ToHaveAttributeAsync("data-level", level!);
                await Expect(Page.GetByTestId("artifact-total")).ToHaveTextAsync(total!);
            }
        }
    }

    [Test] public async Task MobileKeepsAllSlotsAndScrollInsideBoardWithoutPageOverflow()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Open(); await Add("BLUE-N01").ClickAsync();
        await Level("BLUE-N01", 1); await Level("ROOT-L", 0);
        Assert.That(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), Is.True);
        var screenDir = Environment.GetEnvironmentVariable("ARTIFACT_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(screenDir))
        {
            Directory.CreateDirectory(screenDir);
            await Page.GetByTestId("artifact-board").ScreenshotAsync(new() { Path = Path.Combine(screenDir, "phase2b-mobile.png") });
        }
    }

    [TearDown] public async Task CaptureFailure()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Failed) return;
        var screenDir = Environment.GetEnvironmentVariable("ARTIFACT_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenDir)) return;
        Directory.CreateDirectory(screenDir);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(screenDir, "phase2b-failure.png"), FullPage = true });
    }
}
