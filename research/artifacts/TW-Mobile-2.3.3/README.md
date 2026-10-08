# Phase 2A：台服手遊 2.3.3 神器資料包與引擎

資料包ID：`TW-Mobile-2.3.3`，來源封版日：2026-10-09。本階段只有資料、純計算規則與自動測試，不新增UI、不接入或改寫Legacy傷害公式。

## 資料結構

執行檔：`NshmCalculator.MudClient/wwwroot/data/artifacts/TW-Mobile-2.3.3/artifact-pack.json`。

```text
Manifest                 schema/伺服器/平台/版本/來源快照SHA256/38節點/303原始逐級記錄
Rules                    一般父Lv5、ROOT/CORE父Lv1、唯一父鏈、quest預設解鎖、兩組非互斥
CommonNodes[28]           跨流派共用屬性；穩定NodeID、PositionID、ParentID
ProfessionOverrides.LY[10] 龍吟ROOT、終端、兩組雙實體互斥、兩組非互斥
  Options                標準ID、語義名稱、遊戲alias、文字機制
  Levels                 每個等級×當前選項的累積效果與累積成本
  SpecialEffects         核心滿級克制／驚雷實際互動層數，與靜態值分離
  Model                  原封版JSON（僅去掉已明確的最外層龍吟索引），保留技能文字
  SpecRow / SourceIds    原表列與來源ID；不附私人Drive URL/圖片
  Status / Notes         原證據狀態保留，未提升為已實測真值
Diagnostics[40]          逐來源列的衝突／舊備註／未解析效果
```

父鏈與選項以`27_神器2.3.3封版規格`為準。該表一般共同節點的Effect JSON是空物件，屬性採`21_神器節點逐級`的**累積效果JSON**；不使用Legacy收益函數推算。27有完整JSON的節點依該JSON展開，對21的原累積值逐級比對。20只作稽核對照，不將收益式當台服屬性。

`303`是原逐級記錄數；含多選項的節點在執行包展開為`475`筆Level×Option，不能把展開筆數當原始實測筆數。固定每級推算與非線性累積均保留原數值性質；來源快照保存在儲存庫外`../research/phase2a/source.snapshot.json`，不是第二份研究主檔。

單位區分`point`、`percentagePoint`、`second`、`stack`及`displayPoint`。例如1%保存為`1 percentagePoint`，不是0.01，也不與點數相加。`攻擊`和`攻擊力`僅名稱標準化，signed值保留。全技能與四個技能標籤分開。

## 引擎設計與使用

命名空間：`NshmCalculator.Shared.Research.Artifacts`。

- `ArtifactDataPack.Load`：驗證版本、ID、槽位、父鏈、循環、逐級完整性、成本、單位、選項與非互斥規則。同一屬性的常駐與條件觸發效果分開保存，以屬性鍵、作用範圍、觸發條件的組合檢查重複。
- `ArtifactBoardEngine.CreateState`：必須指定角色流派，可指定點數預算與槽位選項。藍色節點依封版允許預設道具已取得，可用`SetQuestUnlocked`明確鎖住；不建立或檢查虛構道具名稱。
- `SetLevel`：先驗證輸入盤，建立副本，沿唯一父鏈遞迴補到最低門檻，驗證互斥與總成本後回傳新盤。已有較高父等級不降低。沒有選定的父鏈可選槽必須由呼叫者補上，不會擅選ROOT／武耀等選項。
- `SwitchOption`：全部8個可選實體槽（含兩個目標流派槽）在Lv0或已投入時都能切換；保留等級、成本及子節點，只替換該槽效果。
- `ReplaceExclusive`：明確將另一顆實體節點退為Lv0，新節點用指定等級，**不繼承**另一顆的等級。一般`SetLevel`遇互斥直接拒絕。
- 降低父節點到不足門檻時拒絕；不偷偷退子節點。失敗操作不修改傳入盤，回傳錯誤碼與原因。成功結果含前後等級／選項、點數差與自動補點原因，可供後續異動紀錄使用。
- `ArtifactAggregator.Summarize`：每顆只取選定Level與Option的**一筆累積效果**，不再相加前面各級，不重複累加已切掉的選項。

```csharp
var pack = ArtifactDataPack.Load(json);
var engine = new ArtifactBoardEngine(pack);
var root = pack.AllNodes.Single(n => n.PositionId == "ROOT-L").Id;
var speed = pack.AllNodes.Single(n => n.PositionId == "CORE-U02").Id;
var state = engine.CreateState("LY", budget: 20,
    selections: new Dictionary<string, string> { [root] = "LY-ROOT-A" });
var result = engine.SetLevel(state, speed, 1); // 總20點：ROOT1/M02=5/M03=5/CORE1/不動5/速刃1
var summary = new ArtifactAggregator(engine).Summarize(result.State);
```

屬性彙總輸出：`StaticAttributes`、`TriggeredAttributes`、`CombinedAttributes`、`SkillEnhancementByTag`、`TargetProfessionModifiers`、逐來源`Contributions`。局部技能加成、冷卻、攻速、減傷及五維顯示增量保存在`ScopedEffects`；每顆技能／文字機制與未接入DPS的限制保存在`UnmodeledMechanics`，不當角色常駐全局增傷。

