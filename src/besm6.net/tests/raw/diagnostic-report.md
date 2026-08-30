# Диагностика: зацикливание на examples/*.dub

> **Актуализация 30.08.2026.** Прежний общий вывод о поллинг-цикле устарел:
> `name`, FORTRAN, MADLEN и Pascal завершаются штатно, если запускать с доступным
> `ref/dubna/tapes`. Из корня репозитория остаётся ошибка разрешения пути к лентам,
> а `DiagShiftTest` по-прежнему воспроизводит чтение `0x8000`. Raw-путь
> `*trans-main` также остаётся сломанным. Текущий статус:
> [plans/simulator-readiness-report.md](../../../../plans/simulator-readiness-report.md).
> Нижеследующий разбор сохранён как историческая диагностика.

## Резюме

| Путь | Статус | Причина |
|------|--------|---------|
| raw-words (tests/raw/*.dub) | ✅ Работает | E64/E50/E70 + STOP |
| MONSYS (examples/*.dub) | ❌ Зацикливание | Поллинг-луп в MONSYS |

## Детальный анализ (trace examples/pi.dub)

### 1. Boot-секвенция (000408–000413) — работает

```
PC=000408  vtm 37773(1), *70 3002    ← читаем ТРП для загрузчика
PC=000409  xta 377, atx 3010        ← берём тракт MONITOR*+/MONTRAN
PC=00040A  xta 363, atx 100
PC=00040B  vtm 13401(7), utc
PC=00040C  *70 3010(1), utc         ← каталоги
PC=00040D  vlm 2014(1), ita 20017
PC=00040E  atx 716, *70 717         ← infloa — статический загрузчик
PC=00040F  xta 17, ati 16
PC=000410  atx 2(6), arx 33001
PC=000411  atx 17, xta 3000
PC=000412  atx 0(6), vtm 1673(5)
PC=000413  uj 0(7), utc             ← ПРЫЖОК в MONSYS → 005701
```

### 2. Код MONSYS (005701–005712) — выполняется

```
PC=005701..00570F  atx × 15         ← читаем слова с диска (MONSYS pages)
PC=005710  wtc 716, vtm 0(6)
PC=005711  ita 20017, mtj 17(6)
PC=005712  its 30001, vtm 37764(1)
```

### 3. Зацикливание (005713↔005714) — НЕ РАБОТАЕТ

```
PC=005713  its 30016(1), utc 22(6)  ← ПРОВЕРКА УСЛОВИЯ (polling)
PC=005714  vlm 0(1), mtj 1(6)       ← ПРЫЖОК НАЗАД → 005713
PC=005713  its 30016(1), utc 22(6)
PC=005714  vlm 0(1), mtj 1(6)
... (∞)
```

## Корневая причина

**MONSYS — это бинарная OS (32K+ слов), которая ожидает:**

1. **I/O completion flag** — после `*70` (disk read) MONSYS опрашивает
   контрольное слово в памяти (адрес зависит от конкретного build), ожидая
   бит "ready". В C++ референсе E70 работает синхронно (данные сразу в памяти),
   но **бит готовности** не ставится. MONSYS видит "not ready" → зацикливание.

2. **IOLIST** — таблица I/O описателей в памяти (адрес 017(6) = M[14]).
   MONSYS заполняет IOLIST при инициализации и ожидает, что драйвер
   (hardware) поменяет статус. У нас нет этого механизма.

3. **Memory layout** — MONSYS ожидает:
   - 00000–00003: загрузочный вектор
   - 00004–00100: ОС (ядро)
   - 00200–01777: пользовательская область
   - 02000–03777: I/O buffers
   - 04000–07777: страницы (page 0–31 × 1024)

   Наше memory: 32768 слов (адреса 0..07777₈). Адрес 0x08000 = 32768₈ —
   **один за границей** → `Memory access violation`.

## Memory access violation 0x08000

```
CoreMemory: 8 banks × 4096 words = 32768 words total
Валидный диапазон: 0..32767
Ошибка: адрес 32768 (0x08000) = 32768 — вне диапазона
```

**Возможные источники:**
 - `page << 10` с page=32 (6-й бит вместо 5-го)

---

## Перехват арифметических ошибок (Intercept / StackCorrection)

### Проблема (до исправления)

C#-порт `Processor.Intercept()` имел **ошибочную реализацию** — он:
1. Менял `_acc` (C++ не трогает ACC)
2. Вызывал собственный `StackCorrection()` внутри (C++ `stack_correction()` — отдельная функция)
3. Сбрасывал `_rmr` (C++ не сбрасывает)

### Реальная семантика C++ (dubna/processor.cpp:68-85)

```cpp
bool Processor::intercept(const std::string &message) {
    if (intercept_count > 0 &&
        (message == MSG_ARITH_OVERFLOW ||   // "Arithmetic overflow"
         message == MSG_ARITH_DIVZERO)) {   // "Division by zero"
        intercept_count--;
        core.PC               = intercept_addr;  // 020 (oct) = 16 (dec) по умолчанию
        core.right_instr_flag = false;
        core.apply_mod_reg    = false;
        core.MOD              = 0;
        return true;
    }
    return false;
}
```

### Цикл запуска (dubna/machine.cpp:98-157)

```cpp
again:
    try {
        for (;;) {
            bool done = cpu.step();
            if (done) { cpu.finish(); return; }
        }
    } catch (const Processor::Exception &ex) {
        cpu.stack_correction();   // M[017] += corr_stack; corr_stack = 0
        cpu.finish();             // e64_finish()
        auto *message = ex.what();
        if (!message[0]) return;  // E74: чистый halt
        std::cerr << "Error: " << message << ...
        if (cpu.intercept(message)) goto again;  // продолжить с нового PC
        throw std::runtime_error(message);
    }
```

### Исправления

| Файл | Изменение |
|------|-----------|
| `src/Core/Processor.cs` | `Intercept()` переписан точно по C++: `count--; PC=addr; flags=0; MOD=0; return true`. **Не меняет ACC/RMR.** |
| `src/Core/Processor.cs` | `StackCorrection()` — теперь no-op (C++ `corr_stack` отсутствует в C#-порте) |
| `src/Core/Processor.cs` | `_interceptAddr` по умолчанию `16` (020 oct), как `intercept_addr{020}` в C++ |
| `src/Loader/DubnaLoader.cs` | `RunBounded()`: `catch (ProcessorException)` → `StackCorrection()` → `Intercept(msg)` → `continue`. Пустое сообщение → `Halt`. |

### Тесты (7 штук)

| Тест | Что проверяет |
|------|---------------|
| `Intercept_DefaultDisabled_ReturnsFalse` | count=0 → перехват отключён |
| `Intercept_Overflow_CountDecremented_PcSetToAddr` | overflow → count--, PC=addr |
| `Intercept_DivZero_CountDecremented_PcSetToAddr` | div-zero → count--, PC=addr |
| `Intercept_UnknownMessage_ReturnsFalse` | не-арифметическая ошибка → false |
| `Intercept_OnceOnly_SecondCall_ReturnsFalse` | одноразовый перехват |
| `Intercept_ResetsFlagsAndMod` | right_instr_flag=false, MOD=0 |
| `StackCorrection_NoOp` | не меняет состояние |

### Вывод

**Перехват арифметических ошибок теперь точно соответствует C++-референсу.**
Программы, использующие E75 (при addr==020) для перехвата overflow/div-by-zero,
теперь работают корректно: ошибка перехватывается, исполнение продолжается
с адреса перехвата (по умолчанию 020 oct = 16 dec), а не аварийно завершается.
- `addr & 0x7FFF` маскирует адрес до 15 бит, но если исходное значение > 32767,
  то `& 0x7FFF` даёт 0 — а в некоторых путях используется без маски
- `ReadToMemory(memAddr, 1024)` с memAddr=31744 читает 1024 слова → до 32767 (OK)
- Но если memAddr=32768 → сразу ошибка

## Что нужно для MONSYS-пути (Фаза 3+)

| Задача | Сложность | Описание |
|--------|-----------|----------|
| I/O completion flag | **Высокая** | После E70 read/write ставить бит готовности в IOLIST; MONSYS опрашивает это поле |
| IOLIST emulation | **Высокая** | Эмулировать структуру I/O descriptors (addr, status, count) |
| Interrupt vector | **Высокая** | MONSYS использует вектор прерывания (адрес 00000–00003) для I/O завершения |
| Memory 64K | **Средняя** | Удвоить до 65536 слов если MONSYS требует |
| COSY format | **Средняя** | Проверить что скрипт на drum 1 в правильном COSY-формате |
| Device flags | **Средняя** | Эмулировать "unit busy/ready" status words |

## Вывод

**MVP (raw-words) полностью работает.** Для ALGOL/FORTRAN/B примеров
нужна полная эмуляция I/O subsystem MONSYS — это задача на **1-2 недели**
разработки, не часы.

### Рекомендуемый следующий шаг

1. ~~Фаза 1: raw-words .dub~~ ✅ **Готово**
2. ~~Фаза 2: фиксы~~ ✅ **Готово**
3. Фаза 3a: **IOLIST emulation + I/O flags** (~2-3 дня)
4. Фаза 3b: **COSY script parser** для MONSYS (~1-2 дня)
5. Фаза 3c: **Compiler pipeline** (FORTRAN/ALGOL via MONSYS) (~1 неделя)

Альтернатива: **bypass MONSYS** — для простых программ достаточно
assem-секций (уже поддерживается) + E50/E64/E70/E71.
