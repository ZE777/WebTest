# SKILL：FileSystemWatcher 檔案監看

> 用途:實作需求 4「指定路徑 JSON 更新時,網頁自動更新」的**後端偵測端**。
> 放在 `Services/Realtime/FileWatcherService.cs`,以 `BackgroundService` 常駐。

## 1. 為什麼用 BackgroundService
- 監看要**整個 App 生命週期常駐**,不綁任何請求 → 用 `IHostedService`/`BackgroundService` 最合適。
- 在 `Program.cs` 註冊:`builder.Services.AddHostedService<FileWatcherService>();`

## 2. 三個一定會踩的坑
1. **一次儲存觸發多個事件**:`Changed` 常一次來好幾發 → 必須**去抖(debounce)**。
2. **讀到寫一半的檔**:別人還在寫,你就去讀會拿到半截 JSON → 讀檔失敗要 try/catch,略過等下次;產檔端用「寫 `.tmp` 再 rename」。
3. **預設不夠用的設定**:要設 `NotifyFilters`、`Filter = "*.json"`、`IncludeSubdirectories` 視需求。

## 3. 骨架(含去抖)
```csharp
public class FileWatcherService : BackgroundService
{
    private readonly string _dir;
    private readonly IMachineService _svc;          // 重讀+推播
    private readonly ILogger<FileWatcherService> _log;
    // 以 machineId 為 key 的去抖計時器
    private readonly ConcurrentDictionary<string, Timer> _debounce = new();

    public FileWatcherService(IConfiguration cfg, IMachineService svc, ILogger<FileWatcherService> log)
    {
        _dir = cfg["DataSource:WatchPath"]!;
        _svc = svc; _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_dir);
        var watcher = new FileSystemWatcher(_dir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        watcher.Changed += (_, e) => Debounce(e.FullPath, isDelete: false);
        watcher.Created += (_, e) => Debounce(e.FullPath, isDelete: false);
        watcher.Deleted += (_, e) => Debounce(e.FullPath, isDelete: true);
        watcher.Renamed += (_, e) => Debounce(e.FullPath, isDelete: false);

        ct.Register(() => { watcher.EnableRaisingEvents = false; watcher.Dispose(); });
        return Task.CompletedTask;   // 事件驅動,不需迴圈
    }

    private void Debounce(string path, bool isDelete)
    {
        var id = Path.GetFileNameWithoutExtension(path);
        // 同一檔 400ms 內多次事件 → 只處理最後一次
        _debounce.AddOrUpdate(id,
            _ => NewTimer(id, path, isDelete),
            (_, old) => { old.Dispose(); return NewTimer(id, path, isDelete); });
    }

    private Timer NewTimer(string id, string path, bool isDelete) =>
        new(async _ =>
        {
            _debounce.TryRemove(id, out var t); t?.Dispose();
            try
            {
                if (isDelete) await _svc.HandleRemovedAsync(id);
                else          await _svc.HandleChangedAsync(id);   // 內部重讀檔→重算狀態→推播
            }
            catch (IOException) { _log.LogWarning("讀到鎖定/半截檔,略過 {Id} 等下次", id); }
            catch (Exception ex) { _log.LogError(ex, "處理檔案變更失敗 {Id}", id); }
        }, null, 400, Timeout.Infinite);
}
```

## 4. 與 SignalR 的銜接
`_svc.HandleChangedAsync(id)` 內:重讀該檔 → 重算狀態(對門檻/ISO)→ 呼叫 `MachineHub` 推 `MachineUpdated`(見 [SignalR skill](SKILL_SignalR即時推播.md))。

## 5. 驗收方式
手動編輯 `App_Data/machines/PUMP-A01.json` 改一個 value 存檔 → 看板上那張卡片應在 1 秒內更新。
