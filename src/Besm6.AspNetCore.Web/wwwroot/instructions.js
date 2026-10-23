(() => {
  const $ = id => document.getElementById(id);
  let operations = [], decoded, timer, sequence = 0;
  const number = () => BigInt('0x' + (decoded?.hex || '0'));
  const width = () => Number($('instructionWidth').value);
  const syntax = () => $('instructionSyntax').value;
  const half = () => width() === 24 ? 0 : Number($('instructionHalf').value);
  function input(value) { $('instructionValue').value = value.toString(Number($('instructionRadix').value)).toUpperCase(); }
  async function api(action, body) {
    const response = await fetch('/api/instructions/' + action, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const result = await response.json(); if (!response.ok) throw new Error(result.error || 'Ошибка преобразования.'); return result;
  }
  function render() {
    if (!decoded) return;
    $('instructionDecoded').textContent = `OCT ${decoded.octal}\nHEX ${decoded.hex}\nDEC ${decoded.decimalValue}\n` + decoded.halves.map((instruction, i) =>
      `${decoded.halves.length === 1 ? '' : i === 0 ? 'L: ' : 'R: '}${instruction[syntax()]}\n  M${instruction.register} · ${instruction.long ? 'длинная' : 'короткая'} · op ${instruction.opcode.toString(8)} · адрес ${instruction.address.toString(8).padStart(5, '0')} (${instruction.address} DEC) · расширение ${instruction.extension ? 1 : 0}`).join('\n');
    const bits = [];
    decoded.halves.forEach((instruction, index) => {
      const group = document.createElement('div'); group.className = 'instruction-bit-row';
      for (let bit = 23; bit >= 0; bit--) {
        const button = document.createElement('button'), on = (instruction.value & (1 << bit)) !== 0;
        const field = bit >= 20 ? 'register' : bit === 19 || (!instruction.long && bit === 18) ? 'format' : bit >= (instruction.long ? 15 : 12) ? 'opcode' : 'address';
        button.type = 'button'; button.className = 'instruction-bit bit-' + field; button.textContent = on ? '1' : '0';
        button.setAttribute('aria-label', `${index === 0 ? 'L' : 'R'}: бит ${bit + 1}, ${field}`); button.setAttribute('aria-pressed', String(on));
        button.addEventListener('click', () => { const shift = BigInt(bit + (decoded.halves.length === 2 && index === 0 ? 24 : 0)); input(number() ^ (1n << shift)); decode(); });
        group.append(button);
      }
      bits.push(group);
    });
    $('instructionBits').replaceChildren(...bits);
    $('instructionHalf').disabled = width() === 24;
    const selected = decoded.halves[half()] || decoded.halves[0];
    $('instructionOpcode').value = String(selected.opcode); $('instructionRegister').value = String(selected.register);
    $('instructionAddress').value = selected.address.toString(8).padStart(5, '0'); $('instructionText').value = selected[syntax()];
    for (const option of $('instructionOpcode').options) { const op = operations.find(op => String(op.opcode) === option.value); option.textContent = `${op.opcode.toString(8).padStart(3, '0')} · ${op[syntax()]} · ${op.long ? 'long' : 'short'}`; }
  }
  async function decode() {
    const current = ++sequence;
    try {
      const result = await api('decode', { value: $('instructionValue').value, radix: Number($('instructionRadix').value), width: width() });
      if (current !== sequence) return;
      decoded = result; $('instructionError').textContent = ''; render();
    } catch (error) { if (current === sequence) $('instructionError').textContent = error.message; }
  }
  async function encode(text) {
    try {
      if (!$('instructionRegister').checkValidity()) throw new Error('Модификатор: 0–15 DEC.');
      const raw = $('instructionAddress').value.trim(); if (!/^-?[0-7]+$/.test(raw)) throw new Error('Адрес задаётся в OCT.');
      const result = await api('encode', text ? { text: $('instructionText').value, syntax: syntax() } :
        { opcode: Number($('instructionOpcode').value), register: Number($('instructionRegister').value), address: parseInt(raw, 8) });
      const value = BigInt('0x' + result.hex);
      input(width() === 24 ? value : half() === 0 ? (value << 24n) | (number() & 0xFFFFFFn) : (number() & 0xFFFFFF000000n) | value);
      await decode();
    } catch (error) { $('instructionError').textContent = error.message; }
  }
  $('instructionValue').addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(decode, 100); });
  $('instructionWidth').addEventListener('change', () => { input(number() & (width() === 24 ? 0xFFFFFFn : 0xFFFFFFFFFFFFn)); decode(); });
  $('instructionRadix').addEventListener('change', () => { input(number()); });
  $('instructionSyntax').addEventListener('change', render); $('instructionHalf').addEventListener('change', render);
  $('instructionEncode').addEventListener('click', () => encode(false)); $('instructionAssemble').addEventListener('click', () => encode(true));
  fetch('/api/instructions').then(response => response.json()).then(data => {
    operations = data;
    $('instructionOpcode').replaceChildren(...data.map(op => { const option = document.createElement('option'); option.value = String(op.opcode); return option; }));
    $('instructionEncode').disabled = false; $('instructionAssemble').disabled = false; return decode();
  }).catch(error => { $('instructionError').textContent = error.message; });
})();
