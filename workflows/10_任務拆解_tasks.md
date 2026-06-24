# 任務拆解(Tasks)

依 SDD 流程,把實作切成可勾選的 Phase。每個 Phase 完成後對需求驗收、必要時回寫 docs。
此檔為**活文件**,隨開發更新勾選狀態。

## Phase 0 — 規格與設計 ✅(已完成)
- [x] 需求分析與領域對焦(docs/01)
- [x] 系統架構設計(docs/02)
- [x] 資料來源設計 JSON/SQL(docs/03)
- [x] 即時更新設計 輪詢/SignalR(docs/04)
- [x] 視覺與互動設計(docs/05)
- [x] AI 協助開發紀錄(docs/06)
- [x] 開發準則(rules/)、SDD 流程(workflows/00)

## Phase 1 — 專案骨架 ✅(已完成)
- [x] `dotnet new mvc` 建立 `src/VibrationDashboard`(+ 根目錄 `WebTest.sln`、`.gitignore`)
- [x] 引入 ECharts 5.5.1 / signalr.js 8.0.7 到 `wwwroot/lib`(jQuery 隨模板附帶);以 `libman.json` 記錄可重現
      <br>※ SignalR **伺服端內建於 ASP.NET Core**,無需 NuGet 套件;此處引入的是瀏覽器端 JS client。
- [x] 設定 `appsettings.json`(`DataSource:Type`、`MachineData` 監看資料夾與門檻預設、`Realtime` 去抖/輪詢)
- [x] 建立分層資料夾結構(Controllers/Api、Hubs、Services/{Machines,Realtime}、DataSources、Models/Base、DTOs、Common/{Exceptions,Settings,Helpers}、Middleware、Data、SqlScripts、App_Data/machines)
- [x] `.csproj` 依 rules 加 `GenerateDocumentationFile` + `NoWarn 1591`
- **驗收**:✅ `dotnet build` 0 警告 0 錯誤;`dotnet run` 起站,首頁 `GET /` 回 **HTTP 200**(實測 port 5084)。

## Phase 2 — 資料層 ✅(已完成)
- [x] `Machine` / `Measurement` / `MachineStatus`(enum)模型（圖檔以 `byte[]` 表示;狀態為衍生值,模型不存 Status）
- [x] `IMachineDataSource` 介面 + `JsonFileDataSource` 實作（data URI 解析為二位元、損毀檔略過、id 白名單防路徑穿越）
- [x] `StatusEvaluator`:由最新值對門檻/ISO 判定狀態（門檻「達到即進入」語意）
- [x] `MachineDataOptions`（強型別設定）+ Program.cs 依 `DataSource:Type` 註冊資料來源
- [x] 小工具 `tools/SeedDataGenerator` 產生 **15 份擬真振動 JSON**(含自製無相依 PNG 編碼器);原子寫檔 tmp→rename;camelCase schema 對齊 docs/03
- [x] xUnit 測試專案 `tests/VibrationDashboard.Tests`（StatusEvaluator + JsonFileDataSource）
- **驗收**:✅ `dotnet test` **18 passed / 0 failed**;產生器輸出狀態分布 **良好 6 / 異常 5 / 危險 4**(三態齊備);整個 solution `build` 0 警告 0 錯誤。

## Phase 3 — API 與圖檔 ✅(已完成)
- [x] `GET /api/machines`(摘要,**不夾大圖**;含 imageUrl、sparkline、狀態字串、危險優先排序)
- [x] `GET /api/machines/{id}`(明細含 10 筆量測)
- [x] `GET /api/machines/{id}/image`(二位元 + **ETag/304** + Cache-Control)
- [x] 統一錯誤格式 **ProblemDetails**(`AppException`/`NotFoundException` + `ExceptionMiddleware`,camelCase 含 errorCode/traceId)
- [x] 薄 Controller → `IMachineService`/`MachineService`(組裝 DTO、判定狀態、排序、ETag);DI 註冊
- [x] **Swagger / OpenAPI**(開發環境)+ enum 以字串序列化
- [x] 新增 7 支 `MachineService` 單元測試(排序/映射/無圖/ETag/NotFound)
- **驗收**:✅ 瀏覽器/HttpClient 取得清單(15 台、危險優先、無 base64)、明細(10 筆)、圖檔(image/png、有效 PNG);**帶 If-None-Match → 304(body 0 bytes)**;未知 id → **404 application/problem+json**;Swagger UI/JSON 200;`dotnet test` **25 passed**。

