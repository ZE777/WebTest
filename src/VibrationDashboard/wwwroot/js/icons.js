/* ===========================================================================
   icons.js — 共用的內嵌 SVG 圖示常數(線稿描邊用 currentColor 跟著主色走)。
   皆為靜態字串、無外部輸入,故以 .html()/字串拼接置入無 XSS 風險。
   供 machineCard.js / panels.js 的各種狀態頁(空/錯誤/搜尋無結果/重試)共用。
   =========================================================================== */
(function (window) {
  'use strict';

  window.Icons = {
    EMPTY:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
      '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/>' +
      '<path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>' +
      '</svg>',
    ERROR:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
      '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>' +
      '<line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>' +
      '</svg>',
    REFRESH:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
      '<polyline points="23 4 23 10 17 10"/>' +
      '<path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>' +
      '</svg>',
    SEARCH:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
      '<circle cx="11" cy="11" r="7"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>' +
      '</svg>'
  };
})(window);
