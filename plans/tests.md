# План портирования CERN-тестов

> **Актуализация 30.08.2026.** В проекте 430 обнаруженных MSTest-тестов: 422 passed,
> 5 failed и 3 skipped. Три падения CERN вызваны тем, что fixture ищет `ref/tests`,
> тогда как тесты находятся в `ref/dubna/tests`; массовый CERN-набор ещё не запущен.
> После исправления пути нужно получить реальную матрицу 420 активных тестов, а не
> считать их пройденными. Общий статус и порядок работ:
> [simulator-readiness-report.md](simulator-readiness-report.md).

Собрал полный контекст. Кратко, что я выяснил до плана:

- **C++ источник**: `ref/tests/cernlib_test.cpp` — 421 тестов (188 × `test_cernlib(1, …)` из `ref/tests/lib1/*.f`, 233 × `test_cernlib(2, …)` из `ref/tests/lib2/*.f`), фикстура `dubna_machine::test_cernlib` в `ref/tests/fixture_machine.h`. Тесты собираются только с флагом CMake `TEST_ALL` (сейчас в репо не прогоняются).
- **Суть каждого теста**: собрать `.dub`-job (пролог `*name X / *tape:12/librar,32 / *library:1,2,3,5,12,23 / *call setftn:one,long / *no list / *no load list` + исходник `.f` + `*end file`), загрузить на барабан #1, бут MONSYS, скомпилировать FORTRAN через B-компилятор, линковать CERN-библиотеку с ленты 12 (образ `librar.12`), выполнить и **строгое** сравнение stdout с `expect_X.txt`.
- **C# сторона готова частично**: `DubnaLoader.RunScript()` + MONSYS (баннер, FORTRAN «Hello World» уже проходит в `IntegrationTests`), E50 DATE* с фиксированной датой 04/07/24 23:45:56 уже реализован — совпадает с expect-файлами.
- **Найден реальный баг при портировании**: `TapeImage.TapeIdByName("librar")` (без номера канала) возвращает `TapeLibrar37`, т.е. `*tape:12/librar,32` в C# смонтирует **librar.37** вместо **librar.12** — для CERN-тестов это сразу массовый сбой.
- Один тест в C++ закомментирован: `w303` (вечный цикл) — в C# он тоже должен быть `[Ignore]`.

Ниже — подробный итеративный план.

---

# План: порт CERN-тестов (C++ `cernlib_test.cpp`) на C# (MSTest)

## 0. Цель и критерий успеха

**Цель:** все 421 тест CERN (lib1 + lib2) исполняются в C#-симуляторе (`src/besm6.net`) и проходят с точностью сравнения как в C++ (`EXPECT_EQ` полный вывод), либо явно классифицируются как `Ignore`/`Fail` с артефактами.

**Критерий успеха (финальный):**
- `dotnet test src/besm6.net/tests/Besm6.Tests --filter CernLib` запускает 420 активных тестов (1 — `[Ignore]`, как `w303` в C++).
- Набор «passed/failed» в C# совпадает с набором в C++ (база: C++ с `TEST_ALL=ON`) в пределах согласованных расхождений.
- Отчёт в `plans/cernlib-port-report.md`: таблица `libN/test → C++ → C# → причина`.

## 1. Соответствие «C++ → C#» (карта)

| C++ (dubna / gtest) | C# (besm6.net / MSTest) | Примечание |
|---|---|---|
| `Machine` + `Memory` | `MachineCore` | |
| `machine->load_script(job.dub)` → барабан #1 | `JobParser.ParseFile` + `DubnaLoader.WriteScriptToDrum` | уже есть |
| `machine->boot_ms_dubna()` | `DubnaLoader.BootMsDubna()` / `BootAndRun` | уже есть |
| `machine->run()` (limit 10^9) | `DubnaLoader.RunBounded()`, `InstructionLimit = 1_000_000_000` | уже есть |
| Перехват `std::cout` | `DubnaLoader.Output` (delegate) + `Console.SetOut` | уже есть в `IntegrationTests` |
| `test_cernlib(lib, name)` — сборка job-файла | новый хелпер `CernLib.Run(job name)` | **сделать** |
| `EXPECT_EQ(result, expect)` | `Assert.AreEqual` + line-diff + артефакт | **сделать** |
| `TEST_F(dubna_machine, cernlib_XXX)` ×421 | `[TestMethod] [DataRow]` в `CernLibTests.cs` | **сделать** |
| `expect_XXX.txt` в `ref/tests/libN` | те же файлы, только **read-only** | ⚠️ в C++ при падении фикстура **переписывает** expect — в C# этого делать **нельзя**, писать `actual_XXX.txt` в `tests-run/cernlib/` |
| gtest timeout 120 c/тест | `[Timeout(...)]` | |

