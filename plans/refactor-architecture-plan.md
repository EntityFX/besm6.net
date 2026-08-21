# План глубокого рефакторинга архитектуры симулятора БЭСМ-6 (C#)

**Дата:** 21.08.2026
**Предыстория:** устранено зависание E64; проведены прогоны всех `.dub`; проведён аудит архитектуры и упоминаний C++.

---

## 1. Результаты прогона всех `.dub` (examples/)

| Файл | Итог | Код остановки | Класс проблемы |
|------|------|---------------|----------------|
| **name.dub** | ✅ OK | `STOP at 0117` (209 393 инст.) | — |
| **fortran.dub** | ⚠️ OK, но вывод `15002`×6 | `STOP at 0117` (530 201 инст.) | результат невалиден (цикл выводит константу) |
| **algol.dub** | ❌ illegal instr | `02001: 002 рег/mod` | привилегированная инструкция не реализована |
| **assem.dub** | ❌ illegal instr | `07C01: 0320 выпр/iret` | `IRET` (возврат из прерывания) не реализован |
| **bemsh.dub** | ⚠️ стоп | E71 ввод | `Console.In.Peek` с перенаправленным stdin → исключение |
| **madlen.dub** | ⚠️ стоп | E71 ввод | то же самое (bemsh) |
| **forex.dub** | ❌ лимит | `052E` (80M) | зависание/медленный компилятор |
| **pascal.dub** | ❌ лимит | `052F` (80M) | зависание/медленный компилятор |
| **pi.dub** | ❌ лимит | `0531` (80M) | зависание/медленный компилятор |

**Вывод:** штатный завершающий путь (boot → MONSYS → E64 → STOP) **работает** (name, fortran).
Блокеры остальных — четыре самостоятельные группы (см. §3).

---

## 2. Текущая архитектура (состояние)

Одно C#-проектное решение `src/besm6.net` (+ `tests/Besm6.Tests`), net8.0. Слои:

```
Program.cs (CLI)
 └─ Cli/  RunCommand, AsmCommand, DisasmCommand, CheckCommand, TuiCommand, MachineFactory, Config
     └─ Loader/  DubnaLoader (сессия/загрузка .dub)
         ├─ JobParser (карты *name/*call/*execute, raw-слова)
         ├─ CosyCodec (COSY + таблицы GOST/KOI7/TEXT)   ← 460 строк, «бог-класс»
         ├─ ExtracodeHandler (+.E50 +.E64)  ← 441+319+787 строк, «бог-класс»
         ├─ TapeImage (ленты/зоны), Besm6Math
     └─ Core/  Processor (240) ← исполнитель, Alu (301), MantissaExponent,
                    CoreMemory, Word48, InstructionExecutor (443), Debugger,
                    + мёртвый код: SystemBus, DeviceManager, DmaController,
                    DiskDevice, MagneticDrumDevice, TeletypeDevice, ConsoleDevice
Tui/  TuiApp (303) — интерактивный режим
```

**Наблюдения (запах):**
1. **Два параллельных пути исполнения** неубиты: реальный — `Processor`; рядом живёт
   `InstructionExecutor` (443 строк) и набор «устройств/шины» (`SystemBus`, `DmaController`,
   `DeviceManager`), которые **не интегрированы** в поток выполнения.
2. **«Бог-классы»:** `ExtracodeHandler.E64.cs` (787), `CosyCodec.cs` (460),
   `ExtracodeHandler.cs` (441). В `CosyCodec` сплетены 4 не связанные задачи:
   COSY-пакование + 3 таблицы кодировок (GOST/KOI7/TEXT).
3. **Мёртвые файлы** (`SystemBus.cs`, `DmaController.cs`, `DeviceManager.cs`,
   `DiskDevice.cs`, `MagneticDrumDevice.cs`, `TeletypeDevice.cs`, `ConsoleDevice.cs`)
   — не используются активным путём, дублируют `TapeImage`/`CoreMemory`.
4. **77 упоминаний C++/dubna/.cpp** в исходниках: комментарии «порт dubna/...» +
   **реальные fallback-пути** `dubna/tapes` (TapeImage.cs) и `dubna/examples` (CheckCommand.cs).
5. **Тесты-«боги»:** `ProcessorTests.cs` (1138 строк) — один файл на всё ядро.

---

## 3. Что осталось до полной работоспособности

