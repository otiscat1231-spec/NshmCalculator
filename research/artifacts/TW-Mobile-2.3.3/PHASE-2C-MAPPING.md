# TW 最終靜態面板 → Legacy 輸入映射

Legacy config `4.0.0.3` / `20250302001`；SHA256 `b291e208e17102b369adaf5f10bb38d70b0b528b13722dc857f36b2b3d2941e4`。

| TW final 屬性 | TW單位 | Legacy欄位 | Legacy單位／轉換 | 可覆寫 | 重複／歧義 |
|---|---|---|---|---|---|
| `attack` | point | `ST_001` | point / ÷1 | 是，基礎未知時不寫入 | 覆寫舊ST攻擊；舊神器攻擊收益禁止再次加入。Legacy仍會除面板攻擊%再乘最終攻擊%。 |
| `bossSuppression` | point | `ST_002` | point / ÷1 | 是，基礎未知時不寫入 | 覆寫舊ST首克；不推定怪物/建築克制合併。舊神器首克收益禁止再次加入。 |
| `technicalSuppression` | point | `ST_003` | point / ÷1 | 是，基礎未知時不寫入 | 技巧克制独立；不得用流派克制替代。 |
| `defensePenetration` | point | `ST_004` | point / ÷1 | 是，基礎未知時不寫入 | 只映射靜態破防；斬焰、藥品、特質等Legacy額外破防仍是候選來源。 |
| `elementAttack` | point | `ST_005` | point / ÷1 | 是，基礎未知時不寫入 | 只映射靜態元素；Legacy仍會處理面板元素%與額外元素來源。 |
| `ignoreElementResistance` | point | `ST_006` | point / ÷1 | 是，基礎未知時不寫入 | 只映射忽視點數；不把穿透率寫入此欄。 |
| `critical` | point | `ST_007` | point / ÷1 | 是，基礎未知時不寫入 | 只映射會心點數；Legacy仍會處理面板會心%及額外會心。 |
| `hit` | point | `ST_008` | point / ÷1 | 是，基礎未知時不寫入 | 只映射命中點數；不把命中率寫入此欄。 |
| `criticalDamage` | percentagePoint | `ST_009` | ratio / ÷100 | 是，基礎未知時不寫入 | 百分點÷100；例175→1.75，是完整會傷倍率而非額外75%；舊刃影收益不得再加。 |
| `bossSuppressionPercent` | percentagePoint | `ST_010` | ratio / ÷100 | 是，基礎未知時不寫入 | 百分點÷100；例5→0.05；非首克點數，Legacy公式另加基底1。 |
| `skillEnhancement.all` | point | `ST_015` | point / ÷1 | 是，基礎未知時不寫入 | 全技能點數單獨覆寫；SH_005占比由Legacy情境保留；不加上四種標籤。 |
| `professionSuppression` | point | 無 | 保留於快照 | 否 | 無獨立Legacy流派克制欄位；ST_003是技巧克制，不可替代。 |
| `skillEnhancement.single` | point | 無 | 保留於快照 | 否 | Legacy只有ST_015全技能；保留標籤增量與有效值，不平均、不相加、不改SH_004。 |
| `skillEnhancement.group` | point | 無 | 保留於快照 | 否 | Legacy只有ST_015全技能；保留標籤增量與有效值，不平均、不相加、不改SH_004。 |
| `skillEnhancement.burst` | point | 無 | 保留於快照 | 否 | Legacy只有ST_015全技能；保留標籤增量與有效值，不平均、不相加、不改SH_004。 |
| `skillEnhancement.sustained` | point | 無 | 保留於快照 | 否 | Legacy只有ST_015全技能；保留標籤增量與有效值，不平均、不相加、不改SH_004。 |

完整輸入盤點：163個前端欄位，220個內部公式，206個結果公式。
逐欄直接／遞迴使用位置、公式原文及規則見 `phase2c-legacy-inventory.json`。

本表由 `tools/audit_tw_legacy_mapping.py` 產生；實作讀取 `data/pve/adapters/tw-static-v1.json`，避免UI另訂數值映射。