## Phase 4 — 前端看板 ✅(已完成)
- [x] `api.js` 統一 AJAX 封裝(jQuery、逾時 8s、antiforgery header、集中 `ajaxError` + ProblemDetails 解析 + Toast + `window.onerror`)
- [x] `appState.js` 單一狀態來源 + 自訂事件廣播(模組解耦)
- [x] KPI 彙總列 + 機器卡片網格(`MachineRenderer` 差異更新 renderAll/patch/sync,不全量重建)
- [x] 卡片:圖檔縮圖、名稱、狀態燈(色+文字+aria)、最新值、迷你 **SVG sparkline**(端點依狀態上色)
- [x] 深色色票(Design Tokens,CSS 變數)、**骨架屏 shimmer**、危險脈衝、更新高亮、EmptyState/區塊錯誤重試
- [x] 改寫 `_Layout`(深色殼/jQuery/antiforgery/Toast)、`Index`(KPI/grid/卡片 `<template>`/明細面板);`Program.cs` 設 antiforgery header
- [x] 卡片選取 → AppState 事件 → 高亮 + 明細面板讀數(Phase 6 接 ECharts)
- **驗收**:✅ 首頁載入顯示 **15 台**(=檔案數)、KPI **良好6/異常5/危險4**、**危險優先排序**、深色視覺符合 docs/05(headless 截圖確認);`build` 0 警告 0 錯誤;JS 三檔 `node --check` 通過。

## Phase 5 — 即時更新 ✅(已完成)
> **取捨記錄(push vs poll)**:決定因素是「源頭能否主動通知」——本專案 `FileSystemWatcher` 偵測得到變更故選 push;輪詢為斷線降級且本身有正當主場。完整決策框架見 [docs/04 §7](../docs/04_即時更新_輪詢與SignalR.md)。
- [x] `FileWatcherService`(BackgroundService)監看資料夾 + **400ms 去抖**;Singleton 經 `IServiceScopeFactory` 解析 Scoped 推播器(避免 captive dependency)
- [x] `MachineHub` + `IMachineNotifier`/`MachineNotifier`:重讀單台→重算狀態→推 `MachineUpdated`(單台 delta)/ `MachineRemoved`
- [x] 前端 `realtime.js`:SignalR 連線 + `withAutomaticReconnect`;收 delta → `Dashboard.applyUpdate` 差異更新 + KPI 重算
- [x] 輪詢降級(連不上/斷線每 5 秒 `Dashboard.refresh`)+ 連線狀態指示(綠:即時 / 琥珀:重連/輪詢 / 紅:中斷)
- [x] **修正 SignalR 序列化**:`AddJsonProtocol` 設 camelCase + enum 字串,與 REST API 一致(否則前端 `dto.status`/`dto.id` 對不上)— 由端到端 SignalR 用戶端測試抓出並修正
- **驗收(需求 4 關鍵)**:✅ 改 JSON value → server log 推 `MachineUpdated`;**連線中的 SignalR 用戶端確實收到單台 delta(camelCase、status="Danger")**;前端看板顯示「● 即時連線」;`dotnet test` 26 passed。