| # | Дефект | Блокит | Природа |
|---|--------|--------|---------|
| A | `002 рег/mod` (priv) — algol | ALGOL | нет привилегированной семантики + супервизор-режима |
| B | `0320 выпр/iret` (IRET) — assem | ASSEM/B | нет возврата из прерывания + механизма прерываний |
| C | E71: `Console.In.Peek` падает при перенаправленном stdin | BEMSH/МАДЛЕН | нет корректного non-blocking ввода с fallback (EOF/пустой) |
| D | forex/pascal/pi уходят в лимит (зависание) | FORTRAN/B/PI | нет прерываний/таймаута на задание; вероятно медленный компилятор или цикл |
| E | `E63/E65/E72/E76` — частичные заглушки | ОС-задачи | страницы памяти (E72), выключатели (E65), рутин-диспетчер (E76) |
| F | `fortran.dub` выводит `15002`×6 | FORTRAN-результат | неверный E50/E64 real-вывод или цикл (не блокирует STOP) |
| G | Таблицы GOST→Unicode искажают кириллицу | вывод | `ПPИMEP` вместо `ПРИМЕР` (E64) |
| H | Прерывания/каналы I/O полностью отсутствуют | интерактивные | синхронный `Processor.Step()`, нет event-модели |

Приоритет: **C → B → A → D** (снимают 5 из 7 проблемных файлов) → E → G/F → H.

---

## 4. План рефакторинга

### Цель
Единый чистый C#-рантайм без «археологических» ссылок на C++; устранение мёртвого
кода; модульная структура (ядро / кодировки / экстракоды / периферия / CLI);
покрытие всех `.dub` штатным завершением; стабильный набор тестов.

### Этап R0 — Гигиена и «де-C++фикация» (не меняет поведения)
- [ ] Убрать все 77 упоминаний C++/dubna/.cpp: переписать комментарии
      («порт dubna/...» → нейтральное описание протокола/семантики).
- [ ] `TapeImage.cs`: удалить fallback `dubna/tapes` (остаются `tapes/` + `BESM6_PATH`).
- [ ] `Cli/CheckCommand.cs`: путь по умолчанию `dubna/examples` → `examples`.
- [ ] Проверка: `search dubna|\.cpp|C\+\+` по `src` и `tests` → **0 совпадений**;
      `dotnet build` 0 ошибок; `dotnet test` зелёный; name/fortran не изменились.

### Этап R1 — Удаление мёртвого кода
- [ ] Удалить неиспользуемые: `SystemBus.cs`, `DmaController.cs`, `DeviceManager.cs`,
      `DiskDevice.cs`, `MagneticDrumDevice.cs`, `TeletypeDevice.cs`, `ConsoleDevice.cs`
      (или встроить в активный путь — выбрать, см. R5).
- [ ] Ревью `InstructionExecutor.cs`: либо убрать (дубль `Processor`), либо сделать
      единственным декодером поверх `Processor` (тонкий слой).
- [ ] `MachineCore.cs`: привести к «фабрике + оркестратор» без дублирования логики.

### Этап R2 — Модульная структура (переупорядочение, без изменения поведения)
```
src/besm6.net/
  Core/          Processor, Alu, MantissaExponent, Word48, CoreMemory,
                 BytePointer, Registers, InterruptController (новый)
  Cpu/           (опц.) вынести InstructionExecutor как декодер-обёртку
  Encoding/      CosyCodec -> расщепить:
                  - CosyCodec.cs (только COSY pack/unpack)
                  - GostTable.cs, Koi7Table.cs, TextTable.cs (чистые таблицы)
  Extracodes/    IExtracodeHandler + по одному классу на E50/E57/E64/E63/E65/E70/E71/E72/E75/E76
  Devices/       TapeImage -> TapeStorage; единый интерфейс IStorage (drum/disk)
  Loader/        DubnaLoader (сессия), JobParser
  Cli/           команды + MachineFactory
```
- [ ] Расщепить `CosyCodec` (R2.1) и `ExtracodeHandler*` (R2.2).
- [ ] Ввести `IExtracode { bool IsSupported(long code); void Execute(Machine m, long addr); }`
      и реестр (dispatch по коду) вместо одного большого `switch`.
- [ ] Ревью `Word48`/`MantissaExponent`: только явные `ulong/long`, без `unsafe`,
      единый источник битовых масок (const-контракт).