## 2. Блокирующие проблемы до массового запуска (Фаза 0)

### P0.1 — Монтаж ленты 12 (критический баг)
`MountScriptTapes` → `TapeIdByName(mount.Name)`; имя из карты — `librar` (без номера), `Zone`-поле `32` игнорируется:
- `TapeImage.TapeIdByName("librar")` → `TapeLibrar37` ❌ (должно быть `TapeLibrar12`, т.к. канал = 12).
- **Фикс:** выбор tape-id по **каналу**: `9→Monsys, 12→Librar12, 37→Librar37, 739→Bemsh, 7→B` (имя — fallback). Точка: `DubnaLoader.MountScriptTapes` + сигнатура `TapeImage.TapeIdByName(name, channel)`.
- Проверяем, что `FindImagePath(TapeLibrar12)` находит `librar.12` в `src/besm6.net/tapes` / `ref/tapes` (файлы есть: 1966080 байт).

### P0.2 — Детерминизм вывода
- Дата: E50 case `067` (DATE*) в C# уже фиксирует `04/07/24 23:45:56` — совпадает со всеми expect-файлами ✔.
- Концевые символы: expect — `\n`; C#-вывод может давать `\r\n`. Решение: нормализация `\r\n→\n` **только на стороне сравнения** (в C++ сравнение идёт над raw-строкой с `\n`).
- Хвостовые пробелы: фикстура C++ — строгое `EXPECT_EQ` (не `check_output`), т.е. хвосты считать **важными**; при расхождении — сначала подозреваем баг C#, а не нормализацию.

### P0.3 — Скорость
Каждый тест = полная компиляция FORTRAN (MONSYS + B-компилятор + линковка CERN). Оценка: секунды–минуты на тест, полный прогон — час+. Меры:
- `[Timeout]` на тест (300 c) + отчёт о количестве инструкций.
- Запуск батчами (`--filter CernLib/Name~lib1_a` и т.п.) как в `tests-run/run-full-matrix.ps1`.
- `[DoNotParallelize]` на классе (как C++ gtest — однопроходное), параллельные проганы только между батчами через отдельные процессы.

## 3. Итеративные фазы

### Фаза 0 — «Зажечь один тест» (1 итерация)
1. `git` — создать ветку `cernlib-port`.
2. Фикс P0.1 (монтаж ленты 12) + юнит-тест `TapeIdByName("librar", 12) == TapeLibrar12`.
3. Реализовать `CernLibFixture` (в `Besm6.Tests/CernLibTests.cs`):
   - `TestInitialize`: `MachineCore` + `DubnaLoader` + перехват `Output` (паттерн из `IntegrationTests.cs`);
   - `BuildJob(name, libDir)`: записать в temp `jobs/{name}.dub` = пролог + `{libDir}/{name}.f` + `*end file` (пролог **в точности** как в `fixture_machine.h`);
   - `RunAndCompare(name)`: `RunScript` → нормализация `\r\n→\n` → сравнение с `ref/tests/libN/expect_{name}.txt`; при падении — `tests-run/cernlib/actual_{name}.txt` + unified-diff в сообщение;
   - поиск каталогов данных: `ref/tests/lib1|lib2` (walk-up от CWD, как `FindFileInParentDirs`).
4. Один тест-«маяк»: `z005` (lib2, короткий, проверяет DATE*) и один из lib1 (например `a400`).
   - **Definition of Done:** оба запускаются до `STOP` без превышения лимита; вывод совпадает с expect (или есть задокументированный точный diff, и мы поняли, где расхождение).

### Фаза 1 — Пилотный батч lib1 (10–20 тестов)
1. Включить 10–20 «простых» тестов lib1 (arithmetic a200, a400–a404, a500; b101, b102; c100, c101, c110, c201…).
2. Для каждого упавшего: сравнить trace C++ (`dubna_ref.exe`/`unit_tests.exe` с `--gtest_filter` + `debug_extracodes`) с C#-trace (`InstructionTrace`/`Diagnostics.cs`), найти **первое расходящееся E-слово/инструкцию** (методология `plans/diagnostics-output.txt` уже есть).
3. Фиксы симулятора (кандидаты по `plans/porting-report.md`): E57 WAIT (case 7) — «pause», а не fatal; E72 no-op; E65; IRET; E71 input=EOF.
   - **DoD:** пилотный батч ≥90% зелёный; каждый красный — с diff-артефактом и гипотезой.

