(() => {
  'use strict';
  // Only registers backed by emulator snapshots. No physical lamp/TEZ map.
  const source = (snapshot, ...path) => ({ snapshot, path, radix: 10 });
  const register = (id, label, bank, bits, field, radix = 8) => ({
    id, label, bank, bits, source: { ...source('registers', field), radix }
  });
  const config = {
    version: 3,
    placement: 'conditional-register-map',
    racks: [
      { id: 'brus', title: 'BRUS' },
      { id: 'uu', title: 'UU' },
      { id: 'au', title: 'AU' }
    ],
    banks: [
      { id: 'protection', rack: 'brus', tier: 1, title: 'Operand protection' },
      { id: 'read', rack: 'brus', tier: 2, line: 'upper', title: 'Upper line — BRCh' },
      { id: 'instruction', rack: 'brus', tier: 2, line: 'lower', title: 'Lower line — BRS' },
      { id: 'write', rack: 'brus', tier: 3, title: 'Write buffers — BRZ' },
      { id: 'interrupts', rack: 'brus', tier: 4, title: 'Interrupts' },
      { id: 'control', rack: 'uu', tier: 1, title: 'Supervisor control' },
      { id: 'index', rack: 'uu', tier: 2, title: 'Index registers' },
      { id: 'cpu', rack: 'uu', tier: 4, title: 'Main registers [?]' },
      { id: 'arithmetic', rack: 'au', tier: 3, title: 'Arithmetic registers' }
    ],
    registers: [
      { id: 'RZ', label: 'РЗ', bank: 'protection', bits: 32, source: source('detail', 'operandProtection') },
      { id: 'INTERRUPTS', label: 'ГРП', bank: 'interrupts', bits: 48, source: source('detail', 'interrupts') },
      { id: 'CONTROL', label: 'УУ', bank: 'control', bits: 15, source: source('detail', 'controlFlags') },
      ...Array.from({ length: 15 }, (_, i) => ({ id: `M${i + 1}`, bank: 'index', bits: 15,
        source: { ...source('registers', 'm', i + 1), radix: 8 } })),
      register('K', 'СчАК (K)', 'cpu', 15, 'k'),
      register('R', 'R', 'cpu', 6, 'r'),
      register('C', 'C', 'cpu', 15, 'c'),
      { id: 'M0', bank: 'cpu', bits: 15, source: { ...source('registers', 'm', 0), radix: 8 } },
      register('A', 'СМ (A)', 'arithmetic', 48, 'a', 16),
      register('Y', 'Y (РМР)', 'arithmetic', 48, 'y', 16)
    ],
    buffers: [
      { bank: 'read', prefix: 'БРЧ', count: 4, requireRawWord: true },
      { bank: 'instruction', prefix: 'БРС', count: 4, addressBits: 15, addressName: 'БАС/БРС (MRU)' },
      { bank: 'write', prefix: 'БРЗ', count: 8 }
    ]
  };
  const freeze = value => {
    if (value && typeof value === 'object') {
      for (const child of Object.values(value)) freeze(child);
      Object.freeze(value);
    }
    return value;
  };
  window.Besm6LampPanelConfig = freeze(config);
})();
