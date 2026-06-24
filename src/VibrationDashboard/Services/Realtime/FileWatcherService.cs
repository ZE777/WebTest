using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using VibrationDashboard.Common.Settings;

namespace VibrationDashboard.Services.Realtime;

/// <summary>
/// 監看 JSON 資料夾(<see cref="BackgroundService"/> 常駐),檔案新增/變更/刪除時,
/// 經<b>去抖</b>後通知 <see cref="IMachineNotifier"/> 重讀並推播(需求 4 偵測端)。
/// </summary>
/// <remarks>
/// 三個必處理的坑(見 skills/SKILL_FileSystemWatcher):
/// ① 一次儲存常觸發多個事件 → 以 machineId 為 key 去抖(400ms)。
/// ② 可能讀到寫一半的檔 → 重讀失敗時略過(由 Notifier/資料來源吞掉 IO 例外)。
/// ③ 預設 NotifyFilter 不夠 → 明確設定。
/// 本服務為 Singleton,透過 <see cref="IServiceScopeFactory"/> 解析 Scoped 的 Notifier,避免 captive dependency。
/// </remarks>
public sealed class FileWatcherService : BackgroundService
{
    private const int DebounceMs = 400;

    private readonly string _folder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly ConcurrentDictionary<string, Timer> _debounce = new();

    private FileSystemWatcher? _watcher;

    public FileWatcherService(
        IOptions<MachineDataOptions> options,
        IHostEnvironment env,
        IServiceScopeFactory scopeFactory,
        ILogger<FileWatcherService> logger)
    {
        var folder = options.Value.JsonFolder;
        // 與 JsonFileDataSource 相同的路徑解析:相對路徑掛在 ContentRoot 下。
        _folder = Path.IsPathRooted(folder) ? folder : Path.Combine(env.ContentRootPath, folder);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_folder);

        _watcher = new FileSystemWatcher(_folder, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Created += (_, e) => Schedule(e.FullPath, isDelete: false);
        _watcher.Changed += (_, e) => Schedule(e.FullPath, isDelete: false);
        _watcher.Renamed += (_, e) => Schedule(e.FullPath, isDelete: false);
        _watcher.Deleted += (_, e) => Schedule(e.FullPath, isDelete: true);
        _watcher.Error   += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher 發生錯誤");

        _logger.LogInformation("開始監看設備資料夾:{Folder}", _folder);

        // 事件驅動,不需輪詢迴圈;停止時清理。
        stoppingToken.Register(Cleanup);
        return Task.CompletedTask;
    }

    /// <summary>同一檔 400ms 內多次事件只處理最後一次。</summary>
    private void Schedule(string path, bool isDelete)
    {
        var id = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(id)) return;

        _debounce.AddOrUpdate(id,
            _ => NewTimer(id, isDelete),
            (_, existing) => { existing.Dispose(); return NewTimer(id, isDelete); });
    }

    private Timer NewTimer(string id, bool isDelete) =>
        new(async _ => await ProcessAsync(id, isDelete), null, DebounceMs, Timeout.Infinite);

    private async Task ProcessAsync(string id, bool isDelete)
    {
        if (_debounce.TryRemove(id, out var timer)) timer.Dispose();
        try
        {
            // 每次事件開一個 scope 解析 Scoped 的 Notifier(內含 Scoped 的 MachineService)。
            using var scope = _scopeFactory.CreateScope();
            var notifier = scope.ServiceProvider.GetRequiredService<IMachineNotifier>();

            if (isDelete) await notifier.NotifyRemovedAsync(id);
            else await notifier.NotifyChangedAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理檔案變更失敗 {Id}", id);
        }
    }

    private void Cleanup()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        foreach (var t in _debounce.Values) t.Dispose();
        _debounce.Clear();
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}
