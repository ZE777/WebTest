/* ===========================================================================
   machineCard.js — 設備卡片渲染元件(只管畫面,不抓資料)。
   buildCard 為卡片工廠(由 <template> clone 後以 .text()/.attr() 填值,防 XSS);
   renderPage 對現有 DOM 做差異更新(patch/remove/reorder),保留動畫與選取。
   相依:Icons(狀態頁圖示)、Sparkline.build(迷你趨勢)、Shared(狀態/格式化)。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var statusKey = Shared.statusKey, statusLabel = Shared.statusLabel, formatValue = Shared.formatValue;

  // ── 動畫小工具(階段 1) ──────────────────────────────────────────────
  var EASE = 'cubic-bezier(.25,.8,.25,1)';                 // 與 CSS --ease 一致
  var RM = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)');
  function reduced() { return !!(RM && RM.matches); }      // 尊重「減少動態」偏好,與 CSS @media 同步

  // 讀數滾動(count-up):from→to 以 rAF 過渡(easeOutCubic),期間用 formatValue 格式化。
  // reduced-motion / 非數字 / 無變化 → 直接設值;重入時取消前一輪,避免多個 rAF 疊加跳動。
  function animateValue(el, from, to) {
    var fromN = Number(from), toN = Number(to);
    if (el._raf) { cancelAnimationFrame(el._raf); el._raf = null; }
    if (reduced() || from == null || to == null || !isFinite(fromN) || !isFinite(toN) || fromN === toN) {
      el.textContent = formatValue(to); return;
    }
    var dur = 500, start = null;
    function step(ts) {
      if (start === null) start = ts;
      var p = Math.min(1, (ts - start) / dur);
      var k = 1 - Math.pow(1 - p, 3);                       // easeOutCubic
      el.textContent = formatValue(fromN + (toN - fromN) * k);
      if (p < 1) { el._raf = requestAnimationFrame(step); }
      else { el._raf = null; el.textContent = formatValue(to); }
    }
    el._raf = requestAnimationFrame(step);
  }

  var MachineRenderer = (function () {
    function grid() { return $('#grid'); }

    // 走勢圖指紋:sparkline 畫的是「整段序列 + 依狀態著色」,故以「狀態 + 全序列值」為內容指紋。
    // patch 比對此指紋決定要不要重畫(與最新值脫鉤:改任一天歷史值,最新值沒變但走勢圖該更新)。
    function sparkKeyOf(m) { return m.status + '|' + (m.sparkline || []).join(','); }

    function buildCard(m) {
      var tpl = document.getElementById('machine-card-template');
      var card = tpl.content.firstElementChild.cloneNode(true);
      var $c = $(card);

      $c.attr('id', 'machine-' + m.id)
        .attr('data-id', m.id)
        .attr('aria-label', '設備 ' + m.name + ' 振動狀態');

      // 圖檔(走獨立 API;無圖則留底色)
      var $img = $c.find('.card__img');
      if (m.hasImage && m.imageUrl) { $img.attr('src', m.imageUrl).attr('alt', m.name + ' 設備圖'); }
      else { $img.remove(); }

      // 狀態(不只靠顏色:補文字 + aria-label)
      $c.find('.status')
        .addClass('status--' + statusKey(m.status))
        .attr('aria-label', '狀態:' + statusLabel(m.status));
      $c.find('.status__label').text(statusLabel(m.status));

      // 設備類型標籤(有才顯示;外部字串 → .text() 防 XSS)。
      // 無類型時「隱藏」而非移除 —— 保留元素,patch 才能在之後改出/改掉類型時來回切換。
      var $type = $c.find('.card__type');
      if (m.type) { $type.text(m.type).prop('hidden', false); } else { $type.text('').prop('hidden', true); }

      $c.find('.card__name').text(m.name);
      $c.find('.card__value-num').text(formatValue(m.latestValue)).data('raw', m.latestValue);
      $c.find('.card__unit').text(m.unit);
      $c.find('.card__spark').html(Sparkline.build(m.sparkline, m.status));
      $c.data('spark-key', sparkKeyOf(m));   // 走勢圖指紋:供 patch 判斷要不要重畫
      // 每台給可區分的 aria-label(否則讀報器 20 顆都唸「查看趨勢」);此鈕為鍵盤進入趨勢圖的入口
      $c.find('.card__btn').attr('data-id', m.id).attr('aria-label', '查看 ' + m.name + ' 趨勢圖');

      $c.data('status', m.status);
      return card;
    }

    // 渲染「當前頁」子集(listQuery 已篩選/排序/切頁)。對現有 DOM 做 diff:
    // 在頁內者 patch(保留動畫/選取/hover),不在頁內者移除,最後依嚴重度排序。
    // opts.hasData = 全量是否有資料(用以區分「全廠無資料」與「篩選無結果」)。
    function renderPage(items, opts) {
      opts = opts || {};
      var $g = grid();
      if (!opts.hasData) { renderEmpty(); return; }    // 全廠無資料
      if (!items.length) { renderNoMatch(); return; }  // 有資料但本次篩選無結果
      $g.children('.state').remove();                  // 清掉殘留的載入/空/錯誤佔位
      var incoming = {};
      items.forEach(function (m) { incoming[m.id] = true; patch(m); });   // patch 不存在則自動補卡
      $g.children('.card').each(function () {
        var id = this.getAttribute('data-id');
        if (!incoming[id]) remove(id);                 // 移除已離開當前頁的卡
      });
      reorder(items.map(function (m) { return m.id; })); // 依目前排序順序排列(順序未變則不碰 DOM)
    }

    // 篩選無結果:沿用 .state 置中視覺,附「清除篩選」次要動作
    function renderNoMatch() {
      grid().html(
        '<div class="state state--empty">' +
          '<div class="state__icon">' + Icons.SEARCH + '</div>' +
          '<h2 class="state__title">沒有符合條件的設備</h2>' +
          '<p class="state__desc">調整關鍵字或狀態篩選後再試一次。</p>' +
          '<button type="button" class="btn-ghost state__btn" data-action="reset-filters">清除篩選</button>' +
        '</div>'
      );
    }

    // 載入中:置中轉圈 + 文字(取代舊骨架屏);靜態標記,以 .html() 安全置入
    function renderLoading() {
      grid().html(
        '<div class="state state--loading" role="status" aria-live="polite">' +
          '<div class="state__spinner" aria-hidden="true"></div>' +
          '<h2 class="state__title">載入中…</h2>' +
          '<p class="state__desc">正在讀取設備資料,請稍候。</p>' +
        '</div>'
      );
    }

    function renderEmpty() {
      grid().html(
        '<div class="state state--empty">' +
          '<div class="state__icon">' + Icons.EMPTY + '</div>' +
          '<h2 class="state__title">目前沒有任何設備資料</h2>' +
          '<p class="state__desc">請於監看資料夾放入 JSON 檔,系統會自動載入。</p>' +
        '</div>'
      );
    }

    // 失敗頁:X icon + 失敗原因 + 後端 ErrorCode(/ traceId)+ 重試
    function renderError(retryFn, info) {
      info = info || {};
      var $box = $(
        '<div class="state state--error" role="alert">' +
          '<div class="state__icon">' + Icons.ERROR + '</div>' +
          '<h2 class="state__title">設備清單載入失敗</h2>' +
        '</div>'
      );
      // 失敗原因(可能來自後端 ProblemDetails.detail;外部字串 → .text() 防 XSS)
      $('<p class="state__desc"></p>')
        .text(info.reason || '無法取得設備資料,請檢查連線後再試一次。')
        .appendTo($box);
      // ErrorCode(+ traceId):機器可讀,利於回報/追查
      if (info.code) {
        var $meta = $('<p class="state__meta"></p>');
        $('<span></span>').text('錯誤代碼 ').appendTo($meta);
        $('<code></code>').text(info.code).appendTo($meta);
        if (info.traceId) {
          $('<span></span>').text(' · traceId ').appendTo($meta);
          $('<code></code>').text(info.traceId).appendTo($meta);
        }
        $meta.appendTo($box);
      }
      $('<button type="button" class="state__action"></button>')
        .html(Icons.REFRESH + '<span>重新載入</span>')   // Icons.REFRESH 為靜態 SVG 常數,無 XSS 風險
        .on('click', retryFn).appendTo($box);
      grid().empty().append($box);
    }

    // 差異更新單張卡片:逐欄比對,只在「該欄真的變了」時才碰 DOM(見 rules/前端/04 §3)。
    // 涵蓋名稱 / 類型 / 單位 / 最新值 / 狀態 / 走勢圖 —— 後端每次推播都送完整摘要,
    // 任一可見欄位變了即時反映(改名、改門檻、改任一天的值都看得到,不必整頁重載)。
    // 回傳 true 代表狀態有變(呼叫端據此決定要不要重排卡片順序)。
    function patch(m) {
      var $c = $('#machine-' + m.id);
      if (!$c.length) { grid().append(buildCard(m)); return true; } // 新卡:需重排定位

      // 名稱(外部字串 → .text() 防 XSS);兩個依賴名稱的 aria-label 一併更新
      var $name = $c.find('.card__name');
      if ($name.text() !== m.name) {
        $name.text(m.name);
        $c.attr('aria-label', '設備 ' + m.name + ' 振動狀態');
        $c.find('.card__btn').attr('aria-label', '查看 ' + m.name + ' 趨勢圖');
      }

      // 類型標籤:有/無/改字皆切換(buildCard 改為隱藏而非移除,元素恆在,故可來回切)
      var $type = $c.find('.card__type');
      var curType = $type.prop('hidden') ? '' : $type.text();
      var newType = m.type || '';
      if (curType !== newType) {
        if (newType) $type.text(newType).prop('hidden', false);
        else $type.text('').prop('hidden', true);
      }

      // 單位
      var $unit = $c.find('.card__unit');
      if ($unit.text() !== m.unit) $unit.text(m.unit);

      // 最新值:變了才改數字並閃光(閃光只對「讀數跳動」有意義)
      var $num = $c.find('.card__value-num');
      if ($num.data('raw') !== m.latestValue) {
        animateValue($num[0], $num.data('raw'), m.latestValue);   // count-up 滾動(階段 1)
        $num.data('raw', m.latestValue);
        flashUpdated($c);
      }

      // 狀態:變了才換 class / 文字 / aria;升級為危險再脈衝一次(CSS 動畫,只動 box-shadow)
      var prev = $c.data('status');
      var statusChanged = prev !== m.status;
      if (statusChanged) {
        $c.data('status', m.status)
          .find('.status')
          .removeClass('status--good status--abnormal status--danger')
          .addClass('status--' + statusKey(m.status))
          .attr('aria-label', '狀態:' + statusLabel(m.status))
          .find('.status__label').text(statusLabel(m.status));
        if (m.status === 'Danger' && prev !== 'Danger') flashDanger($c);
      }

      // 小圖(設備照片):buildCard 只設一次,patch 也要同步 —— 否則圖片「從無到有 / 內容變更」時
      // 主畫面不會刷新(舊行為只能 F5)。imageUrl 帶內容版本指紋(?v=雜湊):內容變了 src 才變 → 換 src
      // 觸發重抓;內容沒變則 src 相同、不動。無圖則移除 img(與 buildCard 行為一致)。
      var $media = $c.find('.card__media');
      var $img = $media.find('.card__img');
      if (m.hasImage && m.imageUrl) {
        if (!$img.length) { $img = $('<img class="card__img" loading="lazy" />').prependTo($media); }
        if ($img.attr('src') !== m.imageUrl) { $img.attr('src', m.imageUrl).attr('alt', m.name + ' 設備圖'); }
      } else if ($img.length) {
        $img.remove();
      }

      // 走勢圖:畫的是整段序列且依狀態著色 → 以「狀態+全序列」指紋比對,序列或狀態任一變就重畫。
      // (修正舊行為:過去把重畫綁在「最新值有沒有變」,改歷史值時走勢圖不會更新。)
      var sparkKey = sparkKeyOf(m);
      if ($c.data('spark-key') !== sparkKey) {
        $c.data('spark-key', sparkKey);
        var $spark = $c.find('.card__spark');
        $spark.html(Sparkline.build(m.sparkline, m.status));
        // 走勢線「描線」動畫(階段 1,選項 B):clip-path 由左至右揭示。
        // sparkline 本就左→右(x=時間)繪製,左到右擦出視覺上等同描線;clip-path 作用於元素方框、
        // 不碰 SVG 座標系,故 preserveAspectRatio="none" 的橫向拉伸也不會斷線,結尾必為完整線。
        if (!reduced()) {
          var sp = $spark[0];
          sp.style.transition = 'none';
          sp.style.webkitClipPath = sp.style.clipPath = 'inset(0 100% 0 0)';   // 全裁(右內縮 100%)
          void sp.getBoundingClientRect();                                     // 強制 reflow,使起點生效
          sp.style.transition = 'clip-path .55s ' + EASE + ', -webkit-clip-path .55s ' + EASE;
          sp.style.webkitClipPath = sp.style.clipPath = 'inset(0 0 0 0)';      // 揭示到全寬
          window.setTimeout(function () {                                      // 動畫後清掉 inline
            sp.style.transition = ''; sp.style.clipPath = ''; sp.style.webkitClipPath = '';
          }, 600);
        }
      }

      return statusChanged;
    }

    function remove(id) { $('#machine-' + id).remove(); }

    // 依嚴重度重排卡片(危險優先,同級依 id),與後端初始排序一致。
    // 只移動既有 DOM 節點(appendChild 是搬移非重建),保留事件/選取/動畫。
    function reorder(orderIds) {
      var $g = grid();
      var cards = $g.children('.card').get();
      if (cards.length < 2) return;
      // 依 dashboard 傳入的「目前排序後 id 順序」排列卡片(支援嚴重度 / 名稱 / 最新值);
      // 未傳則退回危險優先(向後相容)。排序切換時 FLIP 會讓卡片平滑滑到新位置。
      var pos = null;
      if (orderIds && orderIds.length) {
        pos = {};
        for (var k = 0; k < orderIds.length; k++) pos[orderIds[k]] = k;
      }
      cards.sort(function (a, b) {
        if (pos) {
          var pa = pos[a.getAttribute('data-id')]; if (pa == null) pa = Infinity;
          var pb = pos[b.getAttribute('data-id')]; if (pb == null) pb = Infinity;
          return pa - pb;
        }
        var ra = Shared.statusRank($(a).data('status')), rb = Shared.statusRank($(b).data('status'));
        if (ra !== rb) return rb - ra;                  // 危險(高 rank)在前
        var ia = String(a.getAttribute('data-id')).toLowerCase();
        var ib = String(b.getAttribute('data-id')).toLowerCase();
        return ia < ib ? -1 : ia > ib ? 1 : 0;          // 同級:id 升序(對齊後端 OrdinalIgnoreCase)
      });
      // 順序沒變就不碰 DOM(避免無謂 reflow / 動畫重置)
      var dom = $g[0].children;
      var changed = cards.some(function (c, i) { return dom[i] !== c; });
      if (!changed) return;

      // reduced-motion:直接搬位,不做位移動畫
      if (reduced()) {
        var fr = document.createDocumentFragment();
        cards.forEach(function (c) { fr.appendChild(c); });
        $g[0].appendChild(fr);
        return;
      }

      // FLIP 重排動畫(階段 1):First 記舊位置 → 重排(Last)→ Invert 先位移回舊位 → Play 過渡到 0。
      // 卡片「滑」到新位置(例如升危險滑到最前),而非瞬間跳動。
      var first = cards.map(function (c) { return c.getBoundingClientRect(); });
      var frag = document.createDocumentFragment();
      cards.forEach(function (c) { frag.appendChild(c); });
      $g[0].appendChild(frag);
      cards.forEach(function (c, i) {
        var lastR = c.getBoundingClientRect();
        var dx = first[i].left - lastR.left, dy = first[i].top - lastR.top;
        if (!dx && !dy) return;
        c.style.transition = 'none';
        c.style.transform = 'translate(' + dx + 'px,' + dy + 'px)';
      });
      void $g[0].offsetWidth;                              // 強制 reflow,使位移成為動畫起點
      cards.forEach(function (c) {
        c.style.transition = 'transform .4s ' + EASE;
        c.style.transform = '';
      });
      window.setTimeout(function () {                      // 動畫結束清掉 inline,交還 hover 的 transform
        cards.forEach(function (c) { c.style.transition = ''; c.style.transform = ''; });
      }, 460);
    }

    function flashUpdated($card) {
      $card.removeClass('card--updated');
      void $card[0].offsetWidth; // 重置動畫
      $card.addClass('card--updated');
    }

    function flashDanger($card) {
      $card.removeClass('card--alarm');
      void $card[0].offsetWidth; // 重置動畫
      $card.addClass('card--alarm');
    }

    return {
      renderPage: renderPage, renderLoading: renderLoading, renderError: renderError,
      patch: patch, remove: remove, reorder: reorder
    };
  })();

  window.MachineRenderer = MachineRenderer;
})(window, jQuery);
