"""Build a read-only TW artifact export. Never access or write Google Sheets.

python tools/build_artifact_pack.py ../research/phase2a/source.snapshot.json
The private snapshot stays outside the public repository; exports contain no Drive IDs/URLs.
"""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = 'TW-Mobile-2.3.3'
KEYS = {
    '攻擊': ('attack', 'point', 'panel'), '攻擊力': ('attack', 'point', 'panel'),
    '元素攻擊': ('elementAttack', 'point', 'panel'), '命中': ('hit', 'point', 'panel'),
    '會心': ('critical', 'point', 'panel'), '破防': ('defensePenetration', 'point', 'panel'),
    '氣血上限': ('maxHealth', 'point', 'panel'), '格擋': ('block', 'point', 'panel'),
    '防禦': ('defense', 'point', 'panel'), '全元素抗性': ('allElementResistance', 'point', 'panel'),
    '忽視元素抗性': ('ignoreElementResistance', 'point', 'panel'),
    '會心抗性': ('criticalResistance', 'point', 'panel'),
    '流派克制': ('professionSuppression', 'point', 'panel'),
    '首領克制': ('bossSuppression', 'point', 'panel'),
    '流派抵禦': ('professionResistance', 'point', 'panel'),
    '首領抵禦': ('bossResistance', 'point', 'panel'),
    '會心傷害': ('criticalDamage', 'percentagePoint', 'panel'),
    '全技能增強': ('skillEnhancement.all', 'point', 'panel'),
    '單體技能增強': ('skillEnhancement.single', 'point', 'panel'),
    '群體技能增強': ('skillEnhancement.group', 'point', 'panel'),
    '爆發技能增強': ('skillEnhancement.burst', 'point', 'panel'),
    '持續技能增強': ('skillEnhancement.sustained', 'point', 'panel'),
    '指定流派克制': ('targetProfessionSuppression', 'percentagePoint', 'targetProfession'),
    '指定流派抵禦': ('targetProfessionResistance', 'percentagePoint', 'targetProfession'),
    '額外流派克制': ('professionSuppression', 'point', 'triggeredPanel'),
    '額外流派抵禦': ('professionResistance', 'point', 'triggeredPanel'),
    '五維': ('fiveDimensions.displayIncrement', 'displayPoint', 'mechanic'),
    '流派技能傷害提升': ('professionSkill.damageIncrease', 'percentagePoint', 'skill'),
    '受到流派技能傷害降低': ('professionSkill.damageReduction', 'percentagePoint', 'skill'),
    '吟風傷害提升': ('yinFeng.damageIncrease', 'percentagePoint', 'skill'),
    '劍氣對怪物額外傷害': ('swordQi.monsterExtraDamage', 'percentagePoint', 'skill'),
    '誅邪傷害提升': ('zhuXie.damageIncrease', 'percentagePoint', 'skill'),
    '劍意追擊傷害提升': ('swordIntent.pursuitDamageIncrease', 'percentagePoint', 'skill'),
    '怪物額外傷害': ('leiLong.monsterExtraDamage', 'percentagePoint', 'skill'),
    '護盾血量百分比': ('leiLong.shieldHealth', 'percentagePoint', 'skill'),
    '輕功值削減': ('yinFeng.lightnessReduction', 'point', 'skill'),
    '輕功值回復': ('yinFeng.lightnessRecovery', 'point', 'skill'),
    '減傷比例': ('longFei.damageReduction', 'percentagePoint', 'skill'),
}


def effect(label, value, gate='', duration=0):
    key, unit, scope = KEYS[label]
    return dict(Key=key, Label=label, Amount=value, Unit=unit, Scope=scope,
                Gate=gate, DurationSeconds=duration)


def linear_effects(definition, level):
    return [effect(e['type'], e['value'] * level,
                   'enemyPlayersGreaterThan5' if e.get('condition') else '',
                   e.get('durationSeconds', 0))
            for e in definition.get('perLevel', definition.get('effectsPerLevel', []))]


