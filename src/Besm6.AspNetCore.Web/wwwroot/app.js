(() => {
  const $ = id => document.getElementById(id);
  const connection = new signalR.HubConnectionBuilder().withUrl('/emulator').withAutomaticReconnect().build();
  const plotter = new Besm6Plotter($('plotterCanvas'), $('plotterRaw'), $('plotterEmpty'));
  let ready = false, uploaded = false, uploading = false, hasSnapshot = false;
  let activeMode = 'debug', state = 'Idle';
  let memoryAddress = -1, snapshotPending = false, snapshotStarted = 0, plotterFrame = 0;
  let plotterPage = 0;
  const activeStates = new Set(['Loading', 'Running', 'Paused', 'Pausing', 'Stepping', 'Stopping']);
  const setState = value => { state = value; $('status').textContent = value; refreshButtons(); };
  const refreshButtons = () => {
    const active = activeStates.has(state);
    $('run').disabled = !ready || !uploaded || uploading || active;
    $('pause').disabled = !ready || state !== 'Running';
    $('resume').disabled = !ready || state !== 'Paused';
    $('step').disabled = !ready || activeMode !== 'debug' || state !== 'Paused';
    $('stop').disabled = !ready || !['Running', 'Paused', 'Loading'].includes(state);
    $('file').disabled = !ready || uploading || active;
    $('dropzone').setAttribute('aria-disabled', String($('file').disabled));
    document.querySelectorAll('input[name=mode], #speed, #instructionLimit, #wallClockLimit, #guestClock')
      .forEach(input => { input.disabled = active; });
    $('inspect').disabled = !ready || state === 'Loading' || (!hasSnapshot && !['Running', 'Paused'].includes(state));
  };
  const showError = e => { $('error').textContent = e?.message || String(e); };
  const command = async (type, more = {}) => {
    try { await connection.invoke('Command', { type, ...more }); return true; }
    catch (e) { showError(e); return false; }
  };
  const appendOutput = (channel, text) => {
    if (Object.hasOwn(plotter.buffers, channel)) {
      if (!Object.values(plotter.buffers).some(value => value.length)) $('plotterType').value = channel;
      plotter.append(channel, text);
      schedulePlotter(); return;
    }
    const target = $({ punch: 'punchOutput', terminal: 'terminalOutput', print: 'printOutput' }[channel] || 'printOutput');
    target.textContent += text;
    target.scrollTop = target.scrollHeight;
  };
  const renderPlotter = () => {
    const type = $('plotterType').value;
    plotter.render(type);
    $('plotterInfo').textContent = `Page ${Math.max(1, plotterPage)} · ${plotter.buffers[type].length} bytes`;
  };
  const schedulePlotter = () => {
    if ($('plotterPane').classList.contains('active') && !plotterFrame)
      plotterFrame = requestAnimationFrame(() => { plotterFrame = 0; renderPlotter(); });
  };
  const renderRows = (id, rows) => $(id).replaceChildren(...rows.map(values => {
    const row = document.createElement('tr');
    row.append(...values.map(value => { const cell = document.createElement('td'); cell.textContent = value; return cell; }));
    return row;
  }));
  const requestSnapshot = async (includeMachineState = true) => {
    if (!ready || snapshotPending) return;
    snapshotPending = true; snapshotStarted = performance.now();
    if (!await command(includeMachineState ? 'snapshot' : 'devices', { memoryAddress, memoryCount: 32 })) snapshotPending = false;
  };
  const renderRegisters = d => {
    const fields = [['K', d ? `${d.k}${d.rightHalf ? ' R' : ' L'}` : '—'],
      ['A', d?.a ?? '—'], ['Y', d?.y ?? '—'], ['R', d?.r ?? '—'], ['C', d?.c ?? '—'],
      ...Array.from({length: 16}, (_, i) => [`M${i}`, d?.m[i] ?? '—'])];
    $('registers').replaceChildren(...fields.map(([name, value]) => {
      const item = document.createElement('div'); item.className = 'register';
      const key = document.createElement('dt'); key.textContent = name;
      const cell = document.createElement('dd'); cell.textContent = value;
      item.append(key, cell); return item;
    }));
    const count = d?.instructions ?? 0;
    $('instructionCount').textContent = `${count} ${count === 1 ? 'instruction' : 'instructions'}`;
    $('disassembly').textContent = d ? `K ${d.k}: ${d.disassembly}` : '—';
  };
  const clearMachine = () => {
    hasSnapshot = false;
    renderRegisters(null);
    $('memory').textContent = '';
    $('printOutput').textContent = ''; $('punchOutput').textContent = '';
    $('terminalOutput').textContent = '';
    snapshotPending = false; memoryAddress = -1; $('address').value = '';
    plotterPage = 0; plotter.reset(); renderPlotter();
    $('storageMounts').replaceChildren(); $('storageFiles').replaceChildren();
  };
  connection.on('sessionReady', () => { ready = true; $('connection').textContent = 'Connected'; refreshButtons(); });
  connection.on('uploaded', name => { uploaded = true; $('fileName').textContent = name; refreshButtons(); });
  connection.on('workerEvent', event => {
    const d = event.data;
    switch (event.type) {
      case 'state': setState(d.status); break;
      case 'output': appendOutput(d.channel, d.text); break;
      case 'registers': {
        hasSnapshot = true;
        renderRegisters(d);
        refreshButtons();
        break;
      }
      case 'memory':
        $('memory').textContent = d.words.map(w => `${w.address.padStart(5, '0')}  ${w.hex}`).join('\n');
        break;
      case 'plotter':
        if (plotterPage !== d.page) { plotterPage = d.page; schedulePlotter(); }
        break;
      case 'storage':
        renderRows('storageMounts', d.mounts.map(m => [m.kind, m.unit, m.words.toLocaleString(), m.readOnly ? 'Read only' : 'Read / write']));
        renderRows('storageFiles', d.files.map(f => [f.name, f.bytes.toLocaleString()]));
        snapshotPending = false;
        break;
      case 'error': showError(d); break;
      case 'completed':
        if (!d.success) showError({message: d.errorMessage || (d.limitExceeded ? 'Instruction or time limit reached.' : 'Execution failed.')});
        break;
    }
  });
  connection.onreconnecting(() => {
    ready = false; uploaded = false;
    $('file').value = '';
    $('fileName').textContent = 'Connection lost. Upload the job again after reconnecting.';
    clearMachine(); setState('Idle');
    $('connection').textContent = 'Reconnecting…';
  });
  connection.onreconnected(() => { if (!ready) $('connection').textContent = 'Connecting session…'; });
  connection.onclose(e => { ready = false; $('connection').textContent = 'Disconnected'; if (e) showError(e); refreshButtons(); });
  async function upload(file) {
    if (!file || !ready || uploading || activeStates.has(state)) return;
    uploaded = false;
    refreshButtons();
    if (!file.name.toLowerCase().endsWith('.dub') || file.size > 10_000_000 || !file.size) {
      $('fileName').textContent = 'No valid job selected';
      showError({message: 'Select a .dub file between 1 byte and 10 MB.'}); return;
    }
    uploading = true; refreshButtons();
    $('fileName').textContent = `Uploading ${file.name}…`;
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      let binary = '';
      for (let i = 0; i < bytes.length; i += 8192)
        binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
      await connection.invoke('Upload', btoa(binary), file.name);
      $('error').textContent = '';
    }
    catch (e) { showError(e); $('fileName').textContent = 'Upload failed'; }
    finally { uploading = false; refreshButtons(); }
  }
  $('file').addEventListener('change', e => upload(e.target.files[0]));
  const drop = $('dropzone');
  ['dragenter', 'dragover'].forEach(name => drop.addEventListener(name, e => { e.preventDefault(); if (!$('file').disabled) drop.classList.add('dragging'); }));
  ['dragleave', 'drop'].forEach(name => drop.addEventListener(name, e => { e.preventDefault(); drop.classList.remove('dragging'); }));
  drop.addEventListener('drop', e => upload(e.dataTransfer.files[0]));
  $('run').addEventListener('click', async () => {
    if ($('run').disabled) return;
    const instructionLimit = Number($('instructionLimit').value);
    const wallClockLimitMs = Math.round(Number($('wallClockLimit').value) * 1000);
    if (!$('instructionLimit').value.trim() || !$('wallClockLimit').value.trim()
      || !$('instructionLimit').checkValidity() || !$('wallClockLimit').checkValidity()
      || !Number.isInteger(instructionLimit) || instructionLimit < 1 || instructionLimit > 1_000_000_000
      || !Number.isInteger(wallClockLimitMs) || wallClockLimitMs < 0 || wallClockLimitMs > 3_600_000) {
      showError({message: 'Use 1–1,000,000,000 instructions and a time limit from 0 to 3,600 seconds.'}); return;
    }
    activeMode = document.querySelector('input[name=mode]:checked').value;
    const speed = $('speed').value;
    const guestClock = $('guestClock').checked;
    clearMachine(); $('error').textContent = '';
    setState('Loading');
    if (!await command('start', { mode: activeMode, speed, guestClock, instructionLimit, wallClockLimitMs })) setState('Idle');
  });
  for (const [id, nextState] of [['pause', 'Pausing'], ['resume', 'Running'], ['step', 'Stepping'], ['stop', 'Stopping']])
    $(id).addEventListener('click', async () => {
      const previous = state; setState(nextState);
      if (!await command(id)) setState(previous);
    });
  const inspect = () => {
    if ($('inspect').disabled) return;
    const text = $('address').value.trim();
    const address = text ? parseInt(text, 8) : -1;
    if (text && (!/^[0-7]+$/.test(text) || address > 32767)) { showError({message: 'Enter an octal address from 0 to 77777.'}); return; }
    memoryAddress = address;
    snapshotPending = false;
    requestSnapshot();
  };
  $('inspect').addEventListener('click', inspect);
  $('address').addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); inspect(); } });
  $('plotterType').addEventListener('change', renderPlotter);
  $('plotterTab').addEventListener('shown.bs.tab', renderPlotter);
  $('autoRefresh').addEventListener('change', () => {
    if ($('autoRefresh').checked && state === 'Running') requestSnapshot();
  });
  const refreshTimer = setInterval(() => {
    if (ready && state === 'Running') {
      if (snapshotPending && performance.now() - snapshotStarted > 1500) snapshotPending = false;
      requestSnapshot($('autoRefresh').checked);
    }
  }, 300);
  window.addEventListener('pagehide', () => clearInterval(refreshTimer));
  clearMachine(); refreshButtons();
  connection.start().catch(showError);
})();