## Phase 6 — 明細趨勢圖 ✅(已實作,待瀏覽器驗收)
> **呈現方式決議(2026-06-23)**:採**彈出 Modal 大圖**(點卡片→浮層大圖、背景變暗、關閉回看板)。
> 實作檔:`wwwroot/js/trendChart.js`(新模組)、`Index.cshtml`(`#trend-panel`→Modal)、`dashboard.js`(選取→開 Modal、即時更新刷讀數頭)、`dashboard.css`(Modal/seg 樣式 + 分區色帶 token);已回寫 docs/05 §3/§6。
> 與 Phase 5 解耦:Modal 顯示「開啟當下的明細快照」,即時推播只刷讀數頭、不重畫圖 → 兩階段互不影響。
- [x] 點卡片**開啟 Modal** + 懶載入 ECharts(首次點才注入 `echarts.min.js`;實例重用、關閉 Modal 才 `dispose`)
- [x] 趨勢線 + **可移動游標** `axisPointer`(cross)+ tooltip(時間/數值/單位/落在哪區)
- [x] `markLine` 門檻線(黃/紅)+ `markArea` 分區色帶(良好/警告/危險)+ 7天/全部 區間切換
- [x] 越線(≥警告)資料點變色放大強調;(選配)雙游標 Δ 差值讀數 **未做**(列選配)
- **驗收(需求 6)**:⏳ `dotnet build` 編譯通過(C#/Razor **0 真實警告**;2 錯誤僅為「執行中站台鎖住 exe」的複製鎖,非程式問題);`node --check` 三支 JS 通過。**瀏覽器實測(點卡片彈圖、游標、門檻/分區)待重啟執行中站台後確認。**

## Phase 7 — 視覺美化與微互動 ✅(已實作,待瀏覽器驗收)
> **與 Phase 5/6 的相依**:
> - 與 **Phase 5(即時更新)有相依但已滿足**:「自動排序到前」掛在 `Dashboard.applyUpdate` 即時推播路徑;「告警清單」事件來源就是 SignalR 的 `MachineUpdated` 單台 delta。P5 已完成 → 依賴到位。
> - 與 **Phase 6(Modal 趨勢圖)原為條件相依**:「選中連動高亮」需與 Modal 開關共存。執行前已確認條件成立(`trendChart.js` + `Index.cshtml` Modal 標記 + `echarts.min.js` 皆就緒)→ 一併實作。
> - 實作檔:`dashboard.js`(reorder/flashDanger/Alerts 模組/選中↔Modal 連動)、`trendChart.js`(close 時發 `trend:closed`)、`Index.cshtml`(告警清單區塊)、`dashboard.css`(alarm 脈衝 + 告警清單樣式 + `--danger-glow` token)。
- [x] 狀態轉危險脈衝動畫(`.card--alarm`,只動 box-shadow)+ **自動排序到前**(`MachineRenderer.reorder`,危險優先、同級依 id,對齊後端排序)
      <br>※ 排序只在 `applyUpdate`/`sync` 偵測到**狀態變化**時觸發,用 `appendChild` 搬移既有節點(非重建),不破壞差異更新與 `card--selected`/動畫(見 rules/前端/04 §3)。
- [x] hover/選中連動高亮;CSS-only 動畫(transform/opacity)— 選取 ↔ Modal 連動:關閉 Modal 時清掉卡片選取高亮(`trend:closed` 事件,模組解耦)
- [x] 告警事件清單(由推播累積)— `Alerts` 模組:狀態轉移(升級/恢復)各記一筆,最新在上、上限 50 筆;點清單項目連動選取/開圖;全程 `.text()` 防 XSS
- **驗收**:⏳ `node --check` 兩支 JS 通過;C#/Razor 編譯通過(2 錯誤僅為執行中站台鎖 exe 的複製鎖,非程式問題)。**瀏覽器實測待重啟執行中站台後確認**:改 JSON 值使某台轉危險 → 卡片紅暈脈衝 + 自動排到最前 + 告警清單新增一筆。
      <br>※ 注意:站台未開 Razor RuntimeCompilation,**告警清單(改了 `Index.cshtml`)需重新編譯/重啟站台才會出現**;脈衝/排序為 wwwroot 靜態檔,重新整理瀏覽器即生效。

## Phase 8 — SQL 實作 ✅(已完成,Nice-to-have / 可切換)
- [x] EF Core **Code-first**(`Data/AppDbContext` + `Data/Configurations/`)建表;`InitialCreate` migration 對齊 docs/03 §4 正規化 schema(varchar/nvarchar/float/varbinary、FK Cascade、`IX_Measurement_Machine_Time` 複合索引)
- [x] `SqlMachineDataSource` 實作 `IMachineDataSource`(EF 實體 ↔ 中立領域模型映射;`AsNoTracking`/`AsSplitQuery`);DbContext/資料源註冊為 **Scoped**,與唯一消費者 `MachineService`(Scoped)生命週期相容
- [x] `SqlDbInitializer`:啟動 `Database.Migrate()` 建庫 + **空庫時從現有 JSON 種子**(複用已測的 `JsonFileDataSource`,達成「同一份資料、兩種後端、相同畫面」)
- [x] 設定切換 `DataSource:Type = "Sql"` 開箱即用(LocalDB 自動建庫/種子);預設仍 `Json`(即時檔案監看為主場)
- **驗收**:✅ 以 `--DataSource:Type=Sql` 實跑 LocalDB,端到端比對 JSON 模式**完全一致**——`GET /api/machines` **15 台**(危險優先)、明細 **10 筆**、圖檔 **image/png + ETag**、帶 `If-None-Match`→**304**、未知 id→**404 problem+json**;**前端/Controller/Service 零改動**;`build` 0 警告 0 錯誤。

## Phase 9 — 驗證與部署
- [ ] 對 6 條需求逐條驗收(對照下表)
- [ ] `dotnet publish` → IIS 建站(No Managed Code App Pool、開 WebSocket)
- [ ] 加安全標頭/CSP(web.config)
- [ ] 整理 docs/06 AI 紀錄、準備報告講稿
- **驗收**:IIS 上實際跑起來、改檔即時更新可重現。

## Phase 10 — 專家審查修正:P0 缺陷與資安強化 ✅(本次實作)
> 來源:資深 UIUX / PM / Code Reviewer 三方審查(2026-06-24)。原則:先修「會出事」的,再疊加值。
> 這三個 Phase(10/11/12)為審查後強化,**須在實際部署(Phase 9)前完成**。
- [x] **P0-1** 空 / `null` measurements 防禦:`MapToMachine` 用 `(model.Measurements ?? [])`;`ReadFileAsync` catch 放寬為「任何壞檔記錄並略過」,貫徹「單檔損毀不拖垮整體」(原僅攔 `JsonException`/`IOException`,`measurements:null` 會 NRE → 全清單 500)
- [x] **P0-2** 門檻反向健全性檢查:`danger <= warning` 或負值 → 退回預設並記 Warning(避免狀態判定反轉、markArea 畫出負高度色帶)
- [x] **P0-3** 安全標頭 middleware:`X-Content-Type-Options=nosniff`、`X-Frame-Options=DENY`、`Referrer-Policy=no-referrer`(全環境);CSP 於**非開發環境**派發(避免擋 Swagger/熱重載的內嵌腳本),`connect-src` 含 `ws: wss:` 放行 SignalR
- [x] **P0-4** 圖檔 Content-Type 白名單:`ParseDataUri` 僅允許 `image/png|jpeg|webp|gif`(排除 `svg+xml` 防內嵌腳本),非白名單一律拒圖 → 杜絕 `data:text/html` 當 XSS 載體
- **驗收**:✅ `dotnet test` 綠;放一個 `measurements:null` 的檔,`GET /api/machines` 仍 **200**(該檔略過、不再 500);回應標頭含 `nosniff`/`DENY`/`Referrer-Policy`。

## Phase 11 — BI 分析深化(對齊 docs/07)✅(本次實作)
> 把看板從「三色計數」升級成 docs/07 §2 承諾的「風險視圖」:嚴重度共同尺規 + 統計洞察。
> 責任分工(docs/07 §8):需門檻/統計的指標**後端算**(嚴重度、斜率、標摘);純彙總(健康率/分布/Top-N)在前端對已載入小資料集即時 rollup,以維持 SignalR delta 下不打 API 也能即時更新。
- [x] 後端嚴重度:`MachineSummaryDto.Severity = 最新值 ÷ 危險門檻`(跨機共同尺規,危險門檻 ≤0 或無值時為 null)
- [x] 後端統計摘要:`MeasurementAnalyzer`(純函式)算 平均/最大/最小/標準差/**線性回歸斜率(每日)**/超標次數 → `MachineDetailDto.Statistics`
- [x] 前端 KPI 加「健康率 %」+ **狀態分布堆疊長條**(CSS-only,免 echarts、保留懶載入優勢)
- [x] 前端「風險排行 Top 5」面板(依 severity 降序,點擊連動選取/開趨勢圖)
- [x] 明細讀數頭加統計摘要 + **趨勢方向金句**(「每日 +X mm/s,惡化中」)
- [x] 新增 `MeasurementAnalyzerTests` 單元測試(斜率正負/標準差/超標次數/單點與空序列邊界)
- **驗收**:✅ `/api/machines` 含 `severity`;`/api/machines/{id}` 含 `statistics`;`dotnet test` 綠。看板顯示健康率 / 分布 / 風險排行待瀏覽器確認。

## Phase 12 — 無障礙與體驗強化(WCAG)✅(本次實作,部分待瀏覽器/讀報器實測)
- [x] 次要文字對比提亮 `--text-dim` `#7C8BA1` → `#93A4BC`(對 surface 過 WCAG AA)
- [x] Modal **focus trap**(Tab/Shift+Tab 環繞鎖在對話框內,兌現 `aria-modal` 承諾)
- [x] 卡片**鍵盤入口**:「查看趨勢」按鈕為真正可聚焦 / Enter 觸發的進入點 + 每台 `aria-label='查看 {name} 趨勢圖'`
- [x] 修正 `role="status"` 誤用:卡片狀態徽章移除 live region(原會在每次 patch 觸發讀報器洗版),改靜態 `aria-label`;狀態播報交給已有 `aria-live` 的告警清單
- [x] KPI 危險/異常 > 0 時低調強調(`.is-active` → 邊框/淡背景,餘光可辨「今天要不要緊張」)
- [x] sparkline 平線置中(`range≈0` 時不貼底、改畫水平中線,語意=「穩定」)
- [x] 響應式斷點(≤640px:header `flex-wrap`、Modal 高度與圖表高度收斂)
- **驗收**:✅ JS `node --check` 通過、`build` 綠。⏳ 鍵盤從卡片進入趨勢圖、讀報器不洗版、DevTools 對比過 AA、窄螢幕不爆版 —— 待手動瀏覽器/讀報器實測。

## Phase 13 — 看板查詢/篩選/每頁筆數/分頁(前端版)✅(已完成)
> **緣由**:設備數未來可能大量成長。經評估三方案(A 純前端 / B 純後端 / C 分階段+預留接縫),採 **C**:現在前端對已載入全量做、API 契約先定,規模成長再切後端。決策與作法見 [docs/07 §5.3 / §5.4](../docs/07_領域資料與BI分析規劃.md)、報告 [docs/08 §H-Q12](../docs/08_面試報告.md)。
> **釐清**:**儀表板不是資料表格**——大量設備下靠彙總 + 風險 Top-N + 篩選呈現「該關注的那幾台」,真正的「瀏覽全部 + 分頁」屬未來 `/admin` 清單頁(後端分頁需求主要落在那)。
> **實作檔**:`Index.cshtml`(`#filterbar` 工具列 + `#pager` 分頁列)、`dashboard.css`(`.filterbar` / `.pager` / `btn-ghost:disabled`)、`dashboard.js`(`listQuery` 接縫 / `view` 狀態 / `renderPage` / `Controls` 模組 / 唯一重繪入口 `renderView`)。
- [x] 查詢框(名稱 + 編號,去抖 200ms)、狀態篩選(全部/良好/異常/危險 seg)、每頁筆數(12/24/48)、分頁列(上/下一頁,>1 頁才顯示)
- [x] **兩層分離**:`AppState.machines` 全量真相;`listQuery(全量, view) → {items,total,totalPages,page}` 切出當前頁子集,只餵 `#grid`
- [x] **KPI / 風險排行 / 告警仍對全量算**(落實「分頁不弄壞彙總」);彙總來源與「當前頁」解耦
- [x] **告警 `prevStatus` 改取自 `AppState` 全量**(非 DOM)——分頁後離頁機台仍正確記錄升級/恢復(全廠事件,不漏記)
- [x] 排序與 `reorder()`/後端一致(危險優先、同級 id 升序)→ 先全域排序再切頁,分頁邊界穩定;篩選無結果給「清除篩選」出口
- **驗收**:✅ `node --check` dashboard.js 通過;C#/Razor 編譯通過(收尾複製 exe 的鎖為執行中站台所致,非程式問題);**重啟站台後瀏覽器確認**:工具列顯示於「風險排行」與卡片網格之間,查詢/狀態篩選/每頁筆數/分頁運作正常(使用者確認)。
      <br>※ 站台未開 Razor RuntimeCompilation,工具列(改了 `Index.cshtml`)需重新編譯/重啟才出現,故本階段以**重啟**驗收;JS/CSS 為 wwwroot 靜態檔,重新整理即生效。

## Phase 14 — PM 二次審查收口:設備類型 metadata 與口徑統一 ✅(本次實作)
> 來源:PM / BI 二次審查(2026-06-24)。BI 進階加分項(距危險 ETA 外推 / 異常點標記 / 近期vs前期變化率)經評估**屬較複雜加分,暫不做**;先補 PM 的收口項。
- [x] 設備 `type` 加值欄位(docs/07 §7「最低限度只加 type」):JSON schema / 領域模型 / DTO / Service / `JsonFileDataSource` 全鏈;SQL 側 `MachineEntity`/Config/`SqlMachineDataSource`/`SqlDbInitializer` + **EF migration `AddMachineType`**(nvarchar(40);JSON 與 SQL 兩後端一致)
- [x] 種子產生器加 type(9 類:泵浦/風機/馬達/齒輪箱/空壓機/主軸/鼓風機/軸承座/冰水主機);**確定性重跑** 15 份 JSON(固定種子 → 數值與圖檔不變、僅多 `type` 欄位)
- [x] 前端:卡片左上**類型標籤**、明細讀數頭顯示「類型」、搜尋框納入 type(輸入「泵」即篩出所有泵浦)
- [x] 風險排行嚴重度 % 加 **tooltip 說明**(=最新值÷危險門檻、≥100% 已超過危險線)
- [x] **`/admin` 口徑統一**:docs/01 把管理者門檻編輯由「本版加分」改為「Phase 2、架構已留寫入接縫」,避免報告宣稱卻 demo 不出來
- **驗收**:✅ `dotnet build` 0 警告 0 錯誤、`dotnet test` 綠;`GET /api/machines` 含 `type`、明細含 `type`;切 `DataSource:Type=Sql` 後 type 一致(LocalDB)。

## 需求驗收對照表(最終 demo 用)
> 後端 / HTTP 層已於 2026-06-24 實測(`http://localhost:5099` Production、`:5084` Development);標 ⏳ 者為「程式就緒、待瀏覽器目視」。
| 需求 | 驗收方式 | 狀態 |
|---|---|---|
| 1 動態顯示檔案內容 | 看板由 JSON 驅動顯示 | ✅ HTTP 實測:`GET /api/machines` 200,由 15 份 JSON 驅動 |
| 2 FineReport 風視覺 | 對照 docs/05 視覺 | ⏳ 待瀏覽器目視(深色/KPI/分布/排行) |
| 3 含 名稱/圖檔/狀態/10筆數值 | 卡片+明細完整呈現 | ✅ HTTP 實測:明細 10 筆量測、圖檔 `image/png`、三態齊備 |
| 4 檔案更新即時刷新 | 改檔→畫面即時更新 | ⏳ 後端推播 Phase 5 已端到端測;UI 即時刷新待瀏覽器目視 |
| 5 機器數 10~20 不固定 | 提供多份測試檔、增減即反映 | ✅ HTTP 實測:加檔 15→18、刪回 15,FileWatcher 即反映 |
| 6 點機器看趨勢+可移動游標 | 明細圖游標可移動看數值 | ⏳ 待瀏覽器目視(Modal/游標/門檻線) |

> **本次新增的驗收(P10/P11/P12,HTTP 實測)**
> - P0-1:放 `measurements:null` 壞檔 → `GET /api/machines` 仍 **200**(該檔不再使全清單 500)。
> - P0-2:門檻反向(warn 5 / danger 1)→ 退回預設 0.5/0.88,狀態正確判為 Abnormal。
> - P0-3:回應含 `nosniff` / `X-Frame-Options:DENY` / `Referrer-Policy`;Production 另含 CSP(連錯誤回應都帶標頭)。
> - P0-4:惡意 `data:text/html` 圖 → 白名單拒圖,`/{id}/image` 回 **404 problem+json**,絕不吐 `text/html`。
> - **額外修正**:`UseExceptionHandler("/Home/Error")` 原在 Production 搶先攔截端點例外,使 ProblemDetails 形同虛設(連 404 都變 500 HTML);已改由 `ExceptionMiddleware` 單一負責 → 各環境一致回 problem+json(實測未知 id/image 由 500→**404**)。
> - BI:`/api/machines` 含 `severity`;`/api/machines/{id}` 含 `statistics`(平均/標準差/**斜率**/超標);`dotnet test` **32 passed**。
