# 台服傷害研究計算器

研究院 fork：`otiscat1231-spec/NshmCalculator`，研發分支：`tw-pve`。母模型取自 MIT 授權的 dellbeat/NshmCalculator，固定來源 commit `d831d871f3d05894afd645da640a5ce17051316c`。原版公式核心與 `/pve` 保留，原首頁移至 `/legacy-home`。

首頁 `/`、`/tw-pve` 提供龍吟／鐵衣流派選擇及同流派 Build A/B。跨流派會顯示「跨流派比較，需各自完成模型校準」，不產生共用校準的 DPS 或跨流派提升率。

職業係數、技能標籤、技能占比、神器、特質、循環、校準和 Golden 資料集各自登錄於流派資料結構。未確認的龍吟資料保持空白。範例面板來自原 Repo，不代表使用者機體；鐵衣姿態只在鐵衣模式生效。

資料索引：`wwwroot/data/pve/manifest.json`。Legacy 為原版 4.0.0.3 資料完整副本；TW-candidate 目前只引用該候選骨架，沒有冒稱台服當前公式。校準識別包含伺服器、平台、版本、流派、Build、目標與循環，不允許借用其他流派的 λ。

公開儲存庫只保存程式與原 Repo 回歸資料。使用者實測、Build、校準與研究主檔連結由各自的私人研究資料管理，不隨程式碼公開。`pve_legacy_golden.json` 是原版程式輸出回歸資料，不是台服實測證據。預設資料索引不附私人試算表連結。

使用 .NET 8 SDK 執行 `dotnet run --project NshmCalculator.MudClient`。驗證：`dotnet test NshmCalculator.sln --filter "FullyQualifiedName!~.Browser."`。

研發程式保存於研究院 fork 的 `tw-pve` 分支，本機 upstream 推送已停用。這是程式碼儲存庫，尚未發布可直接使用的網站。試算表與計算器目前沒有自動同步。原版 OCR 的大型 LFS 資源尚未下載。既有 NCalcSync 5.2.11 套件警告與原版警告保留，這個階段未改動其依賴。
