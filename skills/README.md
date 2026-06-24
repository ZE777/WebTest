# 技術 How-To(skills)

可重用的「怎麼做」技術筆記,聚焦本專案三個關鍵技術點。
SDD 流程中,實作對應功能前先看這裡;實作後若有新心得回寫補充。

| Skill | 用在哪個需求/Phase | 重點 |
|---|---|---|
| [SKILL_FileSystemWatcher 檔案監看](SKILL_FileSystemWatcher檔案監看.md) | 需求 4 / Phase 5 | 監看 JSON 資料夾、事件去抖、讀到半截檔的防護 |
| [SKILL_SignalR 即時推播](SKILL_SignalR即時推播.md) | 需求 4 / Phase 5 | Hub 推單台 delta、前端連線、`withAutomaticReconnect`、輪詢降級 |
| [SKILL_ECharts 趨勢圖與游標](SKILL_ECharts趨勢圖與游標.md) | 需求 6 / Phase 6 | 可移動游標、門檻線、分區色帶、延遲載入/dispose |

> 這三點也是面試「設計創意/技術深度」最容易被追問的地方,先把原理弄懂。