### Этап R3 — Прерывания и ввод-вывод (снимает C, B, D, частично H)
- [ ] **R3.1 (C):** E71 — non-blocking ввод: `Console.IsInputRedirected` → EOF/пустая строка;
      добавить флаг `--input <file>` и `--stdin-eof` (детерминированный прогон без «Peek»).
- [ ] **R3.2 (B):** реализовать `IRET` (0320): восстановление PC/режима из вектора/стека;
      базовая схема прерываний (молот/вектор) в `InterruptController`.
- [ ] **R3.3 (D):** «защита от зависания» — лимит на *время* задания (напр. N секунд)
      + отдельный счётчик I/O-циклов; детерминированный стоп с диагностикой PC/инструкций.
      Ускорить горячий путь E70 (batch `Span<long>`, уже начато) и E64 (один проход).
- [ ] **R3.4 (H):** (опц.) async-модель I/O для интерактивных программ (adventure и т.п.).

### Этап R4 — Ядро ОС: привилегированные инструкции и E72/E65/E76
- [ ] **R4.1 (A):** `002 рег/mod`: определить семантику (привилегированный доступ к спец.
      регистрам), добавить флаг SUPERVISOR + корректный decode; сверить с таблицей `book/opcodes.md`.
- [ ] **R4.2 (E):** E72 — страницы памяти; E65 — выключатели; E76 — диспетчер рутин.
- [ ] **R4.3 (F/G):** E50/E64 real-формат + таблицы GOST→Unicode (кириллица):
      сверка с эталоном вывода `fortran.dub` и баннером MONSYS.

### Этап R5 — Периферия: единая абстракция
- [ ] `IStorage { ReadWord(addr), WriteWord(addr, w), ReadPage/WritePage }`;
      реализации `TapeDrum`/`TapeDisk` поверх `TapeImage`.
- [ ] Один источник зон/секторов (PAGE=1024, SECTOR=256) в константах.
- [ ] (опц.) вернуть в строй DMA как «быстрый» путь bulk-копии (R1/R5 — выбрать судьбу).

### Этап R6 — Тесты и приёмка
- [ ] Расщепить `ProcessorTests.cs` (1138) на: `InstructionTests`, `AluTests`, `InterruptTests`.
- [ ] Золотые e2e: прогон всех 9 `.dub` со сверкой ожидаемого вывода/кода останова.
- [ ] Детерминизм: `--input` + лимит по времени; CI-прогон без интерактива.
- [ ] Метрики: `dotnet build` 0 предупреждений; `dotnet test` 100%.

### Этап R7 — CLI/UX
- [ ] `--verbose`/`--trace` без утечки в stdout; единый формат отчёта.
- [ ] `besm6 run <file.dub>` — единая точка; `--limit`, `--timeout`, `--input`, `--tapes`, `--seed`.
- [ ] Убрать трекер повторов `[TRACE]` из stderr (в логи только по требованию).

---

## 5. Целевые критерии приёмки (по прогонам)

| Файл | После R3/R4 |
|------|-------------|
| name.dub | ✅ STOP (уже) |
| fortran.dub | ✅ STOP + корректный результат (F) |
| algol.dub | ✅ компиляция+вывод (A) |
| assem.dub | ✅ IRET/прерывания (B) |
| bemsh.dub / madlen.dub | ✅ non-blocking ввод (C) |
| forex.dub / pascal.dub / pi.dub | ✅ штатное завершение или чистый стоп (D) |

**Итог:** 9/9 `.dub` доходят до штатного завершения (STOP/E74) или детерминированного стопа
с диагностикой; 0 упоминаний C++ в исходниках; единый чистый C#-рантайм.

---

## 6. Порядок и оценка

| Этап | Что снимает | Оценка | Зависимости |
|------|-------------|--------|-------------|
| R0 | гигиена (обязателен, без риска) | 0.5 д | — |
| R1 | мёртвый код | 0.5–1 д | R0 |
| R2 | читаемость, «бог-классы» | 1–2 д | R1 |
| R3 | C, B, D (+H) — **5 файлов** | 2–3 д | R1 |
| R4 | A, E, F, G — **4 файла** | 2–4 д | R3 |
| R5 | периферия/масштаб | 1–2 д | R2 |
| R6 | приёмка | 1 д | R3–R5 |
| R7 | UX | 0.5 д | R6 |
| **Итого** | **9/9 `.dub` + чистая архитектура** | **~8–13 д** | |