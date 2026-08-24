# Отчёт: Расхождение C# (besm6.net) ↔ C++ (dubna/) на загрузке MONSYS

**Цель отчёта:** дать самодостаточную «точку старта» для повторного расследования — почему C# MONSYS падает в `E57 WAIT` (case 7), тогда как C++-референс проходит дальше и загружает B-компилятор.

**Статус:** 66/66 юнит-тестов C# проходят; `dotnet build` — 0 ошибок. Пробег MONSYS падает на фазе загрузки job (не на ядре).

---

## 0. Резюме (TL;DR)

- **Фатальная точка C#:** `ec_trace.log`:
  ```
  [EC] 47 (057) aex=07 M16=07 ACC=0900000002000 PC=022003
  [E57] addr=07 ACC=0900000002000 M[13]=053F
  ```
  → `ExtracodeHandler.E57()` `case 7:` → `throw ProcessorException("E57: Task paused waiting for tape")`. **Фатально.**
- **C++ в той же точке:** `02514 *70 2664 → Disk 40 Read Zone 20` (загрузка B-компилятора с b.7) и продолжается.
- **Ключевой факт:** последовательность **всех 41 E70-операции идентична побайтово** в обоих треках (одинаковые unit/page/zone/sector/op и порядок). → расхождение **НЕ в маршрутизации/декодировании E70**.
- **Последняя общая операция:** `00051 *70 45` — `Drum 20 Read Zone 4 [76000-77777]` (это **job card**). После неё MONSYS (один и тот же код, PC совпадают до этой точки) **ветвится по-разному**:
  - C++ → `*70 2664` (Disk 40 Read, B-компилятор)
  - C# → `*57 7` (WAIT, фатальный throw)
- **Вывод:** MONSYS читает job-card с барабана и на её данных (или на состоянии, которое он из неё извлекает через E57) выбирает путь. Разные пути ⇒ либо **разное содержимое job-card (COSY-6 кодировка)**, либо **разное состояние E57 (ASSIGN/монтирование)**. Это две открытые гипотезы.

---

## 1. Среда и файлы

| Роль | Путь | Примечание |
|---|---|---|
| C# исходники | `src/besm6.net/` | `Core/` (Processor, CoreMemory, Word48), `Loader/` (DubnaLoader, ExtracodeHandler, CosyCodec, TapeImage, JobParser), `Asm/` |
| C++ референс | `ref/` | `extracode.cpp`, `e57.cpp`, `e70dec.cpp`, `machine.cpp/h`, `processor.cpp`, `cosy.cpp`, `disk.cpp`, `drum.cpp`, `session.cpp` |
| C# extracode-трейс | `ec_trace.log` (~45 KB) | строки `[EC] …` и `[E70] …` |
| C# instruction-трейс | `instr_trace.log` (~837 KB) | по инструкции |
| Ядро MONSYS | `monsys.9` | **байт-в-байт идентично** в C# и C++ |
| Job-скрипт (B) | `examples/b/hello.dub` | директивы `*name`, `*tape:7/b,40`, `*library:40`, `*trans-main:40020`, `*execute` |
| B-компилятор | `b.7` (tape image) | монтируется как **disk 40** |
| Предыдущий отчёт | `plans/porting-report.md` | содержит ход расследования |

---

## 2. Методология (как сравнивали)

