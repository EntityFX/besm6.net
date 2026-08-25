# План: порт CERN-тестов (C++ `cernlib_test.cpp`) на C# (MSTest)

Финальная версия плана (редакция от 25.08.2026) — уточняет черновик `plans/tests.md`
на основе сверки с кодом. Все ссылки `файл:строка` проверены по состоянию ветки `main`.

## 0. Цель и критерий успеха (ИСПРАВЛЕНО)

**Цель:** все активные CERN-тесты (lib1 + lib2) исполняются в C#-симуляторе и проходят
со сравнением вывода «как в C++» (`EXPECT_EQ`), либо явно классифицируются.

**Корректировка к черновику:** в C++ **не 421, а 397 активных тестов**:
- lib1: **183 активных** (+5 закомментированных: d150, d612, d612a, e200, e410a; всего .f — 188);
- lib2: **214 активных** (+19 закомментированных, в т.ч. `w303` «loops forever»; всего .f — 237);
- в lib2 есть ещё 4 `.f` без теста вообще (relkin, roses, si, sincos) — вне рамок (уже в examples);
- у всех 397 активных тестов есть `.f` и `expect_*.txt` (проверено скриптом
  `plans/_count_cernlib.ps1`, его же переиспользуем для генерации `[DataRow]`).

**Критерий успеха (финальный):**
- `dotnet test src/besm6.net/tests/Besm6.Tests --filter CernLib` — 397 тестов,
  набор «passed/failed» совпадает с C++ (`TEST_ALL=ON`) в пределах согласованных расхождений;
- `w303` — `[Ignore]` (в C++ закомментирован) либо не включается в набор;
- отчёт `plans/cernlib-port-report.md`: таблица `libN/test → C++ → C# → причина`.

## 1. Соответствие «C++ → C#» (верифицировано)

| C++ (dubna / gtest) | C# (besm6.net / MSTest) | Статус |
|---|---|---|
| `Machine` + `Memory` (`fixture_machine.h:44`) | `MachineCore` (`Core/MachineCore.cs`, `Reset()` есть) | ✅ |
| `load_script(job.dub)` → барабан #1, COSY + доп. `*end file` (`machine.cpp:268-291`) | `JobParser.ParseFile` + `WriteScriptToDrum` (`DubnaLoader.cs:161-206`, доп. `*end file` на 190) | ✅ совпадает |
| `boot_ms_dubna()` (`machine.cpp:930`) | `BootMsDubna()` (`DubnaLoader.cs:535`) | ✅ |
| `run()` limit 10^9 | `RunBounded()`, `InstructionLimit = 1e9` (`DubnaLoader.cs:34,424`) | ✅ |
| Перехват `std::cout` | `_loader.Output` + `Console.SetOut` (паттерн `IntegrationTests.cs:20-36`) | ✅ |
| `test_cernlib(lib, name)` (`fixture_machine.h:96-143`): job = пролог + `.f` + `*end file` | **новый** `CernLibFixture.BuildJob/RunAndCompare` | **сделать** |
| `EXPECT_EQ(result, expect)` | `Assert.AreEqual` + line-diff + артефакт в `tests-run/cernlib/` | **сделать** |
| `TEST_F(dubna_machine, cernlib_X)` ×397 | `[TestMethod] [DataRow]` в `CernLibTests.cs` | **сделать** |
| C++ при падении **переписывает** expect (`fixture_machine.h:88,140`) | **запрещено**: артефакты только `tests-run/cernlib/actual_*.txt` | ⚠️ |
| пролог job: `*name X` / `*tape:12/librar,32` / `*library:1,2,3,5,12,23` / `*call setftn:one,long` / `*no list` / `*no load list` (строка 104-109) | идентично, побайтово (в т.ч. без пробела в `1,2,3,5,12,23`) | ⚠️ дословно |

Важные детали, подтверждённые кодом:
- `WriteScriptToDrum` дополнительно транслирует `*assem→*madlen`, `*forex→*fortran`
  (`DubnaLoader.cs:180-185`) — для cernlib-пролога не срабатывает, оставляем как есть.
