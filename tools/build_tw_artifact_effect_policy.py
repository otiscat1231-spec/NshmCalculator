"""Routing metadata, not a second copy of artifact game values. Frozen pack stays read-only."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'NshmCalculator.MudClient/wwwroot/data/artifacts/TW-Mobile-2.3.3'

def inventory(nodes, policy):
    rows = []
    for node in nodes:
        keys = sorted({f"{e['Scope']}:{e['Key']}" for level in node['Levels'] for e in level['Effects']})
        details = [dict(Key=key, **policy['Routes'][key]) for key in keys]
        details += [dict(Key=s['Kind'], **policy['SpecialRoutes'][s['Kind']]) for s in node['SpecialEffects']]
        if node['Id'] in policy['NodeRuntimeNotes']:
            details.append(dict(Key='runtime', Category='Unmodeled', Targets=[], Domain='PvE', Note=policy['NodeRuntimeNotes'][node['Id']]))
        rows.append(dict(NodeId=node['Id'], PositionId=node['PositionId'], Name=node['Name'],
                         ProfessionId=node.get('ProfessionId', ''), Shared=node['Shared'],
                         Categories=sorted({d['Category'] for d in details}), Effects=details, Options=node['Options'],
                         SourceRows=sorted({level['Source'] for level in node['Levels']})))
    output = ROOT / 'research/artifacts/TW-Mobile-2.3.3'
    (output / 'phase2da-effect-inventory.json').write_text(json.dumps(dict(PolicyId=policy['Id'], CoreRule=policy['CoreRule'], Nodes=rows),
                                                        ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    lines = ['# Phase 2D-A 神器效果分類表', '',
             '分類可複數並存；所有效果係數讀取凍結資料包。此表是分類 metadata，不是第二份遊戲數值主檔。完整 38 節點、40 類型路由、選項與作用技能見 `phase2da-effect-inventory.json`。', '',
             '| 節點／位置 | 分類 | 沿用方法／缺口 |', '|---|---|---|']
    for row in rows:
        notes = '；'.join(dict.fromkeys(d['Note'] for d in row['Effects'] if d['Key'] != 'runtime')) or '原Model為技能機制文字；缺執行時間軸與連動資料'
        if row['Name'] == '眾法歸一':
            notes = '五維逐項+9/級；Lv5逐項floor候選。只產生首克；不轉成攻擊等面板'
        lines.append(f"| {row['Name']} / {row['PositionId']} | {'、'.join(row['Categories'])} | {notes} |")
    (output / 'PHASE-2DA-CLASSIFICATION.md').write_text('\n'.join(lines)+'\n', encoding='utf-8')

def main():
    pack = json.loads((DATA / 'artifact-pack.json').read_text(encoding='utf-8'))
    special = {
        'professionSkill.damageIncrease': ('TaggedSkillDamage', ['LY.professionSkills'], 'SH_001', '資料係數×Legacy流派技能占比；只注入一次'),
        'yinFeng.damageIncrease': ('TaggedSkillDamage', ['LY.yinFeng'], '', '缺吟風傷害占比及疊加規則'),
        'swordQi.monsterExtraDamage': ('TaggedSkillDamage', ['LY.swordQi.monster'], '', '需誅邪·借天劍分支、劍氣技能群占比與疊加規則'),
        'zhuXie.damageIncrease': ('TaggedSkillDamage', ['LY.zhuXie'], '', '缺誅邪傷害占比及疊加規則'),
        'swordIntent.pursuitDamageIncrease': ('TaggedSkillDamage', ['LY.swordIntent.pursuit'], '', '缺劍意追擊占比；不同節點同群增傷疊加方式未驗證'),
        'leiLong.monsterExtraDamage': ('TaggedSkillDamage', ['LY.leiLong.shuangChe.monster'], '', '保留技能分支標識；缺傷害占比與適用技能邊界'),
        'jingLei.cooldownReduction': ('RotationEffect', ['LY.jingLei'], '', '缺循環、施放頻率、資源與時間軸；不乘總DPS'),
        'jianDang.attackSpeedIncrease': ('RotationEffect', ['LY.jianDang'], '', '缺攻速對動作、命中次數、循環的映射；不乘總DPS'),
        'jingLei.interactionMaxStacks': ('ConditionalAverage', ['LY.jingLei.interaction'], '', '只提供上限，不推定實際平均層數'),
        'professionSkill.damageReduction': ('Unmodeled', ['incoming.professionSkills'], '', '防禦效果，非PVE輸出增傷'),
        'longFei.damageReduction': ('Unmodeled', ['LY.longFei.incomingDamage'], '', '減傷與技能邊界未完整驗證；非輸出倍率'),
        'leiLong.shieldHealth': ('Unmodeled', ['LY.leiLong.shield'], '', '護盾效果，非輸出倍率'),
        'yinFeng.lightnessReduction': ('Unmodeled', ['target.lightness'], '', '輕功機制不作輸出增傷'),
        'yinFeng.lightnessRecovery': ('Unmodeled', ['self.lightness'], '', '輕功回復不作輸出增傷'),
    }
    pvp = {'professionSuppression', 'professionResistance', 'targetProfessionSuppression', 'targetProfessionResistance'}
    routes = {}
    nodes = pack['CommonNodes'] + pack['ProfessionOverrides']['LY']
    for node in nodes:
        for level in node['Levels']:
            for effect in level['Effects']:
                key, scope = effect['Key'], effect['Scope']
                ident = f'{scope}:{key}'
                if ident in routes:
                    continue
                domain = 'PvP' if key in pvp else 'PvE'
                if scope == 'panel':
                    category, targets, share, note = 'StaticPanel', [], '', '進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性'
                elif key == 'fiveDimensions.displayIncrement':
                    category, targets, share, note = 'StaticPanel', [], '', '使用本次使用者確認的每維增量；不建立五維到戰鬥面板通用轉換'
                elif scope == 'triggeredPanel':
                    category, targets, share, note = 'ConditionalAverage', [], '', '保留auto/manual/off與覆盖率接口；PvP效果不進PVE'
                elif scope == 'targetProfession':
                    category, targets, share, note = 'Unmodeled', ['target.profession'], '', '指定流派PvP效果，不進PVE'
                else:
                    category, targets, share, note = special[key]
                routes[ident] = {'Category': category, 'Targets': targets, 'Domain': domain, 'LegacyShareCode': share, 'Note': note,
                                  'RequiredRootOption': 'LY-ROOT-A' if key == 'swordQi.monsterExtraDamage' else ''}
    policy = {
        'Id': 'TW-Mobile-2.3.3-effects-v1', 'SchemaVersion': 1, 'PackId': pack['Manifest']['Id'],
        'PackSourceSnapshotSha256': pack['Manifest']['SourceSnapshotSha256'], 'SupportedProfession': 'LY',
        'CoreRule': {
            'Id': 'TW-Mobile-2.3.3-core-floor-each-v1', 'Status': '暫定候選', 'Algorithm': 'floor-each-v1',
            'DimensionNames': {'constitution': '根骨', 'strength': '力量', 'spirit': '氣海', 'agility': '身法', 'endurance': '耐力'},
            'IncrementPerLevel': 9, 'BonusLevel': 5, 'BossFactor': 0.1,
            'InputBasis': '未含本盤眾法歸一；公式使用加點後各維數值',
            'Source': '使用者Phase2D-A指示：Lv1～5每級每維+9；Lv5首克逐維floor(各維×0.1)，可版本化替換；流派克制屬PvP不接PVE',
        },
        'Routes': routes,
        'SpecialRoutes': {
            'coreSuppressionAtMax': {'Category': 'ConditionalAverage', 'Targets': ['PvE.bossSuppression'], 'Domain': 'PvE',
                                      'LegacyShareCode': '', 'Note': '版本化候選公式，只產生PVE首克，不產生流派克制'},
            'interactionStacks': {'Category': 'ConditionalAverage', 'Targets': ['LY.jingLei.interaction'], 'Domain': 'PvE',
                                  'LegacyShareCode': '', 'Note': '新盘每層收益×明確平均層數／層數分布；缺覆蓋與層數時不假設滿層'},
        },
        'NodeRuntimeNotes': {n['Id']: '技能替換、氣劍、分支連動與觸發時序仍保留原Model；不自動換算總DPS' for n in nodes if not n['Shared']},
        'ProfessionDamageKey': 'professionSkill.damageIncrease', 'ProfessionDamageInput': 'TW_ART_PROF_DAMAGE_RATIO',
        'EquivalentFunctions': {'attack': 'FHX_005', 'elementAttack': 'FHX_007', 'hit': 'FHX_002', 'critical': 'FHX_001',
                                'defensePenetration': 'FHX_004', 'ignoreElementResistance': 'FHX_003', 'criticalDamage': 'FHX_009',
                                'bossSuppression': 'FHX_006', 'technicalSuppression': 'FHX_019', 'skillEnhancement.all': 'FHX_019'},
    }
    (DATA / 'effect-policy-v1.json').write_text(json.dumps(policy, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    inventory(nodes, policy)
    print(json.dumps({'routes': len(routes), 'specialRoutes': len(policy['SpecialRoutes']), 'nodes':len(nodes)}))

if __name__ == '__main__':
    main()
