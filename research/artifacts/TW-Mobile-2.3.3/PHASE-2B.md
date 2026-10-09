# Phase 2B：神器盤 UI 與 Build 屬性接線

基礎 commit：`cac31463c978f22bebc132c70db857eabb1006c4`，分支：`tw-pve`。

## 操作方式

開啟首頁或`/tw-pve`，流派選龍吟，使用「編輯 Build A 神器／編輯 Build B 神器」切換獨立盤面。

1. 每個節點固定顯示等級、上限、每級成本與投入點數；頂部顯示總神器點數。
2. 點「＋」時沿唯一父鏈補到最低門檻。門檻、等級、成本與選項效果都讀取`artifact-pack.json`，沒有把遊戲數值寫入 UI。
3. 自動補點節點有虛線框及「自動補 N 級」標記。本次補點也會顯示原等級、新等級及點數差。已手動投入的等級不誤算成自動補點；之後再手動加點不清除原自動來源。
4. 點「−」減一級。若仍有子節點需要目前門檻，操作被拒絕並顯示原因；失敗不改盤面或點數。
5. 藍色節點只檢查道具解鎖，不補父鏈。依封版預設已解鎖，可取消核取；有投入時需先退點才能鎖住。
6. 兩組雙實體互斥只允許一邊投入。「改點這邊」明確退還另一邊，並將新邊點到1級，不借用單槽繼承規則。兩組非互斥流派節點可同時投入。
7. 八個單槽選擇節點顯示清單第一項為初始選項，可在零級或已加點時切換，保留該槽等級、成本與子節點，只替換當前效果。
8. 點節點名稱查看完整說明、證據狀態與當前累積效果。固定盤面可左右捲動，窄螢幕不撐寬整頁。

完整盤目前只支援龍吟。切換其他流派時保留所有槽位座標及共同層，缺少職業覆寫的槽位顯示待補並停用盤面操作，不套用龍吟技能。

## 資料與責任分工

| 檔案／類別 | 責任 |
|---|---|
| `artifact-pack.json` | Phase 2A 封版遊戲數值、父鏈、互斥、選項及逐級效果，原檔未修改 |
| `board-layout.json` | 38個固定實體槽位的畫面座標；不保存遊戲屬性、門檻或成本 |
| `ArtifactBoardLayout` | 驗證槽位完整、位置唯一及版本一致；以PositionID合併共同層／流派覆寫 |
| `ArtifactBoardSession` | UI命令呼叫Phase 2A引擎；成功後同時更新盤與自動補點來源，失敗維持原狀 |
| `BuildAttributeLayer` | 每次由基礎面板與當前盤的StaticAttributes重新建立最終面板與A/B差值 |
| `TwArtifactBoard` | 固定盤、連線、選項、加減點、互斥換邊與錯誤呈現 |
| `TwAttributeComparison` | 顯示基礎、神器、最終及B−A；可篩選有變化屬性 |
| `TwBuildEditor` | 編輯未含本盤神器的基礎值；Legacy輸入保留獨立區塊 |

`ResearchBuild`新增`BaseAttributes`、`ArtifactState`、`ArtifactAutoLevels`。只保存基礎值、版本／流派獨立盤與來源標記，**不保存最終面板**。原LocalStorage依資料包、流派、套路隔離A/B的鍵保持兼容；舊Build缺少新欄位時基礎值保留未知，盤初始化為零點，不用Legacy範例冒充台服面板。

`ArtifactAutoLevels`保存目前投入中由自動操作補上的等級數。手動加點保留原標記；減點時先視為退還手動等級，剩餘自動數不超過目前等級；退到零清除標記。這是操作來源紀錄，不是額外遊戲規則。

## 屬性接線與避免重複

```text
ResearchBuild.BaseAttributes（未含本盤神器，decimal，缺值保留未知）
                 +
ArtifactAggregator.StaticAttributes（當前等級×當前選項的一筆累積效果）
                 ↓
BuildAttributeLayer.Project → 最終面板（唯讀推算結果）
BuildAttributeLayer.Compare → A/B基礎、神器、最終、差值
```