- E50 `DATE*` (case 55, 067 oct) в C# уже фиксирован: 04/07/24 23:45:56
  (`ExtracodeHandler.cs:315-328`) — совпадает с expect (проверено по `expect_z005.txt`:
  `04 ИЮЛ 24 23.45`, `DATA=04/07/24`, `IDATA=240704`).
- CERN-линковка в C++ идёт **в runtime**: MONSYS сам делает E57 ASSIGN/FIND с
  tape-id `TAPE_LIBRAR_12` (`machine.cpp:215`, `e57.cpp`); C# уже покрывает ASSIGN →
  `MountTape(unit, tapeId)` → `FindImagePath(tapeId)` → `librar.12` (`DubnaLoader.cs:112-135`).
- Образы лент на месте: `tapes/librar.12` (1 966 080 Б) и `src/besm6.net/tapes/librar.12`;
  `DefaultTapesDir()` (`TapeImage.cs:199-230`) находит их и из CWD, и из `bin/Debug`.

## 2. Блокирующие проблемы до массового запуска (Фаза 0)

### P0.1 — Монтаж по `*tape` карте (критический баг, подтверждён)
`MountScriptTapes` (`DubnaLoader.cs:140-151`) → `TapeImage.TapeIdByName(mount.Name)`
(`TapeImage.cs:159-172`): имя «librar» (без номера) даёт `TapeLibrar37`, канал игнорируется.

**Точный фикс.** Канал в карте `*tape:12/librar,32` парсится как **восьмеричный**
(`JobParser.ParseTapeMount`, `TryParseOctal` → decimal). Номер ленты в tape-id — это тот же
восьмеричный канал: `012(oct)=10(dec)` → `TapeLibrar12`, `037(oct)=31(dec)` → `TapeLibrar37`,
`011(oct)=9(dec)` → `TapeMonsys`, `007(oct)=7(dec)` → `TapeB`, `0331(oct)=217(dec)` → `TapeBemsh`
(таблица из констант `TapeImage.cs:28-32`; «739» в имени файла bemsh — НЕ номер канала).

- Новая сигнатура: `TapeImage.TapeIdByName(string name, int channelOctalDec)`:
  приоритет — канал (mapping выше), имя — fallback (текущее поведение).
- `MountScriptTapes` передаёт `mount.Channel`.
- Юнит-тесты: `("librar", 10) → TapeLibrar12`, `("librar", 31) → TapeLibrar37`,
  `("monsys", 9) → TapeMonsys`, `("b", 7) → TapeB`, fallback без канала не меняется.
- Страховка: в фикстуре после `MountScriptTapes` для CERN-джоба проверяем, что
  `TapeLibrar12` реально загрузился из файла (не «пустой 288-зонный нулевой диск»
  из `MountTape` fallback, `DubnaLoader.cs:120-128`).

### P0.2 — Детерминизм вывода
- expect — LF; C# на Windows может дать CRLF/`\r` (E71 case 6 пишет `-\r`, `ExtracodeHandler.cs:756`).
  Нормализуем **только в сравнении**: `\r\n→\n`, `\r→\n`. Хвостовые пробелы — значимы
  (C++ `EXPECT_EQ` строгий); при расхождении подозреваем баг C#, а не нормализацию.
- **E71 ввод (важно, не было в черновике):** `case 6` при `Input==null` делает
  `Console.ReadLine()` (`ExtracodeHandler.cs:757` → `DubnaLoader.cs:76`) — в тест-хосте
  это может **повиснуть**. Фикстура обязана ставить `_loader.Input = _ => ""`
  (EOF-семантика, как у C++-референса), а не полагаться на консоль.
- Дата: фиксирована (P0.2 черновика — уже ок, см. §1).

### P0.3 — Скорость и стабильность
- `[DoNotParallelize]` на классе (MSTest 4.2.3 поддерживает), как однопроходный gtest.
- `[Timeout(300_000)]` на тест (300 с); полный прогон — батчами по алфавитным группам
  (`--filter FullyQualifiedName~CernLib&DisplayName~a`), как в `tests-run/run-full-matrix.ps1`.
- Loop-detector `RunBounded` (`DubnaLoader.cs:461-484`) при 20K+ инструкций в узком PC-диапазоне
  вернёт `Failed` — для cernlib-тестов это корректный сигнал зависания (не «красим» тест,
  а считаем падением с диагнозом).

