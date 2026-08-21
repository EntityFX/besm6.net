# Отчёт по портированию БЭСМ-6 (dubna/ → C#)

**Дата:** 19.08.2026 (обновлено 14:53)
**Статус:** 66/66 тестов, `dotnet build` — 0 ошибок, 0 предупреждений.

### Ключевое исправление (19.08.2026, 14:40)
Обнаружен и исправлен **критичный баг**: все `case`-ветки в `ExtracodeHandler.Handle()` и `E50()` были указаны как `case 0NN` — в C# это decimal, а НЕ octal. Например, `case 050` — это decimal 50, а НЕ octal 050 (=decimal 40). Результат: ни один экстракод (E50, E57, E64, E70…) не срабатывал. После конвертации всех labels в decimal MONSYS начал реально выполнять сотни тысяч инструкций загрузочной последовательности (вместо мгновенного зависания).

### Прогресс сессии 19.08.2026
- **E70 batch I/O:** disk-загрузка передаёт 1024 слова (страницу) или 256 (сектор) за одну операцию — как в C++.
- **MONSYS:** загрузчик стартует, PC 0434→067E, 10M+ инструкций выполнены (лимит 10^9).
- **E57:** полный порт (ASSIGN/FIND/RELEASE) — больше не STUB.
- **Phys.io:** реализовано в E70 (drum 021+ → disk 030).
- **Лимит инструкций:** 10^9 (как C++ DEFAULT_LIMIT).

---

## 1. Архитектура (C# vs C++)

| Компонент | C++ (dubna/) | C# (src/) | Статус |
|-----------|-------------|-----------|--------|
| Processor | processor.cpp | src/Core/Processor.cs | ✅ Полный порт |
| АЛУ | arithmetic.cpp | src/Core/Processor.cs + MantissaExponent.cs | ✅ Полный порт |
| Память | memory.cpp | src/Core/CoreMemory.cs | ✅ 32768×48 бит |
| Слово | — | src/Core/Word48.cs | ✅ |
| Машина | machine.cpp | src/Loader/DubnaLoader.cs | ⚠️ Частично |
| COSY | cosy.cpp | src/Loader/CosyCodec.cs | ✅ |
| Экстракоды | extracode.cpp, e50/e57/e64.cpp | src/Loader/ExtracodeHandler.cs | ⚠️ Частично |
| Ленты | disk.cpp, drum.cpp | src/Loader/TapeImage.cs | ✅ Чтение + batch I/O |
| Ассемблер | assembler.cpp | src/Asm/Assembler.cs | ⚠️ Частично |
| Сессия | session.cpp | src/Loader/DubnaLoader.cs | ⚠️ |
| CLI | main.cpp | Program.cs | ✅ |

---

## 2. Реализовано полностью

### 2.1 Processor (src/Core/Processor.cs, 1039 строк)
- **Все короткие инструкции** (000–047): atx, stx, xts, sl, vch, vchob, vchab, xta, ntzh, sletz, znak, ili, del, umn, sbr, rzb, ched, ned, sletp, vchp, sd, rzh, schezh, schm, sletpa, vchpa, sda, rja, ui, uim, schie, schim, ui(mtj), slj
- **Все длинные инструкции**: moda(utc), mod(wtc), uia(vtm), slia(utm), po(uza), pe(u1a), pb(uj), pv(vjm), stop, pio(vzm), pino(v1m), e36, tsikl(vlm)
- **Арифметика**: нормализация, округление, деление, сдвиги, перенос в RMR — точный порт C++
- **RAU**: все биты режима (NORM_DISABLE, ROUND_DISABLE, LOG, MULT, ADD, OVF_DISABLE)
- **Экстракоды**: распознавание (0x28–0x3F короткий, 0x80/0x88 длинный) + делегирование