### Фаза 2 — Полный lib1 (188 тестов)
1. Сгенерировать все `[DataRow]` для lib1 (генератор из списка `ref/tests/lib1/*.f` + списка имён из `cernlib_test.cpp` — только те, что есть в C++-файле).
2. Прогон `--filter` по алфавитным группам (`a*`, `b*`, `c*`, …), фиксы.
3. **DoD:** отчёт «188/188 либо N провалов с классификацией»; прогон стабильно повторяем (2 запуска подряд).

### Фаза 3 — Полный lib2 (233 теста)
1. То же для lib2; `w303` — `[Ignore]` с комментарием «This test loops forever» (как в C++).
2. Особое внимание: тестам с `*tape`-I/O (F311 и т.п.), календарным (z005), случайными (ran2/ran3 — seed фиксирован в CERN-коде), кассадным (x/y-серии).
3. **DoD:** полный набор `--filter CernLib` отработал; итоговая таблица.

### Фаза 4 — Базовое выравнивание с C++ (опционально, но желательно)
1. Собрать C++ с `cmake -DTEST_ALL=ON`, прогнать `--gtest_filter=dubna_machine.cernlib_*` (фон, отдельно процесс) — получить эталон passed/failed.
2. Сравнить наборы; для каждого расхождения — запись в отчёт (причина: баг C#, баг C++, ограничение эмуляции у обоих).
3. **DoD:** расхождения ≤5 и каждое объяснено.

### Фаза 5 — Замыкание
1. Отчёт `plans/cernlib-port-report.md` (таблица, метрики: кол-во инструкций, время/тест, top-5 самых медленных).
2. Обновить `plans/porting-report.md` (статус «CERN: N/421»).
3. CI-скрипт `tests-run/run-cernlib.ps1` (фазовые фильтры + таймаут + артефакты в `tests-run/cernlib/`).
4. `git` — чистые коммиты по фазам; `dotnet build` 0 ошибок/0 предупреждений.

## 4. Структура артефактов

```
src/besm6.net/tests/Besm6.Tests/
  CernLibTests.cs          # [TestClass] CernLib: [DataRow(1,"a200")] … [DataRow(2,"z054")]  (421 строка, w303 — отдельный [Ignore])
  CernLibFixture.cs        # сборка job-файла, run, compare, артефакты, поиск ref/tests/libN
plans/cernlib-port-plan.md # этот план
plans/cernlib-port-report.md
tests-run/cernlib/
  jobs/{name}.dub          # сгенерированные job-файлы (temp, в .gitignore)
  actual_{name}.txt        # фактический вывод при падении
  diff_{name}.txt          # unified diff
  run.log
```

## 5. Риски и меры

| Риск | Вероятность | Мера |
|---|---|---|
| Lentar.37 вместо .12 (P0.1) | гарантированно | фикс до Фазы 0 + тест |
| Один «кислотный» E-код, падающий 30% тестов | средняя | Фазы 1–2 изолируют; trace-методика есть |
| Медленный C#-прогон (>1 ч) | высокая | батчи, `[Timeout]`, фоновые проганы как в `tests-run/full` |
| Нестабильность (порядок вывода/EOF) | средняя | `[DoNotParallelize]`, EOF-input, 2 повторных прогона |
| Различия в E50 parse/format (case 014/017) при чтении/печати чисел | низкая | сравнение word-в-слово с `besm6.cpp` |
| «Повышение» expect-файлов | — | запрет записи в `ref/tests/` из тестов (артефакты только в `tests-run/`) |

## 6. Порядок действий в Act-режиме (checklist первого цикла)

1. Сохранить этот план → `plans/cernlib-port-plan.md`.
2. Фикс `TapeIdByName`/`MountScriptTapes` + юнит-тест.
3. `CernLibFixture` + 2 маячных теста (`z005`, `a400`), прогон, диагностика.
4. По результатам — итерации Фазы 1.

---

**Вопрос по плану:** включить ли Фазу 4 (сборка и прогон C++-базы с `TEST_ALL=ON` для эталонного сравнения)? Это займёт значительное время (421 FORTRAN-компиляция в C++), но даёт честный критерий «C# == C++». По умолчанию я включу её как опциональную и выполню, если Фазы 0–3 сойдутся по «маякам».

Если план ок — включите **Act** (переключатель Plan/Act), и я начну с Фазы 0: сохранение плана в `plans/`, фикс монтажа ленты 12, фикстура и два маячных теста.
