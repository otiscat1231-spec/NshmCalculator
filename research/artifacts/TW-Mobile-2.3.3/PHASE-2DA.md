# Phase 2D-A：TW 新神器唯一來源與 Legacy 方法接線

基於 `tw-pve` 的 `a0a31843d95f32249e1375766af22b011ea1e3d6`。Legacy 模式、原設定、公式本體、Golden 與凍結神器包不變。TW 執行使用獨立設定副本：神器數值只由 `TW-Mobile-2.3.3/artifact-pack.json` 提供；候選五維規則另外版本化。此階段提供**局部候選模型值，不是已校準 DPS**，沒有神器最佳化器。

## 結構與呼叫

- `TwArtifactEffectAdapter`：分類、五維候選、條件平均、作用技能群與來源追蹤。
- `effect-policy-v1.json`：40 種效果路由、兩種特殊機制、技能群與平均化接口；不複製逐級遊戲數值。
- `TwArtifactPveAdapter`：核對原 config 雜湊；建立隔離副本；`Prepare` 輸出候選執行輸入；`CompareInputs` 輸出 A/B 差值。
- `EvaluateCandidate`：只計算靜態與明確條件面板、破空·威霆的局部模型值。不是完整神器輸出模擬，也不允許套用校準 λ。
- `EquivalentPanelGain`：呼叫原 `FHX_001/002/003/004/005/006/007/009/019`。單位先依既有 mapping 轉換；全技能點數沿用 `SH_005`。禁止五維、攻速、CD、技能分支作通用面板轉換。
- `InteractionStateExpectation`：明確狀態分布逐狀態執行，再求加權期望；只支援已點驚雷的互動層數，不假設不同節點相互獨立。

台服頁面的「產生 A/B 映射輸入快照」會使用上述新接線。五維另設五個獨立欄位；沒有值便保留未知。快照中間層保留 Phase 2C 的原設定衝突證據；**真正 TW 執行輸入**另列 JSON，沒有 SQ 控制。台服頁不再提供用舊神器情境計算的按鈕；Legacy 資料模式與 `/pve` 仍保留原有功能。沒有加入完整 DPS 預測 UI。

## 五維：候選規則與資料基準

`ResearchBuild.BaseFiveDimensions` 獨立保存：constitution 根骨、strength 力量、spirit 氣海、agility 身法、endurance 耐力。

基礎值契約為「未含本盤眾法歸一」。Lv1～5 各維增量 = Lv × 9；Lv5 以**加點後各維**求：

```
SUM(floor(各維 × 0.1))
```

規則 ID `TW-Mobile-2.3.3-core-floor-each-v1`，狀態「暫定候選」，來源是本次使用者指示。不能用 `floor(五維總和 × 0.1)` 取代。缺任一維便保留未知，不能從 Legacy 的合併力量／氣海欄、舊五維等效收益或默認面板補值。

替代算法需新的 Algorithm/Rule ID 及明確註冊；未知算法拒絕。不建立五維→攻擊、破防、會心、命中等公式；不產生 PvP 流派克制；不回填 Legacy ST_011～014。未申報的基礎值已含神器問題仍無法只靠數字辨識，輸入者必須遵守基礎值契約。

## 來源隔離

| 入口 | TW 處理 | Legacy 處理 |
|---|---|---|
| SQ_003 舊神器選項、SQ_004 等級 | 從 overlay 前端與默認輸入刪除；原 Build 舊值只留核對，不讀取效果 | 原樣保留 |
| FSQ_001 舊神器內部支援 | 從 overlay 刪除 | 原樣保留 |
| 28 個 RF_SQ_* 舊神器等效收益 | 全部從 overlay 刪除；不能注入新盤面板 | 原樣保留 |
| FZJ_010 寫死的 `0.05*[SH_001]` | **只在 TW 副本**換成 `[TW_ART_PROF_DAMAGE_RATIO]*[SH_001]` | 原公式不變 |
| TW_ART_PROF_DAMAGE_RATIO | 新盤破空·威霆實際等級效果 ÷ 100 × 明確覆蓋率；不接受 Build.Parameters 外部注入 | 不新增到原 config |
| TW 靜態面板 | `基礎 + 新盤靜態 + 明確候選條件增量`，一次映射成 ST_*；不得再加一次神器 | 舊獨立面板流程保留 |
| SH_*、FNG_*、FG_*、FHX_* 等方法與情境 | 保留原方法／情境；其他公式與規則逐筆一致，僅 FZJ_010 的神器來源改接 | 原樣保留 |

建構時掃描剩餘公式、參數及 Lambda 依賴，發現 SQ／FSQ／RF_SQ 引用便拒絕。執行前再驗證來源、重複、缺值與新盤係數。每次候選／Legacy 執行重新初始化自身設定並共用計算鎖，避免全域 engine 狀態串用。

