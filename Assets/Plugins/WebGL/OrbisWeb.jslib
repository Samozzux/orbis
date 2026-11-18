// Funzioni del browser usate da Orbis.Platform (WebPlatformServices).
mergeInto(LibraryManager.library, {
  // Pixel del canvas per pixel CSS: tiene conto di config.devicePixelRatio della pagina.
  OrbisWeb_GetPixelsPerPoint: function () {
    var canvas = Module['canvas'];
    if (canvas && canvas.clientWidth > 0 && canvas.width > 0) {
      return canvas.width / canvas.clientWidth;
    }
    return window.devicePixelRatio || 1;
  },

  // Puntatore principale "grossolano" = dito (telefono/tablet).
  OrbisWeb_IsTouchPrimary: function () {
    try {
      return (window.matchMedia && window.matchMedia('(pointer: coarse)').matches) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  OrbisWeb_HasWebGpu: function () {
    return (typeof navigator !== 'undefined' && navigator.gpu) ? 1 : 0;
  },

  // Salva su IndexedDB i file scritti in persistentDataPath (file system IDBFS di Unity).
  OrbisWeb_SyncFs: function () {
    try {
      FS.syncfs(false, function (err) {
        if (err) console.warn('[Orbis] salvataggio nel browser non riuscito', err);
      });
    } catch (e) {
      console.warn('[Orbis] syncfs non disponibile', e);
    }
  },

  // ---------------------------------------------------------------- passkey (WebAuthn, docs/06 §4)

  // Il browser sa usare le passkey (Face ID / Touch ID / Windows Hello / telefono vicino).
  OrbisWeb_PasskeySupported: function () {
    try {
      return (typeof window !== 'undefined' && window.PublicKeyCredential && navigator.credentials && window.isSecureContext) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // Prepara la richiesta e la fa partire al prossimo "gesto" del giocatore (pointerup, touchend, keyup):
  // Safari accetta navigator.credentials.create/get solo dentro un gesto, e Unity elabora i tocchi un frame dopo.
  // create = 1 per creare la passkey, 0 per entrare. Risposta JSON a callback(id, puntatore) da liberare con OrbisWeb_Free.
  OrbisWeb_PasskeyArm: function (create, optionsPtr, requestId, callback) {
    var json = UTF8ToString(optionsPtr);
    function fromB64(s) {
      s = String(s || '').replace(/-/g, '+').replace(/_/g, '/');
      while (s.length % 4) s += '=';
      var bin = atob(s), out = new Uint8Array(bin.length);
      for (var i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
      return out.buffer;
    }
    function toB64(buf) {
      var a = new Uint8Array(buf), s = '';
      for (var i = 0; i < a.length; i++) s += String.fromCharCode(a[i]);
      return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }
    function reply(obj) {
      var text = JSON.stringify(obj);
      var size = lengthBytesUTF8(text) + 1;
      var ptr = _malloc(size);
      stringToUTF8(text, ptr, size);
      {{{ makeDynCall('vii', 'callback') }}}(requestId, ptr);
    }
    var state = Module.orbisPasskeys = Module.orbisPasskeys || {};
    var events = ['pointerup', 'touchend', 'keyup'];
    var pending = { done: false };
    function stop() {
      events.forEach(function (e) { document.removeEventListener(e, pending.run, true); });
      clearTimeout(pending.timer);
      if (state[requestId] === pending) delete state[requestId];
    }
    pending.run = function () {
      if (pending.done) return;
      pending.done = true;
      stop();
      var call;
      try {
        var options = JSON.parse(json);
        var pk = options.publicKey;
        pk.challenge = fromB64(pk.challenge);
        if (create) {
          pk.user.id = fromB64(pk.user.id);
          (pk.excludeCredentials || []).forEach(function (c) { c.id = fromB64(c.id); });
          call = navigator.credentials.create({ publicKey: pk });
        } else {
          (pk.allowCredentials || []).forEach(function (c) { c.id = fromB64(c.id); });
          call = navigator.credentials.get({ publicKey: pk });
        }
      } catch (e) {
        reply({ ok: false, error: (e && e.name) || 'Error' });
        return;
      }
      call.then(function (cred) {
        var r = cred.response;
        var out = { credentialId: toB64(cred.rawId), clientDataJSON: toB64(r.clientDataJSON) };
        if (create) {
          out.attestationObject = toB64(r.attestationObject);
        } else {
          out.authenticatorData = toB64(r.authenticatorData);
          out.signature = toB64(r.signature);
          out.userHandle = r.userHandle ? toB64(r.userHandle) : '';
        }
        reply({ ok: true, credential: out });
      }).catch(function (e) {
        reply({ ok: false, error: (e && e.name) || 'Error' });
      });
    };
    // senza un gesto entro 15 secondi si lascia perdere
    pending.timer = setTimeout(function () {
      if (pending.done) return;
      pending.done = true;
      stop();
      reply({ ok: false, error: 'Timeout' });
    }, 15000);
    state[requestId] = pending;
    events.forEach(function (e) { document.addEventListener(e, pending.run, true); });
  },

  // Il tocco è finito prima che Unity preparasse la richiesta: si parte subito (Chrome e Firefox lo accettano,
  // Safari a volte chiede di toccare di nuovo).
  OrbisWeb_PasskeyRunNow: function (requestId) {
    var state = Module.orbisPasskeys || {};
    var pending = state[requestId];
    if (pending) pending.run();
  },

  OrbisWeb_Free: function (ptr) {
    _free(ptr);
  }
});