- 每次從原始基礎重新計算，不把增量回寫基礎、不相加前面各級、不保留已切掉的選項效果。
- `decimal`保留signed負值；會傷以百分點相加，不把1百分點當1倍。欄位名稱及單位從資料包的面板效果取得。
- 基礎未知時，仍可查看已知神器增量，但最終值及最終差值不填造數值。使用者必須輸入**未含這張神器盤**的基礎面板；不能直接把已含神器的遊戲面板再加一次。
- 彙總時以`off`排除動態條件面板收益，只取`StaticAttributes`。`TriggeredAttributes`、`ScopedEffects`、`TargetProfessionModifiers`與`UnmodeledMechanics`均不進入最終面板或DPS。
- 新Build層沒有改寫`Parameters`。Legacy按鈕使用原本獨立的舊版模型輸入，頁面明確標示未套用新神器盤。未新增已校準DPS主張。
- 盤面操作、選項或基礎值變更會更新比較並清除舊模型結果／儲存提示。儲存前再次驗證盤版本與流派。

## 資料差異與紅隊檢查

本階段沒有新增遊戲數值衝突或修改封版值。Phase 2A的40筆來源位置紀錄繼續保留於`conflicts.json`；核心五維與取整、龍飛作用範圍、其他局部技能時序仍待驗證。

更正先前對Repo的辨識：目前`config_pve.json`版本4.0.0.3的`ST_015`已是**數值型全技能增強**。來源欄位為`FrontParamInfoArray`的「全技能增强」（Mode=1），預設ParamValue為NumberMode=true；`FZJ_020`依`SH_005`技能占比進入通用克制候選式。這項更正是程式來源判讀，沒有改任何遊戲數字或研究主檔。

實際接線風險：既有模型還包含舊神器輸入、技能占比與其他額外技能增強來源，不能只把新盤全部常駐增量塞進原輸入而忽略重複及版本差異。本階段依任務範圍未把Legacy範例參數自動當成使用者台服基礎面板，也未把兩套神器收益重複輸入傷害公式。Phase 2A資料包的SHA256保持不變。

反方檢查：自動補點不代表整個支線需填滿；原先手動3級補到5級只能標2級為自動；同槽切換與兩實體換邊的退點規則不同；未知基礎不能當零；同一流派A/B不得共用可變盤狀態；成功的UI測試不代表遊戲技能機制已實測確認。

## 測試與重現

```text
dotnet build NshmCalculator.MudClient/NshmCalculator.MudClient.csproj --no-restore
dotnet build NshmCalculator.Test/NshmCalculator.Test.csproj --no-restore -p:SolutionDir=<repository-root>/
dotnet test NshmCalculator.Test/NshmCalculator.Test.csproj --no-build --filter FullyQualifiedName!~.Browser.
dotnet run --project NshmCalculator.MudClient --no-build --no-launch-profile --urls http://localhost:5218
dotnet test NshmCalculator.Test/NshmCalculator.Test.csproj --no-build --filter FullyQualifiedName~Browser.ArtifactBoardUiTests -- Playwright.LaunchOptions.Channel=chrome
```

瀏覽器測試沿用專案既有Playwright，使用獨立無介面Chrome測試設定；沒有使用使用者個人瀏覽器帳號。`BASE_URL`可調整本機網址；`ARTIFACT_SCREENSHOT_DIR`可另存測試截圖，預設不寫入儲存庫。[官方瀏覽器選擇說明](https://playwright.dev/dotnet/docs/browsers)。

機器可讀驗收結果會保存在同目錄`phase2b-validation.json`。建置保留原有NCalcSync套件警告；本階段沒有擴充依賴或改動Legacy版本日期來消除提示。

本次最終驗收（2026-10-09）：

- 既有Phase 2A Artifact測試：328項全部通過。
- 新增Build／盤面接線：16項全部通過。
- 實際Chrome UI：10項全部通過，包含八個單槽在零級／已投入時切換、兩組互斥及非互斥、部分前置已有手動點數、加減點、藍色道具、A/B signed差值、儲存重載及390px手機頁面溢位檢查。
- 完整非瀏覽器回歸：1904項，1903通過、1略過、0失敗。略過為既有`PlausibleVersionTest`的舊版本日期提示。
- Legacy Golden：8組案例、1648個輸出維持一致。
- 前端與測試專案均建置成功；封版資料包、Phase 2A引擎、Legacy公式與設定未修改。