`auto/manual/off`只決定條件面板增益是否套用；局部技能**規格值**仍回傳，搭配`ScopedTriggerModes`供下一階段技能時序解讀。`off`不關掉已投入的靜態屬性。手動模式需逐節點明確開關。

條件輸入與限制：

- 持盾／酣歌：auto檢查敵方玩家**>5**，或呼叫者提供0~10秒增益剩餘時間；本引擎不模擬戰鬥時間。缺少條件時回傳Pending，不默認常駐。
- 驚雷互動：必須給實際0~本級上限層數，不因滿級5層就默認實際5層或100%覆蓋。
- 核心：僅Lv5啟用，必須明確給已核對五維總和及候選取整策略；缺一就回傳Pending。+9／級只是原表五維顯示增量，不擅自乘5、加入面板或補造五個屬性。
- `CompleteForRequestedContext`只表示所要求條件面板效果已解析，**不代表全部技能/DPS機制已建模或實測確認**。仍須看`UnmodeledMechanics`與原Status。
- 本包只有龍吟職業覆寫；其他職業可使用獨立共用藍色節點，但走到缺少覆寫的ROOT時會拒絕，絕不借龍吟ROOT。

## 非線性規則

| 節點 | Lv1 | Lv2 | Lv3 | Lv4 | Lv5 |
|---|---:|---:|---:|---:|---:|
| 驚雷CD降低（秒） | 1 | 1.5 | 2 | 2.5 | 3 |
| 劍蕩攻速提升（百分點） | 15 | 20 | 25 | 30 | 35 |
| 龍飛減傷（百分點；範圍待驗） | 20 | 30 | 40 | 50 | 60 |

## 衝突與紅隊檢查

完整逐列報告：同目錄`conflicts.json`。40筆是來源位置紀錄，並非40種獨立遊戲矛盾。

1. 21的三個藍色節點有舊JSON仍標maxLevel=7；27明確是10級／每級2點，21已存在8~10級列。使用27，不回寫研究主檔。
2. 21的吟風·劍自來／驚雷·怒劍JSON仍重複包兩層龍吟；27已單層。封版AUDIT的PASS只適用27，不能延伸為21也無重複。
3. 21的天瑞／星曜／風華等仍有「拓撲待補」歷史狀態；27已明確給父節點。原始備註保留。
4. 不動／鐵壁現版是全技能增強+50／級，Legacy為克制收益；兩者不混用。
5. 20/21的ReleaseReady=False與未驗證備註仍在。封版對程式採用的優先權，不等於所有數值已獨立逐級實測。
6. 核心五維映射／取整、龍飛減傷時機與技能局部乘區仍待核；不將文字直接轉成全局倍率。

反方檢查：原累積JSON若誤把逐級值寫成累積，單靠本包比對仍不能證明遊戲機制；故保留原Status與來源性質。兩實體互斥不能用單槽繼承規則；技能增益不能併入通用面板；缺少實際Buff層數不能用上限代替；版本名稱不作實測證據。

## 重建與驗證

```text
python tools/build_artifact_pack.py ../research/phase2a/source.snapshot.json
dotnet build NshmCalculator.Test/NshmCalculator.Test.csproj --no-restore -p:SolutionDir=<repository-root>/
dotnet test NshmCalculator.Test/NshmCalculator.Test.csproj --no-build --filter FullyQualifiedName~Artifact
dotnet test NshmCalculator.Test/NshmCalculator.Test.csproj --no-build --filter FullyQualifiedName!~.Browser.
```

私有快照完整保留20/21/27原值。重建為確定性匯出，不直接讀寫雲端，也不把私人連結或Build/DPS放進公開資料包。發現新衝突應更新報告，不能自行改遊戲數字。本階段未接自動Sheet同步、UI或Legacy參數映射。

## 本次驗證結果（2026-10-09）

完整可讀取結果：同目錄`validation-results.json`。

- 建置成功，0個錯誤。
- 神器測試328項全部通過：303項直接核對原逐級累積JSON（涵蓋475筆Level×Option），25項規則與彙總行為測試。
- 非瀏覽器回歸：總計1888項，1887項通過，1項略過，0項失敗。略過的是原有`PlausibleVersionTest`，原因為Legacy資料版本日期過舊；既有基線亦略過此測試。本階段沒有改Legacy版本來掩蓋提示。
- `LegacyAllOutputsRemainIdentical`通過：8組Golden案例、1648個輸出維持原結果。
- 確定性重建與現有資料包、Golden來源檔完全相符。
- 結束前重新讀取20、21、27，內容均與本次來源快照相同；未寫入研究主檔。
- Git檢查：只新增本階段的資料、引擎、測試與報告，既有追蹤檔案無修改。尚未推送GitHub。

上述測試驗證程式符合封版資料與盤面規則，不代表已用遊戲實測驗證所有技能機制。瀏覽器/UI測試不在本階段範圍。
