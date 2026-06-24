/* ===========================================================================
   api.js — 統一 AJAX 封裝 + 集中錯誤處理(見 rules/前端/02)
   所有對後端的請求都走這裡,集中管理逾時、antiforgery、錯誤解析。
   不可在各模組裡隨手寫 $.ajax。
   =========================================================================== */
(function (window, $) {
  'use strict';

  var DEFAULT_TIMEOUT = 8000; // 逾時第二道防線(ms)

  // 取 ASP.NET Core antiforgery token(由 Razor @Html.AntiForgeryToken() 產出的隱藏欄位)
  function antiforgeryToken() {
    return $('input[name="__RequestVerificationToken"]').val();
  }

  function request(options) {
    return $.ajax($.extend({
      timeout: DEFAULT_TIMEOUT,
      dataType: 'json',
      headers: { 'RequestVerificationToken': antiforgeryToken() }
    }, options));
  }

  // 對外 API:回傳 jQuery promise(.done/.fail)
  var Api = {
    get:  function (url)       { return request({ url: url, method: 'GET' }); },
    post: function (url, data) { return request({ url: url, method: 'POST', contentType: 'application/json', data: JSON.stringify(data) }); },
    put:  function (url, data) { return request({ url: url, method: 'PUT',  contentType: 'application/json', data: JSON.stringify(data) }); },
    del:  function (url)       { return request({ url: url, method: 'DELETE' }); }
  };

  /* ── ProblemDetails 解析(RFC 7807) ─────────────────────────────────── */
  function parseProblemDetails(jqXHR) {
    try {
      var p = jqXHR.responseJSON || JSON.parse(jqXHR.responseText);
      return {
        title: p.title,
        detail: p.detail || p.title,
        errorCode: p.errorCode || null,
        errors: p.errors || null,
        traceId: p.traceId || null
      };
    } catch (e) {
      return { title: '未知錯誤', detail: '伺服器回應無法解析' };
    }
  }

  /* ── HTTP 狀態碼 → UI 對照(見 rules/前端/02 §4) ──────────────────── */
  function mapStatusToUi(status, thrownError) {
    if (status === 0)              return { kind: 'toast', tone: 'warning', message: '網路連線異常,請確認網路狀態' };
    if (thrownError === 'timeout') return { kind: 'toast', tone: 'warning', message: '連線逾時,請稍後再試' };
    switch (status) {
      case 400: case 404: case 409: case 422: return { kind: 'inline' };
      case 403: return { kind: 'toast', tone: 'danger',  message: '您沒有此操作的權限' };
      case 429: case 503: case 504: return { kind: 'toast', tone: 'warning', message: '請稍後再試' };
      default:  return { kind: 'toast', tone: 'danger', message: '伺服器發生錯誤,請稍後再試' };
    }
  }

  /* ── Toast(角落非阻擋提示) ───────────────────────────────────────── */
  var Toast = (function () {
    function ensureRoot() {
      var root = document.getElementById('toast-root');
      if (!root) {
        root = document.createElement('div');
        root.id = 'toast-root';
        root.className = 'toast-root';
        document.body.appendChild(root);
      }
      return root;
    }
    function show(tone, message) {
      var el = document.createElement('div');
      el.className = 'toast toast--' + (tone || 'accent');
      el.textContent = message;              // textContent → 防 XSS
      ensureRoot().appendChild(el);
      setTimeout(function () {
        el.style.transition = 'opacity .3s';
        el.style.opacity = '0';
        setTimeout(function () { el.remove(); }, 300);
      }, 4000);
    }
    return { show: show };
  })();

  /* ── 集中錯誤處理:所有未被區域處理的 $.ajax 失敗都會經過 ───────────── */
  $(document).ajaxError(function (event, jqXHR, settings, thrownError) {
    if (settings.skipGlobalError) return; // 區域已自行處理者可標記跳過

    var problem = parseProblemDetails(jqXHR);
    var ui = mapStatusToUi(jqXHR.status, thrownError);
    if (ui.kind === 'toast') {
      Toast.show(ui.tone, problem.detail || ui.message);
    }
    // inline 類由呼叫端自行於區塊內呈現
  });

  /* ── 前端執行期例外集中捕捉(見 rules/前端/02 §7) ──────────────────── */
  window.onerror = function (message, source, line, col, error) {
    console.error('[未捕捉錯誤]', message, source + ':' + line + ':' + col, error);
    return false;
  };
  window.addEventListener('unhandledrejection', function (e) {
    console.error('[未處理的 Promise]', e.reason);
  });

  // 對外暴露
  window.Api = Api;
  window.Toast = Toast;
  window.parseProblemDetails = parseProblemDetails;
})(window, jQuery);
