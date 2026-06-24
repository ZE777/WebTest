/* ===========================================================================
   sparkline.js — 迷你趨勢圖元件(純函式,輸入數值陣列 → 輸出 SVG 字串)。
   內容全為數字,無 XSS 風險。供卡片(Sparkline.build)與健康率走勢
   (Sparkline.buildHealth)共用;backfillHealth 由既有量測回推初始走勢。
   =========================================================================== */
(function (window) {
  'use strict';

  // 門檻判定 / 狀態鍵走共用 shared.js
  var statusKey = Shared.statusKey, evalStatus = Shared.evalStatus;

  // 以數值陣列產生迷你 sparkline SVG(內容全為數字,安全)
  function build(values, status) {
    if (!values || !values.length) return '';
    var w = 100, h = 32, pad = 3, n = values.length;
    var min = Math.min.apply(null, values), max = Math.max.apply(null, values);
    var span = max - min;
    var flat = span < 1e-9;             // 幾乎不變:畫水平中線(語意=穩定),不貼底
    var range = flat ? 1 : span;
    var pts = values.map(function (v, i) {
      var x = n === 1 ? 0 : (i / (n - 1)) * w;
      var y = flat ? h / 2 : h - pad - ((v - min) / range) * (h - pad * 2);
      return x.toFixed(1) + ',' + y.toFixed(1);
    }).join(' ');
    var last = pts.split(' ').pop().split(',');
    // 末點圓點改用 HTML 疊層(非 SVG circle):SVG 以 preserveAspectRatio="none" 橫向拉伸,
    // circle 會被壓成橢圓;改用絕對定位 + border-radius 的方塊,永遠保持正圓(見 .spark-dot)。
    var dotX = (parseFloat(last[0]) / w * 100).toFixed(1);
    var dotY = (parseFloat(last[1]) / h * 100).toFixed(1);
    return '<svg viewBox="0 0 ' + w + ' ' + h + '" preserveAspectRatio="none" aria-hidden="true">' +
      '<polyline points="' + pts + '" />' +
      '</svg>' +
      '<i class="spark-dot spark-dot--' + statusKey(status) + '" style="left:' + dotX + '%;top:' + dotY + '%"></i>';
  }

  // 健康率走勢 sparkline(縱軸固定 0..100:線越高=越健康,不做 min/max 自動縮放以免小波動看起來很大)。
  // 內容全為數字,無 XSS 風險;沿用卡片 sparkline 的容器(div)+ 整段 svg 字串作法。
  function buildHealth(values) {
    if (!values || !values.length) return '';
    var w = 100, h = 26, pad = 3, usable = h - pad * 2;
    function yOf(v) { return pad + (1 - Math.max(0, Math.min(100, v)) / 100) * usable; }
    // 只有一點時:橫拉成一條水平線(語意=持平),不要只剩孤點看不出走勢
    var src = values.length === 1 ? [values[0], values[0]] : values;
    var n = src.length, xs = [], pts = [];
    for (var i = 0; i < n; i++) {
      var x = (i / (n - 1)) * w;
      xs.push(x.toFixed(1)); pts.push(x.toFixed(1) + ',' + yOf(src[i]).toFixed(1));
    }
    var line = pts.join(' ');
    var area = xs[0] + ',' + h + ' ' + line + ' ' + xs[n - 1] + ',' + h;   // 折線兩端落到基線 → 封閉面積
    var lastPt = pts[n - 1].split(',');
    // 末點圓點改用 HTML 疊層(理由同卡片 sparkline):避免 preserveAspectRatio="none" 把圓壓成橢圓。
    var dotX = (parseFloat(lastPt[0]) / w * 100).toFixed(1);
    var dotY = (parseFloat(lastPt[1]) / h * 100).toFixed(1);
    return '<svg viewBox="0 0 ' + w + ' ' + h + '" preserveAspectRatio="none" aria-hidden="true">' +
      '<polygon class="dist__spark-area" points="' + area + '" />' +
      '<polyline class="dist__spark-line" points="' + line + '" />' +
      '</svg>' +
      '<i class="dist__spark-dot" style="left:' + dotX + '%;top:' + dotY + '%"></i>';
  }

  // 用每台既有的量測序列「回推」近 W 期全廠健康率(良好占比%),讓走勢一載入就有歷史方向。
  // 各台 sparkline 長度可能不同(7~10),以最短長度為窗、從最新端對齊(最右=現在)。maxW 為視窗上限。
  function backfillHealth(machines, maxW) {
    var withSpark = (machines || []).filter(function (m) { return m.sparkline && m.sparkline.length; });
    if (!withSpark.length) return [];
    var W = Infinity;
    withSpark.forEach(function (m) { W = Math.min(W, m.sparkline.length); });
    if (!isFinite(W) || W < 1) return [];
    W = Math.min(W, maxW);
    var out = [];
    for (var p = 0; p < W; p++) {                  // p=0 最舊 … p=W-1 最新
      var good = 0;
      withSpark.forEach(function (m) {
        var v = m.sparkline[m.sparkline.length - W + p];   // 從最新端對齊
        if (evalStatus(v, m.warningThreshold, m.dangerThreshold) === 'Good') good++;
      });
      out.push(Math.round((good / withSpark.length) * 100));
    }
    return out;
  }

  window.Sparkline = { build: build, buildHealth: buildHealth, backfillHealth: backfillHealth };
})(window);
