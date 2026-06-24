/* ===========================================================================
   shared.js — 跨模組共用的純邏輯(狀態判定 / 格式化 / 節流去抖 / 錯誤推導)
   由 dashboard.js 與 trendChart.js 共用,避免同一套邏輯在多檔複製(見 rules/前端/01)。
   注意:evalStatus(門檻判定)是「前端鏡像後端 StatusEvaluator」的唯一一份,
         與後端 Common/Helpers/StatusEvaluator.cs 同規則(≥危險→Danger、≥警告→Abnormal);
         任一邊調整門檻語意,只需改這裡與後端各一處。
   =========================================================================== */
(function (window) {
  'use strict';

  // 狀態列舉 → 中文標籤(對應後端 MachineStatus)
  var STATUS_LABEL = { Good: '良好', Abnormal: '異常', Danger: '危險' };
  // 嚴重度排名:與後端 OrderByDescending(Status) 一致(危險>異常>良好)
  var STATUS_RANK = { Danger: 2, Abnormal: 1, Good: 0 };

  function statusKey(s) { return String(s || '').toLowerCase(); }
  function statusLabel(s) { return STATUS_LABEL[s] || s; }
  function statusRank(s) { return STATUS_RANK[s] != null ? STATUS_RANK[s] : -1; }

  // 數值顯示:絕對值 <1 用 3 位小數(看得出細微變化),否則 2 位
  function formatValue(v) {
    if (v === null || v === undefined) return '—';
    var n = Number(v);
    return Math.abs(n) < 1 ? n.toFixed(3) : n.toFixed(2);
  }

  // 前端鏡像後端 StatusEvaluator(門檻判定):≥危險→Danger、≥警告→Abnormal、其餘→Good。
  // 前端唯一一份;dashboard 的健康率回推、trendChart 的分區上色都走這支。
  function evalStatus(v, warn, danger) {
    if (v >= danger) return 'Danger';
    if (v >= warn) return 'Abnormal';
    return 'Good';
  }

  // 節流:高頻事件(resize 等)限制每 ms 最多執行一次,首觸即發、尾段補一次
  function throttle(fn, ms) {
    var last = 0, timer = null;
    return function () {
      var now = Date.now(), wait = ms - (now - last);
      if (wait <= 0) { last = now; fn(); }
      else if (!timer) { timer = setTimeout(function () { last = Date.now(); timer = null; fn(); }, wait); }
    };
  }

  // 去抖:連續觸發(輸入等)只在停頓 ms 後執行最後一次,並沿用最後一次的引數
  function debounce(fn, ms) {
    var timer = null;
    return function () {
      var ctx = this, args = arguments;
      clearTimeout(timer);
      timer = setTimeout(function () { fn.apply(ctx, args); }, ms);
    };
  }

  // 由 AJAX 失敗推導 { code, reason, traceId }:網路/逾時給語意碼,其餘解析後端 ProblemDetails。
  // 看板整頁載入失敗、趨勢圖載入失敗共用同一套推導(parseProblemDetails 來自 api.js)。
  function describeFailure(jqXHR, thrownError) {
    var status = jqXHR ? jqXHR.status : 0;
    if (status === 0) return { code: 'NETWORK_ERROR', reason: '網路連線異常,無法連到伺服器,請確認連線。' };
    if (thrownError === 'timeout') return { code: 'TIMEOUT', reason: '連線逾時,請稍後再試。' };
    var p = window.parseProblemDetails ? window.parseProblemDetails(jqXHR) : null;
    return {
      code: (p && p.errorCode) || ('HTTP_' + status),
      reason: (p && (p.detail || p.title)) || ('伺服器回應錯誤(HTTP ' + status + ')。'),
      traceId: p && p.traceId
    };
  }

  window.Shared = {
    STATUS_LABEL: STATUS_LABEL,
    STATUS_RANK: STATUS_RANK,
    statusKey: statusKey,
    statusLabel: statusLabel,
    statusRank: statusRank,
    formatValue: formatValue,
    evalStatus: evalStatus,
    throttle: throttle,
    debounce: debounce,
    describeFailure: describeFailure
  };
})(window);
