namespace NshmCalculator.Shared.Research.Artifacts;

/// <summary>UI command adapter. Selection defaults are visible first options, not inferred game facts.</summary>
public sealed class ArtifactBoardSession
{
    public ResearchBuild Build { get; }
    public ArtifactBoardEngine Engine { get; }
    public ArtifactBoardState State => Build.ArtifactState!;
    public IReadOnlyList<ArtifactBoardChange> LastChanges { get; private set; } = Array.Empty<ArtifactBoardChange>();
    public int TotalCost => Engine.TotalCost(State);

    public ArtifactBoardSession(ResearchBuild build, ArtifactBoardEngine engine)
    {
        if (!engine.Pack.ProfessionOverrides.ContainsKey(build.ProfessionId))
            throw new ArtifactRuleException("unsupported-profession", "此版本尚未提供該流派的完整神器盤");
        Build = build; Engine = engine;
        if (build.ArtifactState is null)
        {
            var defaults = engine.Pack.CommonNodes.Concat(engine.Pack.ProfessionOverrides[build.ProfessionId])
                .Where(n => n.Options.Count > 0).ToDictionary(n => n.Id, n => n.Options[0].Id);
            build.ArtifactState = engine.CreateState(build.ProfessionId, selections: defaults);
        }
        if (State.ProfessionId != build.ProfessionId)
            throw new ArtifactRuleException("profession-mismatch", "Build與神器盤流派不同");
        engine.Validate(State);
    }

    public int Level(string id) => State.Levels.GetValueOrDefault(id);
    public int AutoLevel(string id) => Math.Min(Level(id), Build.ArtifactAutoLevels.GetValueOrDefault(id));
    public bool BlockedByMutex(string id)
    {
        var group = Engine.Node(id).MutualExclusionGroup;
        return !string.IsNullOrEmpty(group) && Engine.Pack.AllNodes.Any(n => n.Id != id
            && n.MutualExclusionGroup == group && Level(n.Id) > 0);
    }

    public void SetLevel(string id, int level) => Apply(Engine.SetLevel(State, id, level));
    public void SwitchOption(string id, string option) => Apply(Engine.SwitchOption(State, id, option));
    public void ReplaceExclusive(string id) => Apply(Engine.ReplaceExclusive(State, id, 1));
    public void SetQuestUnlocked(string id, bool unlocked) => Build.ArtifactState = Engine.SetQuestUnlocked(State, id, unlocked);

    private void Apply(ArtifactBoardResult result)
    {
        // Commit state and provenance together only after the engine accepts the transaction.
        var auto = new Dictionary<string, int>(Build.ArtifactAutoLevels);
        foreach (var c in result.Changes)
        {
            if (c.Reason == "minimum-parent-chain")
                auto[c.NodeId] = auto.GetValueOrDefault(c.NodeId) + c.AfterLevel - c.BeforeLevel;
            else if (c.BeforeLevel != c.AfterLevel && auto.ContainsKey(c.NodeId))
            {
                // Manual additions do not erase earlier auto-allocation provenance.
                // Refund manual levels first; cap remaining auto levels at the actual allocation.
                if (c.AfterLevel == 0) auto.Remove(c.NodeId);
                else auto[c.NodeId] = Math.Min(auto[c.NodeId], c.AfterLevel);
            }
        }
        Build.ArtifactState = result.State;
        Build.ArtifactAutoLevels = auto;
        LastChanges = result.Changes;
    }
}