### P0.4 — Известный риск «кислотного» экстракода (подтверждено кодом)
E57 `case 7` (WAIT / pause) в C# до сих пор **`throw ProcessorException`**
(`ExtracodeHandler.cs:462-464`), в C++ — recoverable pause (`ref/e57.cpp:51-53`).
Forex/HelloWorld проходят не через эту ветку, но CERN-линковка (чтение с ленты 12 + паузы
канала) может в неё попасть. **Мера:** загорятся «маяки» — если красные тесты падают
именно здесь, портим recoverable-паузу по `ref/e57.cpp` (см. также `plans/report-p2..p4.md`).

## 3. Итеративные фазы (исправленные числа)

### Фаза 0 — «Зажечь один тест»
1. `git` — зафиксировать текущие изменения (`TapeImage.cs`, `IntegrationTests.cs`, `plans/*`)
   и создать ветку `cernlib-port` (рабочее дерево сейчас грязное — см. `git status`).
2. Фикс P0.1 + юнит-тесты §2.
3. `CernLibFixture` (`tests/Besm6.Tests/CernLibFixture.cs`):
   - `TestInitialize`: `MachineCore` + `DubnaLoader` + перехват `Output`/`Console`
     (паттерн `IntegrationTests.cs:20-36`), **`Input = _ => ""`**;
   - `BuildJob(lib, name)`: temp `tests-run/cernlib/jobs/{lib}_{name}.dub` = пролог
     (дословно `fixture_machine.h:104-109`, LF-окончания) + `{ref/tests/libN}/{name}.f`
     + `*end file\n` (двойное `*end file` совпадает с C++ — `load_script` добавляет свой);
   - `RunAndCompare`: `RunScript(path)` → нормализация `\r→\n` → сравнение с
     `ref/tests/libN/expect_{name}.txt`; при падении — `actual_{name}.txt` + unified diff
     в сообщение; **никакой записи в `ref/tests/`**;
   - поиск каталогов: walk-up от CWD (паттерн `FindFileInParentDirs`, `IntegrationTests.cs:100`).
4. Маяки: `z005` (lib2, DATE*) и `a400` (lib1).
   - **DoD:** оба доходят до `STOP` без превышения лимита; вывод = expect ИЛИ задокументирован
     точный diff с пониманием точки расхождения (первое расходящееся E-слово, методология
     `plans/diagnostics-output.txt`).

### Фаза 1 — Пилотный батч lib1 (10–20 тестов)
1. Батч: a200, a400–a404, a500, b101, b102, c100, c101, c110, c201…
2. Для каждого красного: trace C++ (`--gtest_filter` + `debug_extracodes`) vs trace C#
   (`InstructionTrace`, `Diagnostics.cs`) → первое расходящееся E-слово; кандидаты на фикс:
   E57 case 7 (P0.4), E71/EOF, E50 case 014/017 (parse/format), E65/E72/IRET.
3. **DoD:** пилот ≥ 90% зелёный; каждый красный — с diff-артефактом и гипотезой.

### Фаза 2 — Полный lib1 (183 теста)
1. Сгенерировать `[DataRow]` из списка `cernlib_test.cpp` (скрипт `plans/_count_cernlib.ps1`
   уже извлекает имена в исходном порядке) — только активные имена; 5 закомментированных —
   `[Ignore]` с причиной из комментария C++ (OTCYTCTBYET DFUN, NABOR, `*file:scratch`, DTOC CTOD).
2. Прогон по группам (`a*`, `b*`, `c*`, …), фиксы.
3. **DoD:** «183/183 либо N провалов с классификацией»; 2 последовательных прогона идентичны.

### Фаза 3 — Полный lib2 (214 тестов)
1. То же; 19 закомментированных — `[Ignore]` с причинами из C++ (ELTRAN, VRAN3S,
   `*file:scratch`, «Tape drive is not supported» k45x, MINCOD, перенесённые в examples:
   q8xx, q9xx, t110b/c); `w303` — `[Ignore]` («This test loops forever»).
2. Особое внимание: F311/ленточный I/O, k-серия (k45x — ожидаемо Ignore), ran2/ran3
   (seed фиксирован в CERN-коде), календарные (z005), x/y-кассады.
