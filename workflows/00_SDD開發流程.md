# SDD 開發流程(Spec-Driven Development)

本專案採**規格驅動開發**:先把「要做什麼、怎麼設計、有哪些約束」寫成文件,
再讓實作(含 AI 協助)在規格與規則的約束下進行。目的是讓產出**有依據、可維護、講得清楚**。

## 為什麼用 SDD(而不是直接寫/直接請 AI 生成)
- 避免「一鍵生成一坨難維護的程式」——先有規格,程式碼才有對齊目標。
- 需求與設計的**取捨被記錄下來**,面試報告時講得出「為什麼這樣做」。
- AI 在明確約束下產出,品質與一致性更高(規則見 `rules/`)。

## 四類產物各司其職
| 資料夾 | 角色 | 內容 |
|---|---|---|
| `docs/` | **規格 + 設計紀錄(What & Why)** | 需求分析、架構、資料/即時/視覺設計、AI 紀錄 |
| `workflows/` | **流程 + 任務(How we work)** | 本流程、任務拆解與驗收 |
| `rules/` | **約束(Constraints)** | 前端/後端開發準則,實作時必須遵守 |
| `skills/` | **可重用技術 how-to** | ECharts/SignalR/FileWatcher 等做法 |

## 開發循環(每個功能都走一遍)
```
①需求收斂 → ②設計 → ③任務拆解 → ④實作(遵守 rules)→ ⑤驗證 → ⑥回寫文件
   docs/01    docs/02~05   workflows/10    程式碼+skills     對需求驗收   更新 docs
        ▲                                                              │
        └──────────────────  發現缺漏就回頭修規格  ◀───────────────────┘
```

## 各階段「完成定義」(Definition of Done)
- **需求收斂**:每條需求有明確解讀與對應做法;邊界(MVP/Nice-to-have)清楚。
- **設計**:架構分層、資料模型、API、即時管線、視覺規範都已落在 `docs/`。
- **任務拆解**:`workflows/10` 有可勾選的 Phase 與驗收項。
- **實作**:通過 `rules/` 的檢查;關鍵決策有註解說明「為什麼」。
- **驗證**:對需求逐條驗收(尤其需求 4 的「改檔→畫面即時更新」要實測)。
- **回寫**:實作中改變的設計,回頭更新對應 `docs/`,保持文件與程式一致。

## 與 AI 的協作紀律(詳見 docs/06)
1. 給足脈絡(職缺/領域/附圖)。
2. 用反問逼出取捨,不照單全收。
3. 先規格後實作,用 `rules/` 約束 AI。
4. 每個決策要求 AI 解釋原理,人做最終把關。
5. 按 Phase 小步快跑,逐步驗證。

## 目前進度
- ✅ 需求收斂、核心設計、規範客製(docs + rules 已建立)
- ✅ Phase 1 專案骨架:MVC 專案 + solution、分層資料夾、前端函式庫、appsettings;`dotnet run` 起站首頁 HTTP 200
- ✅ Phase 2 資料層:領域模型 + `IMachineDataSource`/`JsonFileDataSource` + `StatusEvaluator`;測試資料產生器(15 份,三態齊備);xUnit 18 測試綠燈
- ✅ Phase 3 API 與圖檔:`/api/machines` 清單/明細、圖檔 **ETag/304**、ProblemDetails 全域例外、`MachineService` 服務層、Swagger;xUnit 25 測試綠燈
- ✅ Phase 4 前端看板:深色看板(KPI 彙總、卡片牆、狀態燈、sparkline、骨架屏);`api.js`/`appState.js`/`dashboard.js` 模組化 + 差異更新;首頁顯示 15 台、危險優先
- ✅ Phase 5 即時更新(需求 4):`FileWatcherService` 去抖監看 + `MachineHub`/`MachineNotifier` 推單台 delta + 前端 `realtime.js`(SignalR + 輪詢降級 + 連線狀態);端到端實測改檔→推播投遞成立
- ▶ 下一步:Phase 6 明細趨勢圖(點卡片→**彈出 Modal** ECharts:可移動游標/門檻線/分區色帶)(見 [10 任務拆解](10_任務拆解_tasks.md))
