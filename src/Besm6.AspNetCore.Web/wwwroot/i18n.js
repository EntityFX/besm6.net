(() => {
  'use strict';
  // English keys also serve as the fallback. Russian aliases allow the existing
  // technical UI to be localized without touching guest source or device output.
  const messages = {
    'BESM-6 Console': 'Консоль БЭСМ-6',
    'Main navigation': 'Основная навигация',
    'About BESM-6': 'О БЭСМ-6',
    'Language': 'Язык',
    'Connecting…': 'Подключение…',
    'Connected': 'Подключено',
    'Reconnecting…': 'Переподключение…',
    'Connecting session…': 'Подключение сессии…',
    'Disconnected': 'Нет соединения',
    'Job and machine state': 'Задание и состояние машины',
    'Job': 'Задание',
    'Machine': 'Машина',
    'Dubna + write/instruction buffers (.dub)': 'Dubna + БРЗ/БРС (.dub)',
    'Supervisor — functional (.json)': 'Супервизор — функциональный (.json)',
    'Supervisor — hardware (.json)': 'Супервизор — аппаратный (.json)',
    'Drop .dub / .json or browse': 'Перетащите .dub / .json или выберите файл',
    'No file selected': 'Файл не выбран',
    'Mode': 'Режим',
    'Debug': 'Отладка',
    'Full run': 'Полный запуск',
    'Speed': 'Скорость',
    'Maximum': 'Максимальная',
    'Original': 'Историческая',
    'Virtual': 'Виртуальная',
    'Instruction limit': 'Лимит команд',
    'Time, s (0 = ∞)': 'Время, с (0 = ∞)',
    'Time limit in seconds: 0 = unlimited.': 'Лимит времени в секундах: 0 — без ограничения.',
    'Guest Clock': 'Гостевой таймер',
    'Auto-refresh': 'Автообновление',
    'Statistics': 'Статистика',
    'Refresh every 300 ms while running. Leave unchecked for faster execution. Device output continues updating.': 'Обновление каждые 300 мс при выполнении. Отключите для ускорения. Вывод устройств продолжает обновляться.',
    'Execution controls': 'Управление выполнением',
    'Run': 'Пуск',
    'Pause': 'Пауза',
    'Resume': 'Продолжить',
    'Step': 'Шаг',
    'Stop': 'Остановить',
    'Reset': 'Сброс',
    'Machine state': 'Состояние машины',
    'Registers': 'Регистры',
    'Number base': 'Система счисления',
    '0 instructions': '0 команд',
    '{count} instructions': '{count} команд',
    '{count} instruction': '{count} команда',
    'Processor registers': 'Регистры процессора',
    'Memory': 'Память',
    'Octal address': 'Восьмеричный адрес',
    'Memory address in octal': 'Восьмеричный адрес памяти',
    'Memory address in decimal': 'Десятичный адрес памяти',
    'Memory address in hexadecimal': 'Шестнадцатеричный адрес памяти',
    'Address ({base})': 'Адрес ({base})',
    'Go': 'Перейти',
    'Decode': 'Расшифровка',
    'Memory words': 'Слова памяти',
    'Write / instruction / read buffers and control': 'БРЗ / БРС / БРЧ и управление',
    'State appears after loading a machine.': 'Состояние появится после загрузки машины.',
    'Extended machine registers': 'Расширенные регистры машины',
    'No buffer registers in this mode.': 'В текущем режиме буферные регистры отсутствуют.',
    'Read buffers require hardware mode': 'БРЧ доступны в аппаратном режиме',
    'interrupts': 'прерывания',
    'Program output': 'Вывод программы',
    'Devices': 'Вкладки симулятора',
    'User guide': 'Инструкция пользователя',
    'Printer': 'Печать',
    'Print output': 'Вывод печати',
    'Punch cards': 'Перфокарты',
    'Punch card output': 'Вывод перфокарт',
    'Plotter': 'Плоттер',
    'Terminal': 'Терминал',
    'Storage': 'Накопители',
    'Card generator': 'Генератор перфокарт',
    'Editor': 'Редактор',
    'Instruction codec': 'Кодер команд',
    'Instructions': 'Команды',
    'Examples': 'Примеры',
    'Format': 'Формат',
    'Braille': 'Брайль',
    'Grid': 'Сетка',
    'Card': 'Карта',
    '{count} cards': '{count} карт',
    'Card {number}': 'Карта {number}',
    'Page {page} · {bytes} bytes': 'Страница {page} · {bytes} байт',
    '0 bytes': '0 байт',
    'Plotter drawing': 'Рисунок плоттера',
    'Plotter drawing preview': 'Предпросмотр рисунка плоттера',
    'No plotter output yet.': 'Вывода плоттера пока нет.',
    'Device data received; no drawable lines yet.': 'Данные устройства получены; линий для рисования пока нет.',
    'Device data': 'Данные устройства',
    'Raw plotter data': 'Исходные данные плоттера',
    'BESM-6 punch card generator': 'Генератор перфокарт БЭСМ-6',
    'Encoding': 'Кодировка',
    'IBM — by columns': 'IBM — по колонкам',
    'UPP-IBM — by rows': 'УПП-IBM — по строкам',
    '{count} characters': '{count} символов',
    'Card text': 'Текст карты',
    'Symbol palette': 'Панель символов',
    'Row': 'Ряд',
    'Column': 'Колонка',
    'Punch / close': 'Пробить / закрыть',
    'Clear': 'Очистить',
    'Click to toggle a hole. Arrows select a cell; Space punches it. UPP: 10 characters per row, odd parity. Matching Latin and Cyrillic letters share a code.': 'Клик — изменить отверстие. Стрелки — выбрать ячейку, пробел — пробить. УПП: 10 символов в ряду, нечётность. Совпадающие латинские и кириллические буквы имеют общий код.',
    'Loading code tables…': 'Загрузка таблиц кодирования…',
    '187.325 × 82.55 mm · 80 × 12 · hole 1.397 × 3.175 mm. SVG: print at 100%, without fit to page.': '187.325 × 82.55 мм · 80 × 12 · отверстие 1.397 × 3.175 мм. SVG: печать 100%, без подгонки к странице.',
    'Card capacity: {capacity} characters; entered {count}.': 'На карте помещается {capacity} символов; введено {count}.',
    'Character “{symbol}” at position {position} is not in this historical encoding.': 'Символ «{symbol}» в позиции {position} отсутствует в этой исторической кодировке.',
    '{count} / {capacity} characters · {holes} holes': '{count} / {capacity} символов · {holes} отверстий',
    'Decoded {count} characters · {holes} holes': 'Декодировано {count} символов · {holes} отверстий',
    'parity: character {number}': 'нечётность: символ {number}',
    'unknown punch: character {number}': 'неизвестная пробивка: символ {number}',
    'Punch card: 80 columns and 12 rows': 'Перфокарта 80 колонок на 12 рядов',
    'Row {row}, column {column}': 'Ряд {row}, колонка {column}',
    'Insert {symbol}': 'Вставить {symbol}',
    'Empty card': 'Пустая карта',
    'Column: an integer from 1 to 80.': 'Колонка: целое число от 1 до 80.',
    'Cannot create PNG.': 'Не удалось создать PNG.',
    'Cannot load code tables.': 'Не удалось загрузить кодировки.',
    'Programs from the example catalog': 'Программы из примеров',
    'Search': 'Поиск',
    'Name or directory': 'Название или каталог',
    'Example': 'Пример',
    'Loading…': 'Загрузка…',
    'Load into machine': 'Загрузить в машину',
    'Run example': 'Запустить пример',
    'Edit copy': 'Изменить копию',
    '.dub programs use the Job settings. JSON presets in web-jobs set the program, mode, speed and toggles; machines contains Supervisor images. Select JSON here, upload a file, or edit it in Editor. Debug starts paused.': 'Программы .dub используют настройки задания. JSON из web-jobs задают программу, режим, скорость и параметры; machines содержит образы супервизора. JSON можно выбрать здесь, загрузить файлом или изменить в редакторе. Отладка начинается с паузы.',
    'Example source': 'Исходный текст примера',
    'Select an example to preview.': 'Выберите пример для просмотра.',
    'Select an example': 'Выберите пример',
    '{count} examples': '{count} примеров',
    'Loading source…': 'Загрузка исходного текста…',
    'Cannot read the example.': 'Не удалось прочитать пример.',
    'Cannot load the example catalog.': 'Не удалось загрузить список примеров.',
    '.dub job / .json machine': 'Задание .dub / машина .json',
    'Program or configuration text': 'Текст программы или конфигурации',
    'Run text': 'Выполнить текст',
    'Machine JSON template': 'Шаблон машины JSON',
    '.dub job or supervisor .json configuration, up to 1 MB. Choose Machine in Job. Hardware mode uses explicit timings from JSON. Editing does not change the running program.': 'Задание .dub или конфигурация супервизора .json, до 1 MB. Выберите машину в настройках задания. Аппаратная модель использует явные тайминги из JSON. Правка текста не меняет выполняемую программу.',
    'Syntax highlighting': 'Подсветка кода',
    'Highlighted source code': 'Подсвеченный исходный код',
    'BESM-6 instruction encoder / decoder': 'Кодер / декодер инструкций БЭСМ-6',
    'Bit width': 'Разрядность',
    '24 bits — one instruction': '24 бита — инструкция',
    '48 bits — two instructions': '48 бит — две инструкции',
    'Number': 'Число',
    'Value': 'Значение',
    'Autocode': 'Автокод',
    'BEMSH': 'БЭМШ',
    'Instruction bits': 'Разряды инструкции',
    'Decoded instructions': 'Декодированные инструкции',
    'Encode into': 'Кодировать в',
    'Left / only half': 'Левую / единственную',
    'Right half': 'Правую',
    'Operation': 'Операция',
    'Index register (DEC)': 'Модификатор (DEC)',
    'Address (OCT)': 'Адрес (OCT)',
    'Mnemonic': 'Мнемоника',
    'Encode fields': 'Кодировать поля',
    'Encode mnemonic': 'Кодировать мнемонику',
    'Click a bit to toggle it. Short: M[24–21], format[20], extension[19], opcode[18–13], address[12–1]. Long: M[24–21], format[20], opcode[19–16], address[15–1]. Registers are DEC; addresses are OCT.': 'Клик по разряду переключает бит. Короткая команда: M[24–21], формат[20], расширение[19], операция[18–13], адрес[12–1]. Длинная: M[24–21], формат[20], операция[19–16], адрес[15–1]. Регистры в мнемонике — DEC, адреса — OCT.',
    'Conversion failed.': 'Ошибка преобразования.',
    'Index register: 0–15 DEC.': 'Модификатор: 0–15 DEC.',
    'Address must be OCT.': 'Адрес задаётся в OCT.',
    'long': 'длинная',
    'short': 'короткая',
    'address': 'адрес',
    'extension': 'расширение',
    '{half}: bit {bit}, {field}': '{half}: бит {bit}, {field}',
    'register': 'регистр',
    'format': 'формат',
    'opcode': 'операция',
    'Terminal output': 'Вывод терминала',
    'Input': 'Ввод',
    'Guest terminal (*71)': 'Гостевой терминал (*71)',
    'Teletype': 'Телетайп',
    'Terminal input line': 'Строка ввода терминала',
    'Send line': 'Отправить строку',
    'Media': 'Носители',
    'Image': 'Образ',
    'Custom image': 'Свой образ',
    'Device': 'Устройство',
    'Tape': 'Лента',
    'Disk': 'Диск',
    'Drum': 'Барабан',
    'Unit (': 'Устройство (',
    'Unit (oct)': 'Устройство (oct)',
    'Unit': 'Устройство',
    'Volume ID (HEX)': 'ID тома (HEX)',
    'Read only': 'Только чтение',
    'Read / write': 'Чтение / запись',
    'Replace': 'Заменить',
    'Mount': 'Подключить',
    'Unmount': 'Отключить',
    'Word offset (': 'Смещение слова (',
    'Words': 'Слова',
    'Memory address (': 'Адрес памяти (',
    'Read / Disassemble': 'Прочитать / разобрать',
    'Copy to memory': 'Скопировать в память',
    'Disassembled media code': 'Дизассемблированный код носителя',
    'Mounted devices': 'Подключённые устройства',
    'Session files': 'Файлы сессии',
    'Access': 'Доступ',
    'File': 'Файл',
    'Bytes': 'Байты',
    'Select an image': 'Выберите образ',
    '{name} · {words} words': '{name} · {words} слов',
    '{kind} {unit} · mounted · {words} words': '{kind} {unit} · подключено · {words} слов',
    '{count} / {total} words': '{count} / {total} слов',
    ' · loaded at {address}': ' · загружено по адресу {address}',
    'Reading and mounting are available. Image: 6-byte big-endian words, 1024 words per zone.': 'Чтение и монтирование доступны. Образ: 6 байт big-endian на слово, 1024 слова в зоне.',
    'Pause or stop the machine to read or mount media.': 'Для чтения и монтирования поставьте машину на паузу или остановите её.',
    'Word count: 1–128.': 'Количество слов: 1–128.',
    'Dense image: complete 6-byte words, up to 8 MB.': 'Образ: целые 6-байтовые слова, до 8 MB.',
    'Cannot load media catalog.': 'Не удалось загрузить каталог носителей.',
    'Number is too large.': 'Число слишком велико.',
    '{field}: enter a {base} number.': '{field}: введите число в {base}.',
    'Idle': 'Ожидание',
    'Loading': 'Загрузка',
    'Running': 'Выполнение',
    'WaitingInput': 'Ожидание ввода',
    'Paused': 'Пауза',
    'Pausing': 'Приостановка',
    'Stepping': 'Выполнение шага',
    'Stopping': 'Остановка',
    'Resetting': 'Сброс',
    'Completed': 'Завершено',
    'Halted': 'Остановлено',
    'Failed': 'Ошибка',
    'The program is waiting for an input line.': 'Программа ждёт строку ввода.',
    'Line sent to the device.': 'Строка передана устройству.',
    'Connection lost. Upload the job again after reconnecting.': 'Соединение потеряно. Загрузите задание заново после подключения.',
    'No valid job selected': 'Не выбрано допустимое задание',
    'Select a .dub or .json file between 1 byte and 10 MB.': 'Выберите файл .dub или .json размером от 1 байта до 10 MB.',
    'Uploading {name}…': 'Загрузка {name}…',
    'Upload failed': 'Ошибка загрузки',
    'Instruction or time limit reached.': 'Достигнут лимит команд или времени.',
    'Execution failed.': 'Ошибка выполнения.',
    'Use 1–1,000,000,000 instructions and a time limit from 0 to 3,600 seconds.': 'Укажите от 1 до 1 000 000 000 команд и лимит времени от 0 до 3600 секунд.',
    'Supervisor needs a .json machine configuration. For .dub jobs choose Dubna; a JSON example is in Editor → Machine JSON template.': 'Supervisor требует конфигурацию машины .json. Для задания .dub выберите Dubna; пример JSON доступен в Editor → Machine JSON template.',
    'A .json machine configuration is loaded. Choose Supervisor — functional or hardware.': 'Загружена конфигурация машины .json. Выберите Supervisor — functional или hardware.',
    'Cannot load machine template.': 'Не удалось загрузить шаблон машины.',
    'Enter a {base} address from 0 to {maximum}.': 'Введите адрес в системе {base} от 0 до {maximum}.',
    'Elapsed': 'Время выполнения',
    'Throughput': 'Производительность',
    'Model time': 'Модельное время',
    'Model cycles': 'Модельные такты',
    'Allocated': 'Выделено памяти',
    'Gen 0 collections': 'Сборки поколения 0',
    '{count} bytes': '{count} байт',
    'Instruction input is too long.': 'Слишком длинный ввод команды.',
    'Select 24/48 bits and BIN/OCT/DEC/HEX.': 'Выберите 24/48 бит и BIN/OCT/DEC/HEX.',
    'Value exceeds the selected bit width.': 'Значение превышает выбранную разрядность.',
    'Unknown instruction mnemonic.': 'Неизвестная мнемоника команды.',
    'Select autocode, bemsh or madlen.': 'Выберите автокод, БЭМШ или Madlen.',
    'Use MNEMONIC M3 00123 / mnemonic 3,00123 / ,mnemonic,3 00123,. Addresses are octal; register numbers are decimal.': 'Используйте МНЕМОНИКА M3 00123 / мнемоника 3,00123 / ,мнемоника,3 00123,. Адреса восьмеричные; номера регистров десятичные.'
  };
  const aliases = new Map();
  const templates = [];
  const escape = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  for (const [en, ru] of Object.entries(messages)) {
    aliases.set(en, en); aliases.set(ru, en);
    if (en.includes('{')) for (const source of [en, ru]) {
      const names = [];
      const pattern = source.split(/(\{\w+\})/).map(part => {
        if (!/^\{\w+\}$/.test(part)) return escape(part);
        names.push(part.slice(1, -1)); return '(.+?)';
      }).join('');
      templates.push({ key: en, names, regex: new RegExp('^' + pattern + '$') });
    }
  }
  let language = navigator.language?.toLowerCase().startsWith('ru') ? 'ru' : 'en';
  try { const saved = localStorage.getItem('besm6.language'); if (saved === 'ru' || saved === 'en') language = saved; } catch { }
  function t(source, parameters = {}) {
    source = String(source ?? '');
    let key = aliases.get(source), values = parameters;
    if (!key) for (const template of templates) {
      const match = template.regex.exec(source);
      if (match) { key = template.key; values = Object.fromEntries(template.names.map((name, i) => [name, match[i + 1]])); break; }
    }
    if (!key) return source.includes('; ') ? source.split('; ').map(part => t(part)).join('; ') : source;
    return (language === 'ru' ? messages[key] : key).replace(/\{(\w+)\}/g, (token, name) => {
      if (!(name in values)) return token;
      const value = String(values[name]), nested = aliases.get(value);
      return nested ? (language === 'ru' ? messages[nested] : nested) : value;
    });
  }
  const dynamic = new Map(), textBindings = [], attributeBindings = [];
  function text(element, source) {
    if (element.id === 'status' || element.id === 'connection') element.dataset.state = String(source);
    const rendered = t(source);
    dynamic.set(element, { source: String(source ?? ''), rendered });
    element.textContent = rendered;
  }
  // Bind the initial UI once, before modules populate guest output and source.
  // No MutationObserver or document-wide replacement touches runtime data.
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) {
    const node = walker.currentNode;
    if (node.parentElement.closest('script, style, textarea, pre, code, [data-language], #languageSelect')) continue;
    const value = node.nodeValue.trim();
    if (aliases.has(value)) textBindings.push({ node, source: value, prefix: node.nodeValue.match(/^\s*/)[0], suffix: node.nodeValue.match(/\s*$/)[0] });
  }
  for (const element of document.querySelectorAll('[aria-label], [placeholder], [title]')) {
    for (const name of ['aria-label', 'placeholder', 'title']) {
      const source = element.getAttribute(name);
      if (source && aliases.has(source)) attributeBindings.push({ element, name, source });
    }
  }
  function apply() {
    document.documentElement.lang = language;
    document.title = t('BESM-6 Console');
    for (const binding of textBindings) if (binding.node.isConnected) binding.node.nodeValue = binding.prefix + t(binding.source) + binding.suffix;
    for (const binding of attributeBindings) binding.element.setAttribute(binding.name, t(binding.source));
    for (const [element, binding] of dynamic) {
      if (element.isConnected && element.textContent === binding.rendered) {
        element.textContent = binding.rendered = t(binding.source);
      } else dynamic.delete(element);
    }
    for (const element of document.querySelectorAll('[data-language]')) element.hidden = element.dataset.language !== language;
    document.getElementById('languageSelect').value = language;
  }
  function setLanguage(value) {
    if (value !== 'ru' && value !== 'en') return;
    language = value;
    try { localStorage.setItem('besm6.language', value); } catch { }
    apply();
    window.dispatchEvent(new Event('languagechange'));
  }
  window.Besm6I18n = { t, text, setLanguage, get language() { return language; } };
  document.getElementById('languageSelect').addEventListener('change', event => setLanguage(event.target.value));
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-show-tab]');
    if (button) bootstrap.Tab.getOrCreateInstance(document.getElementById(button.dataset.showTab)).show();
  });
  apply();
})();
