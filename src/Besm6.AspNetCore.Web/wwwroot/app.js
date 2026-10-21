(() => {
  const $ = id => document.getElementById(id);
  const connection = new signalR.HubConnectionBuilder().withUrl('/emulator').withAutomaticReconnect().build();
  const plotter = new Besm6Plotter($('plotterCanvas'), $('plotterRaw'), $('plotterEmpty'));
  let ready = false, uploaded = false, uploading = false, hasSnapshot = false;
  let activeMode = 'debug', state = 'Idle';
  let memoryAddress = -1, snapshotPending = false, snapshotStarted = 0, plotterFrame = 0;
  let plotterPage = 0;
  let numberBase = 8, lastRegisters = null, lastMemory = [], lastMounts = [];
  const pendingOutput = new Map();
  let outputFrame = 0;
  const activeStates = new Set(['Loading', 'Running', 'Paused', 'Pausing', 'Stepping', 'Stopping', 'Resetting']);
  const setState = value => { state = value; $('status').textContent = value; refreshButtons(); };
  const refreshButtons = () => {
    const active = activeStates.has(state);
    $('run').disabled = !ready || !uploaded || uploading || active;
    $('reset').disabled = !ready || uploading || ['Loading', 'Resetting', 'Stopping'].includes(state);
    for (const id of ['exampleLoad', 'exampleRun']) $(id).disabled = !ready || uploading || active || !$('exampleSelect').value;
    $('pause').disabled = !ready || state !== 'Running';
    $('resume').disabled = !ready || state !== 'Paused';
    $('step').disabled = !ready || activeMode !== 'debug' || state !== 'Paused';
    $('stop').disabled = !ready || !['Running', 'Paused', 'Loading'].includes(state);
    $('file').disabled = !ready || uploading || active;
    $('dropzone').setAttribute('aria-disabled', String($('file').disabled));
    document.querySelectorAll('input[name=mode], #speed, #instructionLimit, #wallClockLimit, #guestClock, #statistics')
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
    pendingOutput.set(target, (pendingOutput.get(target) || '') + text);
    if (!outputFrame) outputFrame = requestAnimationFrame(() => {
      outputFrame = 0;
      for (const [element, chunk] of pendingOutput) {
        element.append(document.createTextNode(chunk));
        if (element.closest('.tab-pane').classList.contains('active')) element.scrollTop = element.scrollHeight;
      }
      pendingOutput.clear();
    });
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
  const formatNumber = (value, sourceBase, bits = 15) => {
    const text = parseInt(value, sourceBase).toString(numberBase).toUpperCase();
    return numberBase === 10 ? text : text.padStart(Math.ceil(bits / (numberBase === 8 ? 3 : 4)), '0');
  };
  const addressName = () => ({ 8: 'octal', 10: 'decimal', 16: 'hexadecimal' }[numberBase]);
  const addressWidth = () => numberBase === 16 ? 4 : 5;
  const renderMemory = () => {
    $('memory').textContent = lastMemory.map(w => `${formatNumber(w.address, 8).padStart(addressWidth(), '0')}  ${formatNumber(w.hex, 16, 48)}`).join('\n');
  };
  const renderMounts = () => renderRows('storageMounts', lastMounts.map(m => [m.kind, formatNumber(m.unit, 8, 6), m.words.toLocaleString(), m.readOnly ? 'Read only' : 'Read / write']));
  const renderRegisters = d => {
    const fields = [['K', d ? `${formatNumber(d.k, 8)}${d.rightHalf ? ' R' : ' L'}` : '—'],
      ['A', d ? formatNumber(d.a, 16, 48) : '—'], ['Y', d ? formatNumber(d.y, 16, 48) : '—'],
      ['R', d ? formatNumber(d.r, 8, 6) : '—'], ['C', d ? formatNumber(d.c, 8) : '—'],
      ...Array.from({length: 16}, (_, i) => [`M${i}`, d ? formatNumber(d.m[i], 8) : '—'])];
    $('registers').replaceChildren(...fields.map(([name, value]) => {
      const item = document.createElement('div'); item.className = 'register';
      const key = document.createElement('dt'); key.textContent = name;
      const cell = document.createElement('dd'); cell.textContent = value;
      item.append(key, cell); return item;
    }));
    const count = d?.instructions ?? 0;
    $('instructionCount').textContent = `${count} ${count === 1 ? 'instruction' : 'instructions'}`;
    const disassembly = d?.disassembly.replace(/(?<![\w*])[0-7]+\b/g, value => parseInt(value, 8).toString(numberBase).toUpperCase());
    $('disassembly').textContent = d ? `K ${formatNumber(d.k, 8)}: ${disassembly}` : '—';
  };
  const renderStatistics = d => {
    $('statisticsPanel').hidden = !d;
    const fields = d ? [
      ['Instructions', d.completedInstructions.toLocaleString()],
      ['Elapsed', `${d.elapsedSeconds.toFixed(3)} s`],
      ['Throughput', `${(d.instructionsPerSecond / 1e6).toFixed(3)} MIPS`],
      ['Model time', d.modelSeconds == null ? '—' : `${d.modelSeconds.toFixed(6)} s`],
      ['Model cycles', d.modelCycles?.toLocaleString() ?? '—'],
      ['Allocated', `${d.allocatedBytes.toLocaleString()} bytes`],
      ['Gen 0 collections', d.gen0Collections.toLocaleString()]] : [];
    $('statisticsOutput').replaceChildren(...fields.map(([label, value]) => {
      const item = document.createElement('div'), key = document.createElement('dt'), text = document.createElement('dd');
      key.textContent = label; text.textContent = value; item.append(key, text); return item;
    }));
  };
  const clearMachine = () => {
    hasSnapshot = false;
    lastRegisters = null; lastMemory = []; lastMounts = [];
    pendingOutput.clear(); renderStatistics(null);
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
        lastRegisters = d;
        renderRegisters(d);
        refreshButtons();
        break;
      }
      case 'memory':
        lastMemory = d.words; renderMemory();
        break;
      case 'plotter':
        if (plotterPage !== d.page) { plotterPage = d.page; schedulePlotter(); }
        break;
      case 'storage':
        lastMounts = d.mounts; renderMounts();
        renderRows('storageFiles', d.files.map(f => [f.name, f.bytes.toLocaleString()]));
        snapshotPending = false;
        break;
      case 'error': showError(d); break;
      case 'reset': clearMachine(); $('error').textContent = ''; break;
      case 'statistics': renderStatistics(d); break;
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
  window.addEventListener('exampleSelectionChanged', refreshButtons);
  for (const id of ['exampleLoad', 'exampleRun']) $(id).addEventListener('click', async () => {
    const example = $('exampleSelect').value;
    if (!example || $(id).disabled) return;
    uploading = true; refreshButtons();
    let loaded = false;
    try {
      await connection.invoke('LoadExample', example);
      $('file').value = ''; $('error').textContent = ''; loaded = true;
    } catch (error) { showError(error); }
    finally { uploading = false; refreshButtons(); }
    if (loaded && id === 'exampleRun') $('run').click();
  });
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
    const statistics = $('statistics').checked;
    clearMachine(); $('error').textContent = '';
    setState('Loading');
    if (!await command('start', { mode: activeMode, speed, guestClock, statistics, instructionLimit, wallClockLimitMs })) setState('Idle');
  });
  for (const [id, nextState] of [['pause', 'Pausing'], ['resume', 'Running'], ['step', 'Stepping'], ['stop', 'Stopping'], ['reset', 'Resetting']])
    $(id).addEventListener('click', async () => {
      const previous = state; setState(nextState);
      if (!await command(id)) setState(previous);
    });
  const inspect = () => {
    if ($('inspect').disabled) return;
    const text = $('address').value.trim();
    const address = text ? parseInt(text, numberBase) : -1;
    const valid = { 8: /^[0-7]+$/, 10: /^[0-9]+$/, 16: /^[0-9a-f]+$/i }[numberBase];
    if (text && (!valid.test(text) || address > 32767)) { showError({message: `Enter a ${addressName()} address from 0 to ${(32767).toString(numberBase).toUpperCase()}.`}); return; }
    memoryAddress = address;
    snapshotPending = false;
    requestSnapshot();
  };
  $('inspect').addEventListener('click', inspect);
  $('address').addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); inspect(); } });
  $('plotterType').addEventListener('change', renderPlotter);
  $('plotterTab').addEventListener('shown.bs.tab', renderPlotter);
  $('numberBase').addEventListener('change', () => {
    const draft = $('address').value.trim(), oldBase = numberBase;
    numberBase = Number($('numberBase').value);
    if (draft && { 8: /^[0-7]+$/, 10: /^[0-9]+$/, 16: /^[0-9a-f]+$/i }[oldBase].test(draft))
      $('address').value = parseInt(draft, oldBase).toString(numberBase).toUpperCase();
    $('address').placeholder = `Address (${({8:'OCT',10:'DEC',16:'HEX'})[numberBase]})`;
    $('address').setAttribute('aria-label', `Memory address in ${addressName()}`);
    $('address').inputMode = numberBase === 16 ? 'text' : 'numeric';
    document.querySelector('#storageMounts').closest('table').querySelectorAll('th')[1].textContent = 'Unit';
    renderRegisters(lastRegisters); renderMemory(); renderMounts();
  });
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
