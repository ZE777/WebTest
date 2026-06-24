/* ===========================================================================
   realtime.js — 即時更新連線層(見 docs/04、skills/SKILL_SignalR)
   預設走 SignalR 推播;連不上 / 徹底斷線 → 自動降級為每 5 秒輪詢。
   收到單台 delta 只交給 Dashboard 差異更新該張卡片,不重建整個看板。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var POLL_MS = 5000;

  /* ── 連線狀態指示(KPI 列右側) ───────────────────────────────────── */
  var ConnUi = (function () {
    var MAP = {
      live:         { cls: 'conn--live', text: '即時連線' },
      reconnecting: { cls: 'conn--poll', text: '重新連線中…' },
      polling:      { cls: 'conn--poll', text: '輪詢更新中(每 5 秒)' },
      down:         { cls: 'conn--down', text: '連線中斷' },
      connecting:   { cls: '',           text: '連線中…' }
    };
    function set(state) {
      var s = MAP[state] || MAP.connecting;
      $('#conn-indicator').attr('class', 'conn ' + s.cls);
      $('#conn-text').text(s.text);
    }
    return { set: set };
  })();

  /* ── 輪詢降級:每 5 秒重抓全清單並差異更新 ───────────────────────── */
  var Poll = (function () {
    var timer = null;
    return {
      start: function () {
        if (timer) return;
        ConnUi.set('polling');
        timer = setInterval(function () { Dashboard.refresh(); }, POLL_MS);
      },
      stop: function () { if (timer) { clearInterval(timer); timer = null; } }
    };
  })();

  /* ── SignalR 連線 ─────────────────────────────────────────────────── */
  function start() {
    // 函式庫沒載入(或環境不支援)→ 直接走輪詢降級
    if (!window.signalR) { Poll.start(); return; }

    ConnUi.set('connecting');
    var conn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/machine')
      .withAutomaticReconnect()   // 短暫斷線自動重連
      .build();

    conn.on('MachineUpdated', function (dto) { Dashboard.applyUpdate(dto); }); // 差異更新單張
    conn.on('MachineRemoved', function (id)  { Dashboard.applyRemove(id); });

    conn.onreconnecting(function () { ConnUi.set('reconnecting'); });
    conn.onreconnected(function () {
      ConnUi.set('live');
      Poll.stop();
      Dashboard.refresh(); // 補回斷線期間漏掉的推播
    });
    conn.onclose(function () { ConnUi.set('down'); Poll.start(); }); // 徹底斷線 → 輪詢

    conn.start()
      .then(function () { ConnUi.set('live'); Poll.stop(); })
      .catch(function () { Poll.start(); }); // 連不上(環境不支援 WS)→ 輪詢
  }

  window.Realtime = { start: start };
  $(function () { start(); });
})(window, jQuery);
