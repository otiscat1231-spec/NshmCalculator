"""Read-only Legacy inventory and reproducible Phase 2C mapping contract. No game values edited."""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'NshmCalculator.MudClient/wwwroot/data'
REPORT = ROOT / 'research/artifacts/TW-Mobile-2.3.3'
ADAPTER = DATA / 'pve/adapters/tw-static-v1.json'


def main():
    raw = (DATA / 'config_pve.json').read_bytes()
    config = json.loads(raw)
    sha = hashlib.sha256(raw).hexdigest()
    definitions = [
        ('attack', '攻擊（代表值）', 'ST_001', 'point', 1),
        ('bossSuppression', '首領克制', 'ST_002', 'point', 1),
        ('technicalSuppression', '技巧克制', 'ST_003', 'point', 1),
        ('defensePenetration', '面板破防', 'ST_004', 'point', 1),
        ('elementAttack', '元素攻擊', 'ST_005', 'point', 1),
        ('ignoreElementResistance', '忽視元素抗性', 'ST_006', 'point', 1),
        ('critical', '會心', 'ST_007', 'point', 1),
        ('hit', '命中', 'ST_008', 'point', 1),
        ('criticalDamage', '會心傷害（完整倍率百分點）', 'ST_009', 'percentagePoint', 100),
        ('bossSuppressionPercent', '首領克制百分比（不含倍率基底1）', 'ST_010', 'percentagePoint', 100),
        ('skillEnhancement.all', '全技能增強', 'ST_015', 'point', 1),
    ]
    risks = {
        'ST_001': '覆寫舊ST攻擊；舊神器攻擊收益禁止再次加入。Legacy仍會除面板攻擊%再乘最終攻擊%。',
        'ST_002': '覆寫舊ST首克；不推定怪物/建築克制合併。舊神器首克收益禁止再次加入。',
        'ST_003': '技巧克制独立；不得用流派克制替代。',
        'ST_004': '只映射靜態破防；斬焰、藥品、特質等Legacy額外破防仍是候選來源。',
        'ST_005': '只映射靜態元素；Legacy仍會處理面板元素%與額外元素來源。',
        'ST_006': '只映射忽視點數；不把穿透率寫入此欄。',
        'ST_007': '只映射會心點數；Legacy仍會處理面板會心%及額外會心。',
        'ST_008': '只映射命中點數；不把命中率寫入此欄。',
        'ST_009': '百分點÷100；例175→1.75，是完整會傷倍率而非額外75%；舊刃影收益不得再加。',
        'ST_010': '百分點÷100；例5→0.05；非首克點數，Legacy公式另加基底1。',
        'ST_015': '全技能點數單獨覆寫；SH_005占比由Legacy情境保留；不加上四種標籤。',
    }
    unmapped = [
        {'SourceKey': 'professionSuppression', 'Reason': '無獨立Legacy流派克制欄位；ST_003是技巧克制，不可替代。'},
        *[{'SourceKey': f'skillEnhancement.{tag}', 'Reason': 'Legacy只有ST_015全技能；保留標籤增量與有效值，不平均、不相加、不改SH_004。'}
          for tag in ['single', 'group', 'burst', 'sustained']],
    ]
    spec = {
        'Id': 'TW-FinalStatic-to-Legacy-v1', 'ArtifactPackId': 'TW-Mobile-2.3.3',
        'LegacyConfigSha256': sha, 'LegacyVersion': config['Version'],
        'LegacyInternalVersion': config['InternalVersion'],
        'Mappings': [{'SourceKey': key, 'Label': label, 'SourceUnit': unit, 'LegacyCode': code,
                      'LegacyUnit': 'ratio' if div == 100 else 'point', 'Divisor': div, 'DuplicateRisk': risks[code]}
                     for key, label, code, unit, div in definitions],
        'Unmapped': unmapped,
        # SQ_003 has no "none" option. Choosing a deterministic valid option with level 0
        # disables the control, but cannot erase unconditional RF_SQ marginal-benefit outputs.
        'DisabledInputs': [
            {'Code': 'SQ_003', 'NumberMode': False, 'NumberValue': 0, 'StringValue': '刃影摧风'},
            {'Code': 'SQ_004', 'NumberMode': True, 'NumberValue': 0, 'StringValue': '0'},
        ],
        'ExcludedResultCodes': [f['Code'] for f in config['ResultFormulas'] if f['Code'].startswith('RF_SQ_')],
        'EmbeddedFormulaConflicts': [{
            'Code': 'FZJ_010', 'Name': '技强%', 'Term': '0.05*[SH_001]', 'InputCodes': ['SH_001'],
            'EvidenceCode': 'RF_SQ_030',
            'Reason': 'Legacy無條件含流派技能占比×5%，RF_SQ_030將此項標作流派大節點收益；SQ=0不能移除。新盤M07此效果屬ScopedEffect，尚不允許進DPS。不得以SH_001=0補償，也不修改Legacy公式；阻止TW預測執行。',
        }],
    }
    fronts = {f['Code']: f for f in config['FrontParamInfoArray']}
    formulas = {f['Code']: f for kind in ['InternalFormulas', 'ResultFormulas'] for f in config[kind]}
    known = set(fronts) | set(formulas)
    graph = {}
    for code, f in formulas.items():
        # Include quoted COUNT references, explicit FormulaParam, FHX function calls and rules.
        tokens = set(re.findall(r'\[([^\]]+)\]', f['Formula'])) | set(f.get('FormulaParam') or [])
        tokens |= set(re.findall(r'\b([A-Z][A-Z0-9]*_[0-9]+)\s*\(', f['Formula']))
        for rule in f.get('Rule') or []:
            tokens |= set(rule.get('LambdaParam') or [])
        graph[code] = tokens & known

    def ancestors(code, seen=None):
        seen = set() if seen is None else seen
        if code in seen:
            return set()
        seen.add(code)
        out = set(graph.get(code, []))
        for dep in list(out):
            out |= ancestors(dep, seen)
        return out

    reachable = {code: ancestors(code) for code in formulas}
    inventory = []
    for code, f in fronts.items():
        direct = [c for c, deps in graph.items() if code in deps]
        mapping = next((m for m in spec['Mappings'] if m['LegacyCode'] == code), None)
        inventory.append({
            'Code': code, 'Name': f['Name'], 'Group': f.get('GroupName'), 'Mode': f['Mode'],
            'Options': f.get('Options'), 'Values': f.get('Values'), 'Remark': f.get('Remark'),
            'Unit': mapping['LegacyUnit'] if mapping else 'Legacy原單位／語意須個別確認',
            'TWOverwrite': bool(mapping), 'Mapping': mapping,
            'DirectConsumers': direct,
            'TransitiveConsumers': [c for c, deps in reachable.items() if code in deps],
            'ReachesBaselineFBL06': code in reachable['FBL_06'],
            'Treatment': 'TWFinalStaticPanel' if mapping else 'DisabledLegacyArtifact' if code.startswith('SQ_')
                         else 'BuildProfession' if code == 'KG_001' else 'LegacyContextUnverified',
        })
    report = {
        'LegacyVersion': config['Version'], 'LegacyInternalVersion': config['InternalVersion'], 'LegacyConfigSha256': sha,
        'Engine': 'NshmCalcuator/Shared/PveUtility.cs; all front inputs copied; internal formulas evaluated separately',
        'FrontInputCount': len(fronts), 'InternalFormulaCount': len(config['InternalFormulas']),
        'ResultFormulaCount': len(config['ResultFormulas']),
        'FrontInputs': inventory,
        'StatAndEquivalentFormulas': [{**f, 'Dependencies': sorted(graph[c])} for c, f in formulas.items()
                                      if c.startswith(('FZJ_', 'FSC_', 'FBL_', 'FSQ_', 'FHX_', 'RF_SQ_'))],
        'BaselineDependencies': sorted(reachable['FBL_06']),
        'LegacyArtifactControlsReachBaseline': any(c.startswith('SQ_') for c in reachable['FBL_06']),
        'FSQ001ReachesBaseline': 'FSQ_001' in reachable['FBL_06'],
        'EmbeddedFormulaConflicts': spec['EmbeddedFormulaConflicts'],
        'TagAmbiguity': 'SH_004名稱持續技能，但預設StringValue群體技能；KG_004選占比最多標籤，非四標籤完整占比。',
        'Boundaries': 'Dependency inventory describes code, not validated Taiwan mechanics. FormulaParam/rule/function dependencies included.',
    }
    ADAPTER.parent.mkdir(parents=True, exist_ok=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    for path, obj in [(ADAPTER, spec), (REPORT / 'phase2c-legacy-inventory.json', report)]:
        path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    lines = ['# TW 最終靜態面板 → Legacy 輸入映射', '',
             f"Legacy config `{config['Version']}` / `{config['InternalVersion']}`；SHA256 `{sha}`。", '',
             '| TW final 屬性 | TW單位 | Legacy欄位 | Legacy單位／轉換 | 可覆寫 | 重複／歧義 |',
             '|---|---|---|---|---|---|']
    for m in spec['Mappings']:
        lines.append(f"| `{m['SourceKey']}` | {m['SourceUnit']} | `{m['LegacyCode']}` | {m['LegacyUnit']} / ÷{m['Divisor']} | 是，基礎未知時不寫入 | {m['DuplicateRisk']} |")
    for m in unmapped:
        lines.append(f"| `{m['SourceKey']}` | point | 無 | 保留於快照 | 否 | {m['Reason']} |")
    lines += ['', f"完整輸入盤點：{len(fronts)}個前端欄位，{len(config['InternalFormulas'])}個內部公式，{len(config['ResultFormulas'])}個結果公式。",
              '逐欄直接／遞迴使用位置、公式原文及規則見 `phase2c-legacy-inventory.json`。', '',
              '本表由 `tools/audit_tw_legacy_mapping.py` 產生；實作讀取 `data/pve/adapters/tw-static-v1.json`，避免UI另訂數值映射。']
    (REPORT / 'PHASE-2C-MAPPING.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(json.dumps({'FrontInputs': len(fronts), 'Mappings': len(definitions), 'ExcludedOldArtifactOutputs': len(spec['ExcludedResultCodes']),
                      'SQReachesBaseline': report['LegacyArtifactControlsReachBaseline'],
                      'FSQ001ReachesBaseline': report['FSQ001ReachesBaseline']}, ensure_ascii=False))


if __name__ == '__main__':
    main()
