/* ===========================================================================
   trendChart.js — Phase 6 明細趨勢圖(彈出 Modal 大圖 + ECharts 懶載入)
   職責:點卡片 → 彈出 Modal;首次點才下載 echarts;趨勢線 + 可移動游標 +
         門檻線(黃/紅)+ 分區色帶 + 越線資料點強調 + 7天/30天/全部/自訂 區間切換 +
         Y 軸縮放(dataZoom:右側細滑桿 / 滾輪縮放 / 雙擊還原,值差大時看低值細節)。
   區間設計(docs/05 §5、docs/08 §H-Q14):圖同時服務「近期即時觀察」(7天/30天)與
         「歷史回溯」(全部/自訂),預設壓在近期(30天);過濾在前端做(日資料量級整段撈回即可)。
   即時更新(與 Phase 5 即時層連動):開圖時抓一次明細快照;之後收到「該台」的 SignalR
         推播(onMachineUpdated)會先用摘要刷新讀數頭,再背景重抓完整明細並重畫圖,
         維持目前選取的區間 / 自訂端點(改 JSON 任一天的值,圖也會即時跟著變)。
   安全:外部字串一律 .text();圖表資料皆為數字。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var ECHARTS_SRC = '/lib/echarts/echarts.min.js';
  var DAY_MS = 86400000;
  var reduceMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);

  // 狀態標籤 / 數值格式化 / 節流共用 shared.js;此處只留趨勢圖專屬的時間軸格式化與 CSS 變數讀取
  var statusKey = Shared.statusKey, statusLabel = Shared.statusLabel;
  var fmt = Shared.formatValue, throttle = Shared.throttle;

  // 狀態 → 分區色票:注意異常區的視覺色是 --warning(非 --abnormal),故需此對照表
  var ZONE_VAR = { Good: '--good', Abnormal: '--warning', Danger: '--danger' };

  function cssVar(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }
  function pad(n) { return n < 10 ? '0' + n : '' + n; }
  function fmtAxis(ts) { var d = new Date(ts); return (d.getMonth() + 1) + '/' + d.getDate(); }
  function fmtFull(ts) {
    var d = new Date(ts);
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
      ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
  }

  /* ── 時間區間工具(7天/30天/全部/自訂) ───────────────────────────────
     近期區間(7天/30天)以「最新一筆」為錨往回算(資料是快照、未必到今天,錨在最新較穩);
     自訂期間用日期框,端點補成「當日起 00:00 / 當日迄 23:59」涵蓋整天,並夾進該設備資料跨度。 */
  function startOfDay(ts) { var d = new Date(ts); d.setHours(0, 0, 0, 0); return d.getTime(); }
  function endOfDay(ts) { var d = new Date(ts); d.setHours(23, 59, 59, 999); return d.getTime(); }
  function dateInputVal(ts) { var d = new Date(ts); return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
  function parseDateInput(s) {
    var p = (s || '').split('-');
    if (p.length !== 3) return null;
    var t = new Date(+p[0], (+p[1]) - 1, +p[2]).getTime();
    return isNaN(t) ? null : t;
  }
  // 量測序列已依時間升冪 → 取資料跨度 [min,max](timestamp);無資料回 null
  function dataSpan(detail) {
    var ms = detail && detail.measurements;
    if (!ms || !ms.length) return null;
    return { min: new Date(ms[0].time).getTime(), max: new Date(ms[ms.length - 1].time).getTime() };
  }
  // 自訂期間夾進資料跨度;未選過時預設「近 30 天」;起>迄則對調(對使用者更友善)
  function effectiveCustom(span) {
    var lo = startOfDay(span.min), hi = endOfDay(span.max);
    var from = customFrom == null ? Math.max(lo, hi - 30 * DAY_MS) : customFrom;
    var to = customTo == null ? hi : customTo;
    if (from > to) { var t = from; from = to; to = t; }
    from = Math.min(Math.max(from, lo), hi);
    to = Math.min(Math.max(to, lo), hi);
    return { from: from, to: to };
  }
  // 依區間過濾量測:圖(buildOption)與讀數頭統計(computeStats)共用,確保兩者看同一段資料。
  // 「近 N 天」用**筆數**算:從最新往回取最近 N 筆(含最新一筆),每日一筆 → 即第 N 天。
  //   ① 保證剛好 N 筆,免 fencepost(時間視窗的邊界剛好壓在取樣點會多含一筆 → 7天變 8 筆);
  //   ② 與健康率走勢回推一致——都「依序列位置從最新端對齊、不讀日期」(docs/07 §6.1)。
  // 量測已依時間升冪(dataSpan/buildOption 同此假設),故 slice(-N) 即最近 N 筆。
  // 自訂:使用者明確點選起迄日 → 改用日期閉區間(兩端含)。
  function filterByRange(ms, rangeKey, detail) {
    ms = ms || [];
    if (rangeKey === 'custom') {
      var span = dataSpan(detail);
      if (!span) return ms.slice();
      var r = effectiveCustom(span);
      return ms.filter(function (m) {
        var t = new Date(m.time).getTime();
        return t >= r.from && t <= r.to;
      });
    }
    var n = parseInt(rangeKey, 10);    // '7d'→7、'30d'→30:取最近 N 筆;'all'→NaN → 全取
    return n > 0 ? ms.slice(-n) : ms.slice();
  }

  /* ── 讀數頭統計:前端依「選取區間」重算 ───────────────────────────────────
     鏡像後端 MeasurementAnalyzer.Analyze(公式對齊 docs/07 §5.2),與 Shared.evalStatus 鏡像
     StatusEvaluator 同一個慣例:任一邊改公式,兩處各改一份。改在前端的原因——統計要「跟著
     上方選的 7天/30天/自訂 變動」,而區間是純前端概念、整段資料也已載到瀏覽器,不必再打 API。 */
  function round4(v) { return Math.round(v * 10000) / 10000; }
  function slopePerDay(ms) {
    // 最小平方法線性回歸:x=距首筆天數、y=值;鏡像 MeasurementAnalyzer.ComputeSlopePerDay
    var n = ms.length;
    if (n < 2) return 0;
    var t0 = new Date(ms[0].time).getTime();
    var xs = [], mx = 0, my = 0, i, x;
    for (i = 0; i < n; i++) { x = (new Date(ms[i].time).getTime() - t0) / DAY_MS; xs.push(x); mx += x; my += ms[i].value; }
    mx /= n; my /= n;
    var sxx = 0, sxy = 0, dx;
    for (i = 0; i < n; i++) { dx = xs[i] - mx; sxx += dx * dx; sxy += dx * (ms[i].value - my); }
    return sxx <= 1e-12 ? 0 : sxy / sxx;   // 零跨度(時間全同)→ 0
  }
  function computeStats(ms, warn) {
    var n = ms.length;
    if (!n) return null;
    var sum = 0, max = -Infinity, min = Infinity, exceed = 0, i, v;
    for (i = 0; i < n; i++) {
      v = ms[i].value;
      sum += v;
      if (v > max) max = v;
      if (v < min) min = v;
      if (v >= warn) exceed++;             // 與後端一致:達警告門檻(含 >=)
    }
    var avg = sum / n, varSum = 0, d;
    for (i = 0; i < n; i++) { d = ms[i].value - avg; varSum += d * d; }
    return {
      average: round4(avg), max: round4(max), min: round4(min),
      stdDev: round4(Math.sqrt(varSum / n)),   // 母體標準差(÷N)
      slopePerDay: round4(slopePerDay(ms)),
      exceedWarning: exceed, count: n
    };
  }

  /* ── 懶載入 echarts:首次需要才注入 <script>,回傳 Promise ───────────── */
  var echartsLoader = null;
  function loadECharts() {
    if (window.echarts) return $.Deferred().resolve(window.echarts).promise();
    if (echartsLoader) return echartsLoader;
    var d = $.Deferred();
    var s = document.createElement('script');
    s.src = ECHARTS_SRC;
    s.onload = function () { d.resolve(window.echarts); };
    s.onerror = function () { echartsLoader = null; d.reject(); };
    document.head.appendChild(s);
    echartsLoader = d.promise();
    return echartsLoader;
  }

  /* ── 模組狀態 ─────────────────────────────────────────────────────── */
  var chart = null;    // echarts 實例(關閉 Modal 才 dispose)
  var current = null;  // 目前明細 DTO 快照
  var range = '30d';   // '7d' | '30d' | 'all' | 'custom'(預設「月」:監管系統先看近期動向)
  var customFrom = null, customTo = null; // 自訂期間端點(timestamp);依當前設備資料跨度夾範圍
  var lastFocus = null;
  var pendingId = null;// 目前嘗試載入的設備 id(供載入失敗後「重試」+ 左右切換定位)
  var navIds = [];     // 可左右切換的設備 id 順序(由看板依當前篩選+排序傳入)
  var onResize = throttle(function () { if (chart) chart.resize(); }, 100);

  function $modal() { return $('#trend-modal'); }
  function isOpen() { return !$modal().prop('hidden'); }

  /* ── 圖表設定 ─────────────────────────────────────────────────────── */
  // 值 → 分區色:走共用 Shared.evalStatus,前端門檻判定不再各寫一份
  function colorForValue(v, warn, danger) { return cssVar(ZONE_VAR[Shared.evalStatus(v, warn, danger)]); }

  function buildOption(detail, rangeKey) {
    var unit = detail.unit, warn = detail.warningThreshold, danger = detail.dangerThreshold;
    var ms = filterByRange(detail.measurements, rangeKey, detail);   // 與讀數頭統計共用同一段
    // X 軸用「類目軸」(每筆一格、等距):量測是離散的每日讀數,類目軸能讓
    // ① 資料點精準對齊下方日期標籤(time 軸會把刻度切在午夜整點、與 08:00 的點錯位);
    // ② 軸刻度數量「隨選取資料變動」(自訂只選兩天就只有兩格,不再硬切固定 7 格、標籤重複)。
    var times = ms.map(function (m) { return new Date(m.time).getTime(); });   // 類目值 = timestamp
    // 每點依「值 vs 門檻」上色,越線(>= 警告)的點放大強調
    var data = ms.map(function (m) {
      return {
        value: m.value,
        itemStyle: { color: colorForValue(m.value, warn, danger) },
        symbolSize: m.value >= warn ? 9 : 5
      };
    });
    var maxVal = ms.reduce(function (a, m) { return Math.max(a, m.value); }, 0);
    var yTop = +(Math.max(danger, maxVal) * 1.15).toFixed(4);
    var accent = cssVar('--accent'), dim = cssVar('--text-dim'), grid = cssVar('--grid');

    return {
      animation: !reduceMotion,
      grid: { left: 52, right: 88, top: 28, bottom: 36 },  // right 留寬給門檻線右側標籤(危險/警告)+ Y 軸縮放滑桿,皆不被裁切
      tooltip: {
        trigger: 'axis',
        // 十字游標:x 軸標籤把類目(timestamp)格式化回 M/D,不顯示原始毫秒數
        axisPointer: {
          type: 'cross',
          label: {
            backgroundColor: cssVar('--surface-2'),
            formatter: function (o) { return o.axisDimension === 'x' ? fmtAxis(+o.value) : fmt(o.value); }
          }
        },
        // 與全站 data-tooltip 共用同一套外觀:半透明 + 圓角 + 模糊
        backgroundColor: cssVar('--tooltip-bg'), borderColor: cssVar('--tooltip-border'), borderRadius: 6,
        extraCssText: '-webkit-backdrop-filter: blur(6px); backdrop-filter: blur(6px); box-shadow: 0 6px 20px rgba(0,0,0,.4);',
        textStyle: { color: cssVar('--text') },
        formatter: function (ps) {
          var p = ps[0], v = p.value;                 // 類目軸:value 即數值;時間取 axisValue(類目=timestamp)
          // 分區字樣依狀態上色,對齊看板其他地方的良好/異常/危險用色(走共用 evalStatus)
          var st = Shared.evalStatus(v, warn, danger);
          var col = cssVar(ZONE_VAR[st]);
          var tag = '<span style="color:' + col + ';font-weight:600">' + statusLabel(st) + '</span>';
          return fmtFull(+p.axisValue) + '<br/><b style="color:' + col + '">' + fmt(v) + '</b> ' + unit + ' ・ ' + tag;
        }
      },
      xAxis: {
        type: 'category',
        data: times,                          // 類目 = 各筆 timestamp(label 再格式化成 M/D)
        boundaryGap: false,                   // 折線貼左右緣,首末點不留半格空白 → 與標籤對齊
        axisLine: { lineStyle: { color: grid } },
        // hideOverlap:40 天等多點時自動略過重疊標籤(取代 time 軸硬切固定刻度);少點時逐筆顯示且對齊資料點
        axisLabel: { color: dim, hideOverlap: true, formatter: function (val) { return fmtAxis(+val); } },
        splitLine: { show: false }
      },
      yAxis: {
        type: 'value', min: 0, max: yTop, name: unit,
        nameTextStyle: { color: dim }, axisLabel: { color: dim },
        splitLine: { lineStyle: { color: grid } }
      },
      // Y 軸縮放:值差很大時(例:平常 0.3、尖峰 10)低值區被壓扁,可拉近想看的數值帶。
      // filterMode:'none' 是關鍵 → 只改可視範圍、不過濾掉視窗外的點(否則趨勢線會斷)。
      // inside:圖內滾輪縮放/拖曳平移;slider:右側垂直滑桿(對齊 grid 上下緣)。雙擊還原由 renderChart 接。
      dataZoom: [
        { type: 'inside', yAxisIndex: 0, filterMode: 'none', zoomOnMouseWheel: true, moveOnMouseMove: true, moveOnMouseWheel: false },
        {
          type: 'slider', yAxisIndex: 0, filterMode: 'none',
          right: 8, width: 6, top: 28, bottom: 36, borderRadius: 3,  // 細軌貼右緣,與 grid 上下對齊
          showDetail: false, showDataShadow: false, brushSelect: false,
          backgroundColor: 'rgba(255, 255, 255, .03)', borderColor: 'transparent',  // 極淡軌道
          fillerColor: 'rgba(34, 211, 238, .18)',               // 選取帶:accent 半透明(沿用全站 spark/卡片同款色值)
          handleIcon: 'circle', handleSize: 11,                 // 圓點把手(取代預設粗膠囊,視覺更輕)
          handleStyle: { color: accent, borderColor: cssVar('--bg'), borderWidth: 1.5, shadowBlur: 6, shadowColor: 'rgba(34, 211, 238, .5)' },
          moveHandleSize: 0,                                    // 不要中間那條移動把手,保持清爽
          textStyle: { color: dim },
          dataBackground: { lineStyle: { color: 'transparent' }, areaStyle: { color: 'transparent' } },
          selectedDataBackground: { lineStyle: { color: 'transparent' }, areaStyle: { color: 'transparent' } }
        }
      ],
      series: [{
        type: 'line', smooth: true, symbol: 'circle',
        lineStyle: { color: accent, width: 2 }, itemStyle: { color: accent },
        data: data,
        // 門檻線:警告(黃)、危險(紅)兩條水平虛線
        markLine: {
          symbol: 'none', silent: true,
          data: [
            { yAxis: warn,   lineStyle: { color: cssVar('--warning'), type: 'dashed' }, label: { color: cssVar('--warning'), formatter: '警告 ' + warn } },
            { yAxis: danger, lineStyle: { color: cssVar('--danger'),  type: 'dashed' }, label: { color: cssVar('--danger'),  formatter: '危險 ' + danger } }
          ]
        },
        // 分區色帶:良好 / 警告 / 危險 三段背景(極淡,引用 CSS token)
        markArea: {
          silent: true,
          data: [
            [{ yAxis: 0,      itemStyle: { color: cssVar('--zone-good') } },    { yAxis: warn }],
            [{ yAxis: warn,   itemStyle: { color: cssVar('--zone-warning') } }, { yAxis: danger }],
            [{ yAxis: danger, itemStyle: { color: cssVar('--zone-danger') } },  { yAxis: yTop }]
          ]
        }
      }]
    };
  }

  /* ── 讀數頭(標題 + 當前狀態 + 區間統計) ──────────────────────────────
     狀態 / 最新值 = 設備「當前」狀態(取最新一筆,與區間無關);
     平均/最大/標準差/趨勢/超標 = 依「選取區間」前端重算(computeStats,鏡像後端 MeasurementAnalyzer)。 */
  function renderReadout(d, rangeKey) {
    $('#trend-title').text(d.name);
    var $r = $('#trend-readout').empty();
    function item(label, value, statusForColor, tip) {
      var $s = $('<span class="ro"></span>');
      if (statusForColor) $s.addClass('ro--' + statusKey(statusForColor));
      if (tip) $s.attr('data-tooltip', tip);   // 解釋「怎麼算的」(全站共用 data-tooltip)
      $('<i></i>').text(label + ' ').appendTo($s);
      $('<b></b>').text(value).appendTo($s);
      $s.appendTo($r);
    }
    // 狀態:用徽章(pill)呈現,沿用卡片狀態燈的「圓點 + 色彩語言」,比純文字更一眼。
    function statusItem(label, status, tip) {
      var $s = $('<span class="ro ro--status"></span>');
      if (tip) $s.attr('data-tooltip', tip);
      $('<i></i>').text(label).appendTo($s);
      var $b = $('<span class="status-badge"></span>').addClass('status--' + statusKey(status));
      $('<span class="status__dot" aria-hidden="true"></span>').appendTo($b);
      $('<span class="status__label"></span>').text(statusLabel(status)).appendTo($b);
      $b.appendTo($s);
      $s.appendTo($r);
    }
    if (d.type) item('類型', d.type);
    statusItem('狀態', d.status, '狀態 = 最新值對門檻即時判定:< 警告 → 良好、≥ 警告 → 異常、≥ 危險 → 危險(取最新一筆,與下方區間無關)');
    item('最新值', fmt(d.latestValue) + ' ' + d.unit, null, '設備最新一筆量測值(設備當前狀態,與下方選取區間無關)');
    item('警告門檻', d.warningThreshold + ' ' + d.unit);
    item('危險門檻', d.dangerThreshold + ' ' + d.unit);

    // 統計依「選取區間」在前端重算(切 7天/30天/自訂會跟著變),與上方圖表看同一段資料
    var ms = filterByRange(d.measurements, rangeKey, d);
    var s = computeStats(ms, d.warningThreshold);
    if (s) {
      renderTrend(s.slopePerDay, d.unit, $r);          // 趨勢方向金句(置前,最吸睛)
      item('平均', fmt(s.average) + ' ' + d.unit, null, '選取區間內(' + s.count + ' 筆)所有量測值的算術平均');
      item('最大', fmt(s.max) + ' ' + d.unit, null, '選取區間內(' + s.count + ' 筆)的最大值');
      item('標準差', fmt(s.stdDev), null, '選取區間內的母體標準差 √(Σ(值 − 平均)² ÷ ' + s.count + ');越小代表越穩定');
      item('超標', s.exceedWarning + ' / ' + s.count + ' 筆', null, '超標 = 選取區間內達警告門檻(含 ≥)以上的筆數 ÷ 區間總筆數');
    } else {
      item('此區間', '無量測', null, '選取的自訂期間內沒有任何量測點,請放寬起迄日期');
    }
  }

  // 趨勢方向:由每日斜率判定 惡化/改善/平穩,給對應顏色與箭頭(報告金句來源)
  function renderTrend(slopePerDay, unit, $r) {
    var slope = Number(slopePerDay) || 0;
    var abs = Math.abs(slope);
    var text, cls;
    if (abs < 0.005) { text = '→ 平穩'; cls = 'good'; }
    else if (slope > 0) { text = '↗ 每日 +' + fmt(slope) + ' ' + unit + ',惡化中'; cls = 'danger'; }
    else { text = '↘ 每日 ' + fmt(slope) + ' ' + unit + ',改善中'; cls = 'good'; }
    var $s = $('<span class="ro ro--trend"></span>').addClass('ro--' + cls)
      .attr('data-tooltip', '趨勢 = 選取區間內的量測做最小平方法線性回歸斜率(每日變化量);正 = 惡化、負 = 改善、|斜率| < 0.005 = 平穩(死區,濾雜訊)');
    $('<i></i>').text('趨勢 ').appendTo($s);
    $('<b></b>').text(text).appendTo($s);
    $s.appendTo($r);
  }

  function syncSeg() {
    $('#trend-modal .seg__btn').each(function () {
      var active = $(this).attr('data-range') === range;
      $(this).toggleClass('is-active', active).attr('aria-pressed', active ? 'true' : 'false');
    });
  }

  // 切到/重畫自訂模式:把日期框 min/max 夾到資料跨度、value 同步到目前(夾範圍後的)選取。
  // 也回寫 customFrom/customTo → 換台後若超出新設備跨度會被夾正,輸入框與圖一致。
  function syncCustomInputs(detail) {
    var span = dataSpan(detail);
    if (!span) return;
    var r = effectiveCustom(span);
    customFrom = r.from; customTo = r.to;
    var lo = dateInputVal(span.min), hi = dateInputVal(span.max);
    $('#trend-from').attr({ min: lo, max: hi }).val(dateInputVal(r.from));
    $('#trend-to').attr({ min: lo, max: hi }).val(dateInputVal(r.to));
  }

  function renderChart() {
    if (!current) return;
    renderReadout(current, range);   // 讀數頭統計隨區間重算(與圖看同一段資料);不需等 echarts
    if (!window.echarts) return;     // 圖表本身要等 echarts 載入
    var isCustom = range === 'custom';
    $('#trend-custom').prop('hidden', !isCustom);   // 自訂期間列僅自訂模式顯示
    if (isCustom) syncCustomInputs(current);
    if (!chart) {
      chart = window.echarts.init(document.getElementById('trend-chart'));
      // 雙擊圖面 → 還原 Y 軸縮放(兩個 dataZoom 一起回到 0~100% 全覽)
      chart.getZr().on('dblclick', function () { chart.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); });
      $(window).on('resize.trend', onResize);
    }
    chart.setOption(buildOption(current, range), true); // notMerge:換台/換區間整份重設
    chart.resize();
  }

  /* ── 左右切換(上一台 / 下一台) ──────────────────────────────────────
     順序與位置由看板傳入的 navIds 決定;循環切換(到頭接到尾)。
     切換 = 設定 AppState.selectedId → 看板的 appState:change 會再呼叫 open(該台, 新清單),
     與「點卡片」走同一條路徑,連動卡片選取高亮不必另寫一套。 */
  function renderNav(id) {
    var i = navIds.indexOf(id), n = navIds.length, has = n > 1 && i !== -1;
    $('#trend-prev, #trend-next').prop('hidden', !has);
    var $pos = $('#trend-pos').prop('hidden', !has);
    if (has) $pos.text((i + 1) + ' / ' + n);   // 位置指示:第幾台 / 共幾台
  }
  function go(delta) {
    var i = navIds.indexOf(pendingId), n = navIds.length;
    if (i === -1 || n < 2) return;
    AppState.set('selectedId', navIds[(i + delta + n) % n]);   // 循環;觸發看板重新 open 該台
  }

  /* ── 開 / 關 Modal ───────────────────────────────────────────────── */
  function showModal() {
    $modal().prop('hidden', false);
    $('body').addClass('modal-open');
  }
  function hideModal() {
    $modal().prop('hidden', true);
    $('body').removeClass('modal-open');
  }

  // id = 要顯示的設備;ids = 可左右切換的順序(看板依當前篩選+排序傳入,缺省則不顯示左右鍵)。
  function open(id, ids) {
    var fresh = !isOpen();                  // 首次開啟 vs 已開著的左右切換
    if (fresh) {
      lastFocus = document.activeElement;   // 只在首次記錄觸發點(切換時不覆蓋 → 關閉才好還原焦點)
      range = '30d';                        // 區間僅首次重置為「月」;左右切換沿用目前 7天/30天/全部/自訂 選擇
      customFrom = null; customTo = null;   // 自訂端點重置,改由資料跨度推預設(近 30 天)
      window.setTimeout(function () { $('#trend-modal .modal__close').trigger('focus'); }, 0);
    }
    navIds = Array.isArray(ids) ? ids : [];
    pendingId = id;                         // 供「重試」/左右切換定位目前台
    showModal();
    hideError();
    $('#trend-chart').prop('hidden', false);
    $('#trend-loading').prop('hidden', false);
    $('#trend-title').text('載入中…');
    $('#trend-readout').empty();
    syncSeg();
    renderNav(id);                          // 更新左右鍵顯示 + 位置指示

    // 同時抓明細快照 + 懶載入 echarts;兩者都好才畫
    var reqId = id;                         // 防競態:左右快速切換時,丟棄較早才回來的過時回應
    $.when(Api.get('/api/machines/' + encodeURIComponent(id)), loadECharts())
      .done(function (apiRes) {
        if (pendingId !== reqId) return;    // 已切到別台 → 這份回應作廢,避免畫面跳回舊台
        current = apiRes[0];               // $.when 包裝 ajax → [data, status, xhr]
        $('#trend-loading').prop('hidden', true);
        renderChart();                     // renderChart 內會 renderReadout(current, range)
      })
      .fail(function () {
        if (pendingId !== reqId) return;
        $('#trend-loading').prop('hidden', true);
        showError(describeTrendFailure(arguments));
      });
  }

  /* ── 載入失敗:在 Modal 內顯示置中 X + 原因 + 錯誤碼/traceId + 重試 ────────
     原本只靠 api.js 的全域 toast(右下,4 秒消失),Modal 中央卻一片空白;
     此處補上區塊錯誤狀態,與看板的 renderError 同一套視覺語言。 */
  function describeTrendFailure(failArgs) {
    var jqXHR = failArgs && failArgs[0];
    // 趨勢圖元件(echarts)載入失敗:reject 無 jqXHR(無 .status)→ 專屬碼;其餘走共用推導
    if (!jqXHR || typeof jqXHR.status === 'undefined') {
      return { code: 'CHART_LOAD_ERROR', reason: '趨勢圖元件載入失敗,請重新整理頁面後再試。' };
    }
    return Shared.describeFailure(jqXHR, failArgs[2]);
  }

  function showError(info) {
    info = info || {};
    $('#trend-chart').prop('hidden', true);          // 讓出空白的圖區
    $('#trend-title').text('趨勢資料載入失敗');
    $('#trend-readout').empty();
    // 外部字串一律 .text() 防 XSS
    $('#trend-error-desc').text(info.reason || '無法取得趨勢資料,請稍後再試一次。');
    var $meta = $('#trend-error-meta').empty();
    if (info.code) {
      $('<span></span>').text('錯誤代碼 ').appendTo($meta);
      $('<code></code>').text(info.code).appendTo($meta);
      if (info.traceId) {
        $('<span></span>').text(' · traceId ').appendTo($meta);
        $('<code></code>').text(info.traceId).appendTo($meta);
      }
      $meta.prop('hidden', false);
    } else {
      $meta.prop('hidden', true);
    }
    $('#trend-error').prop('hidden', false);
    window.setTimeout(function () { $('#trend-retry').trigger('focus'); }, 0);
  }

  function hideError() { $('#trend-error').prop('hidden', true); }

  function close() {
    if (!isOpen()) return;
    hideModal();
    hideError();                                  // 重置錯誤狀態,下次開圖乾淨
    $('#trend-custom').prop('hidden', true);      // 收起自訂期間列(下次開圖預設「月」)
    if (chart) { chart.dispose(); chart = null; } // 關閉才 dispose,釋放畫布
    $(window).off('resize.trend');
    current = null;
    pendingId = null;
    if (lastFocus && lastFocus.focus) lastFocus.focus();
    $(document).trigger('trend:closed'); // 通知看板清除卡片選取高亮(選中↔Modal 連動)
  }

  // Phase 5 即時更新:Modal 開著且是同一台時 —— 先用推播摘要即時刷新讀數頭(不等網路),
  // 再背景重抓完整明細並重畫趨勢圖(改 JSON 任一天的值,圖也會即時跟著變)。
  function onMachineUpdated(dto) {
    if (!isOpen() || !current || !dto || dto.id !== current.id) return;
    current.latestValue = dto.latestValue;   // 摘要先補上,讀數頭立即反應
    current.status = dto.status;
    renderReadout(current, range);
    refreshDetail(current.id);               // 再抓完整 measurements 重畫圖
  }

  // 背景重抓「目前開著這台」的完整明細並重畫(沿用目前區間/自訂端點,不顯示載入態、不重置)。
  // 推播 payload 只是「摘要 + 變更信號」,完整 measurements 仍需向明細端點拉 → 推播=信號、明細=按需。
  function refreshDetail(id) {
    var reqId = id;
    Api.get('/api/machines/' + encodeURIComponent(id))
      .done(function (detail) {
        // 防競態/過期:期間若已關閉、切到別台,或回應已非當前台 → 丟棄這份回應
        if (!isOpen() || pendingId !== reqId || !current || current.id !== reqId) return;
        current = detail;
        renderChart();   // 重畫圖 + 讀數頭(notMerge 全量重設;Y 軸縮放會還原,屬可接受成本)
      });
    // 失敗安靜略過:讀數頭已先用推播摘要更新,圖維持舊快照,等下次推播或手動重開
  }

  // 即時更新:正在看的這台被移除(刪檔)→ 關閉 Modal(資料已不存在,留著只會顯示過時快照)
  function onMachineRemoved(id) {
    if (isOpen() && pendingId === id) {
      close();
      if (window.Toast) Toast.show('warning', '此設備已被移除');
    }
  }

  function init() {
    var $m = $modal();
    if (!$m.length) return;
    $m.on('click', '[data-close]', function () { close(); });                 // 背景 / ✕
    $m.on('click', '.seg__btn', function () {                                 // 區間切換(7天/30天/全部/自訂)
      range = $(this).attr('data-range'); syncSeg(); renderChart();
    });
    // 自訂期間:改起/迄日期即重畫(端點補成當日起 00:00 / 當日迄 23:59,涵蓋整天)
    $m.on('change', '#trend-from', function () {
      var t = parseDateInput($(this).val());
      if (t != null) customFrom = startOfDay(t);
      renderChart();
    });
    $m.on('change', '#trend-to', function () {
      var t = parseDateInput($(this).val());
      if (t != null) customTo = endOfDay(t);
      renderChart();
    });
    $m.on('click', '#trend-retry', function () { if (pendingId) open(pendingId, navIds); }); // 載入失敗 → 重試
    $m.on('click', '#trend-prev', function () { go(-1); });                   // 上一台
    $m.on('click', '#trend-next', function () { go(1); });                    // 下一台

    $(document).on('keydown.trend', function (e) {
      if (!isOpen()) return;
      if (e.key === 'Escape') { close(); }                                    // Esc 關閉
      else if (e.key === 'ArrowLeft') { e.preventDefault(); go(-1); }         // ← 上一台
      else if (e.key === 'ArrowRight') { e.preventDefault(); go(1); }         // → 下一台
    });

    // focus trap:Tab / Shift+Tab 環繞鎖在對話框內,兌現 aria-modal 承諾(否則焦點會跑到背景看不見的元素)
    $m.on('keydown', function (e) {
      if (e.key !== 'Tab' || !isOpen()) return;
      var f = $m.find('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')
        .filter(':visible').get();
      if (!f.length) return;
      var first = f[0], last = f[f.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });
  }

  window.TrendChart = { open: open, close: close, onMachineUpdated: onMachineUpdated, onMachineRemoved: onMachineRemoved, init: init };
  $(function () { init(); });
})(window, jQuery);
