/* ===========================================================================
   dashboard.js — 看板容器模組(串接:抓資料 → AppState → 各渲染模組)。
   職責:清單查詢(listQuery)+ 工具列/分頁(Controls)+ 載入/刷新/即時推播的
         唯一重繪入口(renderView)。實際渲染交給拆分出的子模組:
         machineCard.js(卡片)、panels.js(KPI/風險/告警)、sparkline.js / icons.js。
   安全:注入外部字串一律 .text()/.attr()(見各子模組)。
   =========================================================================== */
(function (window, $) {
  'use strict';

  // 狀態排序 / 錯誤推導走共用 shared.js;渲染模組由各自檔案掛上 window
  var statusRank = Shared.statusRank, describeFailure = Shared.describeFailure;
  var MachineRenderer = window.MachineRenderer, Kpi = window.Kpi, RiskRanking = window.RiskRanking, Alerts = window.Alerts;

  // 搜尋去抖:避免每個按鍵都重算/重繪當前頁(見 rules/前端/04 效能)
  var SEARCH_DEBOUNCE_MS = 200;

  /* ── 檢視狀態 + listQuery 接縫 ────────────────────────────────────────────
     view 是「顯示層」狀態,獨立於 AppState.machines(全量真相)。
     listQuery 是唯一的「清單查詢」入口:今天對已載入全量做篩選/排序/分頁;
     未來機器量大時改打 GET /api/machines?search=&status=&page=&pageSize=,
     回傳同形狀 { items, total, totalPages, page },上層(renderView)不必改。
     KPI/風險排行/告警一律另對 AppState 全量算,不受此處篩選分頁影響(見 docs/07 §5.3)。 */
  var DEFAULT_PAGE_SIZE = 12;
  var view = { search: '', status: 'All', pageSize: DEFAULT_PAGE_SIZE, page: 1, sort: 'severity', sortDir: 'desc' };

  // 篩選 + 排序(危險優先、同級 id 升序)。listQuery 分頁與「明細 Modal 左右切換」共用同一份順序,
  // 使用者看到什麼順序、左右切換就照什麼順序(見 navIdsFor)。
  function filterSort(machines, v) {
    var q = (v.search || '').trim().toLowerCase();
    var filtered = (machines || []).filter(function (m) {
      if (v.status !== 'All' && m.status !== v.status) return false;
      if (q) {
        // 名稱 + 編號 + 類型皆可搜(如輸入「泵」即篩出所有泵浦;僅做比對,不進 DOM,無 XSS 風險)
        var hay = (String(m.name || '') + ' ' + String(m.id || '') + ' ' + String(m.type || '')).toLowerCase();
        if (hay.indexOf(q) === -1) return false;
      }
      return true;
    });
    // 排序:嚴重度 / 名稱 / 最新值,各支援升降冪(v.sortDir)。比較式一律以「升冪」為基準,
    // 降冪再乘 -1;以 id 升序做穩定 tiebreak(不隨方向翻轉 → 分頁邊界與 FLIP 重排順序一致)。
    // 無最新值固定排最後(不隨升降冪),避免「—」插在中間。
    var sort = v.sort || 'severity';
    var dir = (v.sortDir === 'asc') ? 1 : -1;
    filtered.sort(function (a, b) {
      var cmp = 0;
      if (sort === 'name') {
        cmp = String(a.name || '').localeCompare(String(b.name || ''), 'zh-Hant');
      } else if (sort === 'value') {
        var an = (a.latestValue == null), bn = (b.latestValue == null);
        if (an !== bn) return an ? 1 : -1;                       // 無值固定最後
        if (!an) cmp = a.latestValue - b.latestValue;            // 升冪:低→高
      } else {
        cmp = statusRank(a.status) - statusRank(b.status);       // 升冪:良好→危險
      }
      if (cmp !== 0) return cmp * dir;
      var ia = String(a.id).toLowerCase(), ib = String(b.id).toLowerCase();
      return ia < ib ? -1 : ia > ib ? 1 : 0;
    });
    return filtered;
  }

  function listQuery(machines, v) {
    var filtered = filterSort(machines, v);
    var total = filtered.length;
    var totalPages = Math.max(1, Math.ceil(total / v.pageSize));
    var page = Math.min(Math.max(1, v.page), totalPages);   // clamp(刪除/篩選後頁碼可能超界)
    var start = (page - 1) * v.pageSize;
    return { items: filtered.slice(start, start + v.pageSize), total: total, totalPages: totalPages, page: page };
  }

  // 明細 Modal「左右切換」用的設備順序 = 目前篩選+排序的全部(跨分頁),讓切換沿著使用者正在瀏覽的清單走。
  // 若該台被目前篩選排除(從風險排行/告警點進來,不在篩選結果內)→ 退回全廠排序,確保仍能左右切換。
  function navIdsFor(id) {
    var all = AppState.get('machines') || [];
    var ids = filterSort(all, view).map(function (m) { return m.id; });
    if (ids.indexOf(id) !== -1) return ids;
    return filterSort(all, { search: '', status: 'All' }).map(function (m) { return m.id; });
  }

  // 相對時間「N 秒前」:stampUpdated 設基準時間,renderUpdated 由 5 秒 ticker 重算顯示。
  // title 仍掛絕對時間(滑上去看得到精確時刻)。
  var lastUpdatedAt = null;
  function relativeTime(ts) {
    var s = Math.max(0, Math.round((Date.now() - ts) / 1000));
    if (s < 5) return '剛剛';
    if (s < 60) return s + ' 秒前';
    var m = Math.floor(s / 60);
    if (m < 60) return m + ' 分前';
    return Math.floor(m / 60) + ' 小時前';
  }
  function renderUpdated() {
    if (!lastUpdatedAt) return;
    $('#last-updated')
      .text('資料更新:' + relativeTime(lastUpdatedAt))
      .attr('title', new Date(lastUpdatedAt).toLocaleString());
  }
  function stampUpdated() {
    lastUpdatedAt = Date.now();
    renderUpdated();
  }

  // 重試所有「載入失敗」的設備圖檔(手動刷新時呼叫)。
  // 圖檔 src 只在建卡時設定一次(patch 差異更新不碰圖),所以一旦首次載入失敗
  // (例:被 DevTools 攔截、暫時斷線),App 內刷新預設不會重抓——這裡在使用者按
  // 「重新整理」時,對失敗的圖重設 src(加 cache-bust 參數)強制重抓,免得非整頁重載救不回。
  function retryBrokenImages() {
    $('#grid .card__img').each(function () {
      var el = this;
      // complete && naturalWidth===0 = 載入已結束但失敗;仍在載入中(complete=false)則略過,不打斷
      if (!(el.complete && el.naturalWidth === 0)) return;
      var base = el.src.split('?')[0];
      el.src = base + '?retry=' + Date.now();   // 新 query → 瀏覽器當成新資源重抓,繞過失敗狀態
    });
  }

  // 把單台 delta 併入清單(替換或新增),維持 AppState 與畫面一致
  function mergeMachine(list, dto) {
    var replaced = false;
    var next = (list || []).map(function (m) {
      if (m.id === dto.id) { replaced = true; return dto; }
      return m;
    });
    if (!replaced) next.push(dto);
    return next;
  }

  /* ── 工具列 + 分頁控制(只改 view 狀態,實際重繪交給 onChange = renderView) ──
     UI 與資料解耦:本模組只負責讀使用者輸入 → 寫 view → 通知重繪;
     計數/分頁列的可見性與停用態則由 sync() 依 listQuery 結果回填。 */
  var Controls = (function () {
    var onChange = function () {};

    function init(changeHandler) {
      onChange = changeHandler || onChange;

      // 搜尋(去抖):輸入停頓後才重算,改動條件一律回到第 1 頁(共用 Shared.debounce)
      var onSearch = Shared.debounce(function (val) {
        view.search = val; view.page = 1; onChange();
      }, SEARCH_DEBOUNCE_MS);
      $('#filter-q').on('input', function () { onSearch(this.value); });

      // 狀態篩選(seg 按鈕):點同一顆不重繪
      $('#filter-status').on('click', '.seg__btn', function () {
        var $b = $(this);
        if ($b.hasClass('is-active')) return;
        $b.addClass('is-active').siblings().removeClass('is-active');
        view.status = $b.attr('data-status') || 'All';
        view.page = 1; onChange();
      });

      // 排序欄位(嚴重度 / 名稱 / 最新值)+ 升降冪切換鈕
      function defaultDir(field) { return field === 'name' ? 'asc' : 'desc'; }   // 名稱預設 A→Z;嚴重度/最新值預設高→低
      function syncSortDir() {
        $('#filter-sort-dir')
          .text(view.sortDir === 'asc' ? '↑' : '↓')
          .attr('aria-label', view.sortDir === 'asc' ? '目前升冪,點擊改降冪' : '目前降冪,點擊改升冪');
      }
      $('#filter-sort').on('change', function () {
        view.sort = this.value || 'severity';
        view.sortDir = defaultDir(view.sort);     // 切欄位 → 套該欄位的自然方向
        view.page = 1; syncSortDir(); onChange();
      });
      $('#filter-sort-dir').on('click', function () {
        view.sortDir = (view.sortDir === 'asc') ? 'desc' : 'asc';
        view.page = 1; syncSortDir(); onChange();
      });
      syncSortDir();   // 初始化方向鈕箭頭

      // 每頁筆數
      $('#filter-page-size').on('change', function () {
        var n = parseInt(this.value, 10);
        if (n > 0) { view.pageSize = n; view.page = 1; onChange(); }
      });

      // 分頁(next 不夾上限,交給 listQuery clamp 後由 sync 停用)
      $('#pager-prev').on('click', function () { if (view.page > 1) { view.page--; onChange(); } });
      $('#pager-next').on('click', function () { view.page++; onChange(); });
    }

    // 依 listQuery 結果回填計數與分頁列;totalAll = 全量台數(決定工具列是否顯示)
    function sync(r, totalAll) {
      $('#filterbar').prop('hidden', totalAll === 0);
      $('#filter-count').text(r.total === totalAll ? ('共 ' + totalAll + ' 台') : (r.total + ' / 共 ' + totalAll + ' 台'));

      var showPager = r.total > 0 && r.totalPages > 1;   // 單頁就不顯示分頁列(減少雜訊)
      $('#pager').prop('hidden', !showPager);
      $('#pager-info').text('第 ' + r.page + ' / ' + r.totalPages + ' 頁');
      $('#pager-prev').prop('disabled', r.page <= 1);
      $('#pager-next').prop('disabled', r.page >= r.totalPages);
    }

    // 清除篩選(供「沒有符合條件」時的次要動作)
    function reset() {
      view.search = ''; view.status = 'All'; view.page = 1;
      $('#filter-q').val('');
      $('#filter-status .seg__btn').removeClass('is-active')
        .filter('[data-status="All"]').addClass('is-active');
      onChange();
    }

    return { init: init, sync: sync, reset: reset };
  })();

  /* ── 容器模組:串接抓取 → 狀態 → 渲染 ─────────────────────────────── */
  var Dashboard = (function () {
    var initialized = false;

    // 全量(AppState) → listQuery → 只渲染當前頁;KPI/排行另對全量算(不受篩選分頁影響)。
    // 載入、刷新、即時推播、工具列/分頁操作一律收斂到這個唯一重繪入口。
    function renderView() {
      var all = AppState.get('machines') || [];
      var r = listQuery(all, view);
      view.page = r.page;                           // 回寫 clamp 後頁碼
      MachineRenderer.renderPage(r.items, { hasData: all.length > 0 });
      Controls.sync(r, all.length);
      // KPI 狀態卡 + 分布長條段反映目前篩選(良好/異常/危險;與 #filter-status 同步)
      $('.kpi__item[data-status], .dist__seg[data-status]').removeClass('is-filtering').attr('aria-pressed', 'false');
      if (view.status !== 'All') {
        $('.kpi__item[data-status="' + view.status + '"], .dist__seg[data-status="' + view.status + '"]')
          .addClass('is-filtering').attr('aria-pressed', 'true');
      }
      // 重繪後補回選取高亮(選取的設備若落在當前頁;不在頁則自然無高亮)
      var sel = AppState.get('selectedId');
      if (sel) $('#machine-' + sel).addClass('card--selected');
    }

    // 即時推播:單台更新(新增則自動補卡)
    function applyUpdate(dto) {
      // 推播前狀態取自 AppState 全量(非 DOM)——分頁後該台可能不在當前頁,
      // 但告警是「全廠事件」,升級/恢復判斷不能只看當前頁有沒有那張卡。
      var all = AppState.get('machines') || [];
      var prev = null;
      for (var i = 0; i < all.length; i++) { if (all[i].id === dto.id) { prev = all[i]; break; } }
      var prevStatus = prev ? prev.status : undefined;

      AppState.set('machines', mergeMachine(all, dto));
      renderView();                                 // 重算當前頁(在頁內者由 patch 帶動畫)
      Kpi.update(AppState.get('machines'));
      RiskRanking.update(AppState.get('machines'));
      stampUpdated();
      Alerts.record(prevStatus, dto);               // 狀態轉移累積成告警事件(全廠)
      // 若明細 Modal 正開著這台,連動刷新讀數頭(趨勢圖維持開啟時的快照)
      if (window.TrendChart) TrendChart.onMachineUpdated(dto);
    }

    // 即時推播:設備移除
    function applyRemove(id) {
      AppState.set('machines', (AppState.get('machines') || []).filter(function (m) { return m.id !== id; }));
      renderView();                                 // 移除後重算當前頁(可能補進下一頁的卡)
      Kpi.update(AppState.get('machines'));
      RiskRanking.update(AppState.get('machines'));
      stampUpdated();
      // 若明細 Modal 正開著這台,連動關閉(該設備已不存在,避免顯示過時資料)
      if (window.TrendChart) TrendChart.onMachineRemoved(id);
    }

    function load(isRefresh) {
      // 初次載入:四個區塊(網格 / 分布 / 排行 / 告警)同步顯示載入態。
      // 刷新(isRefresh)不動這些區塊——失敗時保留現有畫面與已累積的告警。
      if (!isRefresh) {
        MachineRenderer.renderLoading();
        Kpi.setLoading();
        RiskRanking.renderLoading();
        Alerts.renderLoading();
      }
      return Api.get('/api/machines')
        .done(function (machines) {
          AppState.set('machines', machines);
          renderView();                             // 首次/刷新統一走 renderView(內部對 DOM diff)
          Kpi.update(machines);
          RiskRanking.update(machines);
          if (!isRefresh) Alerts.render();          // 解除告警載入態 → 顯示既有/「尚無告警事件」
          stampUpdated();
        })
        .fail(function (jqXHR, textStatus, errorThrown) {
          if (!isRefresh) {
            // 整頁載入失敗:四個區塊各自顯示失敗提示(對齊使用者「整頁斷網」情境)
            MachineRenderer.renderError(function () { load(false); }, describeFailure(jqXHR, errorThrown));
            Kpi.setError();
            RiskRanking.renderError();
            Alerts.renderError();
          }
          // 全域 ajaxError 會再跳 Toast;刷新失敗保留現有畫面
        });
    }

    function init() {
      if (initialized) return;
      initialized = true;

      // KPI 良好/異常/危險 → 篩選下方清單(再點同一個 → 取消回全部);複用 #filter-status 的 seg 邏輯
      $('.kpi').on('click', '.kpi__item[data-status]', function () {
        var status = $(this).attr('data-status');
        var target = (view.status === status) ? 'All' : status;   // target 必定 ≠ 目前 → seg 會重繪
        $('#filter-status .seg__btn[data-status="' + target + '"]').trigger('click');
      });
      $('.kpi').on('keydown', '.kpi__item[data-status]', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $(this).trigger('click'); }
      });

      // 狀態分布長條段 → 篩選該狀態(再點同段取消);與 KPI 同邏輯,共用 #filter-status 接縫
      $('#dist-bar').on('click', '.dist__seg[data-status]', function () {
        var status = $(this).attr('data-status');
        var target = (view.status === status) ? 'All' : status;
        $('#filter-status .seg__btn[data-status="' + target + '"]').trigger('click');
      });
      $('#dist-bar').on('keydown', '.dist__seg[data-status]', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $(this).trigger('click'); }
      });

      // 「查看趨勢」按鈕:鍵盤可聚焦 + Enter 觸發的主要進入點;stopPropagation 避免與卡片點擊重複
      $('#grid').on('click', '.card__btn', function (e) {
        e.stopPropagation();
        AppState.set('selectedId', $(this).attr('data-id'));
      });
      // 點卡片其餘區域(滑鼠便利)→ 設定選取(事件驅動,面板/高亮自動跟上)
      $('#grid').on('click', '.card', function () {
        AppState.set('selectedId', $(this).attr('data-id'));
      });

      // 「清除篩選」:篩選無結果時的次要動作(委派綁定,因 .state 為動態插入)
      $('#grid').on('click', '[data-action="reset-filters"]', function () { Controls.reset(); });

      // 工具列(查詢/篩選/排序/每頁筆數)+ 分頁:操作 → 改 view → renderView 重繪
      Controls.init(renderView);

      // 相對時間「N 秒前」每 5 秒重算(基準時間由 stampUpdated 設定)
      window.setInterval(renderUpdated, 5000);

      // 風險排行:點擊 / Enter / Space → 選取該設備(連動高亮 + 開趨勢圖)
      $('#risk-list')
        .on('click', '.risk-item', function () {
          var id = $(this).attr('data-id');
          if (id) AppState.set('selectedId', id);
        })
        .on('keydown', '.risk-item', function (e) {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            var id = $(this).attr('data-id');
            if (id) AppState.set('selectedId', id);
          }
        });

      // 訂閱狀態變更(解耦:此處不直接被卡片模組呼叫)
      $(document).on('appState:change', function (e, p) {
        if (p.key === 'selectedId') {
          $('.card--selected').removeClass('card--selected');
          if (!p.value) return;                 // 取消選取(如關閉 Modal):只清高亮
          $('#machine-' + p.value).addClass('card--selected');
          // 開啟明細趨勢圖 Modal(ECharts 由 trendChart.js 懶載入);一併傳入可左右切換的設備順序
          if (window.TrendChart) TrendChart.open(p.value, navIdsFor(p.value));
        }
      });

      // 選中 ↔ Modal 連動:關閉 Modal 時清掉卡片選取高亮(條件:Phase 6 已就緒)
      $(document).on('trend:closed', function () {
        if (AppState.get('selectedId')) AppState.set('selectedId', null);
      });

      // 點告警事件 → 選取該設備(連動高亮 + 開趨勢圖)
      $('#alert-list').on('click', '.alert-item', function () {
        var id = $(this).attr('data-id');
        if (id) AppState.set('selectedId', id);
      });
      $('#alert-clear').on('click', function () { Alerts.clear(); });

      // 手動刷新:輕量回饋(忙碌態 + 完成 Toast),不打斷畫面。
      // 主更新走 SignalR push;此鈕為斷線/重連時的備援補拉(見 docs/08 §F)。
      $('#btn-refresh').on('click', function () {
        var $btn = $(this);
        if ($btn.prop('disabled')) return;
        // is-loading → icon 旋轉;只改 label 文字(保留 icon)
        $btn.prop('disabled', true).addClass('is-loading')
          .find('.btn-refresh__label').text('更新中…');
        retryBrokenImages();   // 手動刷新一併救回先前載入失敗的圖檔(不必整頁重載)
        var startedAt = Date.now();
        load(true)
          .done(function (machines) {
            if (window.Toast) Toast.show('good', '已更新（' + (machines ? machines.length : 0) + ' 台）');
          })
          .always(function () {
            // 至少顯示 400ms,讓 icon 轉一下看得到(本機刷新可能 <50ms)
            var wait = Math.max(0, 400 - (Date.now() - startedAt));
            setTimeout(function () {
              $btn.prop('disabled', false).removeClass('is-loading')
                .find('.btn-refresh__label').text('重新整理');
            }, wait);
          });
      });

      load(false); // 初次載入:load() 內會先把四區塊設為載入態,done/fail 再解除
    }

    return {
      init: init,
      refresh: function () { load(true); },
      applyUpdate: applyUpdate,
      applyRemove: applyRemove
    };
  })();

  window.Dashboard = Dashboard;
  $(function () { Dashboard.init(); });
})(window, jQuery);
