/* ===========================================================================
   appState.js — module 層級的單一狀態來源 + 自訂事件廣播(見 rules/前端/01 §4)
   - set 是唯一寫入閘門:改 state 同時廣播,訂閱者必收到通知。
   - document 當廣播中繼站,發布端與訂閱端互不認識 → 模組解耦。
   伺服器資料(machines)只由資料/容器模組寫入,UI 模組只讀(單一寫入者)。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var AppState = (function () {
    var state = {
      machines: [],     // 設備清單(真相在後端,前端只快取呈現)
      selectedId: null  // UI:目前選取的設備
    };

    function set(key, value) {
      state[key] = value;
      $(document).trigger('appState:change', { key: key, value: value });
    }

    function get(key) { return state[key]; }

    return { get: get, set: set };
  })();

  window.AppState = AppState;
})(window, jQuery);
