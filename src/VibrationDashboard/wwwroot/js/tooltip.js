/* ===========================================================================
   tooltip.js — 全站共用自訂提示(取代瀏覽器原生 title)
   任何元素加 data-tooltip="文字" 即可;提示掛在 <body> 層(可跳出 overflow:hidden),
   統一深色半透明 + 圓角樣式(見 .app-tooltip)。內容一律 .text() → 防 XSS。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var SHOW_DELAY = 80;   // 進入後略延遲再顯示,避免快速滑過閃爍
  var GAP = 8;           // 提示與目標的間距
  var $tip = null;
  var timer = null;

  function tip() {
    if (!$tip) {
      $tip = $('<div class="app-tooltip" role="tooltip" aria-hidden="true"></div>').appendTo(document.body);
    }
    return $tip;
  }

  function place(el) {
    var r = el.getBoundingClientRect();
    var $t = tip();
    var tw = $t.outerWidth(), th = $t.outerHeight();
    var left = r.left + r.width / 2 - tw / 2;      // 水平置中於目標
    var top = r.top - th - GAP;                    // 預設在目標上方
    if (top < GAP) top = r.bottom + GAP;           // 上方放不下 → 改放下方
    left = Math.max(GAP, Math.min(left, window.innerWidth - tw - GAP)); // 夾在視窗內
    $t.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
  }

  function show(el) {
    var msg = el.getAttribute('data-tooltip');
    if (!msg) return;
    var $t = tip();
    $t.text(msg).css({ left: '-9999px', top: '0px' }).addClass('is-visible').attr('aria-hidden', 'false');
    place(el);   // 先離畫面量尺寸,再定位
  }

  function hide() {
    clearTimeout(timer);
    if ($tip) $tip.removeClass('is-visible').attr('aria-hidden', 'true');
  }

  // 事件委派:滑鼠與鍵盤焦點皆觸發(focusin/focusout 會冒泡,delegation 可用)
  $(document)
    .on('mouseenter focusin', '[data-tooltip]', function () {
      var el = this;
      clearTimeout(timer);
      timer = setTimeout(function () { show(el); }, SHOW_DELAY);
    })
    .on('mouseleave focusout', '[data-tooltip]', hide)
    .on('click', '[data-tooltip]', hide);          // 點下去就收起(如 KPI 篩選)

  // 捲動 / 改變大小時收起,避免提示停在錯位置
  $(window).on('scroll', hide).on('resize', hide);

  window.Tooltip = { show: show, hide: hide };
})(window, jQuery);
