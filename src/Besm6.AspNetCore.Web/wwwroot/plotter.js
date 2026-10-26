/* Plotter command formats follow https://github.com/besm6/dubna/blob/master/plotter.cpp
 * Copyright (c) 2024 Serge Vakulenko
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
(() => {
  const colors = ['#111827', '#dc2626', '#15803d', '#2563eb', '#a16207', '#0e7490'];
  function segments(data, type) {
    const lines = [];
    let x = 0, y = 0, color = colors[0], positioned = type === 'calcomp', penUp = false;
    const point = (nextX, nextY, draw) => {
      if (draw && positioned && (x !== nextX || y !== nextY)) {
        const last = lines.at(-1);
        const dx = nextX - x, dy = nextY - y;
        if (last && last.color === color && last.x1 === x && last.y1 === y
          && (last.x1 - last.x0) * dy === (last.y1 - last.y0) * dx
          && (last.x1 - last.x0) * dx + (last.y1 - last.y0) * dy > 0) {
          last.x1 = nextX; last.y1 = nextY;
        } else lines.push({ x0: x, y0: y, x1: nextX, y1: nextY, color });
      }
      x = nextX; y = nextY; positioned = true;
    };
    if (type === 'watanabe') {
      for (const line of data.split(/\r?\n/)) {
        const pen = /^J\s*([1-6])\s*$/.exec(line);
        if (pen) { color = colors[Number(pen[1]) - 1]; continue; }
        const command = /^([A-Z])\s*(-?\d+)\s*,\s*(-?\d+)\s*$/.exec(line);
        if (command) point(Number(command[2]), Number(command[3]), command[1] === 'D');
      }
    } else if (type === 'tektronix') {
      for (let i = 0; i < data.length;) {
        let move = false;
        if (data.charCodeAt(i) === 29) { move = true; i++; }
        if (i + 4 > data.length) break;
        const nextY = ((data.charCodeAt(i) & 31) << 5) | (data.charCodeAt(i + 1) & 31);
        const nextX = ((data.charCodeAt(i + 2) & 31) << 5) | (data.charCodeAt(i + 3) & 31);
        point(nextX, nextY, !move); i += 4;
      }
    } else if (type === 'calcomp') {
      for (const character of data) {
        const byte = character.charCodeAt(0);
        if (byte & 4) penUp = true;
        if (byte & 8) penUp = false;
        const dx = byte & 2 ? 1 : byte & 1 ? -1 : 0;
        const dy = byte & 32 ? -1 : byte & 16 ? 1 : 0;
        if (dx || dy) point(x + dx, y + dy, !penUp);
      }
    }
    return lines;
  }

  window.Besm6Plotter = class {
    constructor(canvas, raw, empty) { this.canvas = canvas; this.raw = raw; this.empty = empty; this.reset(); }
    reset() { this.buffers = { watanabe: '', calcomp: '', tektronix: '' }; this.render('watanabe'); }
    append(type, text) { this.buffers[type] += text; }
    render(type) {
      const data = this.buffers[type];
      const lines = segments(data, type);
      this.raw.textContent = data.replace(/[\x00-\x09\x0b-\x1f\x7f-\xff]/g,
        ch => `\\x${ch.charCodeAt(0).toString(16).padStart(2, '0')}`);
      this.empty.hidden = lines.length > 0;
      Besm6I18n.text(this.empty, data.length ? 'Device data received; no drawable lines yet.' : 'No plotter output yet.');
      const { canvas } = this, context = canvas.getContext('2d');
      context.fillStyle = '#ffffff'; context.fillRect(0, 0, canvas.width, canvas.height);
      canvas.dataset.segments = String(lines.length);
      if (!lines.length) return;
      let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
      for (const line of lines) {
        minX = Math.min(minX, line.x0, line.x1); maxX = Math.max(maxX, line.x0, line.x1);
        minY = Math.min(minY, line.y0, line.y1); maxY = Math.max(maxY, line.y0, line.y1);
      }
      const scale = Math.min((canvas.width - 32) / Math.max(1, maxX - minX), (canvas.height - 32) / Math.max(1, maxY - minY));
      const offsetX = (canvas.width - (maxX - minX) * scale) / 2;
      const offsetY = (canvas.height - (maxY - minY) * scale) / 2;
      context.lineWidth = 1.5; context.lineCap = 'round';
      for (const color of colors) {
        context.beginPath(); context.strokeStyle = color;
        for (const line of lines) if (line.color === color) {
          context.moveTo(offsetX + (line.x0 - minX) * scale, offsetY + (maxY - line.y0) * scale);
          context.lineTo(offsetX + (line.x1 - minX) * scale, offsetY + (maxY - line.y1) * scale);
        }
        context.stroke();
      }
    }
  };
})();
