# Phase 2C：TW 最終靜態面板 → Legacy 候選輸入

基於 `tw-pve` commit `4f1ec79e9a62827d9f330302462d7074d3a0c906`。完成的是映射、來源追蹤與防重複，**未執行映射後的傷害公式，未校準 DPS**。研究主檔、遊戲數值、Legacy 公式與 Golden 原檔均未修改。

## 入口與資料

- `NshmCalcuator/Shared/Research/LegacyStaticInputAdapter.cs`：獨立 adapter；不呼叫 `PveUtility`、`Evaluate` 或 `PredictDps`。
- `NshmCalculator.MudClient/wwwroot/data/pve/adapters/tw-static-v1.json`：11 個映射、單位轉換、舊神器停用控制、28 個排除輸出及內嵌公式衝突。
- `PHASE-2C-MAPPING.md`：逐欄映射表，含來源欄位、單位、覆寫與重複風險。
- `phase2c-legacy-inventory.json`：全部 163 個前端輸入；直接及遞迴使用位置、原始公式、函數與特殊規則依賴。圖涵蓋內部公式、結果公式、COUNT 字串引用及 FHX 函數调用。
- `tools/audit_tw_legacy_mapping.py`：只讀原 config，重建上述 metadata 與盤點；不更新任何遊戲數值或正式 Google Sheet。
- 頁面按「產生 A/B 映射輸入快照」，查看值、差值及來源；展開完整 JSON 可複製所有情境欄位與排除清單。修改面板、神器、流派、套路或已接線的 Legacy 輸入時快照失效，須重新產生。

Legacy config 是 `4.0.0.3` / `20250302001`，這是 Repo 資料版本，**不是台服遊戲版本號**。神器使用 `TW-Mobile-2.3.3`。初始化核對 Legacy config SHA256；不同內容即拒絕映射，不能靜默沿用舊欄位假設。

## 唯一來源規則

| 層／來源 | 所有權及處理 |
|---|---|
| TW 基礎面板 | 使用者提供未含本盤神器的固定值；未知留空。不可把戰鬥觸發後的數字冒充靜態基礎。無法從數字辨識未申報的既有神器增量。 |
| 新神器 StaticAttributes | 唯一入口為既有 `BuildAttributeLayer.Project`；基礎 + 靜態增量 = final。只映射 final，禁止 adapter 再加一次神器。 |
| 舊 `ST_*` 面板 | 映射欄位一律被 TW final 取代，舊值只留在 `ReplacedLegacyInput`；未知的 TW base 不退回舊 ST 示範值。 |
| 新神器條件／技能／機制／目標流派效果 | 不注入任何 Legacy 欄位。非 panel 效果和機制仍列於快照，不能透過 `CombinedAttributes` 偷渡。 |
| 舊神器控制 | `SQ_004=0`；`SQ_003` 沒有「無」，所以保留合法選項 `刃影摧风` 並配零等級。原 Build 的獨立 Legacy 輸入不改寫。 |
| 舊神器等效收益 | 28 個 `RF_SQ_*` 全部排除，不可將其回填 ST 或乘上新面板。FSQ_001 也是舊收益計算支援，非 FBL_06 的祖先。 |
| Legacy 無條件神器項 | `FZJ_010` 的 `0.05*[SH_001]` 無法透過 SQ 停用。阻擋 TW 預測執行，不設 SH_001=0，不改公式。 |
| 其他 Legacy 來源 | 目標、內功、特質、藥品、裝備、技能占比等原樣保留於 `LegacyContextUnverified`。不是新增 TW 神器來源，也不是台服已確認效果。缺少情境欄位不補預設值。 |

每個映射欄位只有一筆 `TWFinalStaticPanel` 注入來源，內含基礎與神器增量稽核。顯式申報的 TW 神器第二次注入、Legacy 舊神器、等效收益、條件效果或多來源都會拒絕。未盤點前端欄位、內部公式碼及 RF 收益不可作為輸入。

`Validate` 在快照匯出前重新核对來源、單位、final=base+artifact、欄位值、停用控制及排除清單。`ExportMappedInputs` 只輸出候選輸入（可能不完整）；**不是可執行預測的授權**。`ValidatePredictionHandoff` 一律拒絕本階段的 TW 模型執行。`EngineEvaluationAllowed=false` 即使面板全填也不變。

防重複針對申報的來源及輸入與來源記錄是否一致；不是數值反推機制，也不是快照簽章。若手動把已含神器的數字填入 base，必須由資料來源核對發現。

## 標籤與降階策略

Legacy `ST_015` 是全技能**點數**，在 `FZJ_020` 乘 `SH_005`（所有標籤技能占比）再進通用克制池。不是傷害百分比。SH_004 和 KG_004 不是四類技能增強輸入。

只把 `skillEnhancement.all` 寫入 ST_015。四種標籤各自保存於 `TaggedSkillEnhancement`，不平均、不相加、不用「占比最多標籤」取代。`EffectiveTaggedSkillEnhancement` 表示 all + 各標籤的額外增量；因此四種 base tag 欄位的契約是「相對 all 的額外值」，不是已含 all 的完整面板。all=5600、各 tag 額外值=0 時，四種有效值都為5600；星照爆發增量1400只改爆發有效值，ST_015不跟著增加。缺少任一 tag 的 base 時，其有效值仍未知，即使神器增量已知。

