(() => {
  const ns = 'http://www.w3.org/2000/svg';
  const geometry = Object.freeze({ width: 187.325, height: 82.55, cut: 6.35, holeWidth: 1.397, holeHeight: 3.175, pitchX: 2.2098, pitchY: 6.35, firstX: 4.5466, firstY: 4.7625 });
  function create(holes, caption = '') {
    const node = (name, attrs, text) => {
      const element = document.createElementNS(ns, name); for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, value);
      if (text != null) element.textContent = text; return element;
    };
    const svg = node('svg', { xmlns: ns, width: `${geometry.width}mm`, height: `${geometry.height}mm`, viewBox: `0 0 ${geometry.width} ${geometry.height}`, role: 'img', 'aria-label': caption || 'Перфокарта' });
    svg.append(node('polygon', { points: `${geometry.cut},0 ${geometry.width},0 ${geometry.width},${geometry.height} 0,${geometry.height} 0,${geometry.cut}`, fill: '#FAD7B9', stroke: '#9D7E61', 'stroke-width': .3 }));
    const print = node('g', { fill: '#9D7E61', 'font-family': 'Courier New, monospace', 'font-weight': 'bold', 'text-anchor': 'middle', 'aria-hidden': 'true' });
    print.append(node('text', { x: 7, y: 2.8, 'text-anchor': 'start', 'font-size': 1.8 }, caption));
    for (let column = 0; column < 80; column++) {
      const x = geometry.firstX + column * geometry.pitchX + geometry.holeWidth / 2;
      if (column === 0 || (column + 1) % 2 === 0) print.append(node('text', { x, y: 16, 'font-size': 1.1 }, String(column + 1)));
      for (let row = 2; row < 12; row++) if (!holes[row * 80 + column])
        print.append(node('text', { x, y: geometry.firstY + row * geometry.pitchY + geometry.holeHeight / 2 + .5, 'font-size': 1.5, opacity: .6 }, String(row - 2)));
    }
    const punched = node('g', { fill: '#2b1d14', 'data-holes': '' });
    for (let row = 0; row < 12; row++) for (let column = 0; column < 80; column++) if (holes[row * 80 + column])
      punched.append(node('rect', { x: (geometry.firstX + column * geometry.pitchX).toFixed(4), y: (geometry.firstY + row * geometry.pitchY).toFixed(4), width: geometry.holeWidth, height: geometry.holeHeight }));
    svg.append(print, punched); return svg;
  }
  window.Besm6CardView = { geometry, create };
  window.Besm6PunchOutput = class {
    cards = []; frame = 0; braille = '';
    constructor() {
      document.getElementById('punchFormat').addEventListener('change', () => this.render());
      document.getElementById('punchTab').addEventListener('shown.bs.tab', () => this.render());
    }
    append(data) { this.cards.push(data.rows); if (!this.frame) this.frame = requestAnimationFrame(() => { this.frame = 0; this.render(); }); }
    appendText(text) { this.braille += text; if (document.getElementById('punchFormat').value === 'braille') document.getElementById('punchOutput').append(document.createTextNode(text)); }
    clear() { this.cards = []; this.braille = ''; this.render(); }
    render() {
      const format = document.getElementById('punchFormat').value, text = document.getElementById('punchOutput'), visual = document.getElementById('punchVisual');
      text.hidden = format === 'card'; visual.hidden = format !== 'card';
      text.classList.toggle('punch-braille', format === 'braille');
      document.getElementById('punchCount').textContent = `${this.cards.length} cards`;
      if (format === 'braille') { text.textContent = this.braille; return; }
      if (format === 'card') {
        visual.replaceChildren(...this.cards.map((rows, index) => create(rows.join('').split('').map(c => c === '1'), `БЭСМ-6 · ${index + 1}`)));
      } else {
        const labels = [12, 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        text.textContent = this.cards.map((rows, index) => `Card ${index + 1}\n    ${Array.from({ length: 80 }, (_, i) => (i + 1) % 10).join('')}\n`
          + rows.map((row, i) => `${String(labels[i]).padStart(2, ' ')} |${row.replace(/1/g, format === 'ascii' ? 'O' : '□').replace(/0/g, '.')}|`).join('\n')).join('\n\n');
      }
    }
  };
})();
