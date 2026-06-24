# 03 資料來源:JSON 與 SQL 設計

> 對接職缺要求:MS-SQL、了解資料庫設計與基本正規化。
> 這份回答了一個關鍵討論:**「為什麼用 JSON?JSON 真的比較快嗎?」**

## 1. 先破除迷思:「JSON 比較快」其實看情況
JSON 不是天生比較快。**「快」要看存取模式**:
- 讀一份**小文件、整份讀、不做查詢** → JSON 快(免連線、免查詢引擎)。
- 在**大量資料裡篩選/排序/聚合/查歷史** → SQL(有索引)遠快。

所以這題建議用 JSON,**主因不是速度,而是需求 4 本質上是「檔案系統變更」情境**——`FileSystemWatcher` 可以原生偵測「新增檔=多一台」「改檔=數值更新」。出題者其實在考**檔案監看 + 動態更新畫面**。

## 2. JSON vs SQL 全面對照

| 面向 | JSON 檔 | MS-SQL |
|---|---|---|
| 讀取效能 | 小檔整份讀快;**檔多/檔大就輸**,跨機器查詢得自己全載入 | 有索引時選擇性查詢/聚合/歷史**遠快** |
| 查詢能力 | 無,要自己寫程式篩選 | 強,`SELECT TOP 10 ... ORDER BY time` 一行解決 |
| 寫入/併發 | 無交易、**邊寫邊讀可能讀到半截檔** | ACID 交易、併發安全 |
| 結構彈性 | 半結構化,改結構不需 migration | 結構嚴謹,適合**正規化** |
| 二位元圖檔 | base64 內嵌,**膨脹約 33%**,每次讀都重解析 | `VARBINARY`,不 select 就不佔讀取成本 |
| 完整性/約束 | 無 | 外鍵、約束、預設值 |
| 部署 | **零基礎設施**,丟檔就能 demo | 需 DB server / LocalDB |
| 最適情境 | 一機一檔的快照、檔案監看 | 大量資料、歷史查詢、多人併發 |

**結論**:兩者互補。我們用**介面把兩者抽象化**(見 [02](02_系統架構設計.md)),JSON 滿足需求 4/5,SQL 展示資料庫能力。

## 3. JSON 檔設計(主資料來源)
- **一機一檔**:`App_Data/machines/{machineId}.json`(對應需求 5「一機一檔」)。
- 動態增減:新增檔→多一台、刪檔→少一台、改檔→更新數值。

```jsonc
// App_Data/machines/PUMP-A01.json
{
  "id": "PUMP-A01",
  "name": "冷卻水泵 A01",
  "unit": "mm/s",
  "warningThreshold": 0.5,
  "dangerThreshold": 0.88,
  "status": "Abnormal",            // 也可由程式對門檻自動判定
  "image": "data:image/png;base64,iVBORw0KGgo...",  // 二位元圖檔
  "measurements": [
    { "time": "2026-05-23T08:00:00", "value": 0.18 },
    { "time": "2026-05-24T08:00:00", "value": 0.21 },
    // ... 共 40 筆(每日一筆 × 40 天),含日期時間
    { "time": "2026-07-01T08:00:00", "value": 0.63 }
  ]
}
```

> **寫入安全小技巧**:產生/更新檔時先寫到 `*.tmp` 再 `rename` 覆蓋(原子操作),避免 watcher 讀到寫一半的檔。

> **量測筆數(`MeasurementCount`,預設 40)**:原始需求為「至少 10 筆」;Seed(`tools/SeedDataGenerator`)實作產 **每日一筆 × 40 天**(5/23~7/1),讓趨勢圖「7 天 / 30 天 / 全部」區間切換有意義差異(見 docs/05 §5)。讀取端 `JsonFileDataSource` 以 `MeasurementCount` 取**最近 N 筆**(`TakeLast`),與 SQL 來源行為一致、單檔量測擴充時不無上限成長。改回 10 只需調 `appsettings.json` 的 `MachineData:MeasurementCount`。