1. **Byte-compare `monsys.9`** (C# vs C++) → идентично. Исключено «разное ядро».
2. **Сравнение декодирования E70 control word** (первая E70) → идентично. Исключено «неверное декодирование слов управления».
3. **Проверка drum→disk маппинга** → «расхождение» оказалось артефактом: C#-лог печатает `:X` = **HEX**, т.е. `drum=011`/`mapped=011` (hex) = `021` (octal) = 17 — **совпадает** с C++ `map_drum_to_disk(021, 030)`. Исключено.
4. **Построчное выравнивание всех 41 E70-операций** в обоих треках → **100% идентичны**. Исключено «расхождение в E70».
5. **Сравнение E70-хендлеров** (C# `ExtracodeHandler.E70()` vs C++ `Processor::e70()`) → маршрутизация (disk `[24,56)`, drum, phys-io) совпадает по смыслу. Исключено.
6. **Сравнение E57-хендлеров** (C# `ExtracodeHandler.E57()` vs C++ `Processor::e57()`/`e57_tape()`) → найдено: C# `case 7` = **`throw` (фатально)**, C++ `case 7` = recoverable pause. Но C++ к case 7 **не приходит**.
7. **Фиксация точки расхождения** → `00051 *70 45` (последняя общая) ⇒ разные PC.

---

## 3. Точка расхождения (точная)

| | C++ (dubna) | C# (besm6.net) |
|---|---|---|
| Последняя общая оп. | `00051 *70 45` — Drum 20 Read Zone 4 [76000-77777] (**job card**) | аналогично |
| Следующая оп. | `02514 *70 2664` → **Disk 40 Read Zone 20** (B-компилятор) | `022003 *57 7` → **WAIT** (фатальный throw) |
| Контекст ASSIGN | `24130 … *57 … = 2070` → «Mount b.7 as disk 40» | `[E57] ASSIGN tape=0880000000007 -> unit=040 mounted_id=0880000000007` |

Оба делают ASSIGN (b.7 → disk 40) одинаково. Расхождение — в **следующем** выборе MONSYS.

---

## 4. Кодовые ссылки (быстрый старт)

| Что | C++ | C# |
|---|---|---|
| E70 (disk/drum) | `ref/extracode.cpp:129-190` | `src/besm6.net/Loader/ExtracodeHandler.cs` → `E70()` |
| E70 decode control word | `ref/extracode.cpp:131-139` (`E70_Info`, `info.word = M[016]?mem:ACC`) | `E70()`: `execAddr=M16&0x7FFF; ctrl=(0?ACC:mem); isRead=bit39; unit=(ctrl>>12)&0x3F; page=(ctrl>>30)&0x1F` |
| E70 disk-ветка | `ref/extracode.cpp:148` `disk_io(op, unit-030, zone, 0, page<<10, 1024)` | `E70()`: `unit in [24,56)` → `_diskByUnit(unit)`, `memAddr=page<<10`, `ReadToMemory(zone,0,memAddr,1024)` |
| E70 drum/phys-io | `ref/extracode.cpp:153-190` | `E70()`: `tract=ctrl&0x1F; sector=(ctrl>>6)&3; paragraph=(ctrl>>24)&3; physIo=bit38; sectIo=bit47; rawSect=bit35` |
| E70 phys-io зона | `ref/extracode.cpp:172` `zone = tract + (this_drum-mapped_drum)*040` | `E70()`: `diskZone = tract + (thisDrum-_mappedDrum)*32` |
| E57 dispatch | `ref/e57.cpp:31-74` | `ExtracodeHandler.cs` → `E57()` |
| **E57 case 7 (WAIT)** | `ref/e57.cpp:51-53` (recoverable) | `ExtracodeHandler.cs` `E57()`: `case 7: throw …` (**фатально**) |
| E57 ASSIGN | `ref/e57.cpp:95-102` `disk_mount(M[015], ACC, write)`; `ACC=M[015]` | `E57()`: `diskUnit=M[13]&0x7F; _mountTape(ACC,diskUnit); SetAcc(diskUnit)` |
| E57 FIND | `ref/e57.cpp` (e57_tape) | `E57()`: `_findTape(ACC)` → `SetAcc(unit)` |
| `disk_mount` | `ref/machine.cpp:415-420` (`disk_unit-=030`) | `DubnaLoader.cs` / `ExtracodeHandler.cs` `_mountTape` |
| `map_drum_to_disk` | `ref/machine.cpp:685-693`; `map_drum_to_disk(021,030)` в `machine.cpp:937` | `DubnaLoader.cs` `_mappedDrum`,`_physIoDisk` |
| `PHYS_IO_UNIT` | `ref/machine.h:139` = `0100` (octal) | `DubnaLoader.cs` |
| COSY-кодек | `ref/cosy.cpp` | `src/besm6.net/Loader/CosyCodec.cs` → `EncodeCosy`/`DecodeCosy` |
| Трейс E70 (C#) | — | `ExtracodeHandler.cs` E70 trace block (`[E70] …`, формат `:X` = **hex**) |

**Важно про трейс C#:** формат `Convert.ToString(x,8)` для unit/page/zone, но `:X` (HEX) для `drum`/`mapped`/`tape`. Не путать.

---


## 5. Гипотезы

### ✅ Опровергнуто
- **Drum→disk маппинг** — «расхождение» было артефактом hex/octal в логе.
- **Расхождение в E70-маршрутизации/декодировании** — все 41 оп. идентичны.
- **Phys-io маппинг** — `mapped_drum=021` в обоих; формула зоны `tract+(thisDrum-mapped)*32` совпадает.
- **Disk-unit offset** (`-030` в C++ vs нет в C#) — каждая система самосогласована (ASSIGN и READ используют один и тот же индекс); физический диск один и тот же (b.7 → disk 40).

### 🟡 Подтверждено
- **C# E57 `case 7` = фатальный `throw`**, C++ = recoverable pause; C++ к case 7 не приходит.
- **Разные PC** после последней общей операции → MONSYS ветвится по-разному на данных job-card / состоянии E57.
- **E70 последовательность идентична** (41 оп., порядок, поля).
- ASSIGN одинаков (b.7 → disk 40) в обоих.

### 🔴 Открыто (главные кандидаты)
1. **COSY-кодировка job-card:** `EncodeCosy`/KOI-7-маппинг C# vs C++ — если разное, MONSYS читает другую job-card ⇒ другой путь.
2. **Состояние E57 после ASSIGN:** что лежит в ACC/M[13]/M[15] после ASSIGN и что возвращает `_findTape`/ASSIGN в C# vs C++.

---

## 6. Следующие шаги (по приоритету)

1. **Сравнить содержимое job-card побайтово.**
   - Job-card = `Drum 20 Zone 4 [76000-77777]` (256 слов × 48 бит).
   - В C#: дампить `_drumByUnit(20).ReadToMemory(...)` в `page 037` после `00051 *70 45`.
   - В C++: аналогичный дамп (`machine.drum_io('r', 20, 4, 0, addr, 256)`).
   - Сравнить 256×6 байт. Любая разница = корневая причина.
2. **Сравнить COSY-энкодеры:** `CosyCodec.EncodeCosy` (C#) vs `cosy.cpp` (C++) + KOI-7/GOST-10859 таблицы. Проверить порядок байт в слове (byte[0] MSB vs LSB) и кодирование пробелов.
3. **Если job-card идентична → искать в E57-состоянии:** после ASSIGN дампить `ACC`, `M[13]`, `M[15]` в обоих; сравнить результат `_findTape`/ASSIGN.
4. **Добавить в C# дамп E70-данных** (эквивалент C++ `dump_io_flag`, `ref/machine.cpp:343-346`), чтобы иметь побайтовый дамп каждой E70-операции.
5. **Локализация ветвления:** в C# поставить точечный дамп ACC/M13/M16/PC за 1–2 инструкции до `022003` и до C++ `02514`, чтобы увидеть, от какого именно слова MONSYS выбирает путь.

---

## 7. Что проверить ПЕРВЫМ делом (чек-лист для новой сессии)

- [ ] Прочитать `CosyCodec.cs` (`EncodeCosy`) и `ref/cosy.cpp` — сравнить побайтово.
- [ ] Прочитать `ExtracodeHandler.cs` `E57()` целиком (case 7 + ASSIGN + FIND) — сверить с `ref/e57.cpp`.
- [ ] Прочитать `DubnaLoader.cs`: `_mountTape`, `_mappedDrum`, `_physIoDisk`, `_diskByUnit`, `_drumByUnit` — сверить с `machine.cpp` `disk_mount`/`map_drum_to_disk`/`disk_io`.
- [ ] Сгенерировать в C# побайтовый дамп job-card (`Drum 20 Zone 4`) и сравнить с C++.
- [ ] Проверить: в C# `case 7` = `throw`; надо ли сделать его recoverable (как в C++)? Но это **лечение симптома**, не причины — C++ к нему не приходит.
- [ ] Подтвердить: последние общие PC `…00051 *70 45`, затем C++ `02514`, C# `022003`.

---


## 8. Приложения (сырые строки)

### C# `ec_trace.log` (финал до падения)
```
[EC] 56 (070) aex=021 M16=041 ACC=080C7C0011001 PC=076673
[E70] m16=041 cw=080C7C0011001 op=R unit=021 page=037 zone=01 tract=01 sect=0 par=00 rawSect=0 physIo=1 sectIo=1 -> PHYSIO(drum=011,mapped=011)
[EC] 56 (070) aex=02B M16=053 ACC=04C0010000 PC=077203
[E70] m16=053 cw=00004C0010000 op=W unit=020 page=023 zone=00 tract=00 sect=0 par=00 rawSect=0 physIo=0 sectIo=0 -> DRUM(010)
[EC] 61 (075) aex=029 M16=051 ACC=038025090000 PC=076106
[EC] 61 (075) aex=02B M16=053 ACC=09800D0C0000 PC=076106
[EC] 56 (070) aex=025 M16=045 ACC=000000163 PC=051
[E70] m16=045 cw=00087C0010004 op=R unit=020 page=037 zone=04 tract=04 sect=0 par=00 rawSect=0 physIo=0 sectIo=0 -> DRUM(010)
[EC] 47 (057) aex=07 M16=07 ACC=0900000002000 PC=022003
[E57] addr=07 ACC=0900000002000 M[13]=053F
```
> `ACC=0900000002000` — слово-статус/код job-card, по которому MONSYS решил «ждать ленту». `M[13]=053F`.

### C++ (финал, из `plans/porting-report.md`)
```
00051 *70 45   → Drum 20 Read [76000-77777] = Zone 4   (job card)
02514 *70 2664 → Disk 40 Read [74000-75777] = Zone 20   (B-компилятор)
… продолжает
```

### C# E57 case 7 (код)
```csharp
case 7:
    // Task paused waiting for tape.
    throw new ProcessorException("E57: Task paused waiting for tape");
```

### C++ E57 case 7 (код)
```cpp
case 7:
    // Delay the task, presumably waiting for tape to be installed by operator.
    throw Exception("Task paused waiting for tape");
```
> Оба «бросают», но в C++ это обрабатывается монитором как pause (recoverable), в C# — летально. Тем не менее **C++ к этой ветке не приходит** — значит, причина в том, что MONSYS её выбирает, а не в семантике броска.

