/* ===========================================================================
   panels.js — 總覽 BI 三面板:KPI 彙總/狀態分布(Kpi)、風險排行(RiskRanking)、
   告警事件(Alerts)。皆對 AppState 全量即時 rollup,不受清單篩選/分頁影響。
   相依:Icons(失敗提示)、Sparkline(健康率走勢)、Shared(狀態/格式化)。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var statusKey = Shared.statusKey, statusLabel = Shared.statusLabel, statusRank = Shared.statusRank;

  /* ── KPI 彙總 + 狀態分布 ───────────────────────────────────────────────
     純彙總在前端對已載入小資料集即時 rollup(docs/07 §8),維持 SignalR delta
     下不打 API 也能即時更新健康率/分布/排行。需門檻的指標(嚴重度)由後端算好。 */
  var Kpi = (function () {
    var MAXH = 30;          // 健康率走勢取樣上限(滑動視窗)
    var healthHist = [];    // 每次彙總更新(載入/推播/移除)累積一點;不受篩選分頁影響
    var seeded = false;     // 是否已用既有量測回推過初始歷史(只做一次)
    function pct(n, total) { return total ? Math.round((n / total) * 100) : 0; }
    function $bar() { return $('#dist-bar'); }
    function clearDistStates() {
      $bar().removeClass('is-loading');
      $('.panel--dist').removeClass('is-error');   // 還原長條 + 圖例顯示
      $('.panel--dist .panel__note').remove();
    }

    // 設定單一長條段:寬度 + 提示 + 可點擊篩選的 a11y(占比為 0 的段不進 tab 序、對讀報器隱藏)
    function setSeg(id, label, count, total) {
      var p = pct(count, total);
      var $s = $(id).css('width', p + '%')
        .attr('data-tooltip', label + ' ' + count + ' 台(' + p + '%)')
        .attr('aria-label', '篩選' + label + '設備:' + count + ' 台,占 ' + p + '%');
      if (count > 0) $s.attr('tabindex', '0').removeAttr('aria-hidden');
      else $s.attr('tabindex', '-1').attr('aria-hidden', 'true');
    }

    // 健康率走勢:畫 sparkline + 「較前次」變化箭頭(改善=綠↑、惡化=紅↓、持平=灰→)
    function renderTrend() {
      var $t = $('#dist-trend');
      if (!$t.length) return;
      $('#dist-trend-n').text(healthHist.length);
      $('#dist-spark').html(Sparkline.buildHealth(healthHist));
      var now = healthHist[healthHist.length - 1];
      var $d = $('#dist-delta');
      if (healthHist.length < 2) {
        $d.attr('class', 'dist__delta dist__delta--none').text('—');
      } else {
        var diff = now - healthHist[healthHist.length - 2];
        if (diff > 0) $d.attr('class', 'dist__delta dist__delta--up').text('↑ +' + diff + '%');
        else if (diff < 0) $d.attr('class', 'dist__delta dist__delta--down').text('↓ −' + Math.abs(diff) + '%');
        else $d.attr('class', 'dist__delta dist__delta--flat').text('→ 持平');
      }
      $t.attr('aria-label', '健康率走勢:目前 ' + now + '%,近 ' + healthHist.length + ' 次取樣')
        .prop('hidden', false);
    }

    // 初次載入:分布長條 shimmer(段落退出 tab 序;走勢維持隱藏直到有資料)
    function setLoading() {
      clearDistStates();
      $bar().addClass('is-loading').attr('aria-label', '狀態分布載入中');
      $('#dist-good,#dist-abnormal,#dist-danger').css('width', '0%')
        .attr('tabindex', '-1').attr('aria-hidden', 'true');
    }
    // 載入失敗:藏掉長條 + 圖例 + 走勢,只留失敗提示(Icons.ERROR 為靜態 SVG 常數,無 XSS 風險)
    function setError() {
      clearDistStates();
      $('.panel--dist').addClass('is-error');      // 由 CSS 藏掉長條、圖例與走勢
      $('#dist-trend').prop('hidden', true);
      $bar().attr('aria-label', '狀態分布載入失敗');
      var $note = $('<p class="panel__note"></p>');
      $(Icons.ERROR).appendTo($note);
      $('<span></span>').text('無法載入分布').appendTo($note);
      $('.panel--dist').append($note);
    }

    function update(machines) {
      clearDistStates();   // 解除載入/失敗態
      var c = { Good: 0, Abnormal: 0, Danger: 0 };
      machines.forEach(function (m) { if (c[m.status] !== undefined) c[m.status]++; });
      var total = machines.length;

      $('#kpi-total').text(total);
      $('#kpi-good').text(c.Good);
      $('#kpi-abnormal').text(c.Abnormal);
      $('#kpi-danger').text(c.Danger);
      $('#kpi-health').text(total ? pct(c.Good, total) + '%' : '—');

      // 危險/異常 > 0 時低調強調該 KPI 格(餘光可辨「今天要不要緊張」)
      $('.kpi__item--danger').toggleClass('is-active', c.Danger > 0);
      $('.kpi__item--abnormal').toggleClass('is-active', c.Abnormal > 0);

      // 狀態分布堆疊長條:依占比設定三段寬度 + 可點擊篩選的 a11y + 無障礙標題
      setSeg('#dist-good', '良好', c.Good, total);
      setSeg('#dist-abnormal', '異常', c.Abnormal, total);
      setSeg('#dist-danger', '危險', c.Danger, total);
      $('#dist-bar').attr('aria-label',
        '狀態分布:良好 ' + c.Good + '、異常 ' + c.Abnormal + '、危險 ' + c.Danger + ' 台');

      // 健康率走勢(全廠口徑,不受篩選/分頁影響):
      //  · 首次有資料 → 用既有量測回推近 W 期歷史,讓走勢一載入就有方向(最右點對齊目前 KPI 健康率)
      //  · 之後每次彙總更新(推播/刷新/移除)→ 追加一點,滑動視窗
      if (total > 0) {
        var health = pct(c.Good, total);
        if (!seeded) {
          var bf = Sparkline.backfillHealth(machines, MAXH);
          healthHist = bf.length ? bf : [health];
          healthHist[healthHist.length - 1] = health;   // 最右點 = 目前快照,與 KPI 一致
          seeded = true;
        } else {
          healthHist.push(health);
          if (healthHist.length > MAXH) healthHist.shift();
        }
        renderTrend();
      }
    }
    return { update: update, setLoading: setLoading, setError: setError };
  })();

  /* ── 風險排行 Top N(依後端算的嚴重度 = 值÷危險門檻 降序) ───────────────
     嚴重度是 docs/07 §2 的跨機共同尺規;前端只排序/呈現已載入的小資料集。 */
  var RiskRanking = (function () {
    var TOP_N = 5;

    function list() { return $('#risk-list'); }

    // 初次載入:skeleton 骨架行
    function renderLoading() {
      var $l = list();
      if (!$l.length) return;
      $l.empty();
      for (var i = 0; i < 4; i++) $('<li class="risk-skeleton skeleton"></li>').appendTo($l);
    }
    // 載入失敗:精簡失敗提示行
    function renderError() {
      var $l = list();
      if (!$l.length) return;
      $l.empty();
      var $li = $('<li class="list-error"></li>');
      $(Icons.ERROR).appendTo($li);
      $('<span></span>').text('風險排行載入失敗').appendTo($li);
      $li.appendTo($l);
    }

    function update(machines) {
      var $l = list();
      if (!$l.length) return;
      var ranked = (machines || [])
        .filter(function (m) { return m.severity != null; })
        .sort(function (a, b) { return b.severity - a.severity; })
        .slice(0, TOP_N);

      $l.empty();
      if (!ranked.length) {
        $('<li class="risk-empty"></li>').text('尚無可排行的設備').appendTo($l);
        return;
      }
      var frag = document.createDocumentFragment();
      ranked.forEach(function (m, i) {
        // 全程 .text()/.attr():設備名為外部字串,絕不進 innerHTML(防 XSS)
        var $li = $('<li class="risk-item"></li>')
          .addClass('risk-item--' + statusKey(m.status))
          .attr('data-id', m.id)
          .attr('tabindex', '0')
          .attr('role', 'button')
          .attr('aria-label', '第 ' + (i + 1) + ' 名 ' + m.name + ',嚴重度 ' + Math.round(m.severity * 100) + '%');
        $('<span class="risk-item__rank"></span>').text(i + 1).appendTo($li);
        $('<span class="risk-item__dot" aria-hidden="true"></span>').appendTo($li);
        $('<b class="risk-item__name"></b>').text(m.name).appendTo($li);
        // 嚴重度以「距危險」百分比呈現(≥100% = 已達/超過危險門檻);tooltip 解釋演算法
        $('<span class="risk-item__sev"></span>')
          .text(Math.round(m.severity * 100) + '%')
          .attr('data-tooltip', '嚴重度 = 最新值 ÷ 危險門檻;100% = 剛好到危險線,>100% 已超過')
          .appendTo($li);
        frag.appendChild($li[0]);
      });
      $l[0].appendChild(frag);
    }
    return { update: update, renderLoading: renderLoading, renderError: renderError };
  })();

  /* ── 告警事件清單(由 Phase 5 即時推播累積) ─────────────────────────
     狀態發生轉移即新增一筆(升級/恢復),最新在上、上限 MAX 筆。
     資料來源 = SignalR 推來的單台 delta(見 Dashboard.applyUpdate)。 */
  var Alerts = (function () {
    var MAX = 50;
    var items = [];

    function list() { return $('#alert-list'); }

    // prevStatus 為推播前該台狀態;首次出現(undefined)或無變化都不記,避免洗版
    function record(prevStatus, dto) {
      if (prevStatus == null || prevStatus === dto.status) return;
      items.unshift({
        id: dto.id, name: dto.name, status: dto.status,
        worsened: statusRank(dto.status) > statusRank(prevStatus),
        time: new Date()
      });
      if (items.length > MAX) items.length = MAX;
      render();
    }

    function clear() { items = []; render(); }

    // 初次載入:skeleton 骨架行(告警雖走即時推播,初載仍給一致的載入回饋)
    function renderLoading() {
      var $l = list();
      if (!$l.length) return;
      $('#alert-count').text('—');
      $l.empty();
      for (var i = 0; i < 3; i++) $('<li class="alert-skeleton skeleton"></li>').appendTo($l);
    }
    // 載入失敗:精簡失敗提示行
    function renderError() {
      var $l = list();
      if (!$l.length) return;
      $('#alert-count').text('—');
      $l.empty();
      var $li = $('<li class="list-error"></li>');
      $(Icons.ERROR).appendTo($li);
      $('<span></span>').text('無法載入告警事件').appendTo($li);
      $li.appendTo($l);
    }

    function render() {
      var $l = list();
      if (!$l.length) return;
      $('#alert-count').text(items.length);
      $l.empty();
      if (!items.length) {
        $('<li class="alert-empty"></li>').text('尚無告警事件').appendTo($l);
        return;
      }
      var frag = document.createDocumentFragment();
      items.forEach(function (it) {
        // 全程 .text()/.attr():設備名為外部字串,絕不進 innerHTML(防 XSS)
        var $li = $('<li class="alert-item"></li>')
          .addClass('alert-item--' + statusKey(it.status))
          .toggleClass('alert-item--up', it.worsened)
          .attr('data-id', it.id);
        $('<span class="alert-item__dot" aria-hidden="true"></span>').appendTo($li);
        var $body = $('<div class="alert-item__body"></div>').appendTo($li);
        $('<b class="alert-item__name"></b>').text(it.name).appendTo($body);
        $('<span class="alert-item__msg"></span>')
          .text((it.worsened ? '升為 ' : '恢復為 ') + statusLabel(it.status)).appendTo($body);
        $('<time class="alert-item__time"></time>').text(it.time.toLocaleTimeString()).appendTo($body);
        frag.appendChild($li[0]);
      });
      $l[0].appendChild(frag);
    }

    return { record: record, clear: clear, render: render, renderLoading: renderLoading, renderError: renderError };
  })();

  window.Kpi = Kpi;
  window.RiskRanking = RiskRanking;
  window.Alerts = Alerts;
})(window, jQuery);
