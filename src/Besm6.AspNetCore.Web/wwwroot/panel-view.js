(() => {
  'use strict';
  const t = Besm6I18n.t;
  const panel = new Besm6LampPanel(document.getElementById('lampPanel'));
  const status = document.getElementById('vizSession');
  const session = new URLSearchParams(location.search).get('session');
  let channel = null, live = false, beatTimer = 0, beat = () => {};
  let lastRegisters = null, lastDetail = null, lastState = null, lastBase = null;
  const updateStatus = () => {
    status.textContent = !session ? t('Open this page from the console Panel tab.')
      : live ? `${t('Live mirror')} · ${session.slice(0, 8)}` : t('Waiting for the console session…');
  };
  const apply = message => {
    if (message.type === 'registers') {
      const key = JSON.stringify(message.data ? [
        message.data.a, message.data.y, message.data.k, message.data.c,
        message.data.r, message.data.m, message.data.rightHalf
      ] : null);
      if (key === lastRegisters) return;
      lastRegisters = key; panel.updateRegisters(message.data ?? null);
    } else if (message.type === 'detail') {
      const key = JSON.stringify(message.data ? [
        message.data.buffers, message.data.controlFlags, message.data.interrupts,
        message.data.specialRegisters, message.data.pageAssignment, message.data.operandProtection
      ] : null);
      if (key === lastDetail) return;
      lastDetail = key; panel.updateDetail(message.data ?? null);
    } else if (message.type === 'state') {
      const value = message.data || 'Idle';
      if (value === lastState) return;
      lastState = value; panel.updateState(value);
    } else if (message.type === 'base') {
      const value = Number(message.data) || 8;
      if (value === lastBase) return;
      lastBase = value; panel.setBase(value);
    } else if (message.type === 'disconnected') {
      lastRegisters = lastDetail = null; panel.updateRegisters(null); panel.updateDetail(null);
      live = false; updateStatus(); return;
    } else return;
    if (!live) { live = true; updateStatus(); }
  };

  if (session && 'BroadcastChannel' in window) {
    channel = new BroadcastChannel(`besm6-panel:${session}`);
    channel.addEventListener('message', event => {
      if (event.data && typeof event.data.type === 'string') apply(event.data);
    });
    beat = () => channel.postMessage({ type: 'watch' });
    // The console answers with its current snapshot; no second machine is created.
    channel.postMessage({ type: 'request' }); beat();
    beatTimer = setInterval(beat, 2000);
    document.addEventListener('visibilitychange', beat);
  }
  updateStatus();

  window.addEventListener('languagechange', () => { panel.schedule(); updateStatus(); });
  window.addEventListener('pagehide', () => {
    clearInterval(beatTimer);
    document.removeEventListener('visibilitychange', beat);
    channel?.close();
  });
})();