## 4. SQL 設計(展示正規化,可切換)

把上面的文件**正規化**成三張表,消除重複、保證完整性:

```sql
-- 1) 設備主檔
CREATE TABLE Machine (
    Id               VARCHAR(50)   NOT NULL PRIMARY KEY,
    Name             NVARCHAR(100) NOT NULL,
    Unit             VARCHAR(20)   NOT NULL DEFAULT 'mm/s',
    WarningThreshold FLOAT         NOT NULL,
    DangerThreshold  FLOAT         NOT NULL,
    Image            VARBINARY(MAX) NULL          -- 二位元圖檔
);

-- 2) 量測資料(一對多;每台多筆,含時間)
CREATE TABLE Measurement (
    Id          BIGINT IDENTITY PRIMARY KEY,
    MachineId   VARCHAR(50) NOT NULL
                REFERENCES Machine(Id),           -- 外鍵 → 參照完整性
    MeasuredAt  DATETIME2   NOT NULL,
    Value       FLOAT       NOT NULL
);
CREATE INDEX IX_Measurement_Machine_Time
    ON Measurement(MachineId, MeasuredAt DESC);    -- 取最近 N 筆很快

-- 3) 狀態為「衍生值」,不存死,由最新值對門檻計算(避免資料不一致)
```

**正規化說明(報告可講)**:
- 設備屬性(名稱/單位/門檻)放 `Machine`,量測值放 `Measurement` → **避免每筆量測重複存設備資訊**(第二正規化:消除部分相依)。
- `status` 是**衍生資料**,由「最新值 vs 門檻」即時算出,不存進表 → **避免更新異常**(數值更新了但狀態忘了改)。
- 「取某台最近 N 筆」(N=`MeasurementCount`,預設 40)= `SELECT TOP (N) ... WHERE MachineId=@id ORDER BY MeasuredAt DESC`,靠 `IX_Measurement_Machine_Time` 索引很快。

> **實作狀態(Phase 8 已完成)**:上述 schema 改以 **EF Core Code-first** 落實——實體 `Data/Entities/`、對映 `Data/Configurations/`、由 `InitialCreate` migration 產生(欄位型別/FK/索引與本節一致),故**不手寫建表 DDL**(見 rules/後端/01 §5、`SqlScripts/README.md`)。資料源 `SqlMachineDataSource` 實作同一支 `IMachineDataSource`,切 `DataSource:Type=Sql` 即用,前端/Controller/Service 零改動;種子資料於空庫時自現有 JSON 匯入。

> **啟動兩關(SQL 模式)**:`SqlDbInitializer` 啟動時做兩件**獨立**的事——① `Migrate()` 確保「**表**」在:比對「migration 檔 vs `__EFMigrationsHistory`(套了哪些)」,**跟實體無關**(實體→migration 的差異是設計時 `dotnet ef migrations add` 算的);② 再看「**表空不空**」決定種不種「**資料**」:空就讀 JSON 種一次、有 rows 就跳過**不蓋**。**簡記:第①步管表(看 migration 套了沒)、第②步管料(看有沒有 rows)。**

## 5. 二位元圖檔的存放取捨
| 方式 | JSON | SQL |
|---|---|---|
| 存法 | base64 字串內嵌 | `VARBINARY(MAX)` |
| 缺點 | 檔案膨脹、每次讀都解析 | 大圖讓資料列變胖 |
| 我們的做法 | 圖檔走**獨立 API + ETag 快取**,看板初次載入**不夾帶**大圖,需要時才抓、之後走 304 |

## 6. 連線設定與驗證策略(部署考量)

> **對接情境**:這是面試專案。方向是**開發者用 SQL**(開發/展示資料庫設計與正規化能力)、**發布/正式版用 JSON**(零基礎設施、clone 或部署即跑,不必先備好 DB);資料來源切換只改設定、上層零改動。本節說明連線設定放哪、用哪種驗證、為什麼。