原 Legacy config：`4.0.0.3 / 20250302001`；這是 Repo 版本，不是台服遊戲版本。其他目標、內功、裝備、特質、技能占比仍屬 `LegacyContextUnverified`，不是本階段新增遊戲事實。

## 分類與成熟方法

完整 38 節點表見 [PHASE-2DA-CLASSIFICATION.md](PHASE-2DA-CLASSIFICATION.md)，逐效果機器資料見 `phase2da-effect-inventory.json`。

| 分類 | 實際接口 | 不做的推定 |
|---|---|---|
| StaticPanel | 原 Build final → ST_* mapping；signed 值保留 | 不把五維或 PvP 值轉成 PVE 攻擊等 |
| TaggedSkillDamage | 保留精確技能／技能群；占比 × 新數值 × 覆蓋率方法；目前只有破空·威霆接 SH_001 | 不把吟風、誅邪、追擊、劍氣與雷龍的占比當成 SH_001；不假設分支疊加 |
| ConditionalAverage | auto/manual/off；manual 明確覆蓋／平均層數；auto 接實測覆蓋／層數／完整分布 | 不假設驚雷一直滿層，也不以 CD 取代互動層數資料 |
| RotationEffect | 保存驚雷逐級 CD 與劍蕩逐級攻速 | 不乘總 DPS；不硬推額外施放次數 |
| Unmodeled | 保留技能替換、氣劍連動、護盾、減傷、輕功等原機制與來源 | 不轉成輸出倍率 |

使用者「冷卻結束即觸發」準則實作為 `min(1, 持續時間/冷卻)` 的**覆蓋率推估接口**；來源與狀態保留為推估。它無法提供缺少的持續時間，亦無法推得驚雷互動層數。auto 無所需資料便 Pending；manual 無明確值也 Pending；off 清除條件增量。PvP 條件即使手動指定，也不進本 PVE overlay。

## 衝突、缺口與紅隊

1. 凍結 2A CORE 特殊規格仍保存舊「五維總和／未定取整」描述。此為歷史研究歧義。本次新候選規則明確覆蓋 TW 接線解讀，**沒有改研究主檔或原凍結數值**。
2. Legacy 固定神器 5% 與新盤可變等級來源衝突，已在 TW 副本隔離。`SH_001` 占比框架保留，但其中示範占比仍未獲台服龍吟實測驗證。
3. `SH_004` 的名稱／示範技能標籤存在持續與群體歧義；不拿它替代吟風、劍氣或其他細分技能占比。星照全技能／單體／群體／爆發／持續點數保持分欄，ST_015 只映射全技能。
4. 誅邪·建武文字中的特定分支怪物額外效果、兩個 ROOT 選項的技能替換／氣劍／進入狀態機制未完整結構化；保留原 Model，沒有自造數值或把它壓成全局增傷。
5. 驚雷缺實際平均層數、層數停留時間及與循環的相關性；劍蕩攻速、驚雷 CD 缺循環、施放頻率、資源與動作時間軸。龍飛減傷、雷龍護盾與輕功不屬輸出模型。
6. **D(平均屬性) ≠ 平均[D(各狀態)]**：前者是顯式近似；後者需要狀態分布，提供獨立接口。沒有資料就不能排除這項誤差。
7. 加點 CORE 會自動補父鏈；其他面板差異可能是父鏈收益，不能單獨歸因於五維。測試以相同完整盤靜態投影作控制。

## 驗證

已執行：

- 非瀏覽器 regression：1989 項，1988 通過、1 項既有 PlausibleVersionTest 略過、0 失敗。
- 新 Phase2D-A adapter：44 項通過。
- 既有 Artifact：25 規則 + 303 來源 Golden，全部通過；Build：16 項通過；Phase2C mapping：41 項通過。
- Legacy Golden：8 組、1648 輸出，原期望值不變。
- Chrome 無頭 UI regression：14 項全部通過（既有盤面操作、映射與新來源／五維接線）。
- Client 與 Test 建置成功；既有 NU1902（NCalcSync 5.2.11）警告保留，沒有更動依賴。
- 五份保護檔案內容與基準 commit 一致，Git diff 無變動。詳細計數／Git blob 及 Windows checkout 雜湊見 `phase2da-validation.json`；Windows 換行使用 CRLF，Git blob 使用 LF。

原始保護雜湊：

- artifact-pack.json：`fd3fab922ceebeaf5c4fc6307fd962e24b0f5dcf0dbce97b906c605dcae97351`
- config_pve.json：`b291e208e17102b369adaf5f10bb38d70b0b528b13722dc857f36b2b3d2941e4`
- PveUtility.cs：`082def1a8a771fd74a1efe3056608a4bc3ab8b658aee4fd45389e43357dbf0d4`
- pve_legacy_golden.json：`2c9cc240e3a562a952222b6e8985cadf35f7bb52b75c5dea2a501589f59195c8`
