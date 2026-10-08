namespace NshmCalculator.Shared.Research.Artifacts;

public sealed class ArtifactBoardState
{
    public string PackId { get; set; } = "";
    public string ProfessionId { get; set; } = "";
    public int? PointBudget { get; set; }
    public Dictionary<string, int> Levels { get; set; } = new();
    public Dictionary<string, string> Selections { get; set; } = new();
    public Dictionary<string, bool> QuestUnlocks { get; set; } = new();
    public ArtifactBoardState Copy() => new() { PackId = PackId, ProfessionId = ProfessionId,
        PointBudget = PointBudget, Levels = new(Levels), Selections = new(Selections), QuestUnlocks = new(QuestUnlocks) };
}
public sealed record ArtifactBoardChange(string NodeId, int BeforeLevel, int AfterLevel,
    string? BeforeOption, string? AfterOption, int PointDelta, string Reason);
public sealed record ArtifactBoardResult(ArtifactBoardState State, IReadOnlyList<ArtifactBoardChange> Changes,
    int TotalCost, int PointDelta);

/// <summary>Pure transactional board operations. Failed requests never modify caller state.</summary>
public sealed class ArtifactBoardEngine
{
    public ArtifactDataPack Pack { get; }
    private readonly Dictionary<string, ArtifactNode> nodes;
    public ArtifactBoardEngine(ArtifactDataPack pack)
    {
        pack.ValidateDefinition();
        // Hold an isolated definition so a caller cannot mutate the input pack underneath a transaction.
        Pack = ArtifactDataPack.Load(System.Text.Json.JsonSerializer.Serialize(pack));
        nodes = Pack.AllNodes.ToDictionary(n => n.Id);
    }
    public ArtifactNode Node(string id) => nodes.TryGetValue(id, out var n) ? n
        : throw new ArtifactRuleException("unknown-node", $"未知節點：{id}");

    public ArtifactBoardState CreateState(string professionId, int? budget = null,
        IReadOnlyDictionary<string, string>? selections = null)
    {
        var state = new ArtifactBoardState { PackId = Pack.Manifest.Id, ProfessionId = professionId, PointBudget = budget,
            QuestUnlocks = nodes.Values.Where(n => n.UnlockType == "questItem").ToDictionary(n => n.Id, _ => Pack.Rules.QuestDefaultUnlocked) };
        if (selections is not null)
            foreach (var (id, option) in selections) state.Selections[id] = Node(id).ResolveOption(option).Id;
        Validate(state);
        return state;
    }

    public int TotalCost(ArtifactBoardState state) => state.Levels.Sum(p => checked(Node(p.Key).CostPerLevel * p.Value));

    public void Validate(ArtifactBoardState state)
    {
        if (state.PackId != Pack.Manifest.Id) throw new ArtifactRuleException("version-mismatch", "神器盤版本不符");
        if (string.IsNullOrWhiteSpace(state.ProfessionId)) throw new ArtifactRuleException("profession-required", "須提供流派");
        if (state.PointBudget < 0) throw new ArtifactRuleException("invalid-budget", "點數不可為負");
        foreach (var (id, unlocked) in state.QuestUnlocks)
            if (Node(id).UnlockType != "questItem") throw new ArtifactRuleException("invalid-quest", "此節點不是任務道具解鎖");
        foreach (var (id, option) in state.Selections)
        {
            var n = Node(id);
            RequireProfession(state, n);
            if (n.ResolveOption(option).Id != option) throw new ArtifactRuleException("noncanonical-option", "儲存狀態须使用標準選項ID");
        }
        foreach (var (id, level) in state.Levels)
        {
            var n = Node(id);
            if (level < 0 || level > n.MaxLevel) throw new ArtifactRuleException("invalid-level", $"{n.Name}超出等級範圍");
            if (level == 0) continue;
            RequireProfession(state, n);
            if (n.UnlockType == "questItem" && !state.QuestUnlocks.GetValueOrDefault(id))
                throw new ArtifactRuleException("quest-locked", $"{n.Name}未取得解鎖道具");
            if (n.ParentId is not null && state.Levels.GetValueOrDefault(n.ParentId) < n.RequiredParentLevel)
                throw new ArtifactRuleException("parent-level", $"{n.Name}需父節點Lv{n.RequiredParentLevel}");
            if (n.Options.Count > 0 && !state.Selections.ContainsKey(id))
                throw new ArtifactRuleException("selection-required", $"{n.Name}須先選擇同槽選項");
        }
        foreach (var group in nodes.Values.Where(n => !string.IsNullOrEmpty(n.MutualExclusionGroup)).GroupBy(n => n.MutualExclusionGroup))
            if (group.Count(n => state.Levels.GetValueOrDefault(n.Id) > 0) > 1)
                throw new ArtifactRuleException("mutual-exclusion", $"雙實體互斥組：{group.Key}");
        if (state.PointBudget is int budget && TotalCost(state) > budget)
            throw new ArtifactRuleException("insufficient-points", "不足以支付節點與最低父鏈點數");
    }

