namespace NshmCalculator.Shared.Research.Artifacts;

public enum ArtifactTriggerMode { Auto, Manual, Off }
public enum ArtifactRoundingPolicy { UnroundedCandidate, Floor, Ceiling, AwayFromZero }
public sealed class ArtifactTriggerContext
{
    public ArtifactTriggerMode Mode { get; init; } = ArtifactTriggerMode.Auto;
    public Dictionary<string, ArtifactTriggerMode> NodeModes { get; init; } = new();
    public Dictionary<string, bool> ManualActive { get; init; } = new();
    public int? NearbyEnemyPlayers { get; init; }
    // A combat adapter may explicitly supply a remaining timer; this engine does not simulate time.
    public decimal? PvpBuffRemainingSeconds { get; init; }
    public int? InteractionStacks { get; init; }
    // Caller must supply a verified total including/excluding artifact gains correctly; never inferred from display +9.
    public decimal? VerifiedFiveDimensionSum { get; init; }
    public ArtifactRoundingPolicy? CoreRoundingPolicy { get; init; }
}
public sealed record ArtifactContribution(string NodeId, string OptionId, string Key, decimal Amount,
    string Unit, string Scope, string Status, string Source);
public sealed record ArtifactPendingEffect(string NodeId, string Kind, string Reason);
public sealed class ArtifactSummary
{
    public Dictionary<string, decimal> StaticAttributes { get; } = new();
    public Dictionary<string, decimal> TriggeredAttributes { get; } = new();
    public Dictionary<string, decimal> CombinedAttributes { get; } = new();
    public Dictionary<string, decimal> SkillEnhancementByTag { get; } = new();
    public Dictionary<string, Dictionary<string, decimal>> TargetProfessionModifiers { get; } = new();
    public List<ArtifactContribution> Contributions { get; } = new();
    // Scoped skill percentages/seconds/mechanics are reported independently. No unverified damage multiplication.
    public List<ArtifactContribution> ScopedEffects { get; } = new();
    public Dictionary<string, ArtifactTriggerMode> ScopedTriggerModes { get; } = new();
    public List<ArtifactPendingEffect> UnmodeledMechanics { get; } = new();
    public List<ArtifactPendingEffect> PendingEffects { get; } = new();
    public List<string> Warnings { get; } = new();
    public int TotalCost { get; internal set; }
    public bool CompleteForRequestedContext => PendingEffects.Count == 0;
}