### 2.2 JobParser (src/Loader/JobParser.cs)
- Карты управления: `*name`, `*tape:N/имя,Z`, `*library:N`, `*trans-main:ADDR`, `*call`, `*execute`, `*end file`, `*read`, `*no load list`
- Raw octal слова: `` `... ``
- Комментарии: `/* ... */`
- ParseOctalWord с валидацией

### 2.3 CosyCodec (src/Loader/CosyCodec.cs)
- `EncodeCosy(byte[])` — 6 байт → 48-битное слово, пакование пробелов
- `DecodeCosy(long)` — обратное
- Кодировки: UTF-8 ↔ KOI-7, ГОСТ-10859
- COSY_END_FILE маркер

### 2.4 ExtracodeHandler (src/Loader/ExtracodeHandler.cs)

| Экстракод | Описание | Статус C# |
|-----------|----------|-----------|
| **E50** | Мат. функции (sqrt, sin, cos, atan, log, exp, floor) | ✅ Полный порт |
| **E57** | Монтаж лент (ASSIGN/FIND/RELEASE/BYNAME/FILE) | ✅ Полный порт |
| **E63** | ОС Дубна | ⚠️ Часть no-op |
| **E64** | Вывод текста | ✅ |
| **E65** | Выключатели | ⚠️ Частично |
| **E67** | Debug | ✅ |
| **E70** | Disk/Drum I/O (batch: 1024/256 слов) | ✅ |
| **E71** | Ввод/вывод терминала | ⚠️ Console only |
| **E72** | Страницы памяти (ОС Дубна) | ❌ No-op |
| **E75** | Write with check bits | ✅ |
| **E76** | Вызов рутин ядра | ⚠️ Частично |

### 2.5 TapeImage (src/Loader/TapeImage.cs)
- Загрузка образов из `dubna/tapes/*` (monsys.9, b.7, librar.12/37, bemsh.739)
- ReadWord/WriteWord, ReadToMemory/WriteFromMemory (batch)
- PageNWords = 1024, SectorNWords = 256
- Зонная адресация (zone, sector)

### 2.6 CLI (Program.cs)
- `besm6 run <file.dub> [--limit N] [--verbose]`
- `besm6 asm <source>` → octal word
- `besm6 disasm <octal_word>` → mnemonics
- Интерактивный отладчик (без аргументов)

### 2.7 Тесты (66 штук)
- ProcessorTests (ядро, инструкции)
- AluTests (арифметика)
- AssemblerTests (ассемблер/дассемблер)
- JobParserTests (парсинг .dub)
- CosyCodecTests (COSY encode/decode)
- Besm6MathTests (Sqrt, Sin, Cos, Atan, Asin, Log, Exp, Floor)
- DubnaLoaderTests (RunRawWords)
- ExtracodeHandlerTests (E64 вывод)

---

## 3. Реализовано как STUB (заглушки)

| Экстракод | Описание | Статус C# | C++ |
|-----------|----------|-----------|-----|
| **E72** | ОС Дубна (страницы памяти) | ❌ Пустой (все no-op) | Реальные операции |
| **E76** | Вызов рутин ядра | ⚠️ Частично | Полный dispatch |
| **E63** (default) | ОС Дубна | ⚠️ Частично | Полный dispatch |
| **E65** (default) | Выключатели | ⚠️ Частично | Полный dispatch |
| **E71** | Терминал | ⚠️ Console.ReadLine | Полный ввод-вывод |

---

## 4. Чего НЕТ в C# (полностью отсутствует)

### 4.1 Прерывания и channel I/O
- C++ имеет механизм прерываний: после `pio`/`pino` процессор блокируется до завершения I/O
- C# `Processor.Step()` выполняет инструкции синхронно; нет асинхронного I/O
- **Влияние:** программы, использующие PIO/PINO с реальными устройствами, не будут работать корректно

### 4.2 DMA Controller
- `DmaController.cs` существует, но не интегрирован в E70
- C++ disk.cpp/drums.cpp использует DMA для bulk-передач
- **Влияние:** E70 работает по одному слову в цикле (медленно), но функционально корректно

### 4.3 Plotter / Puncher
- `dubna/plotter.cpp`, `dubna/puncher.cpp` — вывод на плоттер/перфоленту
- В C# E64 упрощён до console output

### 4.4 MONSYS-специфичное
- MONSYS — сложная ОС на ~288 страницах (294,912 слов, 1.77 МБ)
- Ожидает: правильные таблицы (03000–03010), phys.io, IOLIST*, CHEKJOB*
- C# `BootMsDubna()` создаёт таблицу и магический код
- **Статус:** MONSYS стартует (PC 0434→067E), но полная загрузка требует больше инструкций

---

## 5. Анализ: можем ли мы запустить произвольную программу?

### Сценарий A: Raw words (inline machine code)
**Статус: ✅ РАБОТАЕТ** — слова загружаются в память, PC ставится, исполняется.

### Сценарий B: MONSYS + компилятор B (fibonacci.dub)
**Статус: ⚠️ ЧАСТИЧНО** — MONSYS стартует, PC двигается (0434→067E), 10M+ инструкций.

| Шаг | Требуется | Статус |
|-----|-----------|--------|
| 1. Parse .dub | JobParser | ✅ |
| 2. Mount MONSYS (tape 9 → disk 030) | TapeImage.LoadFromFile | ✅ |
| 3. Map drum 021 → disk 030 | phys.io | ✅ |
| 4. Assemble magic code | Assembler.Asm() | ⚠️ |
| 5. Execute magic code (E70 reads from disk) | E70 handler | ✅ (batch 1024) |
| 6. MONSYS executes (reads B compiler from tape 7) | E57 mount tape 7 | ✅ |
| 7. B compiler translates ALGOL → machine code | MONSYS+compiler | ❌ Неэмулировано |
| 8. Execute translated code | Processor | ✅ |
| 9. Output (E64/E71) | ExtracodeHandler | ⚠️ Console only |

### Сценарий C: Adventure1.dub (интерактивная игра)
**Статус: ❌ НЕ РАБОТАЕТ** — нужны:
- MONSYS (загрузка, трансляция)
- E71 input (терминал) — частично
- Библиотека 37 (librar.37) — файл
- Прерывания для I/O

---

## 6. Что нужно для запуска реальной программы (приоритет)

### Фаза 1: Устранение критичных stub'ов (1-2 дня)
1. ✅ **E57 — реальный монтаж лент** (ASSIGN/FIND/RELEASE) — ГОТОВО
2. ✅ **Phys.io mapping** (drum 021 → disk 030) — ГОТОВО
3. ⚠️ **Проверить Assembler.Asm()** на корректность всех инструкций в magic code
4. ⚠️ **E72 — страницы памяти** (если MONSYS их использует)

### Фаза 2: Проверка MONSYS boot (1-2 дня)
5. ✅ **Проверить наличие dubna/tapes/monsys.9** — ЕСТЬ (1.77 МБ, 288 страниц)
6. ⚠️ **Прогнать `besm6 run dubna/examples/b/fibonacci.dub --limit 100000000`**
   - MONSYS стартует (PC 0434→067E)
   - Нужен больше инструкций для полной загрузки
7. ⚠️ **Дополнить E70** — phys.io sector I/O (256 слов) — ЧАСТИЧНО

### Фаза 3: Расширение (некритично для MVP)
8. **E76** — полный dispatch рутин ядра
9. **Прерывания** — для I/O с ожиданием
10. **Plotter/Puncher** — для полноты

---

## 7. Оценка: "можем ли грузить ЛЮБУЮ программу?"

**Краткий ответ: ЧАСТИЧНО ДА.**

| Тип программы | Работает? | Что нужно |
|---------------|-----------|-----------|
| Raw words (встроенный машинный код) | ✅ | Ничего |
| Простая программа с E64 (вывод) | ✅ | MONSYS не нужен |
| Программа с E50 (мат. функции) | ✅ | Besm6Math реализован |
| Программа с E70 (disk I/O) | ✅ | Batch 1024/256 слов |
| Программа с E57 (монтирование) | ✅ | ASSIGN/FIND/RELEASE |
| Программа через MONSYS (компилятор) | ⚠️ | Полная загрузка MONSYS (медленно) |
| Интерактивная (adventure1) | ❌ | E71 input + MONSYS + прерывания |

**Что заблокировано:** Полная загрузка MONSYS (288 страниц × 1024 слова) — требует много инструкций.

**Что НЕ заблокировано:** Processor, COSY, JobParser, E50, E57, E64, E70 (batch), E75.

---

## 8. Рекомендации по следующему шагу

1. ✅ **Исправить E57** — ГОТОВО (ASSIGN/FIND/RELEASE).
2. ✅ **Добавить phys.io** — ГОТОВО (drum 021+ → disk 030).
3. ⚠️ **Прогнать fibonacci.dub** — MONSYS стартует, PC двигается, но нужна полная загрузка.
4. ⚠️ **Добавить progress** — в `RunBounded()` печатать PC каждые N инструкций.
5. ⚠️ **E72** — реализовать страницы памяти (если MONSYS их использует).
6. ⚠️ **Ускорить E70** — использовать `Span<long>` для batch copy.