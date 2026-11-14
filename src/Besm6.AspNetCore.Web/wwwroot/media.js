(() => {
const t = Besm6I18n.t, ui = (id, value) => Besm6I18n.text(document.getElementById(id), value);
window.Besm6Media = class {
  entries = []; mounts = []; view = null; ready = false; state = 'Idle'; radix = 8; uploading = false;
  constructor(send, upload, report) {
    this.send = send; this.report = report; const $ = id => document.getElementById(id);
    $('mediaSource').addEventListener('change', () => {
      const entry = this.entries.find(e => e.id === $('mediaSource').value);
      if (entry) $('mediaVolume').value = entry.volume;
      const parts = $('mediaSource').value.split(':');
      if (parts[0] === 'mounted') { $('mediaKind').value = parts[1]; $('mediaUnit').value = this.number(Number(parts[2])); }
      this.updateState(this.ready, this.state);
    });
    $('mediaKind').addEventListener('change', () => { $('mediaUnit').value = this.number($('mediaKind').value === 'Drum' ? 1 : 24); });
    for (const [id, type] of Object.entries({ mediaRead: 'readMedia', mediaLoad: 'mediaToMemory', mediaMount: 'mount', mediaUnmount: 'unmount' }))
      $(id).addEventListener('click', async () => {
        try {
          const data = { mediaId: $('mediaSource').value, kind: $('mediaKind').value, unit: this.parse('mediaUnit'), volume: $('mediaVolume').value.trim(), readOnly: $('mediaReadOnly').checked, replace: $('mediaReplace').checked,
            mediaOffset: this.parse('mediaOffset'), mediaCount: Number($('mediaCount').value), memoryAddress: this.parse('mediaAddress') };
          if (!$('mediaCount').checkValidity() || !Number.isInteger(data.mediaCount)) throw new Error('Word count: 1–128.');
          await send(type, data);
        } catch (error) { report(error); }
      });
    $('mediaFile').addEventListener('change', async event => {
      const file = event.target.files[0]; if (!file) return;
      try {
        if (!file.size || file.size > 8_000_000 || file.size % 6) throw new Error('Dense image: complete 6-byte words, up to 8 MB.');
        this.uploading = true; this.updateState(this.ready, this.state);
        await upload(file);
      } catch (error) { report(error); }
      finally { this.uploading = false; $('mediaFile').value = ''; this.updateState(this.ready, this.state); }
    });
    fetch('/api/media').then(response => { if (!response.ok) throw new Error('Cannot load media catalog.'); return response.json(); })
      .then(entries => { this.entries = [...entries, ...this.entries]; this.options(); }).catch(report);
  }
  parse(id) {
    const text = document.getElementById(id).value.trim();
    if (!({ 8: /^[0-7]+$/, 10: /^[0-9]+$/, 16: /^[0-9a-f]+$/i }[this.radix].test(text))) throw new Error(`${id}: enter a ${this.radix === 8 ? 'OCT' : this.radix === 16 ? 'HEX' : 'DEC'} number.`);
    const value = parseInt(text, this.radix); if (!Number.isSafeInteger(value)) throw new Error('Number is too large.'); return value;
  }
  number(value, width = 0) { return value.toString(this.radix).toUpperCase().padStart(width, '0'); }
  options(selected = document.getElementById('mediaSource').value) {
    const select = document.getElementById('mediaSource'), first = document.createElement('option'); first.value = ''; first.textContent = t('Select an image');
    const options = this.entries.map(entry => [entry.id, t('{name} · {words} words', { name: entry.name, words: entry.words.toLocaleString(Besm6I18n.language) })]);
    for (const mount of this.mounts) options.push([`mounted:${mount.kind}:${parseInt(mount.unit, 8)}`, t('{kind} {unit} · mounted · {words} words', { kind: t(mount.kind), unit: this.number(parseInt(mount.unit, 8)), words: mount.words.toLocaleString(Besm6I18n.language) })]);
    select.replaceChildren(first, ...options.map(([value, text]) => { const option = document.createElement('option'); option.value = value; option.textContent = text; return option; }));
    select.value = options.some(([id]) => id === selected) ? selected : '';
    this.updateState(this.ready, this.state);
  }
  uploaded(entry) { this.entries.push(entry); this.options(entry.id); document.getElementById('mediaVolume').value = entry.volume; }
  storage(data) { this.mounts = data.mounts; this.options(); }
  updateState(ready, state) {
    this.ready = ready; this.state = state;
    const safe = ready && ['Idle', 'Paused', 'Completed', 'Halted', 'Failed'].includes(state), selected = !!document.getElementById('mediaSource').value;
    for (const id of ['mediaRead', 'mediaLoad', 'mediaMount']) document.getElementById(id).disabled = !safe || !selected || this.uploading;
    document.getElementById('mediaUnmount').disabled = !safe || this.uploading;
    document.getElementById('mediaFile').disabled = !ready || this.uploading;
    document.getElementById('mediaHelp').textContent = safe ? 'Чтение и монтирование доступны. Образ: 6 байт big-endian на слово, 1024 слова в зоне.' : 'Для чтения и монтирования поставьте машину на паузу или остановите её.';
  }
  setBase(radix) {
    for (const id of ['mediaUnit', 'mediaOffset', 'mediaAddress']) {
      try { const value = this.parse(id); document.getElementById(id).value = value.toString(radix).toUpperCase(); } catch { }
    }
    this.radix = radix;
    for (const id of ['mediaUnitBase', 'mediaOffsetBase', 'mediaAddressBase']) document.getElementById(id).textContent = ({8:'OCT',10:'DEC',16:'HEX'})[radix];
    this.options(); if (this.view) this.display(this.view);
  }
  display(data) {
    this.view = data;
    const message = document.getElementById('mediaMessage'); Besm6I18n.text(message, t('{count} / {total} words', { count: data.words.length, total: data.totalWords.toLocaleString(Besm6I18n.language) }) + (data.loadedAddress == null ? '' : t(' · loaded at {address}', { address: this.number(data.loadedAddress) })));
    const source = data.words.map(word => `${this.number(word.offset, this.radix === 16 ? 4 : 5)}  ${this.number(parseInt(word.hex, 16), this.radix === 8 ? 16 : this.radix === 16 ? 12 : 0)}  ${Besm6Source.disassembly(word.disassembly, this.radix).padEnd(34)}  ${word.text}`).join('\n');
    Besm6Source.render(document.getElementById('mediaDisassembly'), source);
  }
  clear() { this.view = null; document.getElementById('mediaMessage').textContent = ''; document.getElementById('mediaDisassembly').replaceChildren(); }
  sessionLost() { this.entries = this.entries.filter(entry => entry.id.startsWith('builtin:')); this.mounts = []; this.options(); this.clear(); }
};

})();
