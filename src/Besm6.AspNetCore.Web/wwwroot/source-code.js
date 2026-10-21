(() => {
  const words = new Set(('program subroutine function end stop call print read write format dimension common data do if then else endif enddo goto go to return integer real double precision logical character complex implicit external parameter continue equivalence save begin procedure array for step until while switch own value boolean true false label string comment atx xta aex aax aox arx avx a*x a/x a+x a-x x-a amx e+x e-x asx asn anx ntr rta yta ati ita its sti mtj j+m vtm utm uza u1a uj vjm vlm vim vzm utc wtc mod acx apx aux xts').split(' '));
  // Build nodes from text, so source code and strings can never become markup.
  window.Besm6Source = {
    disassembly(text, radix) { return text.replace(/(?<![\w*])[0-7]+\b/g, value => parseInt(value, 8).toString(radix).toUpperCase()); },
    render(element, source) {
      const fragment = document.createDocumentFragment();
      let plain = '';
      let language = '';
      for (const line of source.split(/(?<=\n)/)) {
        const control = /^\s*\*(?:name|fortran|forex|algol|assem|execute|end|tape|trans|b\b)/i.test(line);
        if (control) {
          const match = line.match(/^\s*\*(fortran|forex|algol|assem|b)\b/i); if (match) language = match[1].toLowerCase();
          add('control', line); continue;
        }
        if (/^(?:[cC*](?:\s|$)|\s*\/\/)/.test(line) && ['fortran', 'forex', ''].includes(language)) { add('comment', line); continue; }
        const tokens = /('(?:''|[^'])*'|"(?:""|[^"])*"|\/\/[^\n]*|![^\n]*|`[0-7]+|\b\d+(?:\.\d*)?(?:[edED][+-]?\d+)?\b|[\p{L}_][\p{L}\p{N}_]*|\*\d+|[^\p{L}\p{N}_])/gu;
        for (const match of line.matchAll(tokens)) {
          const text = match[0];
          const type = /^['"]/.test(text) ? 'string' : /^(?:!|\/\/)/.test(text) ? 'comment' : /^(?:\d|`)/.test(text) ? 'number'
            : words.has(text.toLowerCase()) || /^\*\d+$/.test(text) ? 'keyword' : '';
          add(type, text);
        }
      }
      flush(); element.replaceChildren(fragment);
      function flush() { if (plain) { fragment.append(document.createTextNode(plain)); plain = ''; } }
      function add(type, text) {
        if (!type) plain += text;
        else { flush(); const span = document.createElement('span'); span.className = 'syntax-' + type; span.textContent = text; fragment.append(span); }
      }
    }
  };
  const input = document.getElementById('sourceText'), preview = document.getElementById('sourcePreview');
  const update = () => { Besm6Source.render(preview, input.value); window.dispatchEvent(new Event('sourceChanged')); };
  input.addEventListener('input', update);
  input.addEventListener('keydown', event => {
    if (event.key === 'Tab') { event.preventDefault(); input.setRangeText('    ', input.selectionStart, input.selectionEnd, 'end'); update(); }
  });
  document.getElementById('exampleEdit').addEventListener('click', () => {
    input.value = document.getElementById('exampleSource').textContent; update();
    bootstrap.Tab.getOrCreateInstance(document.getElementById('editorTab')).show(); input.focus();
  });
  update();
})();
