# Phase 2D-A 神器效果分類表

分類可複數並存；所有效果係數讀取凍結資料包。此表是分類 metadata，不是第二份遊戲數值主檔。完整 38 節點、40 類型路由、選項與作用技能見 `phase2da-effect-inventory.json`。

| 節點／位置 | 分類 | 沿用方法／缺口 |
|---|---|---|
| 困鬥 / ROOT-U02 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 矯準 / M02 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 御心 / ROOT-U01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 化解 / ROOT-D01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 百戰 / M02-D01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 枕戈 / M03-U01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 沉舟 / ROOT-D02 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 不動 / CORE-U01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 護佑 / M05 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 禍斗 / M06 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 天瑞 / COMMON-PATH-TIANRUI | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 鐵壁 / CORE-D01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 禦敵 / M03 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 速刃 / CORE-U02 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 歸心 / CORE-D02 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 星曜 / COMMON-PATH-STAR | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 凝玉勁 / BLUE-N01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 眾法歸一 / CORE | ConditionalAverage、StaticPanel | 五維逐項+9/級；Lv5逐項floor候選。只產生首克；不轉成攻擊等面板 |
| 萬鈞 / M05-BRANCH-WANJUN | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 風華 / M06-BRANCH-FENGHUA | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 持盾千鈞／酣歌劍起 / COMMON-SEL-PVP01 | ConditionalAverage、StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性；保留auto/manual/off與覆盖率接口；PvP效果不進PVE |
| 鳴戈 / COMMON-SEL-MINGGE | StaticPanel、Unmodeled | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性；指定流派PvP效果，不進PVE |
| 知彼 / COMMON-SEL-ZHIBI | StaticPanel、Unmodeled | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性；指定流派PvP效果，不進PVE |
| 武耀靈威／疾電奔星 / COMMON-SEL-OFFELE01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 星照 / COMMON-SEL-XINGZHAO | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 絕電誅鋒／刃影摧風 / COMMON-SEL-CRIT01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 墨攻 / BLUE-N02 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 擊連鋒／凌烈風 / BLUE-SEL01 | StaticPanel | 進既有靜態面板映射；無欄位的屬性保留，不混成其他屬性 |
| 破空·威霆 / M07 | TaggedSkillDamage、Unmodeled | 資料係數×Legacy流派技能占比；只注入一次；防禦效果，非PVE輸出增傷 |
| 左側起始大節點／流派特殊二選一 / ROOT-L | Unmodeled | 原Model為技能機制文字；缺執行時間軸與連動資料 |
| 吟風·風朔 / CORE-U03-L | TaggedSkillDamage、Unmodeled | 需誅邪·借天劍分支、劍氣技能群占比與疊加規則；缺吟風傷害占比及疊加規則 |
| 誅邪·建武 / CORE-U03-R | TaggedSkillDamage、Unmodeled | 缺劍意追擊占比；不同節點同群增傷疊加方式未驗證；缺誅邪傷害占比及疊加規則 |
| 龍飛·雲躍 / CORE-D03-L | Unmodeled | 減傷與技能邊界未完整驗證；非輸出倍率 |
| 雷龍·霜徹 / CORE-D03-R | TaggedSkillDamage、Unmodeled | 保留技能分支標識；缺傷害占比與適用技能邊界；護盾效果，非輸出倍率 |
| 吟風·劍自來 / CLASS-LY-UP01 | Unmodeled | 輕功回復不作輸出增傷；輕功機制不作輸出增傷 |
| 驚雷·怒劍 / CLASS-LY-UP02 | ConditionalAverage、RotationEffect、Unmodeled | 只提供上限，不推定實際平均層數；缺循環、施放頻率、資源與時間軸；不乘總DPS；新盘每層收益×明確平均層數／層數分布；缺覆蓋與層數時不假設滿層 |
| 白虹·貫日 / CLASS-LY-DN01 | TaggedSkillDamage、Unmodeled | 缺劍意追擊占比；不同節點同群增傷疊加方式未驗證 |
| 劍蕩·不盡鋒 / CLASS-LY-DN02 | RotationEffect、Unmodeled | 缺攻速對動作、命中次數、循環的映射；不乘總DPS |
