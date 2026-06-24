# SKILL：ECharts 趨勢圖與可移動游標

> 用途:需求 6「點機器用圖表顯示趨勢,游標可移動查看數值」。
> 對應附圖:mm/s 趨勢線 + 黃線(警告)+ 紅線(危險)+ 7 天區間。

## 1. 為什麼選 ECharts
- 內建 `axisPointer`(**可移動游標**)、`tooltip`(數值讀數)、`markLine`(門檻線)、`markArea`(分區色帶)——需求 6 與附圖幾乎開箱即用。
- 暗色主題、效能好,適合工業儀表板。

## 2. 延遲載入 + instance 管理(效能準則)
- ECharts 是大型庫 → **點機器看明細時才載入/初始化**,不在首頁就 init。
- 一個容器只 `echarts.init` 一次,切換機器時用 `setOption` 更新資料;**關閉明細時 `dispose()`** 釋放。
- 視窗 resize → `chart.resize()`,並用 debounce 包住。

## 3. 核心設定(可移動游標 + 門檻 + 分區)
```javascript
const chart = echarts.init(document.getElementById('trend'), 'dark');
chart.setOption({
  grid: { left: 48, right: 24, top: 24, bottom: 32 },
  xAxis: { type: 'time' },
  yAxis: { type: 'value', name: detail.unit /* mm/s */ },
  // ① 可移動游標:十字準星,滑鼠移到哪就顯示哪一點
  tooltip: {
    trigger: 'axis',
    axisPointer: { type: 'cross', snap: true,
      label: { backgroundColor: '#1E3050' } },
    formatter: p => {
      const d = p[0];
      return `${echarts.format.formatTime('yyyy-MM-dd hh:mm', d.value[0])}
              <b>${d.value[1]} ${detail.unit}</b>`;
    }
  },
  series: [{
    type: 'line', smooth: true, showSymbol: true,
    data: detail.measurements.map(m => [m.time, m.value]),
    lineStyle: { color: 'var-accent /* #22D3EE */' },
    // ② 門檻線:警告(黃)、危險(紅)—— 對應附圖兩條線
    markLine: {
      symbol: 'none', silent: true,
      data: [
        { yAxis: detail.warningThreshold, lineStyle: { color: '#F59E0B' }, label: { formatter: '警告' } },
        { yAxis: detail.dangerThreshold,  lineStyle: { color: '#EF4444' }, label: { formatter: '危險' } }
      ]
    },
    // ③ 分區色帶:良好/警告/危險三段背景(極淡),強化「現在落在哪一區」
    markArea: {
      silent: true,
      data: [
        [{ yAxis: 0 },                       { yAxis: detail.warningThreshold, itemStyle:{color:'rgba(34,197,94,.06)'} }],
        [{ yAxis: detail.warningThreshold }, { yAxis: detail.dangerThreshold,  itemStyle:{color:'rgba(245,158,11,.08)'} }],
        [{ yAxis: detail.dangerThreshold },  { yAxis: 'max',                   itemStyle:{color:'rgba(239,68,68,.08)'} }]
      ]
    }
  }]
});
```

## 4. 越線資料點強調
用 `series.data` 的逐點樣式:超過門檻的點換色放大,呼應「強調需觀覽資料」:
```javascript
data: detail.measurements.map(m => ({
  value: [m.time, m.value],
  itemStyle: { color: m.value >= detail.dangerThreshold ? '#EF4444'
             : m.value >= detail.warningThreshold ? '#F59E0B' : '#22C55E' },
  symbolSize: m.value >= detail.warningThreshold ? 9 : 5
}))
```

## 5. 與看板的連動
- 點卡片 → 載明細 → `chart.setOption`(切換該台資料)。
- 卡片的迷你 sparkline 也可用同一份資料的精簡版 ECharts(關閉軸/tooltip)。
- 游標移動的數值讀數,可同步更新明細區的「目前值/時間/落在哪區」文字。

## 6. 驗收方式
明細圖出現後,滑鼠左右移動 → 十字游標跟隨、tooltip 顯示對應時間點數值;黃/紅門檻線與分區色帶正確;越線點變色。
