# SqlScripts（SQL 路徑說明）

SQL 路徑採 **EF Core Code-first + Migrations**（見 rules/後端實作準則/01 §5），因此：

- **建表 DDL 不手寫**，由 `Data/Migrations/`（`InitialCreate` + `AddMachineType`）擁有，啟動時 `Database.Migrate()` 套用。
  schema 對齊 docs/03 §4 的正規化設計（Machine / Measurement、FK、`IX_Measurement_Machine_Time`）。
- **種子資料不寫死 SQL**，改由 `Data/SqlDbInitializer` 在**空庫時**從現有 `App_Data/machines/*.json`
  讀入（複用已測過的 `JsonFileDataSource`）。好處：
  - 二位元圖檔（`varbinary`）透過 EF 直接寫入，免在 SQL 腳本裡塞 base64。
  - JSON 與 SQL 兩種後端**共用同一份資料來源**，畫面完全一致，易於對照展示。

> 所以本資料夾**刻意不放建表 / 種子的 SQL 腳本**——schema 與種子都是**啟動時程式自動完成**，不需手動腳本。

## SQL 模式啟動「兩關」（schema 與 data 分離）

`SqlDbInitializer.InitializeAsync()` 在 SQL 模式啟動時，做兩件**互相獨立**的事：

1. **第①關 `Migrate()` — 管「表」**：比對「程式裡的 migration 檔」vs「DB 的 `__EFMigrationsHistory`（已套用哪些）」，補上沒套用的 → 建 / 改表。**跟實體無關**（實體→migration 的差異是設計時 `dotnet ef migrations add` 算的，不是啟動時）。跑完這關，表必定存在。
2. **第②關 種子 — 管「資料(rows)」**：看 `Machine` 表**空不空**（`AnyAsync`）——空就讀 `App_Data` 的 JSON 種一次；有 rows 就跳過、**不覆蓋**。

> **簡記：第①步管表、第②步管料；表看「migration 套了沒」，料看「有沒有 rows」。**

## 資料來源方向（本案）
- **開發者 = SQL**：`appsettings.Development.json` 設 `DataSource:Type=Sql`，連 `.\SQLEXPRESS` 的 `WebTest`（Windows 整合式驗證、無帳密）。
- **發布 / 正式版 = JSON**：base `appsettings.json` 維持 `Type=Json`（零基礎設施、可攜）。
- 臨時覆寫：`dotnet run --DataSource:Type=Sql`（或 `=Json`）。完整考量見 docs/03 §6。

## 日常怎麼用：其實什麼都不用做
切到 SQL 模式直接啟動 App，上面「兩關」會自動建表 + 空庫種子：
```powershell
cd src/VibrationDashboard
dotnet run --DataSource:Type=Sql      # 看 log「SQL 種子完成：N 台…」即成功
```
> 前置：本機 `.\SQLEXPRESS` 有 `WebTest` 庫、你的 Windows 帳號有權限；`App_Data/machines` 有 JSON 種子檔（沒有就先跑下面的 SeedDataGenerator）。

### （選配）不啟動 App 也想先把 DB 備好
```powershell
# ① 產生 JSON 種子檔（15 台、各 40 天，含自繪 PNG 圖檔）—— App_Data 是空的才需要
dotnet run --project tools/SeedDataGenerator -- src/VibrationDashboard/App_Data/machines

# ② 建資料表（在 web 專案目錄下執行，設計時工廠才讀得到連線字串）
cd src/VibrationDashboard
dotnet ef database update
```
> 這只是「先把 DB 準備好」的便利分步；**正常開發不必跑**——啟動 App 的「兩關」就全自動處理了。

## 重新產生 / 演進 migration
```powershell
cd src/VibrationDashboard
dotnet ef migrations add <Name> -o Data/Migrations
dotnet ef database update          # 或直接啟動 App，Migrate() 會套用
```
> 設計時連線由 `Data/AppDbContextFactory` 讀 `appsettings.json` + `appsettings.Development.json` 的
> `ConnectionStrings:Default`（連線字串現放 Development.json），與執行階段 `DataSource:Type` 無關。
