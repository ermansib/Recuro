// Recuro sign-in theme: applies the workspace look the portal sent (frontend/src/api/http/signInBrand.ts)
// and wires the small touches of the portal's card. Runs in <head>, before the body renders, so colours
// and light/dark mode apply without a flash.
//
// The look arrives as the recuro_brand query parameter on the first page of a sign-in and is kept in
// sessionStorage (this tab only) for the pages after it, such as a wrong-password retry. Everything in it
// is checked: colours must be plain hex or rgb() values, text is only ever set as textContent, and the
// "back to the portal" link only goes to the origin Keycloak already accepted as the redirect_uri.
(function () {
  'use strict';

  var STORAGE_KEY = 'recuro.signInBrand';
  var PARAM = 'recuro_brand';
  var COLOUR = /^(#[0-9a-f]{3,8}|rgba?\([\d\s.,%]+\))$/i;
  var TOKENS = [
    'navy', 'navy-dk', 'navy-lt', 'navy-tint', 'on-navy',
    'gold', 'gold-lt', 'accent-line', 'accent-tx',
    'bar', 'bar-dk', 'bar-lt', 'on-bar', 'on-bar-muted',
    'red', 'red-lt', 'red-tx', 'green', 'green-lt', 'amber-lt', 'amber-tx',
    'bg', 'card', 'line', 'surface-2', 'surface-3', 'txt', 'txt-2', 'muted', 'faint'
  ];

  function clean(value) {
    if (!value || typeof value !== 'object' || value.v !== 1) return null;
    if (typeof value.slug !== 'string' || typeof value.name !== 'string') return null;
    var tokens = {};
    var given = value.tokens && typeof value.tokens === 'object' ? value.tokens : {};
    TOKENS.forEach(function (token) {
      var colour = given[token];
      if (typeof colour === 'string' && COLOUR.test(colour)) tokens[token] = colour;
    });
    return {
      slug: value.slug.slice(0, 100),
      name: value.name.slice(0, 120),
      tagline: typeof value.tagline === 'string' ? value.tagline.slice(0, 200) : '',
      scheme: value.scheme === 'dark' ? 'dark' : 'light',
      tokens: tokens
    };
  }

  function decode(encoded) {
    try {
      var base64 = encoded.replace(/-/g, '+').replace(/_/g, '/');
      var bytes = Uint8Array.from(atob(base64), function (c) { return c.charCodeAt(0); });
      return clean(JSON.parse(new TextDecoder().decode(bytes)));
    } catch (e) {
      return null;
    }
  }

  function originOf(url) {
    try {
      var parsed = new URL(url);
      return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.origin : null;
    } catch (e) {
      return null;
    }
  }

  var params = new URLSearchParams(window.location.search);
  var brand = null;
  var portal = null;
  if (params.has(PARAM)) {
    // First page of the sign-in: Keycloak has already checked redirect_uri against the client's list.
    brand = decode(params.get(PARAM) || '');
    portal = originOf(params.get('redirect_uri') || '');
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ brand: brand, portal: portal }));
    } catch (e) { /* storage unavailable: later pages fall back to the Recuro look */ }
  } else {
    try {
      var saved = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || 'null');
      if (saved) {
        brand = clean(saved.brand ? Object.assign({ v: 1 }, saved.brand) : null);
        portal = typeof saved.portal === 'string' ? originOf(saved.portal) : null;
      }
    } catch (e) { /* unreadable: use the Recuro look */ }
  }

  var root = document.documentElement;
  if (brand) {
    root.setAttribute('data-theme', brand.scheme);
    Object.keys(brand.tokens).forEach(function (token) {
      root.style.setProperty('--' + token, brand.tokens[token]);
    });
  } else if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
    root.setAttribute('data-theme', 'dark');
  }

  function each(selector, fn) {
    Array.prototype.forEach.call(document.querySelectorAll(selector), fn);
  }

  var EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  function restartAnimation(el) {
    el.style.animation = 'none';
    void el.offsetWidth;
    el.style.animation = '';
  }

  // Opened without an email: ask for it first, then the password, in this same card and without another
  // page load. Both fields stay in one form so Keycloak gets them together, as on its own page.
  function identifierFirst(form) {
    var emailStep = form.querySelector('[data-step=email]');
    var passwordStep = form.querySelector('[data-step=password]');
    var username = form.querySelector('#username');
    var password = form.querySelector('#password');
    var error = form.querySelector('[data-email-error]');
    var chip = document.querySelector('[data-email-chip]');
    var card = form.closest('.rc-step');
    if (!emailStep || !passwordStep || !username || !password || !chip) return;
    var onEmail = true;

    function setText(toPassword) {
      each('[data-password-text]', function (el) {
        if (!el.hasAttribute('data-email-text')) el.setAttribute('data-email-text', el.textContent);
        el.textContent = el.getAttribute(toPassword ? 'data-password-text' : 'data-email-text');
      });
      each('[data-step-mark]', function (li) {
        var current = li.getAttribute('data-step-mark') === (toPassword ? 'password' : 'email');
        li.className = current ? 'on' : (toPassword ? 'done' : '');
        if (current) li.setAttribute('aria-current', 'step');
        else li.removeAttribute('aria-current');
      });
    }

    function showError(text) {
      error.textContent = text;
      error.hidden = !text;
      username.setAttribute('aria-invalid', text ? 'true' : 'false');
      emailStep.classList.toggle('rc-err', !!text);
    }

    function toPassword() {
      var email = username.value.trim();
      if (!EMAIL.test(email)) {
        showError(form.getAttribute('data-email-invalid') || '');
        username.focus();
        return;
      }
      username.value = email;
      showError('');
      onEmail = false;
      chip.querySelector('[data-email-value]').textContent = email;
      chip.querySelector('[data-email-initial]').textContent = email.charAt(0).toUpperCase();
      // The email field stays in the form (visually hidden) so it is sent and password managers pair it.
      emailStep.classList.add('rc-sr-only');
      username.setAttribute('tabindex', '-1');
      chip.hidden = false;
      passwordStep.hidden = false;
      setText(true);
      if (card) restartAnimation(card);
      password.focus();
    }

    function toEmail() {
      onEmail = true;
      emailStep.classList.remove('rc-sr-only');
      username.removeAttribute('tabindex');
      chip.hidden = true;
      passwordStep.hidden = true;
      password.value = '';
      setText(false);
      if (card) restartAnimation(card);
      username.focus();
      username.select();
    }

    // A server error with no email (Keycloak re-rendered the page) belongs to the email step.
    var serverError = form.querySelector('#input-error');
    if (serverError && !username.value.trim()) showError(serverError.textContent.trim());

    form.querySelector('[data-continue]').hidden = false;
    form.querySelector('[data-continue]').addEventListener('click', toPassword);
    chip.querySelector('[data-change-email]').addEventListener('click', toEmail);
    // Enter in the email field means Continue, not Sign in.
    form.addEventListener('submit', function (event) {
      if (!onEmail) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      toPassword();
    });
    passwordStep.hidden = true;
  }

  document.addEventListener('DOMContentLoaded', function () {
    if (brand) {
      each('[data-brand-tag]', function (el) {
        el.textContent = (el.getAttribute('data-pattern') || '{0}').replace('{0}', brand.name);
        el.hidden = false;
      });
      if (brand.tagline) each('[data-brand-tagline]', function (el) { el.textContent = brand.tagline; });
      each('[data-brand-points]', function (el) { el.hidden = true; });
      each('[data-brand-powered]', function (el) { el.hidden = false; });
      each('[data-brand-name]', function (el) { el.textContent = brand.name; });
      each('[data-brand-initial]', function (el) { el.textContent = brand.name.trim().charAt(0).toUpperCase(); });
      each('[data-brand-chip], [data-brand-only]', function (el) { el.hidden = false; });
    }

    // Back to the portal's email step for this workspace, with the email filled in.
    if (brand && portal) {
      each('[data-portal-back]', function (el) {
        var query = new URLSearchParams({ workspace: brand.slug });
        var email = el.getAttribute('data-email');
        if (email) query.set('email', email);
        el.setAttribute('href', portal + '/signin?' + query.toString());
        el.hidden = false;
      });
    }

    each('form[data-identifier-first]', identifierFirst);

    // Busy state on submit, like the portal's buttons. Keycloak re-renders the page on any error.
    each('form', function (form) {
      form.addEventListener('submit', function () {
        var button = form.querySelector('button[type=submit], input[type=submit]');
        if (!button) return;
        window.setTimeout(function () { button.disabled = true; }, 0);
        button.setAttribute('aria-busy', 'true');
        var label = form.getAttribute('data-signing-in');
        if (label && button.tagName === 'BUTTON') {
          button.textContent = '';
          var spinner = document.createElement('span');
          spinner.className = 'rc-spinner';
          spinner.setAttribute('aria-hidden', 'true');
          button.appendChild(spinner);
          button.appendChild(document.createTextNode(label));
        }
      });
    });
  });
})();
