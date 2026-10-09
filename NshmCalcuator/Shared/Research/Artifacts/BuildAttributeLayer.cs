namespace NshmCalculator.Shared.Research.Artifacts;

public sealed record AttributeDefinition(string Key, string Label, string Unit);
public sealed record BuildAttributeRow(string Key, decimal? Base, decimal Artifact, decimal? Final);
public sealed record BuildAttributeDifference(string Key, BuildAttributeRow A, BuildAttributeRow B,
    decimal? FinalDifference, decimal ArtifactDifference);

/// <summary>Idempotent projection: base + static artifact. Never mutates base or Legacy inputs.</summary>
public static class BuildAttributeLayer
{
    public static AttributeDefinition[] Definitions(ArtifactDataPack pack) => pack.AllNodes
        .SelectMany(n => n.Levels).SelectMany(l => l.Effects).Where(e => e.Scope == "panel")
        .GroupBy(e => e.Key).Select(g => new AttributeDefinition(g.Key, g.First().Label, g.First().Unit))
        .OrderBy(d => d.Key, StringComparer.Ordinal).ToArray();

    public static BuildAttributeRow[] Project(ResearchBuild build, ArtifactBoardEngine engine)
    {
        var staticValues = new Dictionary<string, decimal>();
        if (build.ArtifactState is { } state)
        {
            if (build.ProfessionId != state.ProfessionId)
                throw new ArtifactRuleException("profession-mismatch", "Build與神器盤流派不同");
            // Off affects conditional gains only. Scoped effects are deliberately not added to the panel.
            staticValues = new ArtifactAggregator(engine).Summarize(state,
                new ArtifactTriggerContext { Mode = ArtifactTriggerMode.Off }).StaticAttributes;
        }
        return Definitions(engine.Pack).Select(d => d.Key).Union(build.BaseAttributes.Keys)
            .Union(staticValues.Keys).OrderBy(k => k, StringComparer.Ordinal).Select(key => {
                decimal? basis = build.BaseAttributes.TryGetValue(key, out var value) ? value : null;
                var increment = staticValues.GetValueOrDefault(key);
                return new BuildAttributeRow(key, basis, increment, basis + increment);
            }).ToArray();
    }

    public static BuildAttributeDifference[] Compare(ResearchBuild a, ResearchBuild b, ArtifactBoardEngine engine)
    {
        var pa = Project(a, engine).ToDictionary(r => r.Key);
        var pb = Project(b, engine).ToDictionary(r => r.Key);
        return pa.Keys.Union(pb.Keys).OrderBy(k => k, StringComparer.Ordinal).Select(k => {
            var ra = pa.GetValueOrDefault(k) ?? new(k, null, 0, null);
            var rb = pb.GetValueOrDefault(k) ?? new(k, null, 0, null);
            return new BuildAttributeDifference(k, ra, rb, rb.Final - ra.Final, rb.Artifact - ra.Artifact);
        }).ToArray();
    }
}
