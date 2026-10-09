using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NshmCalculator.Shared.Research.Artifacts;
namespace NshmCalculator.Shared.Research;

// Serialized immutable snapshot: no references to mutable current/candidate dictionaries or board state.
public sealed record TwActualPanelAnchor(string Id,string PackId,string ProfessionId,string LegacyMode,string SnapshotJson,string SnapshotSha256,
    string EffectPolicyId,string CoreRuleId);
public sealed record TwAnchorAttribute(string Key,decimal? Observed,decimal? CurrentArtifact,decimal? CandidateArtifact,
    decimal? Delta,decimal? CandidatePanel,string Status);
public sealed record TwAnchorProjection(string AnchorId,string AnchorSha256,string InputBasis,TwAnchorAttribute[] Attributes,
    TwFiveDimensionRow[] FiveDimensions,decimal? CurrentCoreBoss,decimal? CandidateCoreBoss,decimal? CoreBossDelta,
    string[] Limitations,TwArtifactProjection Artifact);
public sealed class TwActualPanelSession
{
    public ResearchBuild Current { get; set; }=new();
    public ResearchBuild Candidate { get; set; }=new();
    public TwActualPanelAnchor? Anchor { get; set; }
    public TwTargetSelection? Target { get; set; }
}

public sealed class TwActualPanelAdapter
{
    private readonly ArtifactBoardEngine engine;
    private readonly TwArtifactEffectAdapter effects;
    private string CoreId=>engine.Pack.AllNodes.Single(n=>n.PositionId=="CORE").Id;
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static ResearchBuild Copy(ResearchBuild build)=>JsonSerializer.Deserialize<ResearchBuild>(JsonSerializer.Serialize(build))!;
    public TwActualPanelAdapter(ArtifactBoardEngine engine,TwArtifactEffectAdapter effects)
    {
        this.engine=engine;this.effects=effects;
        if(engine.Pack.Manifest.Id!=effects.PackId) throw new ArgumentException("錨定與神器版本不符");
    }
    public TwActualPanelAnchor Capture(ResearchBuild current)
    {
        if(current.ArtifactState is null) throw new ArgumentException("必須明確提供目前實際神器盤，不能假設零點盤");
        ValidateCurrent(current);
        var text=JsonSerializer.Serialize(current);
        return new(Guid.NewGuid().ToString("N"),engine.Pack.Manifest.Id,current.ProfessionId,current.LegacyMode,text,Hash(text),effects.PolicyId,effects.CoreRuleId);
    }
    private void ValidateCurrent(ResearchBuild current)
    {
        if(current.ArtifactState is null || current.ArtifactState.ProfessionId!=current.ProfessionId) throw new ArgumentException("目前盤與流派不符");
        engine.Validate(current.ArtifactState);
        if(current.ObservedFiveDimensions.Keys.Except(effects.DimensionNames.Keys).Any() || current.ObservedFiveDimensions.Values.Any(v=>v<0))
            throw new ArgumentException("實際五維欄位／數值無效");
        var core=current.ArtifactState.Levels.GetValueOrDefault(CoreId)*effects.CoreIncrementPerLevel;
        if(current.ObservedFiveDimensions.Values.Any(v=>v<core)) throw new ArgumentException("實際五維小於目前CORE增量，請確認輸入包含目前神器");
    }
    public ResearchBuild Current(TwActualPanelAnchor anchor)
    {
        if(anchor.PackId!=engine.Pack.Manifest.Id || anchor.EffectPolicyId!=effects.PolicyId || anchor.CoreRuleId!=effects.CoreRuleId
            || anchor.SnapshotSha256!=Hash(anchor.SnapshotJson)) throw new ArgumentException("錨點神器／效果／CORE規則版本或內容驗證失敗；請明確更新錨點");
        var current=JsonSerializer.Deserialize<ResearchBuild>(anchor.SnapshotJson) ?? throw new ArgumentException("錨點資料為空");
        if(current.ProfessionId!=anchor.ProfessionId || current.LegacyMode!=anchor.LegacyMode) throw new ArgumentException("錨點身分不符");
        ValidateCurrent(current);return current;
    }
    public bool Matches(TwActualPanelAnchor anchor,ResearchBuild current)
    {
        try { Current(anchor);return anchor.SnapshotSha256==Hash(JsonSerializer.Serialize(current)); }
        catch(ArgumentException) { return false; }
    }
    private TwArtifactEffectContext OutOfCombat()=>new() { DefaultMode=ArtifactTriggerMode.Off, Controls=new() {
        [CoreId]=new(){Mode=ArtifactTriggerMode.Auto},
        [engine.Pack.AllNodes.Single(n=>n.PositionId=="M07").Id]=new(){Mode=ArtifactTriggerMode.Auto} } };
    public TwAnchorProjection Project(TwActualPanelAnchor anchor,ResearchBuild candidate,TwArtifactEffectContext? candidateContext=null)
    {
        var current=Current(anchor);
        if(candidate.ProfessionId!=current.ProfessionId || candidate.LegacyMode!=current.LegacyMode || candidate.ArtifactState is null)
            throw new ArgumentException("候選盤必須同流派／套路且明確提供神器配置");
        engine.Validate(candidate.ArtifactState);
        var currentCore=current.ArtifactState!.Levels.GetValueOrDefault(CoreId);
        var candidateCore=candidate.ArtifactState.Levels.GetValueOrDefault(CoreId);
        var bareFive=current.ObservedFiveDimensions.ToDictionary(d=>d.Key,d=>d.Value-currentCore*effects.CoreIncrementPerLevel);
        ResearchBuild ForProjection(ResearchBuild b) {
            var copy=Copy(b);copy.BaseAttributes=new();copy.BaseFiveDimensions=new(bareFive);return copy;
        }
        var cp=effects.Project(ForProjection(current),OutOfCombat());
        var np=effects.Project(ForProjection(candidate),candidateContext ?? OutOfCombat());
        var oldCoreBoss=cp.FiveDimensions.CandidateBossSuppression;
        var newCoreBoss=np.FiveDimensions.CandidateBossSuppression;
        // Same CORE level with unchanged five-dimensional basis cancels even if its absolute bonus is unknown.
        var cancels=candidateContext is null && candidateCore==currentCore;
        decimal? coreDelta=cancels ? 0 : newCoreBoss-oldCoreBoss;
        var pending=np.Pending.Where(s=>!(cancels && s.StartsWith("CORE："))).ToList();
        if(coreDelta is null) pending.Add("Anchor CORE：目前／候選首克差量未知，需實際五維；不使用Legacy補值");
        var keys=BuildAttributeLayer.Definitions(engine.Pack).Select(d=>d.Key).Union(current.ObservedAttributes.Keys)
            .Union(cp.StaticAttributes.Keys).Union(np.StaticAttributes.Keys).Union(np.ConditionalMeanAttributes.Keys).Append("bossSuppression").Distinct().Order().ToArray();
        var audits=new List<TwAnchorAttribute>();var rows=new List<BuildAttributeRow>();
        foreach(var key in keys) {
            decimal? observed=current.ObservedAttributes.TryGetValue(key,out var value) ? value : null;
            decimal? old=cp.StaticAttributes.GetValueOrDefault(key)+(key=="bossSuppression" ? oldCoreBoss : 0);
            decimal? next=np.StaticAttributes.GetValueOrDefault(key)+np.ConditionalMeanAttributes.GetValueOrDefault(key);
            decimal? delta=np.StaticAttributes.GetValueOrDefault(key)-cp.StaticAttributes.GetValueOrDefault(key)
                +np.ConditionalMeanAttributes.GetValueOrDefault(key)-(key=="bossSuppression" ? newCoreBoss ?? 0 : 0)
                +(key=="bossSuppression" ? coreDelta : 0);
            if(key=="bossSuppression" && newCoreBoss is null) next=null;
            decimal? final=observed+delta;
            var status=delta is null ? "神器差量未知" : observed is null ? "實際面板未知" : "可建模候選面板；非已校準DPS";
            audits.Add(new(key,observed,old,next,delta,final,status));
            // Mapping invariant remains final=base+artifact. Unknown delta must not become a fake zero.
            rows.Add(new(key,delta is null ? null : observed,delta ?? 0,final));
        }
        var five=effects.DimensionNames.Select(d=> {
            decimal? observed=current.ObservedFiveDimensions.TryGetValue(d.Key,out var v) ? v : null;
            decimal delta=(candidateCore-currentCore)*effects.CoreIncrementPerLevel;
            return new TwFiveDimensionRow(d.Key,d.Value,observed,delta,observed+delta);
        }).ToArray();
        var limits=np.ModelLimitations.Append("實際脫戰面板已含目前神器；僅套用候選−目前差量，暫時Buff不屬錨點。")
            .Append("五維變動對攻擊／破防／會心等間接影響未建模；此面板只包含可建模的神器差量。").ToArray();
        var projected=new TwArtifactProjection { PolicyId=np.PolicyId,ProfessionId=np.ProfessionId,CoreRuleId=np.CoreRuleId,
            FiveDimensions=new(np.CoreRuleId,"暫定候選","實際五維已含目前CORE；候選−目前差量",candidateCore,five,newCoreBoss,np.FiveDimensions.Reason),
            StaticAttributes=new(np.StaticAttributes),ConditionalMeanAttributes=new(np.ConditionalMeanAttributes),Panel=rows.ToArray(),
            Effects=np.Effects,Pending=pending.ToArray(),ProfessionDamageRatio=np.ProfessionDamageRatio,ModelLimitations=limits };
        return new(anchor.Id,anchor.SnapshotSha256,"ActualOutOfCombatPanel+CandidateArtifact-CurrentArtifact",audits.ToArray(),five,
            oldCoreBoss,newCoreBoss,coreDelta,limits,projected);
    }
    public BuildAttributeDifference[] Compare(TwActualPanelAnchor anchor,ResearchBuild candidate)
    {
        var a=Project(anchor,Current(anchor)).Artifact.Panel.ToDictionary(r=>r.Key);
        var b=Project(anchor,candidate).Artifact.Panel.ToDictionary(r=>r.Key);
        return a.Keys.Union(b.Keys).Order().Select(k=> {
            var ra=a.GetValueOrDefault(k) ?? new(k,null,0,null);var rb=b.GetValueOrDefault(k) ?? new(k,null,0,null);
            return new BuildAttributeDifference(k,ra,rb,rb.Final-ra.Final,rb.Artifact-ra.Artifact);
        }).ToArray();
    }
}
