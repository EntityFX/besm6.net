(() => {
  'use strict';
  const t = value => Besm6I18n.t(value);
  const element = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };
  const text = (node, value) => { if (node.textContent !== value) node.textContent = value; };
  const parse = (raw, radix, bits) => {
    if (raw == null || raw === '' || (typeof raw === 'number' && !Number.isSafeInteger(raw))) return null;
    try {
      const value = typeof raw === 'boolean' ? BigInt(Number(raw))
        : BigInt((radix === 16 ? '0x' : radix === 8 ? '0o' : '') + String(raw));
      return value >= 0n && value < (1n << BigInt(bits)) ? value : null;
    } catch { return null; }
  };
  const label = name => Besm6I18n.language === 'ru' ? name : name
    .replace(/БРЗ/g, 'BRZ').replace(/БРС/g, 'BRS').replace(/БРЧ/g, 'BRCh')
    .replace(/РГПр/g, 'RGP').replace(/РСП/g, 'RSP').replace(/МСП/g, 'MSP')
    .replace(/ГРП/g, 'GRP').replace(/РП/g, 'RP').replace(/РЗ/g, 'RZ').replace(/Маска/g, 'Mask')
    .replace(/^РИ$/, 'RI')
    .replace(/СчАК/g, 'SchAK').replace(/РгВ/g, 'RgV').replace(/РгМ/g, 'RgM')
    .replace(/СМ/g, 'SM').replace(/РМР/g, 'RMR').replace(/РМ/g, 'RM').replace(/БАЗ/g, 'BAZ')
    .replace(/БАБУ/g, 'BABU').replace(/ИСУ А/g, 'ISU A').replace(/ИСУ Б/g, 'ISU B').replace(/мл\./g, 'low')
    .replace(/М-регистры \(дубль\)/g, 'M registers (copy)').replace(/адр\./g, 'addr.')
    .replace(/У54–У51/g, 'U54–U51').replace(/ПАУ/g, 'PAU').replace(/УПР/g, 'UPR').replace(/^УУ$/, 'UU');
  const access = value => ({ ReadOnly: 'read-only', WriteOnly: 'write-only',
    ReadWrite: 'read/write', HostOnly: 'host-only' }[value] || '');

  // One schematic renderer for the console and its single standalone mirror.
  // No physical coordinates, presence masks, TEZ cells, timers or machine reads.
  class LampPanel {
    constructor(root, config = Besm6LampPanelConfig) {
      this.root = root; this.config = config;
      this.registers = null; this.detail = null; this.state = 'Idle'; this.base = 8; this.frame = 0;
      this.lastPaint = -Infinity; this.repaintTimer = 0;
      this.rows = new Map(); this.groups = new Map(); this.racks = new Map(); this.tiers = new Map();
      this.columns = new Map();
      const host = root.querySelector('.lamp-banks');
      this.rackHost = host;
      host.replaceChildren(); host.className = 'lamp-banks lamp-racks';
      for (const definition of config.racks) {
        const columnId = definition.column || definition.id;
        let column = this.columns.get(columnId)?.node;
        if (!column) {
          column = element('section', 'lamp-column');
          column.dataset.column = columnId;
          host.append(column);
          this.columns.set(columnId, { node: column, racks: [] });
        }
        const rack = element('section', 'lamp-rack'); rack.dataset.rack = definition.id;
        const heading = element('h4', 'lamp-rack-heading'); rack.append(heading); column.append(rack);
        this.racks.set(definition.id, { definition, heading, node: rack });
        this.columns.get(columnId).racks.push(rack);
        for (let number = 1; number <= 4; number++) {
          const tier = element('section', 'lamp-tier');
          Object.assign(tier.dataset, { rack: definition.id, tier: String(number) });
          const title = element('h5', 'lamp-tier-heading'); tier.append(title); rack.append(tier);
          this.tiers.set(`${definition.id}:${number}`, { node: tier, title, number });
        }
      }
      for (const definition of config.banks) {
        const section = element('section', 'lamp-bank');
        Object.assign(section.dataset, { bank: definition.id, line: definition.line || 'single' });
        const title = element('h6', 'lamp-bank-heading'), rows = element('div', 'lamp-rows');
        section.append(title, rows); this.tiers.get(`${definition.rack}:${definition.tier}`).node.append(section);
        this.groups.set(definition.id, { definition, title, rows, node: section });
      }
      for (const definition of config.registers) this.addRow(definition);
      for (const buffer of config.buffers) {
        for (let slot = 0; slot < buffer.count; slot++) {
          const id = `${buffer.prefix}[${slot}]`;
          this.addRow({ id, bank: buffer.bank, bits: 48, controlBits: 2, buffer, slot });
          if (buffer.addressBits) this.addRow({ id: `${id}:address`, label: `${buffer.prefix} адр.[${slot}]`,
            bank: buffer.bank, bits: buffer.addressBits, buffer, slot, address: true });
        }
      }
      this.render();
    }

    addRow(definition) {
      const group = this.groups.get(definition.bank), { bits, controlBits = 0 } = definition;
      const node = element('div', 'lamp-row');
      Object.assign(node.dataset, { reg: definition.id, register: definition.id, rack: group.definition.rack,
        tier: String(group.definition.tier), line: group.definition.line || 'single',
        width: bits == null ? '' : String(bits), storageWidth: bits == null ? '' : String(bits + controlBits),
        placement: this.config.placement,
        source: definition.register ? `detail.registers.${definition.register}`
          : definition.groupRegisters ? `detail.registers.${definition.groupRegisters.join(',')}`
          : definition.source ? [definition.source.snapshot, ...definition.source.path].join('.')
          : definition.buffer ? 'detail.buffers' : '—' });
      if (definition.address) node.dataset.kind = 'address';
      node.setAttribute('role', 'img');
      const name = element('span', 'lamp-register-label');
      const track = element('div', 'lamp-track'); track.setAttribute('aria-hidden', 'true');
      const lamps = [];
      {
        const main = element('span', 'lamp-word');
        main.style.setProperty('--lamp-bits', bits);
        // Main word: MSB first. Correction bits grow to the right: 49, then 50.
        for (let bit = bits; bit > 0; bit--) {
          const lamp = element('span', 'register-lamp'); lamp.dataset.bit = String(bit);
          main.append(lamp); lamps.push(lamp);
        }
        track.append(main);
        if (controlBits) {
          const correction = element('span', 'lamp-correction');
          for (let bit = bits + 1; bit <= bits + controlBits; bit++) {
            const lamp = element('span', 'register-lamp lamp-control-bit'); lamp.dataset.bit = String(bit);
            correction.append(lamp); lamps.push(lamp);
          }
          track.append(correction);
        }
      }
      node.append(name, track); group.rows.append(node);
      this.rows.set(definition.id, { definition, node, name, lamps, signature: null });
    }

    visible() {
      const pane = this.root.closest('.tab-pane');
      return !pane || pane.classList.contains('active');
    }
    updateRegisters(value) { this.registers = value; this.schedule(); }
    updateDetail(value) { this.detail = value; this.schedule(); }
    updateState(value) { this.state = value; this.schedule(); }
    setBase(value) { this.base = value; this.schedule(); }
    schedule() {
      if (this.state !== 'Running' && this.repaintTimer) {
        clearTimeout(this.repaintTimer); this.repaintTimer = 0;
      }
      if (!this.visible() || this.frame) return;
      const delay = 1000 - (performance.now() - this.lastPaint);
      if (this.state === 'Running' && delay > 0) {
        if (!this.repaintTimer) this.repaintTimer = setTimeout(() => {
          this.repaintTimer = 0; this.schedule();
        }, delay);
        return;
      }
      this.frame = requestAnimationFrame(() => { this.frame = 0; if (this.visible()) this.render(); });
    }

    sample(definition) {
      const { source, buffer, slot, bits } = definition;
      if (definition.register || definition.groupRegisters) {
        const registers = this.detail?.registers || [];
        const byName = new Map(registers.map(entry => [entry.name, entry]));
        if (definition.register) {
          const entry = byName.get(definition.register);
          if (!entry) return { value: null, control: null, note: 'No snapshot' };
          const note = [t(access(entry.access)), entry.address, entry.alias].filter(Boolean).join(' · ');
          return { value: parse(entry.value, 10, entry.width ?? bits), control: null, note };
        }
        const entries = definition.groupRegisters.map(name => byName.get(name));
        if (entries.some(entry => !entry)) return { value: null, control: null, note: 'No snapshot' };
        const width = definition.fieldBits ?? 9;
        let packed = 0n;
        for (let index = entries.length - 1; index >= 0; index--)
          packed = (packed << BigInt(width)) | BigInt(entries[index].value ?? 0);
        const pages = entries.map(entry => entry.value).join(', ');
        const note = [t(access(entries[0].access)), entries[0].address, pages].filter(Boolean).join(' · ');
        return { value: packed, control: null, note };
      }
      if (!buffer) return {
        value: bits == null ? null : parse(source?.path.reduce((value, key) => value?.[key], this[source.snapshot]), source?.radix, bits),
        control: null, note: definition.note || (!source ? 'No snapshot' : '')
      };
      const entries = this.detail?.buffers || [];
      const physical = entries.some(entry => entry.name === buffer.prefix);
      const entry = entries.find(entry => entry.slot === slot && entry.name === (physical ? buffer.prefix : `${buffer.prefix} (MRU)`));
      const notes = [];
      if (!physical && entry) notes.push(t('MRU order, not physical slots'));
      if (definition.address) {
        // MRU address rows are explicitly labelled; never attach them to physical slots.
        const addressed = entry?.address != null ? entry
          : entries.find(item => item.name === buffer.addressName && item.slot === slot);
        const raw = parse(addressed?.address, 10, 16);
        const marker = addressed?.name === buffer.addressName;
        if (addressed?.name.includes('MRU')) notes.push(t('MRU order, not physical slots'));
        return { value: raw == null ? null : marker ? raw & 0x7FFFn : parse(addressed.address, 10, bits),
          control: null, note: notes.join(' · ') };
      }
      for (const [field, title] of [['ready', 'Ready'], ['active', 'Active'], ['pendingWrite', 'Pending write']])
        if (entry?.[field] != null) notes.push(`${t(title)}=${Number(entry[field])}`);
      return { value: buffer.requireRawWord && entry?.raw50 == null ? null : parse(entry?.value, 10, bits),
        control: parse(entry?.raw50, 10, 50), note: notes.join(' · ') };
    }

    paint(row) {
      const { definition, node, name, lamps } = row;
      const { value, control, note } = this.sample(definition);
      const signature = `${value}:${control}:${note}:${this.base}:${Besm6I18n.language}`;
      if (row.signature === signature) return;
      row.signature = signature;
      const display = label(definition.label || definition.id);
      const formatted = value == null ? t('No snapshot') : value.toString(this.base).toUpperCase()
        .padStart(this.base === 10 ? 1 : Math.ceil(definition.bits / (this.base === 8 ? 3 : 4)), '0')
          + ' ' + ({ 8: 'OCT', 10: 'DEC', 16: 'HEX' }[this.base]);
      text(name, display); name.title = display;
      node.dataset.known = String(value != null);
      node.dataset.value = value == null ? '' : value.toString(16).toUpperCase();
      node.title = `${display}: ${formatted}, ${definition.bits ?? '?'} ${t('bits')}${note ? ' · ' + t(note) : ''}`;
      node.setAttribute('aria-label', node.title + (definition.controlBits ? ', ' + t('Control bits 50/49')
        + ': ' + (control == null ? t('No snapshot') : `${Number((control >> 49n) & 1n)}/${Number((control >> 48n) & 1n)}`) : ''));
      for (const lamp of lamps) {
        const bit = Number(lamp.dataset.bit), sample = bit > definition.bits ? control : value;
        const on = sample != null && ((sample >> BigInt(bit - 1)) & 1n) !== 0n;
        lamp.classList.toggle('is-on', on);
        lamp.dataset.on = String(on);
        lamp.title = `${display} · ${t('Bit')} ${bit}: ${sample == null ? '—' : on ? '1' : '0'}${note ? ' · ' + t(note) : ''}`;
      }
    }

    render() {
      const now = performance.now();
      if (this.state === 'Running' && now - this.lastPaint < 1000) { this.schedule(); return; }
      this.lastPaint = now;
      if (this.root.dataset.state !== this.state) this.root.dataset.state = this.state;
      const snapshot = String(this.registers != null);
      if (this.root.dataset.snapshot !== snapshot) this.root.dataset.snapshot = snapshot;
      for (const rack of this.racks.values()) text(rack.heading, t(rack.definition.title));
      for (const tier of this.tiers.values()) text(tier.title, `${t('Tier')} ${tier.number}`);
      for (const group of this.groups.values()) text(group.title, t(group.definition.title));
      for (const row of this.rows.values()) this.paint(row);
      for (const group of this.groups.values()) {
        const hidden = [...group.rows.children].every(row => row.hidden);
        if (group.node.hidden !== hidden) group.node.hidden = hidden;
      }
      for (const tier of this.tiers.values()) {
        const hidden = [...tier.node.querySelectorAll('.lamp-bank')].every(group => group.hidden);
        if (tier.node.hidden !== hidden) tier.node.hidden = hidden;
      }
      for (const rack of this.racks.values()) {
        const hidden = [...rack.node.querySelectorAll('.lamp-tier')].every(tier => tier.hidden);
        if (rack.node.hidden !== hidden) rack.node.hidden = hidden;
      }
      for (const column of this.columns.values()) {
        const hidden = column.racks.every(rack => rack.hidden);
        if (column.node.hidden !== hidden) column.node.hidden = hidden;
      }
      const columnCount = String(Math.max(1, [...this.columns.values()].filter(column => !column.node.hidden).length));
      if (this.rackHost.style.getPropertyValue('--lamp-rack-count') !== columnCount)
        this.rackHost.style.setProperty('--lamp-rack-count', columnCount);
    }
  }
  window.Besm6LampPanel = LampPanel;
})();
