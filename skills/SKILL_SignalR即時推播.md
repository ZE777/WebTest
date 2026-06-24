# SKILL：SignalR 即時推播

> 用途:需求 4 的**傳輸端**。檔案變更時,後端**只推「變動那一台」的 delta**,前端即時更新單張卡片。
> 對照討論見 [docs/04 輪詢與 SignalR](../docs/04_即時更新_輪詢與SignalR.md)。

> **觀念釐清:SignalR ≠ WebSocket。** WebSocket 是底層連線協定;SignalR 是架在其上的函式庫,自動做**傳輸協商**(WebSocket → SSE → Long Polling)、**自動重連**、**RPC 式呼叫**。所以下面 `conn.on("MachineUpdated", …)` 這種「像呼叫方法」的寫法、以及連不上自動降級,都是 SignalR 提供的,不是裸 WebSocket。本案在傳輸協商之外**再加一層**「整個連不上 → HTTP 輪詢」(見 §3),共兩層保險。

## 1. 後端 Hub
```csharp
// Hubs/MachineHub.cs —— 本專案前端→後端不需呼叫方法,Hub 主要當推播通道
public class MachineHub : Hub { }
```
```csharp
// Program.cs
builder.Services.AddSignalR();
app.MapHub<MachineHub>("/hubs/machine");
```
從 Service 推播(透過 `IHubContext`,可在 BackgroundService 注入):
```csharp
public class MachineService : IMachineService
{
    private readonly IHubContext<MachineHub> _hub;
    // ...
    public async Task HandleChangedAsync(string id)
    {
        var dto = await BuildSummaryAsync(id);                 // 重讀+重算狀態
        await _hub.Clients.All.SendAsync("MachineUpdated", dto); // 只推這一台
    }
    public async Task HandleRemovedAsync(string id)
        => await _hub.Clients.All.SendAsync("MachineRemoved", id);
}
```

> **重點**:推的是**單台 DTO(delta)**,不是全部 20 台。這就是「複雜 Dashboard 用推播反而比盲輪詢省」的關鍵。

## 2. 前端連線(`wwwroot/js/realtime.js`)
```javascript
const conn = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/machine")
    .withAutomaticReconnect()          // 斷線自動重連
    .build();

conn.on("MachineUpdated", dto => Dashboard.upsertCard(dto)); // 差異更新單張卡片
conn.on("MachineRemoved", id  => Dashboard.removeCard(id));

conn.onreconnecting(() => UI.setConnState("reconnecting"));
conn.onreconnected (() => { UI.setConnState("live"); Dashboard.reloadAll(); });
conn.onclose       (() => Poll.start());   // 徹底斷線 → 啟動輪詢降級

conn.start()
    .then(() => UI.setConnState("live"))
    .catch(() => Poll.start());            // 連不上(環境不支援 WS)→ 輪詢降級
```

## 3. 輪詢降級(`Poll`)
```javascript
const Poll = (function () {
    let timer = null;
    return {
        start() { if (!timer) { UI.setConnState("polling");
                   timer = setInterval(() => api.get("/api/machines").then(Dashboard.renderAll), 5000); } },
        stop()  { clearInterval(timer); timer = null; }
    };
})();
```
→ 兼顧題目寫的「**定時或動態**讀取」:預設動態(推播),連不上才定時(每 5 秒)。

## 4. IIS 注意事項
- IIS 要啟用 **WebSocket Protocol** 功能,SignalR 才能走 WebSocket(否則退回 SSE/long-polling)。
- CSP `connect-src` 要放行 `ws:`/`wss:`(見前端資安準則)。

## 5. 連線狀態 UI
KPI 列右側顯示:🟢 即時(live)/ 🟡 重連中 / ⚪ 輪詢中——讓使用者知道資料新鮮度。
