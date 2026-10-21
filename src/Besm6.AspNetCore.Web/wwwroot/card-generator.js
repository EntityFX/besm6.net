(() => {
  const $ = id => document.getElementById(id), ns = 'http://www.w3.org/2000/svg';
  const geometry = Object.freeze({ width: 187.325, height: 82.55, cut: 6.35, holeWidth: 1.397, holeHeight: 3.175, pitchX: 2.2098, pitchY: 6.35, firstX: 4.5466, firstY: 4.7625 });
  const rows = [12, 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
  let tables, holes = Array(960).fill(false), svg, activeCell = 0;
  const capacity = () => $('cardEncoding').value === 'ibm' ? 80 : 120;
  const table = () => tables[$('cardEncoding').value];
  const create = (name, attrs = {}, text) => {
    const element = document.createElementNS(ns, name);
    for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, value);
    if (text != null) element.textContent = text;
    return element;
  };
  const message = (text, error = false) => {
    $('cardMessage').textContent = text;
    $('cardMessage').classList.toggle('text-danger', error);
    $('cardText').setAttribute('aria-invalid', String(error));
  };
  const normalize = character => {
    character = character.toUpperCase();
    return ({'А':'A','В':'B','С':'C','Е':'E','Н':'H','К':'K','М':'M','О':'O','Р':'P','Т':'T','Х':'X','У':'Y'})[character] || character;
  };
  function encode() {
    if (!tables) return;
    const text = Array.from($('cardText').value);
    if (text.length > capacity()) { message(`На карте помещается ${capacity()} символов; введено ${text.length}.`, true); return; }
    const next = Array(960).fill(false), ibm = $('cardEncoding').value === 'ibm';
    for (let i = 0; i < text.length; i++) {
      const code = table()[normalize(text[i])];
      if (code == null) { message(`Символ «${text[i]}» в позиции ${i + 1} отсутствует в этой исторической кодировке.`, true); return; }
      for (let bit = 0; bit < (ibm ? 12 : 8); bit++)
        if (code & (1 << (ibm ? bit : 7 - bit))) next[ibm ? bit * 80 + i : Math.floor(i / 10) * 80 + (i % 10) * 8 + bit] = true;
    }
    holes = next; render(); message(`${text.length} / ${capacity()} символов · ${holes.filter(Boolean).length} отверстий`);
  }
  function decode() {
    const ibm = $('cardEncoding').value === 'ibm', reverse = new Map();
    for (const [character, code] of Object.entries(table())) if (!reverse.has(code)) reverse.set(code, character);
    const characters = [], errors = [];
    let lastUsed = -1;
    for (let i = 0; i < capacity(); i++) {
      let code = 0;
      for (let bit = 0; bit < (ibm ? 12 : 8); bit++)
        if (holes[ibm ? bit * 80 + i : Math.floor(i / 10) * 80 + (i % 10) * 8 + bit]) code |= 1 << (ibm ? bit : 7 - bit);
      if (code) lastUsed = i;
      if (!ibm && code && code.toString(2).replace(/0/g, '').length % 2 !== 1) {
        characters.push('�'); errors.push(`нечётность: символ ${i + 1}`);
      } else if (code && !reverse.has(code)) {
        characters.push('�'); errors.push(`неизвестная пробивка: символ ${i + 1}`);
      } else characters.push(code ? reverse.get(code) : ' ');
    }
    $('cardText').value = characters.slice(0, lastUsed + 1).join('');
    render(); message(errors.length ? errors.join('; ') : `Декодировано ${lastUsed + 1} символов · ${holes.filter(Boolean).length} отверстий`, errors.length > 0);
  }
  function render() {
    svg = create('svg', { xmlns: ns, width: `${geometry.width}mm`, height: `${geometry.height}mm`, viewBox: `0 0 ${geometry.width} ${geometry.height}`, role: 'group', 'aria-label': 'Перфокарта 80 колонок на 12 рядов' });
    svg.append(create('polygon', { points: `${geometry.cut},0 ${geometry.width},0 ${geometry.width},${geometry.height} 0,${geometry.height} 0,${geometry.cut}`, fill: '#FAD7B9', stroke: '#9D7E61', 'stroke-width': .3 }));
    const print = create('g', { fill: '#9D7E61', 'font-family': 'Courier New, monospace', 'font-weight': 'bold', 'text-anchor': 'middle', 'aria-hidden': 'true' });
    print.append(create('text', { x: 7, y: 2.8, 'text-anchor': 'start', 'font-size': 1.8 }, `БЭСМ-6 / ${$('cardEncoding').value === 'ibm' ? 'IBM' : 'УПП-IBM'}  ${$('cardText').value.toUpperCase()}`));
    for (let column = 0; column < 80; column++) {
      const x = geometry.firstX + column * geometry.pitchX + geometry.holeWidth / 2;
      if (column === 0 || (column + 1) % 2 === 0)
        print.append(create('text', { x, y: 16, 'font-size': 1.1 }, String(column + 1)));
      for (let row = 2; row < 12; row++)
        if (!holes[row * 80 + column]) print.append(create('text', { x, y: geometry.firstY + row * geometry.pitchY + geometry.holeHeight / 2 + .5, 'font-size': 1.5, opacity: .6 }, String(row - 2)));
    }
    svg.append(print);
    const punched = create('g', { fill: '#2b1d14', 'data-holes': '' }), cells = create('g', { 'data-editor': '' });
    for (let row = 0; row < 12; row++) for (let column = 0; column < 80; column++) {
      const x = geometry.firstX + column * geometry.pitchX, y = geometry.firstY + row * geometry.pitchY, index = row * 80 + column;
      if (holes[index]) punched.append(create('rect', { x: x.toFixed(4), y: y.toFixed(4), width: geometry.holeWidth, height: geometry.holeHeight }));
      cells.append(create('rect', { x: x - (geometry.pitchX - geometry.holeWidth) / 2, y: y - (geometry.pitchY - geometry.holeHeight) / 2, width: geometry.pitchX, height: geometry.pitchY, fill: 'transparent', class: 'card-cell', 'data-index': index, role: 'button', tabindex: index === activeCell ? 0 : -1, 'aria-label': `Ряд ${rows[row]}, колонка ${column + 1}`, 'aria-pressed': holes[index] }));
    }
    svg.append(punched, cells); $('cardPreview').replaceChildren(svg);
  }
  const toggle = index => { activeCell = index; holes[index] = !holes[index]; decode(); };
  const selectCell = index => {
    activeCell = index; $('cardRow').value = String(Math.floor(index / 80)); $('cardColumn').value = String(index % 80 + 1);
    svg.querySelectorAll('.card-cell').forEach(cell => { cell.setAttribute('tabindex', Number(cell.dataset.index) === index ? '0' : '-1'); });
  };
  $('cardPreview').addEventListener('click', event => {
    const cell = event.target.closest('.card-cell'); if (!cell) return;
    toggle(Number(cell.dataset.index)); selectCell(activeCell);
    svg.querySelector(`[data-index="${activeCell}"]`).focus();
  });
  $('cardPreview').addEventListener('keydown', event => {
    const cell = event.target.closest('.card-cell'); if (!cell) return;
    let index = Number(cell.dataset.index), row = Math.floor(index / 80), column = index % 80;
    if (event.key === ' ' || event.key === 'Enter') toggle(index);
    else if (event.key === 'ArrowLeft') index = row * 80 + Math.max(0, column - 1);
    else if (event.key === 'ArrowRight') index = row * 80 + Math.min(79, column + 1);
    else if (event.key === 'ArrowUp') index = Math.max(0, row - 1) * 80 + column;
    else if (event.key === 'ArrowDown') index = Math.min(11, row + 1) * 80 + column;
    else return;
    event.preventDefault(); selectCell(index); svg.querySelector(`[data-index="${index}"]`).focus();
  });
  function download(blob, name) {
    const url = URL.createObjectURL(blob), anchor = document.createElement('a');
    anchor.href = url; anchor.download = name; document.body.append(anchor); anchor.click(); anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  function exportedSvg() {
    const clone = svg.cloneNode(true); clone.querySelector('[data-editor]').remove();
    clone.removeAttribute('role'); clone.removeAttribute('aria-label');
    return new XMLSerializer().serializeToString(clone);
  }
  $('cardSvg').addEventListener('click', () => download(new Blob([exportedSvg()], { type: 'image/svg+xml;charset=utf-8' }), 'besm6_punchcard.svg'));
  $('cardPng').addEventListener('click', async () => {
    $('cardPng').disabled = true;
    const url = URL.createObjectURL(new Blob([exportedSvg()], { type: 'image/svg+xml' }));
    try {
      const image = new Image(); image.src = url; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = Math.round(geometry.width / 25.4 * 600); canvas.height = Math.round(geometry.height / 25.4 * 600);
      canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
      const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
      if (!blob) throw new Error('Не удалось создать PNG.');
      const bytes = new Uint8Array(await blob.arrayBuffer()), chunk = new Uint8Array(21), view = new DataView(chunk.buffer);
      view.setUint32(0, 9); chunk.set([112, 72, 89, 115], 4); view.setUint32(8, 23622); view.setUint32(12, 23622); chunk[16] = 1;
      let crc = 0xffffffff;
      for (const byte of chunk.subarray(4, 17)) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
      view.setUint32(17, (crc ^ 0xffffffff) >>> 0);
      download(new Blob([bytes.subarray(0, 33), chunk, bytes.subarray(33)], { type: 'image/png' }), 'besm6_punchcard.png');
    } catch (error) { message(error.message, true); }
    finally { URL.revokeObjectURL(url); $('cardPng').disabled = false; }
  });
  function symbols() {
    const buttons = Object.keys(table()).filter(character => character !== ' ').map(character => {
      const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-sm btn-outline-secondary'; button.textContent = character;
      button.setAttribute('aria-label', `Вставить ${character}`);
      button.addEventListener('click', () => { const input = $('cardText'); input.setRangeText(character, input.selectionStart, input.selectionEnd, 'end'); input.focus(); encode(); });
      return button;
    });
    $('cardSymbols').replaceChildren(...buttons); $('cardCapacity').textContent = `${capacity()} символов`; $('cardText').maxLength = capacity();
  }
  $('cardText').addEventListener('input', encode);
  $('cardEncoding').addEventListener('change', () => { if (tables) { symbols(); encode(); } });
  $('cardClear').addEventListener('click', () => { $('cardText').value = ''; holes.fill(false); render(); message('Пустая карта'); });
  $('cardToggle').addEventListener('click', () => {
    if (!$('cardColumn').checkValidity() || !$('cardColumn').value) { message('Колонка: целое число от 1 до 80.', true); return; }
    toggle(Number($('cardRow').value) * 80 + Number($('cardColumn').value) - 1);
  });
  $('cardRow').replaceChildren(...rows.map((label, index) => { const option = document.createElement('option'); option.value = String(index); option.textContent = String(label); return option; }));
  fetch('/api/punchcards/codebook').then(response => { if (!response.ok) throw new Error('Не удалось загрузить кодировки.'); return response.json(); }).then(data => {
    tables = data; symbols(); encode();
    for (const id of ['cardText', 'cardToggle', 'cardClear', 'cardSvg', 'cardPng']) $(id).disabled = false;
  }).catch(error => message(error.message, true));
})();
