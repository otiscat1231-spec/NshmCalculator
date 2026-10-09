# Phase 2D-B：TW 目標與實際面板錨定

基準 commit：`4f1ef98eca702b0598b490f786bcabcec10d1f64`。本階段新增目標參數層與 actual-panel anchor；沒有新增技能標籤／占比模型、攻速／CD 的 DPS 換算或神器最佳化器。Legacy 模式、公式本體、目標原檔與 Golden 保留。

## TW Target

資料包 `data/pve/targets/tw-mobile-2.3.3-v1.json`，ID `TW-Mobile-2.3.3-targets-v1`，台服手遊研究包；現服完整版本與其餘目標條件仍須獨立核對。來源 `USER-Phase2D-B`。Hero／Honor 是使用者提供的研究代號，不擅自替換成其他木樁名稱。

| 目標 | defense | defenseConstant | elementResistance | elementConstant |
|---|---:|---:|---:|---:|
| Hero | 31555 | 28060 | 6132.5 | 7012 |
| Honor | 29779 | 26526 | 5786 | 6615 |

Hero 元素參數採原候選範圍中點：抗性 6130～6135、常數 7004～7020。原範圍保存，沒有改觀察值或拿比例推常數；範圍所有組合並非都已驗證。

```
R_D = K_D / (K_D + max(D-P,0)) - 0.1
R_E = K_E / (K_E + max(E-I,0))
```

`K_D/D≈0.89`、`K_E/E≈1.143` 只留 metadata。公式使用各目標名義參數。顯示採百分比一位小數、AwayFromZero 捨入；這是回歸用的候選顯示規則，不代表已證明遊戲內部捨入實作。

全部 22 個使用者顯示點保留原值，按名義參數回歸時顯示值逐項相等，原始百分比誤差均小於 0.05 百分點。顯示點吻合不等於唯一識別所有參數或證明公式。

`TwTargetModel` 不讀 Legacy config、BO 默認值或比例。TW adapter 將 **全部 12 個 BO 目標欄**改成 TW source：BO_01/03/06/07 採新包；首領抵禦、會抗、格擋、命中／會心常數、保底攻擊及目標單位等 8 欄保留未知，不沿用舊值。原值僅在核對快照的 ReplacedLegacyInput 留作歷史證據。

UI 顯示 Hero／Honor、目標資料版本、研究候選狀態、名義參數與原範圍；產生快照後顯示 A/B 防穿與元素穿透百分比。這兩項只按可建模面板的破防／忽視計算，不自動添加戰鬥短暫 Buff。

## Actual panel anchor

- `ResearchBuild.ObservedAttributes`：目前遊戲實際脫戰面板，已含目前盤，避免短暫 Buff。
- `ObservedFiveDimensions`：目前實際根骨、力量、氣海、身法、耐力，已含目前 CORE。
- 歷史 `BaseAttributes/BaseFiveDimensions` 保留原語意；不從裸面板或 Legacy 示範值自動補觀測。
- `TwActualPanelAnchor`：目前 Build、實際讀值、盤面及條件的獨立 JSON 快照，帶 ID／神器包版本／效果Policy及CORE規則版本／SHA256；不引用可變候選字典。
- `TwActualPanelSession`：目前盤、候選盤、固定 anchor、目標選擇分開保存。

```
candidatePanel = observedCurrentPanel + candidateArtifact - currentArtifact
candidateFive  = observedCurrentFive  + candidateCoreGain - currentCoreGain
```

五維只扣回目前 CORE 各維的已知增量，用於算候選 CORE；不對攻擊、破防等建立通用五維轉換。Lv5 首克仍使用 `core-floor-each-v1`，按 candidateFive 每維乘 0.1 各自 floor 再相加。

實際首克面板若已含目前 Lv5 CORE，本接線減去目前 CORE 首克候選、再加候選 CORE 候選值。普通靜態 signed 增量也同樣只作差，不會再將整個候選增量加在已含神器的面板上。

缺值不補 0、不退回 Legacy：

