namespace NshmCalculator.MudClient.Resources;

public static class TwPveText
{
    public const string Title = "逆水寒 PVE 傷害計算器";
    public const string Uncalibrated = "台服校準：待驗證";
    public const string LegacyExample = "目前載入 Legacy 範例參數；請填入自己的面板與進階設定。";
    public static readonly (string Code, string Label)[] CommonInputs = {
        ("ST_001", "攻擊"), ("ST_004", "破防"), ("ST_015", "技能增強"),
        ("ST_007", "會心"), ("ST_009", "會心傷害（小數倍率）"), ("ST_008", "命中"),
        ("ST_005", "元素攻擊"), ("ST_006", "忽視元素抗性"),
        ("ST_002", "首領克制（Repo輸入）"), ("ST_003", "技巧克制"),
        ("ST_010", "首領克制%（小數）")
    };
}