def build(snapshot):
    sheets = snapshot['sheets']
    spec = sheets['27_神器2.3.3封版規格']
    source_nodes = [(i + 1, r) for i, r in enumerate(spec) if r and r[0] == VERSION]
    all_levels = sheets['21_神器節點逐級']
    diagnostics = []
    nodes = []
    golden = []
    for spec_row, r in source_nodes:
        node_id, name, position = r[1:4]
        shared = str(r[5]).lower() == 'true'
        model = json.loads(r[16])
        definition = model if shared else model.get('龍吟', model)
        max_level, cost = int(r[9]), int(r[10])
        lrows = [(i + 1, x) for i, x in enumerate(all_levels)
                 if i and x[0] == node_id and '2.3.3' in str(x[10])]
        assert sorted(int(x[2]) for _, x in lrows) == list(range(1, max_level + 1)), name
        assert all(int(x[7]) == cost and int(x[8]) == int(x[2]) * cost for _, x in lrows), name
        lrows.sort(key=lambda x: int(x[1][2]))
        for row_num, x in lrows:
            old = json.loads(x[18] or '{}')
            old = old.get('龍吟', old)
            if '龍吟' in old:
                diagnostics.append(dict(Code='DUPLICATE_PROFESSION_WRAPPER', NodeId=node_id,
                    Source=f'21_神器節點逐級!S{row_num}', Detail='逐級JSON重複龍吟包裝；使用27單層JSON，原值保留於私人快照。'))
                old = old['龍吟']
            if old.get('maxLevel', max_level) != max_level:
                diagnostics.append(dict(Code='STALE_MAX_LEVEL', NodeId=node_id,
                    Source=f'21_神器節點逐級!S{row_num}', Detail=f"逐級JSON上限{old['maxLevel']}；27封版為{max_level}。"))
        if any('待定位' in str(x[12]) or '拓撲待' in str(x[21] if len(x) > 21 else '') for _, x in lrows):
            diagnostics.append(dict(Code='STALE_TOPOLOGY_STATUS', NodeId=node_id,
                Source='21_神器節點逐級', Detail='逐級表仍標拓撲待補；本包父鏈採27封版明確ParentID。'))
        if position in ['CORE-U01', 'CORE-D01']:
            diagnostics.append(dict(Code='LEGACY_EFFECT_CONFLICT', NodeId=node_id, Source='27_神器2.3.3封版規格',
                Detail='現版全技能增強+50/級；Legacy克制收益公式不等價，不接入舊公式。'))
        selection = r[12] or 'none'
        options = []
        if selection == 'targetProfession':
            options = [dict(Id=p, Name=p, Aliases=[], Description='目標流派；不代表角色流派')
                       for p in definition['targetProfessionOptions']]
        else:
            for o in definition.get('options', []):
                label = o.get('canonicalLabel', o['name'] if 'name' in o else o['inGameName'])
                options.append(dict(Id=o.get('id', label), Name=label,
                    Aliases=[o['inGameName']] if 'inGameName' in o else [],
                    Description=o.get('effect', '')))
        ids = [o['Id'] for o in options] or ['']
        data = []
        for level in range(1, max_level + 1):
            source_row, lr = lrows[level - 1]
            cumulative = json.loads(lr[17])
            for option_id in ids:
                d = definition
                if options and selection != 'targetProfession':
                    d = definition['options'][ids.index(option_id)]
                effects = linear_effects(d, level)
                if definition.get('levelEffects'):
                    steps = definition['levelEffects'][:level]
                    if position == 'CLASS-LY-UP02':
                        effects = [dict(Key='jingLei.cooldownReduction', Label='驚雷冷卻降低',
                            Amount=sum(x['cooldownReductionSec'] for x in steps), Unit='second', Scope='skill', Gate='', DurationSeconds=0),
                            dict(Key='jingLei.interactionMaxStacks', Label='氣劍互動最大層數',
                            Amount=sum(x['interactionMaxStacksIncrement'] for x in steps), Unit='stack', Scope='mechanic', Gate='', DurationSeconds=0)]
                    elif position == 'CLASS-LY-DN02':
                        effects = [dict(Key='jianDang.attackSpeedIncrease', Label='劍蕩攻速提升',
                            Amount=sum(x['attackSpeedIncreasePct'] for x in steps), Unit='percentagePoint', Scope='skill', Gate='', DurationSeconds=0)]
                    else:
                        effects = [effect(steps[-1]['type'], steps[-1]['cumulative'])]
                if not model:  # Only common base nodes have blank 27 model; read exact 21 cumulative, never Legacy.
                    assert isinstance(cumulative, list), name
                    effects = [effect(e['type'], e.get('value', e.get('cumulativeIncrement')))
                               for e in cumulative if e.get('value') != 'dynamic']
                data.append(dict(Level=level, OptionId=option_id, Cost=level * cost, Effects=effects,
                    Source=f'21_神器節點逐級!R{source_row}', LevelRecordId=lr[19]))
            golden.append(dict(NodeId=node_id, Level=level, RawCumulative=cumulative,
                SourceRow=source_row, LevelRecordId=lr[19]))
        special = []
        if position == 'CORE':
            special = [dict(Kind='coreSuppressionAtMax', MinimumLevel=5, Factor=0.1,
                Inputs=['verifiedFiveDimensionSum', 'explicitRoundingPolicy'],
                Outputs=['professionSuppression', 'bossSuppression'],
                Note='五維映射、是否已含本節點五維增量、遊戲取整規則未驗證；不得推定。')]
        if position == 'CLASS-LY-UP02':
            special = [dict(Kind='interactionStacks', MinimumLevel=1,
                PerStack={KEYS[k][0]: v for k, v in definition['interactionBenefitPerStack'].items() if k != 'unit'},
                Inputs=['interactionStacks'], Note='實際層數與覆蓋率不由上限反推；僅套用明確提供的層數。')]
        ptype = r[11]
        node = dict(Id=node_id, Name=name, PositionId=position, NodeClass=r[4], Shared=shared,
            ProfessionId='' if shared else 'LY', ParentId=r[7] if ptype == 'parentLevel' else None,
            RequiredParentLevel=int(r[8]) if ptype == 'parentLevel' else 0,
            MaxLevel=max_level, CostPerLevel=cost, UnlockType=ptype,
            SelectionType=selection, SelectionGroup=None if r[13] in [None, '', 'null'] else r[13],
            MutualExclusionGroup=None if r[14] in [None, '', 'null'] else r[14],
            InheritLevelOnSwitch=str(r[15]).lower() == 'true', Options=options, Levels=data,
            SpecialEffects=special, TriggerMode=r[17], Status=r[18], Notes=r[19],
            Model=definition, SpecRow=spec_row,
            SourceIds=sorted({s.strip() for _, lr in lrows for s in str(lr[11]).split('；')}))
        nodes.append(node)
    lookup = {n['Id']: n for n in nodes}
    assert len(lookup) == len(nodes) == 38
    for n in nodes:
        if n['ParentId']:
            p = lookup[n['ParentId']]
            assert n['RequiredParentLevel'] <= p['MaxLevel']
            assert n['RequiredParentLevel'] == (1 if p['PositionId'] in ['ROOT-L', 'CORE'] else 5)
        seen = set()
        p = n
        while p['ParentId']:
            assert p['Id'] not in seen
            seen.add(p['Id'])
            p = lookup[p['ParentId']]
    diagnostics.append(dict(Code='RELEASE_FLAG_SCOPE', NodeId='', Source='20/21',
        Detail='20/21的ReleaseReady仍為False，部分Status/備註仍待驗證；27封版是本包實作依據，不提升研究證據等級。'))
    diagnostics.append(dict(Code='CORE_UNRESOLVED', NodeId=next(n['Id'] for n in nodes if n['PositionId'] == 'CORE'),
        Source='27/21', Detail='核心五維映射與滿級克制取整待核，缺明確输入即回傳未解析效果。'))
    diagnostics.append(dict(Code='DYNAMIC_SCOPE_UNRESOLVED', NodeId='ART-SLOT-CORE-D03-L', Source='27',
        Detail='龍飛減傷時機/範圍待核；技能百分比與Root文字機制保留為局部效果，不當全程面板/DPS。'))
    digest = hashlib.sha256(json.dumps(sheets, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    pack = dict(Manifest=dict(Id=VERSION, SchemaVersion=1, Server='TW', Platform='Mobile', GameVersion='2.3.3',
        FrozenDate='2026-10-09', SupportedProfessionOverrides=['LY'], SourceSnapshotSha256=digest,
        SourceTabs=['27_神器2.3.3封版規格', '21_神器節點逐級', '20_神器節點主表'],
        NodeCount=len(nodes), LevelRecordCount=len(golden), ExportPurpose='artifact-attributes-and-board-rules',
        DamageFormulaAttached=False, EvidencePolicy='保留原始Status；封版採用不代表所有逐級或機制已實測'),
        Rules=dict(GeneralParentLevel=5, SpecialParentLevels={'ROOT-L': 1, 'CORE': 1},
            AutoPrerequisite='unique-parent-chain-minimum', QuestDefaultUnlocked=True,
            SupportedTriggerModes=['auto', 'manual', 'off'],
            NonExclusiveGroups=[['CLASS-LY-UP01', 'CLASS-LY-UP02'], ['CLASS-LY-DN01', 'CLASS-LY-DN02']]),
        CommonNodes=[n for n in nodes if n['Shared']],
        ProfessionOverrides={'LY': [n for n in nodes if not n['Shared']]}, Diagnostics=diagnostics)
    return pack, golden


def main():
    source = Path(sys.argv[1])
    pack, golden = build(json.loads(source.read_text(encoding='utf-8-sig')))
    dest = ROOT / 'NshmCalculator.MudClient/wwwroot/data/artifacts' / VERSION
    dest.mkdir(parents=True, exist_ok=True)
    (dest / 'artifact-pack.json').write_text(json.dumps(pack, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    test_dir = ROOT / 'NshmCalculator.Test/TestData'
    (test_dir / 'artifact_233_source_golden.json').write_text(json.dumps(golden, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    report = ROOT / 'research/artifacts' / VERSION
    report.mkdir(parents=True, exist_ok=True)
    (report / 'conflicts.json').write_text(json.dumps(pack['Diagnostics'], ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(nodes=pack['Manifest']['NodeCount'], levels=pack['Manifest']['LevelRecordCount'],
        conflicts=len(pack['Diagnostics']), common=len(pack['CommonNodes']),
        profession=len(pack['ProfessionOverrides']['LY'])), ensure_ascii=False))


if __name__ == '__main__':
    main()