- 未填某觀測欄，候選該欄保持未知，即使神器增量已知。
- 目前與候選 CORE 等級相同、五維基準不變時，CORE 首克差量確定為 0，允許未知絕對值相消；仍保留各維未知。
- 跨越 Lv5 且缺少五維時，CORE 首克差量及候選首克面板保持未知，映射不注入 ST_002。
- 目前盤必須明確提供，不能把缺盤當零盤。五維比目前 CORE 增量還低時拒絕，提醒檢查實際面板契約。

脫戰錨點不扣除不存在的驚雷等短暫觸發 Buff。預設投影關閉短暫條件，保留 CORE 常駐候選與破空·威霆原有接口；Phase2D-A 的明確研究情境接口仍保留，沒有開始新的技能／占比計算。

## 產品操作與保存

1. 選 Hero 或 Honor（標示研究候選）。
2. 在 A 填目前實際面板與五維，配置目前實際神器盤。
3. 按「建立錨點，並以目前盤初始化候選盤」。首次建立會深複製目前盤到候選盤。
4. 選「編輯 Build B 神器」試候選盤。B 不另填實際面板，全部差量參照固定 anchor。
5. 按「產生 A/B 映射輸入快照」看面板、差值、目標百分比與逐欄來源。
6. 「儲存此流派 A/B」分開保存目前盤、候選盤、anchor 及 TargetSelection。

編輯候選不改 anchor。編輯目前觀測、目前盤或目前條件時，舊 anchor 保留並提示需更新；不自動覆寫、不允許用未確認的新目前資料產生快照或儲存。更新 anchor 是明確操作，保留候選盤。可用「候選盤複製固定目前盤」恢復基準。

actual anchor 使用獨立 `:actual-anchor-v1` 存檔 namespace。舊裸面板存檔與 Legacy 存檔不刪除、不重解讀。Legacy 資料包／原版頁保留原操作。本產品配置的 TW adapter 拒絕 bare `Prepare` 回退，必須使用 `PrepareAnchored` 與版本化 TW target。

## 未建模與紅隊

- 五維對流派攻擊／元素／破防／會心等的間接影響未建模；候選面板為可建模部分，不保證完整重現遊戲換盤後面板。
- 規則仍是候選；只憑顯示精度資料不能排除其他函數，也不能把參數視為精確唯一解。
- 基準實際面板若混入短暫 Buff，或目前盤填錯，無法只靠數字可靠辨識；UI 明確要求脫戰、避免短暫 Buff、目前盤一致。
- CORE 首克是否與遊戲面板完全同層仍屬候選；目前／候選使用同一版本規則作差，未證明的部分明確標記。
- TW 的首領抵禦／會抗／格擋等未知，完整 DPS 執行仍被缺值阻擋；不混用 Legacy 目標。
- 技能標籤、技能占比、攻速／CD 時間軸、神器 optimizer 及 DPS 校準均未新增。

## 驗證

- 新增目標／錨定回歸：34 項全部通過，包含 22 個顯示點的精確顯示回歸、原始誤差、版本隔離、signed 差量、CORE各級、缺值、保存／回讀與防止 anchor 被候選修改。
- 全部非瀏覽器 regression：2023 項，2022 通過、1 項既有 PlausibleVersionTest 略過、0 失敗。
- Artifact 規則 25 + 來源 Golden 303、Build 16、Phase2C mapping 41、Phase2D-A 44 均通過。
- Legacy Golden：8 組、1648 輸出，原期望值不變。
- 全部可發現的瀏覽器 regression：16 項全部通過，含 Hero／Honor 切換、候選不修改 anchor、目前資料須明確 recapture、signed A/B 差量、存檔／重載與既有盤面操作。
- Client／Test 建置成功；原有 NU1902（NCalcSync 5.2.11）警告保留，依賴未更動。
- 原神器包、兩份 Legacy config、PveUtility 與 Golden 內容／Git diff 不變；Legacy manifest 條目逐項一致。

詳細 22 點預測、殘差、計數與保護檔案雜湊見 `phase2db-validation.json`。