    private static void RequireProfession(ArtifactBoardState state, ArtifactNode n)
    {
        if (!n.Shared && n.ProfessionId != state.ProfessionId)
            throw new ArtifactRuleException("unsupported-profession", $"{n.Name}缺少{state.ProfessionId}版本覆寫");
    }

    public ArtifactBoardResult SetLevel(ArtifactBoardState state, string id, int level,
        IReadOnlyDictionary<string, string>? selections = null) => ChangeLevel(state, id, level, selections, false);

    /// <summary>Explicitly refund the other physical node; new node level is requested, never inherited.</summary>
    public ArtifactBoardResult ReplaceExclusive(ArtifactBoardState state, string id, int level,
        IReadOnlyDictionary<string, string>? selections = null)
    {
        if (string.IsNullOrEmpty(Node(id).MutualExclusionGroup) || level <= 0)
            throw new ArtifactRuleException("not-exclusive", "須指定互斥實體節點及正等級");
        return ChangeLevel(state, id, level, selections, true);
    }

    private ArtifactBoardResult ChangeLevel(ArtifactBoardState before, string id, int level,
        IReadOnlyDictionary<string, string>? selections, bool replace)
    {
        Validate(before);
        var requested = Node(id);
        if (level < 0 || level > requested.MaxLevel) throw new ArtifactRuleException("invalid-level", "目標等級超出上限");
        var next = before.Copy();
        var reasons = new Dictionary<string, string>();
        if (selections is not null)
            foreach (var (slot, option) in selections)
            {
                next.Selections[slot] = Node(slot).ResolveOption(option).Id;
                reasons[slot] = "explicit-option";
            }
        if (replace)
            foreach (var other in nodes.Values.Where(n => n.Id != id && n.MutualExclusionGroup == requested.MutualExclusionGroup))
            {
                next.Levels[other.Id] = 0;
                reasons[other.Id] = "explicit-mutex-refund";
            }
        void EnsureParents(ArtifactNode n)
        {
            RequireProfession(next, n);
            if (n.ParentId is null) return;
            var p = Node(n.ParentId);
            EnsureParents(p);
            if (next.Levels.GetValueOrDefault(p.Id) < n.RequiredParentLevel)
            {
                next.Levels[p.Id] = n.RequiredParentLevel;
                reasons[p.Id] = "minimum-parent-chain";
            }
        }
        if (level > 0) EnsureParents(requested);
        next.Levels[id] = level;
        reasons[id] = "requested-level";
        Validate(next); // catches unsupported selections, quest locks, descendants, budget and both mutexes atomically
        return Result(before, next, reasons);
    }

    public ArtifactBoardResult SwitchOption(ArtifactBoardState state, string id, string option)
    {
        Validate(state);
        var n = Node(id);
        if (!n.InheritLevelOnSwitch) throw new ArtifactRuleException("not-selectable", "此實體節點不是單槽選擇");
        var next = state.Copy();
        next.Selections[id] = n.ResolveOption(option).Id;
        Validate(next);
        return Result(state, next, new() { [id] = "switch-inherit-level" });
    }

    public ArtifactBoardState SetQuestUnlocked(ArtifactBoardState state, string id, bool unlocked)
    {
        Validate(state);
        if (Node(id).UnlockType != "questItem") throw new ArtifactRuleException("invalid-quest", "非任務道具節點");
        var next = state.Copy(); next.QuestUnlocks[id] = unlocked; Validate(next); return next;
    }

    private ArtifactBoardResult Result(ArtifactBoardState before, ArtifactBoardState next, Dictionary<string, string> reasons)
    {
        int Depth(ArtifactNode n) => n.ParentId is null ? 0 : 1 + Depth(Node(n.ParentId));
        var changes = reasons.Keys.Select(id => new ArtifactBoardChange(id,
                before.Levels.GetValueOrDefault(id), next.Levels.GetValueOrDefault(id),
                before.Selections.GetValueOrDefault(id), next.Selections.GetValueOrDefault(id),
                (next.Levels.GetValueOrDefault(id) - before.Levels.GetValueOrDefault(id)) * Node(id).CostPerLevel, reasons[id]))
            .Where(c => c.BeforeLevel != c.AfterLevel || c.BeforeOption != c.AfterOption)
            .OrderBy(c => Depth(Node(c.NodeId))).ThenBy(c => c.NodeId, StringComparer.Ordinal).ToArray();
        var cost = TotalCost(next);
        return new(next, changes, cost, cost - TotalCost(before));
    }
}
