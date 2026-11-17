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
  }
});