這是**不完整、有資訊保留的降階**：Legacy 暫不計標籤專屬增量，不能稱為完整神器傷害模型。斷金戈爆發12%等其他技能效果不由此 adapter 新增。

## 所有辨識出的歧義／衝突

| 欄位／機制 | 觀察與處理 |
|---|---|
| FZJ_010 / SH_001 / RF_SQ_030 | FZJ_010無條件含流派占比×5%；RF_SQ_030明確把此項用於「流派大節點」收益。新盤M07流派增傷在ScopedEffect，尚不准进DPS。SQ=0不能移除；阻擋TW預測執行，須後續獨立處理公式隔離。 |
| professionSuppression | Legacy沒有獨立流派克制輸入；ST_003是技巧克制，兩者不可互換。保留於Omissions。 |
| skillEnhancement.single/group/burst/sustained | 無一對一Legacy欄位，保留標籤與未知值；不併入ST_015。 |
| SH_004 / KG_004 | SH_004的Name為「持续技能」，預設StringValue是「群体技能」；KG_004只選占比最多標籤。不能用它推導完整四標籤占比。 |
| attack / ST_001 | 只有單一代表攻擊值；最小/最大攻擊與抽樣分布如何合併未驗證，不自动平均。Legacy還會除面板攻擊%再乘最終攻擊%。 |
| criticalDamage / ST_009 | TW用完整倍率百分點175→Legacy1.75；不是把額外75%當完整倍率。百分點轉小數，不是遊戲新常數。 |
| bossSuppressionPercent / ST_010 | 5→0.05；Legacy另加基底1。不能把首領克制點數填進此欄。此base由使用者明確提供，不自動從斬焰文字推填。 |
| bossSuppression / ST_002 | 只映射首領克制點數；怪物/建築數值及目標流派百分比不得自行合併。 |
| ST_004/005/006/007/008及FZJ額外池 | 只接受靜態點數，不能把穿透率、會心率或命中率寫入點數欄，也不能把已含戰鬥buff的面板再疊Legacy额外池。 |
| 內功KG_020～031、FNG、打造FDZ、裝備FMB、特質FTZ、藥品 | 舊版假設與普通／靈韻仍需獨立核對。Legacy示範預設包含靈韻選項，不能當作使用者目前六顆普通內功。未把它們標成已確認TW配置，也沒有替使用者猜配装或改數值。 |
| BO_* / KG_002 / SH_* / FG_* | 目標常數、木樁普通/英雄與技能占比／覆蓋率保留原情境及來源欄；不可從Repo選項推定台服目標，不跨流派套校準。 |
| ROOT/CORE五維、驚雷、龍飛、劍蕩等 | 未建模的五維映射、層數、取整、觸發與技能時序不進static輸入。原2A/2B資料包及40筆診斷原封保留；不是40個新增遊戲數值衝突。 |

本次不自行修正上述遊戲規則或研究主檔。直接程式證據與候選機制判讀分開：163個欄位及引用關係是程式盤點結果；其台服真實適用性仍待驗證。

## 驗證與交付

測試結果見 `phase2c-validation.json`：Artifact規則及來源Golden、Build接線、新adapter單位／signed／缺值／五標籤／來源防重複／快照重載／跨流派／公式衝突拒絕，以及Legacy完整回歸。瀏覽器另驗證來源顯示、差值、缺值、靜態增量、快照失效、JSON與神器盤既有操作。

`phase2c-synthetic-input-snapshots.json` 是明確標註的**程式測試例**，仅展示來源與差值，不是使用者木樁資料，不回寫研究主檔。完整快照可由頁面產生；目前没有可靠用户TW基础面板，不能交付真实配置的預測輸入或DPS。

保護檢查：`PveUtility.cs`、公式DTO、原始config及其archive、神器資料包、Legacy Golden均未改。舊版獨立示範比較入口仍獨立，不接收新映射結果；本階段無任何 mapped-input → DPS 執行路徑。

## 新增／修改檔案

新增：

- `NshmCalcuator/Shared/Research/LegacyStaticInputAdapter.cs`
- `NshmCalculator.MudClient/Components/TwLegacyInputSnapshots.razor`
- `NshmCalculator.MudClient/wwwroot/data/pve/adapters/tw-static-v1.json`
- `NshmCalculator.Test/Research/LegacyStaticInputAdapterTest.cs`
- `NshmCalculator.Test/Browser/LegacyInputSnapshotUiTests.cs`
- `tools/audit_tw_legacy_mapping.py`
- `research/artifacts/TW-Mobile-2.3.3/PHASE-2C.md`
- `research/artifacts/TW-Mobile-2.3.3/PHASE-2C-MAPPING.md`
- `research/artifacts/TW-Mobile-2.3.3/phase2c-legacy-inventory.json`
- `research/artifacts/TW-Mobile-2.3.3/phase2c-synthetic-input-snapshots.json`
- `research/artifacts/TW-Mobile-2.3.3/phase2c-validation.json`

修改：

- `NshmCalculator.MudClient/Components/TwBuildEditor.razor`：補技巧克制／首克百分比輸入，編輯時使快照失效。
- `NshmCalculator.MudClient/Pages/Calculators/TwPveCalculator.razor`：資料規格載入、A/B候選輸入快照；獨立Legacy比較不改公式。