public sealed class ArtifactAggregator(ArtifactBoardEngine board)
{
    public ArtifactSummary Summarize(ArtifactBoardState state, ArtifactTriggerContext? context = null)
    {
        board.Validate(state);
        context ??= new();
        if (!Enum.IsDefined(context.Mode) || context.NodeModes.Any(x => !Enum.IsDefined(x.Value)))
            throw new ArtifactRuleException("invalid-trigger-mode", "未知觸發模式");
        foreach (var id in context.NodeModes.Keys.Concat(context.ManualActive.Keys)) board.Node(id);
        if (context.NearbyEnemyPlayers < 0 || context.InteractionStacks < 0 || context.PvpBuffRemainingSeconds < 0
            || context.PvpBuffRemainingSeconds > 10 || context.VerifiedFiveDimensionSum < 0)
            throw new ArtifactRuleException("invalid-context", "觸發輸入超出範圍");
        if (context.CoreRoundingPolicy is { } rounding && !Enum.IsDefined(rounding))
            throw new ArtifactRuleException("invalid-rounding", "未知取整策略");
        var result = new ArtifactSummary { TotalCost = board.TotalCost(state) };
        void Add(Dictionary<string, decimal> target, string key, decimal amount) => target[key] = target.GetValueOrDefault(key) + amount;
        void Pending(ArtifactNode n, string kind, string why) => result.PendingEffects.Add(new(n.Id, kind, why));
        foreach (var (id, level) in state.Levels.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var node = board.Node(id);
            var option = state.Selections.GetValueOrDefault(id, "");
            var record = node.Levels.Single(x => x.Level == level && x.OptionId == option);
            var mode = context.NodeModes.GetValueOrDefault(id, context.Mode);
            result.ScopedTriggerModes[id] = mode;
            if (!node.Shared)
            {
                result.Warnings.Add($"{node.Name}為{node.ProfessionId}局部技能機制，未接入DPS");
                result.UnmodeledMechanics.Add(new(id, "skill-runtime", "技能分支、連動與觸發時序保留在資料包Model；未解讀為全局乘區"));
            }
            if (node.PositionId == "CORE") result.UnmodeledMechanics.Add(new(id, "five-dimension-mapping", "五維顯示增量不代表已確認各維面板映射"));
            foreach (var e in record.Effects)
            {
                var contribution = new ArtifactContribution(id, option, e.Key, e.Amount, e.Unit, e.Scope, node.Status, record.Source);
                switch (e.Scope)
                {
                    case "panel": Add(result.StaticAttributes, e.Key, e.Amount); result.Contributions.Add(contribution); break;
                    case "targetProfession":
                        if (!result.TargetProfessionModifiers.TryGetValue(option, out var modifiers))
                            result.TargetProfessionModifiers[option] = modifiers = new();
                        Add(modifiers, e.Key, e.Amount); result.Contributions.Add(contribution); break;
                    case "triggeredPanel":
                        bool? active = mode switch {
                            ArtifactTriggerMode.Off => false,
                            ArtifactTriggerMode.Manual => context.ManualActive.TryGetValue(id, out var manual) ? manual : null,
                            _ => context.PvpBuffRemainingSeconds > 0 || context.NearbyEnemyPlayers > 5 ? true
                                : context.NearbyEnemyPlayers is null && context.PvpBuffRemainingSeconds is null ? null : false
                        };
                        if (active is null) Pending(node, e.Gate, "缺少敵方玩家數／增益計時／手動開關，不推定覆蓋率");
                        if (active == true) { Add(result.TriggeredAttributes, e.Key, e.Amount); result.Contributions.Add(contribution); }
                        break;
                    default: result.ScopedEffects.Add(contribution); break;
                }
            }
            foreach (var special in node.SpecialEffects.Where(e => level >= e.MinimumLevel))
            {
                if (mode == ArtifactTriggerMode.Off) continue;
                if (mode == ArtifactTriggerMode.Manual)
                {
                    if (!context.ManualActive.TryGetValue(id, out var manual)) { Pending(node, special.Kind, "未提供手動開關"); continue; }
                    if (!manual) continue;
                }
                if (special.Kind == "coreSuppressionAtMax")
                {
                    if (level != node.MaxLevel) continue;
                    if (context.VerifiedFiveDimensionSum is not decimal sum || context.CoreRoundingPolicy is not { } policy)
                    { Pending(node, special.Kind, special.Note); continue; }
                    var raw = sum * special.Factor;
                    var value = policy switch { ArtifactRoundingPolicy.Floor => decimal.Floor(raw),
                        ArtifactRoundingPolicy.Ceiling => decimal.Ceiling(raw),
                        ArtifactRoundingPolicy.AwayFromZero => decimal.Round(raw, 0, MidpointRounding.AwayFromZero), _ => raw };
                    foreach (var key in new[] { "professionSuppression", "bossSuppression" })
                    {
                        Add(result.TriggeredAttributes, key, value);
                        result.Contributions.Add(new(id, option, key, value, "point", "triggeredPanel", "取整策略由呼叫者明確指定，遊戲規則仍待核", record.Source));
                    }
                    result.Warnings.Add("核心五維總和與取整是明確情境輸入；不代表已驗證遊戲取整規則");
                }
                else if (special.Kind == "interactionStacks")
                {
                    if (context.InteractionStacks is not int stacks) { Pending(node, special.Kind, special.Note); continue; }
                    var cap = record.Effects.Single(e => e.Key == "jingLei.interactionMaxStacks").Amount;
                    if (stacks > cap) throw new ArtifactRuleException("invalid-stacks", "氣劍互動層數超出本級上限");
                    foreach (var (key, value) in special.PerStack)
                    {
                        Add(result.TriggeredAttributes, key, stacks * value);
                        result.Contributions.Add(new(id, option, key, stacks * value, "point", "triggeredPanel", node.Status, record.Source));
                    }
                }
            }
        }
        foreach (var (key, value) in result.StaticAttributes) Add(result.CombinedAttributes, key, value);
        foreach (var (key, value) in result.TriggeredAttributes) Add(result.CombinedAttributes, key, value);
        foreach (var tag in new[] { "single", "group", "burst", "sustained" })
            result.SkillEnhancementByTag[tag] = result.CombinedAttributes.GetValueOrDefault("skillEnhancement.all")
                + result.CombinedAttributes.GetValueOrDefault($"skillEnhancement.{tag}");
        return result;
    }
}