3. **DoD:** `--filter CernLib` целиком отработал; итоговая таблица.

### Фаза 4 — Базовое выравнивание с C++ (опционально)
1. `cmake -DTEST_ALL=ON`, прогон `--gtest_filter=dubna_machine.cernlib_*` (отдельный процесс,
   фон), эталон passed/failed.
2. Сравнение наборов; каждое расхождение — в отчёт (баг C# / баг C++ / ограничение эмуляции).
3. **DoD:** расхождения ≤ 5 и каждое объяснено.

### Фаза 5 — Замыкание
1. `plans/cernlib-port-report.md` (таблица 397 строк, метрики: инструкции/время/тест,
   top-5 медленных).
2. `plans/porting-report.md` — строка статуса «CERN: N/397».
3. `tests-run/run-cernlib.ps1` (фазовые фильтры + watchdog-таймаут по образцу
   `run-full-matrix.ps1` + артефакты в `tests-run/cernlib/`).
4. `.gitignore` — добавить `tests-run/` (сейчас каталог untracked).
5. Чистые коммиты по фазам; `dotnet build` — 0 ошибок/0 предупреждений;
   остальные 66 тестов — зелёные.

## 4. Структура артефактов

```
src/besm6.net/tests/Besm6.Tests/
  CernLibFixture.cs        # build-job, run, compare, артефакты, walk-up до ref/tests/libN
  CernLibTests.cs          # [TestClass][DoNotParallelize] CernLib: 397 [DataRow] + 24 [Ignore]
plans/cernlib-port-plan.md # этот план
plans/cernlib-port-report.md
plans/_count_cernlib.ps1   # генератор списка имён (уже создан, проверен)
tests-run/cernlib/
  jobs/{lib}_{name}.dub    # сгенерированные job-файлы
  actual_{name}.txt        # фактический вывод при падении
  diff_{name}.txt          # unified diff
  run.log
```

## 5. Риски и меры

| Риск | Вероятность | Мера |
|---|---|---|
| librar.37 вместо librar.12 (P0.1) | гарантированно | фикс + юнит-тест до Фазы 0 |
| E57 case 7 (WAIT) = `throw` (P0.4) | высокая (кандидат «кислотный») | загорится на маяках; порт по `ref/e57.cpp` |
| Повисание на E71-вводе | средняя | `_loader.Input = _ => ""` в фикстуре (P0.2) |
| «Пустой нулевой диск» вместо librar.12 (fallback `MountTape`) | низкая | assertion в фикстуре (P0.1) |
| Различия E50 014/017 parse/format | средняя | word-в-слово сравнение с `ref/e50.cpp` |
| Медленный прогон (>1 ч) | высокая | батчи, `[Timeout]`, watchdog-скрипт |
| Нестабильность (порядок/EOF) | средняя | `[DoNotParallelize]`, 2 повторных прогона |
| «Повышение» expect-файлов | — | запрет записи в `ref/tests/`; артефакты в `tests-run/` |

## 6. Checklist первого цикла (Act)

1. [x] Финальный план сохранён в `plans/cernlib-port-plan.md` (этот файл).
2. [ ] `git`: коммит текущих изменений → ветка `cernlib-port`.
3. [ ] Фикс P0.1 (`TapeIdByName(name, channel)`, `MountScriptTapes`) + юнит-тесты.
4. [ ] `CernLibFixture` + 2 маяка (`z005`, `a400`): прогон, trace-диагностика.
5. [ ] По результатам — итерации Фазы 1 (пилот lib1).

## 7. Ответ на вопрос черновика про Фазу 4

Фазу 4 (C++-базу с `TEST_ALL=ON`) оставляем **опциональной и по умолчанию отключаемой**:
полный C++-прогон 397 FORTRAN-компиляций — это часы машинного времени, а для большинства
расхождений достаточно trace-методики (P0.4 / Фаза 1). Запускаем один раз перед Фазой 5,
чтобы закрыть «C# == C++» честным эталоном; если C++ не собирается на этой машине —
фиксируем в отчёте и ориентируемся на expect-файлы (они и есть эталон C++-вывода).