### 6.1 一條判斷原則(決定設定放哪)
一個設定值要不要藏、往哪一層放,只看兩軸:
- **會不會洩密?**(有帳密=會;Windows 驗證=不會)
- **會不會綁特定機器?**(寫死機器名=會;用 `.` 代表本機=不會)

兩軸都「不會」→ 放進版控的 `appsettings` 最單純;任一「會」→ 往上層(環境變數 / 祕密庫)或先把它**機器無關化**。

### 6.2 我們的決策
| 決策 | 內容 | 理由 |
|---|---|---|
| 設定放 **appsettings**,**不放 web.config** | 連線字串在 `ConnectionStrings:Default`、開關在 `DataSource:Type` | Core 的 app **不讀 web.config 的應用設定**;web.config 只管「IIS 怎麼啟動程序」,且僅 IIS 下生效(`dotnet run` 不讀) |
| **Windows 整合式驗證**(`Trusted_Connection=True`) | 不帶帳號密碼 | **無帳密=無祕密**,可安全進版控;直接免掉「明文 vs 加密」整個問題 |
| Server 用 **`.\SQLEXPRESS`** | `.` = 本機 | **機器無關**:任何開發機都用同一條字串連自己的本地 DB,不綁特定機器名 |
| **方向：Development=Sql、Production=Json** | `appsettings.Development.json` 設 `Type=Sql`(SQL 設定集中於此)；base `appsettings.json` 與 `appsettings.Production.json` 維持 `Type=Json` | **開發者**(`dotnet run`)對 SQL 開發/demo 資料庫；**發布/正式版**純 JSON、零基礎設施、可攜 |
| (選配)**IIS 跑 SQL 才需授權 App Pool 身分** | `CREATE LOGIN [IIS APPPOOL\站台名] FROM WINDOWS` + `WebTest` 的 `db_owner` | 預設 IIS=Production=JSON、不需 DB；**若刻意讓 IIS 測 SQL**(命令列/環境覆寫),IIS 行程身分是 App Pool 非你的帳號,須在該機 SQL 開登入,`db_owner` 因 Migrations 要建表 |

### 6.3 為什麼不用 SQL 帳密 / 不加密 appsettings
- **首選 Windows 驗證**:本機 `dotnet run` 用你的帳號、IIS 用 App Pool 身分、地端可用網域服務帳號——**全程不存密碼**,自然不必加密。
- **「加密 appsettings」是 .NET Framework 的做法**(`aspnet_regiis -pe`),**.NET Core 沒有內建**。Core 對機敏資料的正解是:repo 永不放真實憑證,改由**環境變數 / 祕密庫(Key Vault 等)/ User Secrets(僅開發)** 提供。
- 真的**非用 SQL 帳密不可**才談保護(例:app 連**遠端**那台 DB、又無網域 —— Windows 驗證跨機需 AD 網域帳號):此時把含密碼的連線字串放**環境變數**或祕密庫,或用 **DPAPI** 機器綁定加密,**仍不寫進版控的 appsettings**。

### 6.4 不同部署型態對照
| 部署型態 | 連線字串放哪 | 帳密保護 |
|---|---|---|
| 開發機(本案,用 SQL) | `appsettings.Development.json`(`.\SQLEXPRESS`,機器無關) | Windows 驗證,**無帳密** |
| 雲端 SaaS(自管伺服器) | 平台環境變數 / Key Vault | 受管祕密庫 |
| 客戶機 / 地端 | 客戶機上的 `appsettings.Production.json` 或環境變數(**安裝時填**,不進你的 git) | 首選 Windows 服務帳號;非用密碼不可才加密 |

> **金句**:「連線字串放哪不是『新舊』問題,而是『**這個值會不會洩密、會不會綁特定環境**』。我用 Windows 驗證把帳密消掉、用 `.` 把機器名消掉,所以它能安心待在 appsettings(SQL 設定集中在 Development.json、發布版純 JSON);web.config 從頭到尾不碰,因為那是 **IIS 啟動層**、不是**應用設定層**。」
