using System.Text.Json;
namespace NshmCalculator.Shared.Research;

public sealed class TwTargetPack
{
    public string Id { get; init; } = "";
    public int SchemaVersion { get; init; }
    public string Server { get; init; } = "";
    public string Platform { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public string Status { get; init; } = "";
    public string DefenseAlgorithm { get; init; } = "";
    public string ElementAlgorithm { get; init; } = "";
    public decimal DefenseOffset { get; init; }
    public int DisplayDecimals { get; init; }
    public string DisplayRounding { get; init; } = "";
    public string SourceID { get; init; } = "";
    public Dictionary<string,string> RatioCandidates { get; init; } = new();
    public string[] UnknownTargetFields { get; init; } = [];
    public TwTargetDefinition[] Targets { get; init; } = [];
}
public sealed class TwTargetDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal Defense { get; init; }
    public decimal DefenseConstant { get; init; }
    public decimal ElementResistance { get; init; }
    public decimal ElementConstant { get; init; }
    public decimal[] ElementResistanceRange { get; init; } = [];
    public decimal[] ElementConstantRange { get; init; } = [];
    public string ParameterChoice { get; init; } = "";
    public TwTargetObservation[] Observations { get; init; } = [];
}
public sealed record TwTargetObservation(string Id, string Kind, decimal Input, decimal DisplayPercent, string SourceID);
public sealed record TwTargetSelection(string PackId, string TargetId);
public sealed record TwTargetInput(string Code, string Key, decimal? Value, string Status);
public sealed record TwTargetSnapshot(string PackId, string TargetId, string Name, string GameVersion, string Status,
    TwTargetInput[] Fields, decimal? DefenseRatio, decimal? ElementRatio, decimal? DefenseDisplayPercent,
    decimal? ElementDisplayPercent, decimal[] ElementResistanceRange, decimal[] ElementConstantRange, string SourceID);

/// <summary>TW-owned target parameters; never imports Legacy BO defaults or derives constants from ratios.</summary>
public sealed class TwTargetModel
{
    private readonly string json;
    private readonly TwTargetPack pack;
    public string Id => pack.Id;
    public TwTargetDefinition[] Definitions => JsonSerializer.Deserialize<TwTargetPack>(json)!.Targets;
    public TwTargetModel(string json)
    {
        this.json=json; pack=JsonSerializer.Deserialize<TwTargetPack>(json) ?? throw new ArgumentException("目標資料為空");
        if(pack.SchemaVersion!=1 || pack.Server!="TW" || pack.Platform!="mobile" || string.IsNullOrWhiteSpace(pack.Id)
            || pack.DefenseAlgorithm!="remaining-defense-offset-v1" || pack.ElementAlgorithm!="remaining-element-v1"
            || pack.DisplayDecimals!=1 || pack.DisplayRounding!="AwayFromZero" || pack.DefenseOffset!=.1m
            || pack.Targets.Length==0 || pack.Targets.Select(t=>t.Id).Distinct().Count()!=pack.Targets.Length)
            throw new ArgumentException("未知TW目標版本／算法／顯示規格");
        foreach(var t in pack.Targets)
            if(string.IsNullOrWhiteSpace(t.Id) || t.Defense<=0 || t.DefenseConstant<=0 || t.ElementResistance<=0 || t.ElementConstant<=0
                || !ValidRange(t.ElementResistanceRange,t.ElementResistance) || !ValidRange(t.ElementConstantRange,t.ElementConstant))
                throw new ArgumentException("目標參數／候選範圍無效");
    }
    private static bool ValidRange(decimal[] range,decimal value)=>range.Length==2 && range[0]>0 && range[0]<=value && value<=range[1];
    private TwTargetDefinition Target(TwTargetSelection selection)
    {
        if(selection.PackId!=pack.Id) throw new ArgumentException("TW目標版本不符；不得使用Legacy目標");
        return pack.Targets.SingleOrDefault(t=>t.Id==selection.TargetId) ?? throw new ArgumentException("未知TW目標");
    }
    public decimal DefenseRatio(TwTargetSelection selection,decimal penetration)
    {
        if(penetration<0) throw new ArgumentException("有效破防不可為負");
        var t=Target(selection);return t.DefenseConstant/(t.DefenseConstant+Math.Max(t.Defense-penetration,0))-pack.DefenseOffset;
    }
    public decimal ElementRatio(TwTargetSelection selection,decimal ignore)
    {
        if(ignore<0) throw new ArgumentException("有效忽視不可為負");
        var t=Target(selection);return t.ElementConstant/(t.ElementConstant+Math.Max(t.ElementResistance-ignore,0));
    }
    public decimal DisplayPercent(decimal ratio)=>decimal.Round(ratio*100,pack.DisplayDecimals,MidpointRounding.AwayFromZero);
    public TwTargetSnapshot Snapshot(TwTargetSelection selection,decimal? penetration,decimal? ignore)
    {
        var t=Target(selection);
        decimal? d=penetration is {} p ? DefenseRatio(selection,p) : null;
        decimal? e=ignore is {} i ? ElementRatio(selection,i) : null;
        TwTargetInput[] fields=[new("BO_01","defense",t.Defense,t.Status),new("BO_03","elementResistance",t.ElementResistance,t.Status),
            new("BO_06","defenseConstant",t.DefenseConstant,t.Status),new("BO_07","elementConstant",t.ElementConstant,t.Status),
            new("BO_02","bossResistance",null,"未知"),new("BO_04","criticalResistance",null,"未知"),new("BO_05","block",null,"未知"),
            new("BO_08","hitConstant",null,"未知"),new("BO_09","criticalConstant1",null,"未知"),new("BO_10","criticalConstant2",null,"未知"),
            new("BO_11","minimumAttack",null,"未知"),new("BO_12","targetUnit",null,"未知")];
        return new(pack.Id,t.Id,t.Name,pack.GameVersion,t.Status,fields,d,e,d is {} dv ? DisplayPercent(dv) : null,
            e is {} ev ? DisplayPercent(ev) : null,t.ElementResistanceRange.ToArray(),t.ElementConstantRange.ToArray(),pack.SourceID);
    }
}
