# 開發準則(rules)

實作時**必須遵守**的約束。SDD 流程中,程式碼產出(含 AI 協助)都要通過這些準則的檢查。
每份結尾都有檢查清單,可在 PR/自審時快速核對。

## 命名規範(跨前後端,唯一權威來源)
| 文件 | 重點 |
|---|---|
| [命名規範](命名規範.md) | 由實際程式碼掃描歸納:後端類別後綴/命名空間/載體分層、前端 BEM+模組/id/CSS 變數、前後端 PascalCase↔camelCase 橋接與刻意例外 |

## 前端實作準則(`前端實作準則/`)
> 已從原本的 React/Next/Vue 範本**精簡重寫**為本專案技術棧(Razor + jQuery + ECharts + SignalR)。

| 文件 | 重點 |
|---|---|
| [01 前端架構與可維護性](前端實作準則/01_前端架構與可維護性.md) | MVP 收斂、語意化/a11y、Razor Partial + JS 模組切分、`appState`+事件 |
| [02 錯誤處理與韌性](前端實作準則/02_錯誤處理與韌性.md) | `api.js` 統一封裝、ProblemDetails 解析、SignalR 重連、骨架屏 |
| [03 視覺與微互動準則](前端實作準則/03_視覺與微互動準則.md) | 深色儀表板色票(Design Tokens)、CSS 優先動畫、狀態脈衝/連動 |
| [04 效能準則](前端實作準則/04_效能準則.md) | ECharts 延遲載入、游標 throttle、卡片差異更新、靜態快取 |
| [05 前端資安準則](前端實作準則/05_前端資安準則.md) | XSS(`.text` vs `.html`)、antiforgery、CSP(含 SignalR) |

## 後端實作準則(`後端實作準則/`)
> 分層**參考既有專案**,並依本專案(MVC + JSON 主來源 + SignalR)調整。

| 文件 | 重點 |
|---|---|
| [01 分層架構與專案結構](後端實作準則/01_分層架構與專案結構.md) | Controller→Service→`IMachineDataSource`、資料夾結構、相依鐵則 |
| [02 編碼慣例與錯誤處理](後端實作準則/02_編碼慣例與錯誤處理.md) | csproj/命名/EF 慣例、`AppException`+`ExceptionMiddleware`→ProblemDetails、Serilog |

## 前後端共識(對接點)
- **錯誤格式**:後端統一回 RFC 7807 **ProblemDetails**(含 `errorCode`/`traceId`),前端用同一份解析 → 見前端 02 / 後端 02。
- **資料邊界**:後端只回 `DTOs/`,前端不假設內部模型結構。
- **即時**:SignalR 為主、輪詢降級;CSP `connect-src` 需放行 `ws/wss`。
