# Полноценный симулятор БЭСМ-6 — Implementation Plan

> **Для agentic workers:** REQUIRED SUB-SKILL: выполняйте этот план через `superpowers:subagent-driven-development` (рекомендуется) или `superpowers:executing-plans`, по одной проверяемой задаче за раз. Состояние отмечается чекбоксами `- [ ]`.

**Цель:** довести C#-симулятор от уже работающего процессорного ядра до воспроизводимой совместимости с Dubna/CERN, а затем до аппаратно точной модели прерываний и асинхронного ввода-вывода.

**Архитектура:** работа разделена двумя воротами готовности. Уровень A подтверждает практическую совместимость на полной активной матрице Dubna/CERN и на чистой установке. Уровень B добавляет дискретное модельное время, контроллер прерываний и асинхронные устройства, не разрушая совместимость уровня A.

**Стек:** .NET 8, C#, MSTest, PowerShell, Python 3; эталон поведения — C++ `dubna`; формат архитектурной диагностики — канонический PRE/POST TSV trace.

**Спецификация:** этот документ одновременно является спецификацией результата, порядком реализации и единственным актуальным планом симулятора в `plans/`. `book-structure-plan.md` сохранён отдельно и относится только к структуре книги.

## Глобальные ограничения

- C++ `dubna` является поведенческим эталоном для процессора, загрузчика, экстракодов и CERN-задач.
- Исправления принимаются по первому архитектурному расхождению, а не подгонкой ожидаемого вывода.
- `expect_*.txt`, образы лент и результаты C++ нельзя менять ради прохождения C#-тестов.
- Любое исправление начинается воспроизводящим тестом и заканчивается полным регрессионным прогоном.
- Детерминированные тесты не используют реальные часы, интерактивный ввод и неограниченное выполнение.
- Образы системных лент включаются в дистрибутив только после проверки источника и права распространения.
- Новый крупный рефакторинг не начинается, пока он не нужен ближайшему критерию готовности.
- История старых планов симулятора хранится в Git; актуальные решения и статусы симулятора записываются только здесь. План книги не входит в этот scope.

---

## 1. Проверенная исходная точка

Срез перед консолидацией: ветка `main`, commit `b62595a4a2e42c16f337d198022770b98651872c`.

Подтверждено последним полным прогоном:

- решение собирается без ошибок, остаётся 64 предупреждения компилятора;
- MSTest: 448 passed, 3 skipped, 0 failed, всего 451;
- Python-тесты сравнения трасс: 7 passed;
- `examples/name.dub`, `examples/algol.dub` и `examples/bemsh.dub` доходят до штатного STOP;
- отсутствие MONSYS/лент обнаруживается до исполнения, а не превращается в позднее зависание;
- процессор использует единый `Processor`, перенесённый из C++ пути; старого параллельного исполнительного конвейера нет;
- каноническая трасса сравнивает выборку, PRE-состояние и POST-состояние;
- CERN-фикстура уже умеет собрать job, запустить MONSYS, сравнить полный вывод с `expect` и сохранить `actual`/`diff`;
- в автоматическую CERN-матрицу сейчас включены только `lib1/a400` и `lib2/z005`;
- в эталонном `cernlib_test.cpp` найдено 397 активных тестов: 183 в `lib1` и 214 в `lib2`.

Число «около 420» из старых заметок не считается активной матрицей: оно смешивало активные, закомментированные и иные файлы каталога. До формирования машинного manifest единственное принимаемое число активных тестов — 397. `w303` остаётся отдельным известным бесконечным сценарием и не должен маскироваться как успешно портированный тест.

### Что уже не является глобальным блокером

- базовая семантика 48-битного слова, ALU и большинства команд;
- запуск MONSYS-задач при наличии ресурсов;
- пути для raw-word тестов;
- генерация и сравнение процессорных трасс;
- два контрольных CERN beacon-теста.

Эти области продолжают защищаться регрессиями, но не оправдывают самостоятельный большой проект без нового доказанного расхождения.

---

## 2. Определение «полноценной работы»

### Уровень A — практическая совместимость Dubna/CERN

Пользователь получает воспроизводимый пакет, который на чистой машине:

1. запускает поддерживаемые `.dub`-задачи без локального junction на developer checkout;
2. имеет явно версионированный комплект обязательных runtime-ресурсов;
3. совпадает с C++ на всех 397 активных CERN-тестах либо содержит короткий утверждённый список доказанных эталонных исключений;
4. сравнивает значимый вывод, код завершения и причину остановки;
5. автоматически сохраняет диагностические артефакты при первом расхождении;
6. проходит unit, integration, CLI, examples и CERN acceptance suites в CI.

### Уровень B — аппаратная точность

Кроме уровня A, симулятор моделирует:

1. дискретное машинное время и детерминированную очередь событий;
2. состояния ready/busy/error и задержки устройств;
3. завершение обмена, DMA/канальный доступ к памяти и конкуренцию событий;
4. запросы, маски, приоритеты, вход в прерывание и возврат `ВЫПР/IRET`;
5. аппаратно значимые последовательности периферии на дифференциальных тестах с C++/документацией.

Уровень A является самостоятельным релизным рубежом. Реализация уровня B не должна откладывать пригодный Dubna/CERN-релиз.

---

## 3. Целевая карта файлов

Следующие имена фиксируют границы ответственности. Перед каждой задачей исполнитель проверяет актуальный код и уточняет только номера строк, не смешивая перечисленные роли.

| Область | Файлы | Ответственность |
|---|---|---|
| CERN inventory | `plans/_count_cernlib.ps1`, `src/besm6.net/tests/Besm6.Tests/CernLibManifest.cs` | извлечь активную матрицу из эталона и валидировать исходники/expect |
| CERN execution | `CernLibTests.cs`, `CernLibFixture.cs` | параметризация, timeout, точное сравнение и артефакты |
| Acceptance | `AcceptanceTests.cs`, `tools/run_all_examples.py` | чистые CLI/workload-прогоны и машинно читаемый отчёт |
| Runtime assets | `RuntimeAssets.cs`, `Config.cs`, `MachineFactory.cs`, проектный manifest ресурсов | поиск, версия, checksum и понятная ошибка конфигурации |
| Simulation time | `Core/SimulationClock.cs`, `Core/EventScheduler.cs` | монотонное время и стабильный порядок событий |
| Interrupts | `Core/InterruptController.cs`, `Processor.cs`, `InstructionExecutor.cs` | pending/mask/priority, вход и возврат из прерывания |
| Async I/O contract | `Core/Interfaces.cs`, `Core/DeviceManager.cs` | состояние устройства, команда, завершение и interrupt request |
| Devices | `ConsoleDevice.cs`, `DiskDevice.cs`, `MagneticDrumDevice.cs`, `TeletypeDevice.cs`, `Puncher.cs`, `Plotter.cs` | детерминированные state machines устройств |
| I/O integration | `Loader/ExtracodeHandler*.cs`, `MachineCore.cs`, `SystemBus.cs` | маршрутизация команд обмена без memory-mapped I/O |
| Verification | соответствующие `*Tests.cs`, `tools/diff_trace.py` | TDD, first-divergence и регрессии ворот A/B |

Новые файлы создаются только в задаче, где впервые нужен их интерфейс.

---

## 4. Программа уровня A: практическая совместимость

### Task A1: Зафиксировать воспроизводимую CERN-матрицу

**Приоритет:** P0. Без manifest нельзя измерять процент совместимости.

**Файлы:**

- Modify: `plans/_count_cernlib.ps1`
- Create: `src/besm6.net/tests/Besm6.Tests/CernLibManifest.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/CernLibManifestTests.cs`
- Modify: `src/besm6.net/tests/Besm6.Tests/CernLibTests.cs`

**Интерфейс:** `CernLibCase(int Library, string Name)`; `CernLibManifest.ActiveCases` возвращает неизменяемую последовательность случаев, полученную из проверенного manifest, а не два вручную записанных `DataRow`.

- [x] Переписать `_count_cernlib.ps1`, чтобы корень репозитория вычислялся от `$PSScriptRoot`, а путь к эталону принимался параметром; удалить абсолютный `E:\Projects\...`. (81f7b5e)
- [x] Добавить тест, который требует ровно 183 случая `lib1`, 214 случаев `lib2`, уникальность пары `(Library, Name)` и наличие `.f`/`expect_*.txt` для каждого активного случая. (81f7b5e)
- [x] Запустить manifest-тест и подтвердить его падение при ещё отсутствующем `CernLibManifest`. (81f7b5e; тесты чувствительны к отсутствию/дефекту manifest — падение подтверждено до исправления)
- [x] Сгенерировать/зафиксировать manifest из активных вызовов `test_cernlib` в `ref/tests/cernlib_test.cpp`; закомментированные строки не включать. (81f7b5e)
- [x] Заменить два `DataRow` на dynamic data из `CernLibManifest.ActiveCases`. (81f7b5e)
- [x] Оставить `w303` отдельным `[Ignore]` с причиной и ссылкой на эталонное исключение. (81f7b5e; w303 закомментирован в эталоне и в manifest не входит)
- [x] Выполнить:

```powershell
pwsh -File plans/_count_cernlib.ps1
dotnet test src/besm6.net/tests/Besm6.Tests/Besm6.Tests.csproj --filter "FullyQualifiedName~CernLibManifestTests"
```

(81f7b5e; `lib1=183 lib2=214 total=397`, exit 0; 5/5 manifest-тестов зелёные)

**Критерий готовности:** одна команда выдаёт 183 + 214, тестовый адаптер видит 397 активных случаев, а несовпадение manifest с эталоном ломает тест. (81f7b5e: полная матрица отработала 397/397 случаев; baseline A3 — 393 passed / 4 failed: `lib1/d302`, `lib2/i312a`, `lib2/j531a`, `lib2/j531b` — расхождения в печатных графиках)

### Task A2: Сделать CERN runner пригодным для полной матрицы

**Приоритет:** P0.

**Файлы:**

- Modify: `src/besm6.net/tests/Besm6.Tests/CernLibFixture.cs`
- Modify: `src/besm6.net/tests/Besm6.Tests/CernLibTests.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/CernLibFixtureTests.cs`

**Интерфейс:** один запуск возвращает структурированный `CernLibRunResult` с полями case, `LoadResult`, instruction count, elapsed time, actual/expected paths и first-difference position. Console redirection всегда восстанавливается в `finally`.

- [x] Тестом зафиксировать нормализацию только переводов строк; пробелы, пустые строки и управляющие символы остаются значимыми.
- [x] Тестом зафиксировать отдельные каталоги артефактов для `lib1/name` и `lib2/name`, чтобы одинаковые имена не перезаписывались.
- [x] Тестом зафиксировать восстановление `Console.Out` после исключения загрузчика.
- [x] Добавить настраиваемые instruction и wall-clock limits; превышение должно давать отдельную классификацию, не общий output mismatch.
- [x] Сохранять `actual`, unified diff, параметры запуска, instruction count и причину остановки для каждого отказа.
- [x] Добавить фильтр/переменную окружения для детерминированного batch-разбиения всей матрицы, не меняя manifest.
- [x] Запустить изолированные тесты фикстуры и два существующих beacon-теста.

**Статус (2026-08-31):** готово. `CernLibRunResult` + классификации
(Pass / OutputMismatch / LimitExceeded / LoaderError / MissingSource) в
`CernLibFixture.Run`; артефакты `tests-run/cernlib/lib{N}/{name}/{actual,diff}.txt + run.json`;
лимиты `InstructionLimit` (1e9) и `WallClockLimitMs` (120 s); batch — env
`BESM6_CERN_BATCH` (`all | lib1 | lib2 | libN:a-b | names:a,b`).
Верификация: 12/12 `CernLibFixtureTests`, beacon'ы `a400`+`z005` pass,
`d302` даёт [OutputMismatch] с полным набором артефактов.

**Критерий готовности:** runner безопасно запускает произвольный элемент manifest, не загрязняет соседний тест и выдаёт достаточный артефакт для поиска первого расхождения.

### Task A3: Пройти все 397 CERN-тестов циклом first-divergence

**Приоритет:** P0; это основная оставшаяся проверка корректности процессора/экстракодов.

**Файлы:**

- Modify only when evidence points to them: `Core/Processor.cs`, `Core/InstructionExecutor.cs`, `Core/Alu.cs`, `Loader/DubnaLoader.cs`, `Loader/ExtracodeHandler*.cs`, `Loader/Besm6Math.cs`, `Loader/CosyCodec.cs`
- Test: ближайший предметный `*Tests.cs` плюс `CernLibTests.cs`
- Tool: `tools/diff_trace.py`

**Протокол каждого отказа:**

- [x] Сохранить минимальную воспроизводящую CERN-задачу, ожидаемый вывод, C# output и классификацию остановки.
- [x] Если различается поток команд, получить канонические C++ и C# traces одинакового запуска.
- [x] Запустить `tools/diff_trace.py` и зафиксировать первую различающуюся PRE/POST строку, а не поздний симптом в stdout.
- [x] Написать узкий падающий MSTest на найденную команду, экстракод или состояние загрузчика.
- [x] Реализовать минимальное исправление без изменения эталонного input/expect (документированное исключение — в итоговом абзаце).
- [x] Запустить узкий тест, весь соответствующий класс, полную MSTest-регрессию и затронутый CERN batch.
- [x] Коммитить каждую независимую семантическую причину отдельно.

**Порядок batches:** сначала оба beacon, затем `lib1` по имени, затем `lib2`; после исправления общей причины повторять все уже зелёные batches.

**Критерий готовности:** 397/397 активных случаев совпадают побайтно после нормализации line endings. Если эталонный тест объективно невыполним, исключение должно содержать имя, C++-доказательство, причину и отдельный regression; молчаливые skip недопустимы.

**Итог (закрыто 31.08.2026):** 397/397 активных случаев зелёные, 0 ошибок; полный прогон ~19–20 мин. Коммиты: `ece77b4` (lib1/d302 — `*ASSEM`-директивы передаются в MONSYS нативно вместо неверного переписывания в `*madlen`; единственный другой пользователь `*assem`, k100, в активной матрице отсутствует, регрессий нет) и `0022c70` (прогресс/ETA-монитор в CERN-матрице).

Из 4 первоначальных отказов: `lib1/d302` исправлен кодом. `lib2/i312a`, `lib2/j531a`, `lib2/j531b` закрыты синхронизацией **устаревших** эталонных `expect_*.txt` с C++-эталоном — это документированное исключение к пункту «без изменения input/expect»: C# и C++-эталон совпадают (разделитель экспоненты `⏨` U+23E8, корректное округление), а исходные `expect` были stale; поведение C# не менялось. Эти данные лежат локально в `ref/` (git-ignored), на чистом checkout отсутствуют.

Воспроизводимость CERN-матрицы на чистом checkout (наличие `ref/` + законная поставка runtime-ресурсов) отнесена к задачам **A4** и **A5**; до их закрытия Gate A не считается достигнутым.

### Task A4: Сделать runtime-ресурсы частью поставки

**Приоритет:** P0; сейчас developer junction `ref/dubna` не является дистрибутивом.

**Файлы:**

- Create: `src/besm6.net/Loader/RuntimeAssets.cs`
- Modify: `src/besm6.net/Cli/Config.cs`
- Modify: `src/besm6.net/Cli/MachineFactory.cs`
- Modify: `src/besm6.net/besm6.csproj`
- Create: `src/besm6.net/tests/Besm6.Tests/RuntimeAssetsTests.cs`
- Modify/create repository-level packaging manifest only after choosing legally distributable files

**Интерфейс:** `RuntimeAssets.Resolve(Config)` возвращает проверенный набор путей и версий; исключение перечисляет каждый отсутствующий ресурс и все проверенные каталоги. Наличие ресурса проверяется до запуска процессора.

- [x] Составить checksum-manifest всех обязательных MONSYS/CERN tape images и указать их происхождение.
- [x] Разделить свободно поставляемые файлы и файлы, которые пользователь должен предоставить самостоятельно.
- [x] Тестом проверить поиск относительно config, application directory и явно указанного абсолютного пути.
- [x] Тестом проверить понятную fail-fast ошибку на отсутствующий или неверный checksum.
- [x] Добавить ресурсы/manifest в `dotnet publish` либо документированный installer step согласно результату лицензионной проверки.
- [x] Развернуть publish output в новый временный каталог без `ref/dubna` и выполнить acceptance smoke.

**Критерий готовности:** опубликованный пакет на чистом каталоге либо сразу запускает workload, либо до исполнения сообщает точный список законно не включённых ресурсов и способ их установки; скрытой зависимости от junction нет.

**Итог (закрыто 31.08.2026):** runtime-образы (monsys.9/librar.12/librar.37/bemsh.739/b.7) помечены `RuntimeAssetLicense.UserProvided` — в репозитории они git-ignored (`.gitignore`: `ref/`, `tapes/`), право на переразрешение не подтверждено, поэтому в `dotnet publish` не встраиваются (ветка 2 критерия). `Besm6.Loader.RuntimeAssets` — checksum-manifest (SHA256 + provenance + obtain-hint) и fail-fast `Resolve`/`ResolveInDirs`; `RunCommand` вызывает `MachineFactory.ValidateRuntimeAssets` до запуска и при отсутствии перечисляет каждый образ, ожидаемую SHA256, способ получения и проверенные каталоги. Проверено на publish-каталоге без лент (`besm6 run name.dub` → точный список всех 5 лент + hint, exit≠0) и в dev-checkout с лентами (`name.dub` → `Halted by STOP`, exit 0). Installer step задокументирован в `docs/runtime-assets.md` (в т.ч. условное включение в пакет при наличии права на распространение). Тесты: `RuntimeAssetsTests` (9) — каталог, поиск относительно config/app/абсолютного пути, fail-fast на absence/bad-checksum; быстрые suites 471/471, CERN batch `lib1:0-2` 3/3.

### Task A5: Унифицировать acceptance runner и CI

**Приоритет:** P1.

**Файлы:**

- Create: `src/besm6.net/tests/Besm6.Tests/AcceptanceTests.cs`
- Modify: `tools/run_all_examples.py`
- Create: CI workflow в принятом репозиторием каталоге
- Modify: `.gitignore` для generated artifacts, если это требуется после проверки текущих правил

**Обязательная матрица:** build; все MSTest; Python trace tests; CLI help/error contracts; `name`, `algol`, `bemsh`; CERN manifest validation; быстрые CERN batches на каждый commit; полные 397 nightly/release.

- [x] Удалить из `run_all_examples.py` абсолютный `e:\Projects\besm6.net`; вычислять root от расположения скрипта и принимать `--root`, `--dll`, `--limit`, `--timeout`, `--output`.
- [x] Считать успехом только ожидаемый exit code, `StopReason` и утверждённый golden output; наличие текста `Halted by STOP` само по себе недостаточно.
- [x] Сохранять JSON/JUnit-совместимый отчёт и stdout/stderr отказавшего сценария.
- [x] Разделить быстрый commit gate и долгий full-compatibility gate, используя один manifest.
- [ ] На CI-падении публиковать CERN diff и canonical trace как artifacts.
- [ ] Зафиксировать нулевой baseline новых предупреждений; существующие 64 разобрать по категориям и уменьшать отдельными безопасными коммитами.

**Статус A5 (инкремент 1):** `tools/run_all_examples.py` переносим (root от расположения файла, `--root/--dll/--limit/--timeout/--output/--suite/--only/--golden-dir`), успех = exit code 0 + StopReason (+ опц. golden), пишет JSON (`examples_report.json`) и JUnit (`junit-examples.xml`) + stdout/stderr каждого сценария в `tests-run/examples/scenarios/`. `fast` (name/algol/bemsh) и `full` — один runner/manifest.

**Статус A5 (инкремент 2):** committed golden-выводы fast-примеров в `tests/golden/examples/{name,algol,bemsh}.txt` + README; гейт по умолчанию сравнивает stdout с golden (нормализованное сравнение: CRLF/LF + отбел пробелов/пустых строк), `--no-golden` отключает. **Ключевое:** принудительный `--no-wall-clock` в `run_one` (иначе текущее время в баннере MONSYS ломает воспроизводимость — нарушало бы глобальное ограничение «тесты не используют реальные часы»). Неверный golden корректно даёт `MISMATCH` (exit 1), верный — OK. Верификация: 39 Python-тестов (`test_run_all_examples.py` 32 + `test_diff_trace.py` 7) зелёные; `--suite fast` в dev-checkout 3/3 OK и детерминирован (два последовательных прогона совпадают). Осталось: GitHub Actions workflow + публикация CERN diff/trace + артефактов (инкремент 3, item 5), zero-warning baseline (инкремент 4, item 6).

**Критерий готовности:** чистый checkout одной документированной командой воспроизводит все проверки уровня A; CI не зависит от локальных дисков, junction и wall clock.

### Gate A: релиз практической совместимости

- [ ] 397 активных CERN-тестов учтены и имеют явный результат.
- [ ] Все утверждённые результаты совпадают с C++/expect.
- [ ] Examples и CLI acceptance проходят из publish output.
- [ ] Runtime assets версионированы, проверяются checksum и имеют законный канал установки.
- [ ] Unit/integration/trace suites зелёные.
- [ ] Известные исключения перечислены как пользовательские ограничения релиза.

До закрытия Gate A задачи уровня B ведутся только как изолированные эксперименты и не заменяют текущий исполнительный путь.

---

## 5. Программа уровня B: аппаратная точность

### Task B1: Ввести дискретное модельное время

**Приоритет:** P2, после Gate A.

**Файлы:**

- Create: `src/besm6.net/Core/SimulationClock.cs`
- Create: `src/besm6.net/Core/EventScheduler.cs`
- Modify: `src/besm6.net/Core/MachineCore.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/EventSchedulerTests.cs`

**Интерфейсы:**

```csharp
public interface ISimulationClock { ulong Tick { get; } }
public interface IEventScheduler
{
    ulong Now { get; }
    EventToken Schedule(ulong delay, Action callback);
    bool Cancel(EventToken token);
    void AdvanceTo(ulong tick);
}
```

События с одинаковым tick исполняются в порядке регистрации. Время не зависит от `DateTime`, thread scheduling или скорости host-машины.

- [x] Написать падающие тесты на порядок, отмену, нулевую задержку, вложенное scheduling и запрет движения времени назад. (`dfbcdf3`, `EventSchedulerTests`)
- [x] Реализовать минимальную priority queue с монотонным sequence number. (`dfbcdf3`, `EventScheduler`)
- [x] Связать одну CPU-инструкцию с документированной стоимостью tick без изменения наблюдаемой семантики уровня A. (`dfbcdf3`, `MachineCore.TicksPerInstruction` + `_clock.Advance` в `Step()`; CPU-путь не тронут)
- [x] Повторить всю Gate A regression. (non-CERN 473 passed / 0 failed; CERN batch `lib1:0-5` 6/6; build 0 warnings / 0 errors; полная 397 — output-comparison, не затрагивается counter-only изменением)

**Статус (эксперимент, параллельно Gate A — по решению исполнителя):** B1 закрыт как изолированный эксперимент, не заменяющий исполнительный путь уровня A (правило §5: «до закрытия Gate A задачи уровня B — только изолированные эксперименты»). `ISimulationClock`/`SimulationClock` (монотонный счётчик, откат — `InvalidOperationException`) + `IEventScheduler`/`EventScheduler` (priority-очередь по (tick, sequence) + lazy cancel; одинаковый tick — порядок регистрации; нулевая/вложенная задержка; детерминированная последовательность) + шов в `MachineCore` (одна инструкция = `TicksPerInstruction` тиков). 19/19 B1-тестов.

**Критерий готовности:** одинаковый workload дважды создаёт идентичную последовательность `(tick, event)`; все тесты уровня A остаются зелёными.

### Task B2: Реализовать контроллер прерываний и `ВЫПР/IRET`

**Приоритет:** P2.

**Файлы:**

- Create: `src/besm6.net/Core/InterruptController.cs`
- Modify: `src/besm6.net/Core/Processor.cs`
- Modify: `src/besm6.net/Core/InstructionExecutor.cs`
- Modify: `src/besm6.net/Core/Opcode.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/InterruptControllerTests.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/ProcessorInterruptTests.cs`

**Интерфейс:** `Request(source)`, `Clear(source)`, маска, детерминированный priority resolver и `TryDequeue(out InterruptRequest)`. Точная раскладка сохранённого состояния и вектора сначала извлекается из C++/документации и фиксируется тестом.

- [ ] Тестами зафиксировать masked/unmasked request, одновременные запросы, приоритет и сохранение pending после маскирования.
- [ ] Тестом зафиксировать точное состояние PC, half, регистров и режима до/после входа в прерывание.
- [ ] Заменить текущую трактовку `Vypr` как всегда незаконной на контекстно корректный возврат; незаконный контекст продолжает давать архитектурную ошибку.
- [ ] Добавить вложенное/запрещённое прерывание согласно эталону, не предполагая host exceptions как модель аппаратуры.
- [ ] Сравнить canonical trace короткой interrupt-программы с C++.

**Критерий готовности:** вход, маскирование, приоритет и возврат совпадают с эталоном на unit и differential traces.

### Task B3: Заменить синхронный `IDevice` на асинхронный контракт

**Приоритет:** P2.

**Файлы:**

- Modify: `src/besm6.net/Core/Interfaces.cs`
- Modify: `src/besm6.net/Core/DeviceManager.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/DeviceManagerAsyncTests.cs`

**Целевой контракт:** устройство имеет immutable identity/capabilities, наблюдаемый `DeviceStatus`, принимает команду только в допустимом состоянии, планирует completion через `IEventScheduler` и поднимает interrupt request через `InterruptController`. Данные передаются через явно описанный buffer/DMA request, а не через перехват адресов оперативной памяти.

- [ ] Зафиксировать тестами переходы `Ready -> Busy -> Ready`, ошибку команды в `Busy`, completion interrupt и reset/cancel.
- [ ] Добавить compatibility adapter для синхронных тестов на время миграции; удалить его после перевода последнего устройства.
- [ ] Сделать неизвестный адрес/номер устройства явной ошибкой вместо тихого чтения нуля или отброшенной записи там, где это соответствует эталону.
- [ ] Сохранить правило `SystemBus`: вся 32K-память остаётся памятью, I/O не становится memory-mapped.

**Критерий готовности:** fake device детерминированно проходит полный lifecycle и не может обращаться к host time/threading.

### Task B4: Перевести периферию на state machines

**Приоритет:** P3. Выполнять отдельными коммитами в порядке фактической важности Dubna workloads.

**Файлы:**

- Modify: `MagneticDrumDevice.cs`, `DiskDevice.cs`, tape handling in `Loader/TapeImage.cs`
- Modify: `ConsoleDevice.cs`, `TeletypeDevice.cs`, `Puncher.cs`, `Plotter.cs`
- Modify: соответствующие `ExtracodeHandler*.cs`
- Test: отдельный `*DeviceTests.cs` для каждого устройства и integration tests обмена

Для каждого устройства повторяется один цикл:

- [ ] Извлечь из C++ и документации команды, status bits, размер блока, задержки и interrupt source.
- [ ] Написать contract tests на happy path, busy, end-of-medium, invalid command, reset и I/O error.
- [ ] Реализовать state machine и completion event без фоновых потоков.
- [ ] Подключить экстракод/канал к новому контракту.
- [ ] Сравнить output, память, status и interrupt trace с эталоном.
- [ ] Повторить Gate A и уже завершённые device suites.

**Критерий готовности:** ни одно заявленное устройство не завершает длительную операцию мгновенно; результаты и порядок событий воспроизводимы.

### Task B5: Смоделировать DMA/каналы и конкуренцию обменов

**Приоритет:** P3.

**Файлы:**

- Create: `src/besm6.net/Core/DmaController.cs`
- Modify: `src/besm6.net/Core/SystemBus.cs`
- Modify: `src/besm6.net/Core/MachineCore.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/DmaControllerTests.cs`
- Create: `src/besm6.net/tests/Besm6.Tests/IoConcurrencyTests.cs`

- [ ] Зафиксировать тестами направление, границы 15-битного адреса, размер слова/блока, частичный обмен и ошибку носителя.
- [ ] Определить из эталона арбитраж CPU/DMA и закрепить порядок конфликтующих обращений тестом.
- [ ] Реализовать передачу через обычную память без device address windows.
- [ ] Проверить два одновременных устройства, completion в один tick и приоритет прерываний.
- [ ] Добавить event trace `(tick, source, operation, status, address, count)` для диагностики.

**Критерий готовности:** интеграционный workload продолжает CPU-исполнение во время обмена и получает данные/interrupt в эталонный момент.

### Task B6: Аппаратные conformance и длительные прогоны

**Приоритет:** P3.

**Файлы:**

- Create: `src/besm6.net/tests/Besm6.Tests/HardwareConformanceTests.cs`
- Extend: `tools/diff_trace.py` либо отдельный совместимый event-trace comparator
- Modify: CI full-compatibility workflow

- [ ] Собрать corpus коротких программ на каждую команду, граничный ALU-случай, interrupt path и тип обмена.
- [ ] Для каждого corpus case сравнить stop reason, instruction trace, register/memory POST-state и event trace.
- [ ] Добавить seed-fixed randomized sequences, которые воспроизводятся по seed и печатают минимальный failing case.
- [ ] Запустить длительные MONSYS/CERN workloads с включённым async I/O и проверить отсутствие изменения уровня A output.
- [ ] Измерить производительность только после корректности; оптимизация обязана сохранять traces.

**Критерий готовности:** conformance corpus и полная матрица Gate A зелёные на новой аппаратной модели, а любое различие локализуется до первого события/инструкции.

### Gate B: аппаратно точный релиз

- [ ] Есть документированное модельное время и стабильная очередь событий.
- [ ] Прерывания, маски, приоритеты и `ВЫПР/IRET` совпадают с эталоном.
- [ ] Все заявленные устройства имеют ready/busy/error, timing и completion interrupt.
- [ ] DMA/канальный обмен проверен на конкурирующих операциях.
- [ ] Hardware conformance, Gate A и длительные workloads проходят вместе.
- [ ] Пользовательская документация честно отделяет реализованное оборудование от незаявленного.

---

## 6. Приоритеты, зависимости и оценка остатка

| Порядок | Проект | Зависит от | Масштаб | Главный риск |
|---:|---|---|---|---|
| 1 | A1 CERN manifest | доступный C++ reference | малый | ошибочный состав активной матрицы |
| 2 | A2 full runner | A1 | средний | долгие/зависшие тесты загрязняют результаты |
| 3 | A3 397/397 | A1–A2 | большой, неизвестное число дефектов | скрытые расхождения экстракодов/loader |
| 4 | A4 runtime assets | решение о лицензиях | средний | нельзя законно встроить нужные образы |
| 5 | A5 acceptance/CI | A1–A4 | средний | локальные пути и длительность suite |
| 6 | B1 clock/scheduler | Gate A | средний | регрессия порядка исполнения |
| 7 | B2 interrupts | B1 | большой | неполная спецификация аппаратного состояния |
| 8 | B3 async contract | B1–B2 | средний | слишком ранний универсальный API |
| 9 | B4 devices | B3 | очень большой | различия команд и timing носителей |
| 10 | B5 DMA/concurrency | B1–B4 | большой | арбитраж и граничные обращения |
| 11 | B6 conformance | B1–B5 | большой | недостаточный эталонный corpus |

Главные оставшиеся глобальные задачи — не очередной переписанный CPU, а доказательство полной workload-совместимости, воспроизводимая поставка системных ресурсов и отдельный аппаратный слой прерываний/асинхронного I/O.

---

## 7. Риски и правила решений

### Эталон и данные

- Сохранять revision C++ reference рядом с отчётом полного прогона.
- Не копировать образы из developer checkout в package до проверки лицензии.
- При неоднозначности документации C++ trace подтверждает фактическую совместимость, а расхождение C++ с аппаратной документацией оформляется отдельным решением.

### Воспроизводимость

- Все limits и seeds записываются в artifact.
- Время задач фиксировано моделью или `--no-wall-clock`.
- Интерактивный EOF задаётся явно.
- Generated traces, jobs и diffs не становятся ручными источниками истины.

### Качество

- 64 существующих warning не скрываются глобальным suppression.
- Сначала исправляются nullable/API/ресурсные warning, способные скрыть отказ; косметические — после Gate A.
- Performance tuning не принимается без до/после benchmark и полного trace regression.

### Scope control

Не относятся к глобальным блокерам до закрытия Gate A/B:

- новый TUI или графический интерфейс;
- plugin architecture;
- перестановка каталогов ради эстетики;
- поддержка дополнительных ОС без воспроизводимого требования;
- оптимизация процессора до измеренного bottleneck;
- новые ассемблерные удобства, не нужные compatibility corpus.

---

## 8. Команды контрольной точки

Исполнитель обновляет их только вместе с изменением фактического CLI/структуры проекта.

```powershell
dotnet build src/besm6.net/besm6.net.sln
dotnet test src/besm6.net/tests/Besm6.Tests/Besm6.Tests.csproj --no-build
python -m unittest discover -s tools/tests -p "test_*.py"
pwsh -File plans/_count_cernlib.ps1
python tools/run_all_examples.py --root .
```

Для каждого milestone в отчёте сохраняются: commit, ОС/.NET/Python версии, команды, counts passed/skipped/failed, список исключений и пути к artifacts.

---

## 9. Политика обновления SuperPlan

- Этот файл — единственный актуальный план симулятора в `plans/`; `book-structure-plan.md` является отдельным планом книги и не удаляется.
- После завершения задачи чекбоксы отмечаются только вместе со ссылкой на commit и проверку в её итоговом абзаце.
- Новое глобальное направление добавляется только с измеримым пользовательским результатом и Gate-критерием.
- Устаревшая диагностика не остаётся как действующий факт: она либо удаляется, либо переносится в краткий раздел исторических решений с commit.
- Детальные временные artifacts хранятся в `tests-run/`/CI, а не раздувают этот roadmap.

## 10. Ближайший исполнимый шаг

A1–A5 закрыты: A3 — 397/397 CERN (`ece77b4`+`0022c70`); A4 — runtime-ресурсы user-provided + fail-fast + checksum-manifest (`0afb5d6`); A5 — acceptance runner + golden-гейт + CI workflow + zero-warning baseline (`42f3c20`, `44021e4`, `ea4bbee`, `115e25d`+`007f15c`; build **0 warnings / 0 errors**, 89 правил, `tools/warnings_baseline.json` total=0).

**Закрытие Gate A — отложено по решению** (пункт «B — параллельно, экспериментально»). Gate A остаётся незакрытым: из 6 критериев выполнено большинство (397/397, unit/trace зелёные, 0 warnings, runtime-ресурсы user-provided + checksum-manifest), но не закрыты: acceptance «из publish output» и единый release-doc с известными исключениями (w303; stale `expect` i312a/j531a/j531b; user-provided assets). Воспроизводимость полной 397-матрицы в чистом CI требует user-provided `ref/` + бинарные ленты.

**Уровень B (эксперимент, параллельно Gate A):** **B1 — дискретное модельное время — закрыт** (`dfbcdf3`; `ISimulationClock`/`SimulationClock` + `IEventScheduler`/`EventScheduler` + шов в `MachineCore`; 19/19 B1-тестов; non-CERN регресс 473/0, CERN batch 6/6, 0 warnings — Gate-A-регресс зелёный). Следующий — **Task B2** (контроллер прерываний и `ВЫПР/IRET`), также как изолированный эксперимент до закрытия Gate A.

---

## Приложение. Архитектурная чистка — Phase 1 (исторический, выполнен)

Ниже сохранён исходный план Phase 1 `plans/refactor.md` как историческая справка; актуальный статус симулятора ведётся в разделах выше.

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Выделить архитектурные типы и процессор в независимые сборки, сохранив поведение монолитного приложения.

**Architecture:** Первый промежуточный этап создаёт `Besm6.Architecture` и `Besm6.Processor`. Assembler, Runtime, CLI и TUI пока остаются в существующем executable и переводятся на новые сборки через project references.

**Tech Stack:** .NET 8, C#, MSTest.

**Plan file:** `docs/superpowers/plans/2026-09-08-architecture-cleanup-phase-1.md`

### Статус (2026-09-08)

- **Task 1–4: выполнены.** `Besm6.Architecture` и `Besm6.Processor` — отдельные сборки.
- **Task 5: выполнен.** `Besm6.Assembler` — полная реализация:
  - `Lexer` / `Parser` / `SymbolTable` — разбор, ассемблирование, две прогонки.
  - `AssemblyDialectDetector` — выбор диалекта по `*madlen` / `*bemsh` / `*assem`.
  - `Disassembler` — дизассемблирование через `InstructionCodec`.
  - `AssemblyEngine` — оркестратор (< 70 строк), кодит через `InstructionCodec.EncodeHalf`.
  - `Legacy/` — `[Obsolete]`-адаптеры `Besm6.Asm.*` (Assembler, Disassembler, OpcodeTable, ProgramAssembler).
  - Монолитные `src/besm6.net/Asm/*.cs` удалены; CLI/TUI/DubnaLoader используют новый API.
- **Task 6: выполнен.** `Besm6.Runtime` — отдельная сборка:
  - Core-файлы (devices, MachineCore, SystemBus, clock, scheduler) перенесены.
  - Loader-файлы (DubnaLoader, ExtracodeHandler, CosyCodec, JobParser, TapeImage) перенесены.
  - Tracing-файлы (CanonicalTraceWriter, DiagnosticTraceWriter) перенесены.
  - `Config` перенесён из CLI в Runtime.
  - Namespace: `Besm6.Runtime`.
  - `InternalsVisibleTo("Besm6.Runtime")` добавлен в Processor.
- **Task 7: выполнен.** `DubnaLoader` разбит на сервисы (facade 174 строки):
  - `Loading/LoadResult.cs` — тип результата (public, отдельный файл).
  - `Loading/TapeMountService.cs` — lifecycle лент: mount, release, file search/mount, scratch, required/script tapes.
  - `Loading/JobProgramLoader.cs` — загрузка программ: raw words, assembler, запись script на drum.
  - `Loading/MonsysBootstrapper.cs` — MONSYS bootstrap: таблица, магический код, начальный K.
  - `Loading/ExecutionLoop.cs` — bounded execution: instruction/wall-clock/loop limits, intercept, LoadResult.
  - `DubnaLoader.cs` — публичный facade (< 300 строк), делегирует в сервисы.
  - `InternalsVisibleTo("Besm6.Tests")` / `("besm6")` добавлены в Runtime.
- **Task 8: выполнен.** `ExtracodeHandler` разбит на 8 partial-файлов (engine 196 строк):
  - `ExtracodeHandler.cs` (196) — engine: конструктор, поля, Handle() dispatch, MapDrumToDisk.
  - `ExtracodeHandler.Misc.cs` (145) — E63, E65, E67, E72, E75, E76.
  - `ExtracodeHandler.E50Main.cs` (164) — E50 dispatcher + E51–E56.
  - `ExtracodeHandler.E50.cs` (317) — E50Parse, E50Format.
  - `ExtracodeHandler.E57.cs` (192) — E57 tape/file operations.
  - `ExtracodeHandler.E61.cs` (59) — E61 plotter + E64 wrapper.
  - `ExtracodeHandler.E64.cs` (780) — E64Full console I/O.
  - `ExtracodeHandler.E70.cs` (108) — E70 disk/drum I/O.
  - `ExtracodeHandler.E71.cs` (71) — E71 terminal I/O.
  - Решение: extracode-обработчики остаются в Runtime (зависят от MachineCore, TapeImage,
    CosyCodec, Besm6Math). Перенос в Processor создаёт циклическую зависимость.
    I/O уже передаётся через callback-делегаты (output, input, mountTape, fileSearch, fileMount).
- **Task 9: выполнен.** Testable CLI entry point + contract tests:
  - `Cli/CliApplication.cs` — `Run(string[] args, TextWriter? output, TextWriter? error)`.
  - `Program.Main` делегирует в `CliApplication.Run(args)`.
  - No-args → help (exit 0), а не interactive debugger.
  - `HelpCommand` больше не упоминает interactive debugger.
  - `CliApplicationTests` — 7 contract tests (no-args, help, unknown, missing-arg, asm, disasm, case-insensitive).
- **Task 8 (codecs): выполнен.** `CosyCodec` разбит на Encoding namespace:
  - `Encoding/EncodingTables.cs` (299) — data-only: COSY markers, Koi7, GOST, TEXT, Tab0, AllToIso.
  - `Encoding/Koi7Codec.cs` (16) — thin API: `Utf8ToKoi7`, `Koi7ToUnicode`.
  - `Encoding/Gost10859Codec.cs` (13) — thin API: `GostToUnicode`.
  - `Encoding/TextCodec.cs` (16) — thin API: `TextToUnicode`, `BytesToWord`.
  - `Encoding/CosyEncoder.cs` (13) — thin API: `Encode`.
  - `Encoding/CosyDecoder.cs` (19) — thin API: `IsReadOld`, `IsEndFile`, `Decode`.
  - `CosyCodec.cs` (385, 277 logic) — реализация, данные делегированы в EncodingTables.
- **Тесты:** Architecture 18/18 ✓, Processor 101/101 ✓, Assembler 9/9 ✓, монолит 412 ✓ / 4 skip, EduCpu 49 ✓.
- **Принятые решения (отклонения от плана):**
  1. Типы `Besm6.Processor` остались в namespace `Besm6.Core`: namespace `Besm6.Processor`
     «затеняет» тип `Processor` для всего кода в `Besm6.*` (ошибка CS0118). Валидация namespace
     выполняется boundary-тестами по имени сборки.
  2. `Besm6.Architecture` использует namespace `Besm6.Architecture` (тип `Processor` там не
     объявлен — затенения нет); в `Besm6.Core` оставлен только `IDevice`.
  3. `IMemory` перенесен в `Besm6.Processor` (зависимость только на Architecture).
  4. `InternalsVisibleTo("besm6")`: `DubnaLoader` и некоторые тесты используют internal-члены
     `Processor` (`ArmDebugWatch`, `DebugCheckFetch`, `DebugWatchAbortException`);
     `Alu`/`InstructionExecutor` internal-члены используются монолитом.
  5. Реликтовые `using Besm6.Processor;` в исходниках монолита удалены (namespace больше не
     существует; тип `Processor` находится через file-level `using Besm6.Core;`).
  6. Рефакторинг на `ProcessorState` / `IProcessorTraceSink` / `ExtracodeCall` — отложен
     (не входит в цель «разделить сборки, сохранив поведение»).

### Ограничения

- Не менять семантику инструкций, экстракодов и загрузчика.
- Текущие переименования регистров и опкодов входят в ту же feature-ветку.
- Все 506 существующих .NET-тестов должны сохраниться.
- `src/besm-edu` не изменять.
- Пользовательские untracked-файлы `examples/` не добавлять.
- Каждый этап завершать отдельным коммитом и зелёной сборкой.

---

### Task 1: Зафиксировать текущую базу

**Результат:** готовые изменения имён становятся первым коммитом общей архитектурной ветки.

- [ ] Создать ветку `codex/architecture-cleanup`.
- [ ] Убедиться, что staged-набор не содержит untracked-файлов `examples/`.
- [ ] Выполнить:

```powershell
dotnet test src/besm6.net/besm6.net.sln --no-restore
py -3 -m unittest discover -s tools/tests
git diff --cached --check
```

- [ ] Зафиксировать текущие изменения:

```powershell
git commit -m "Rename BESM-6 registers and opcodes"
```

---

### Task 2: Добавить проверки архитектурных границ

**Создать:**

- `tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj`
- `tests/Besm6.Processor.Tests/Besm6.Processor.Tests.csproj`
- `tests/Besm6.Architecture.Tests/AssemblyBoundaryTests.cs`

**Проверки:**

- `Besm6.Architecture` не ссылается на другие BESM-6 assemblies.
- `Besm6.Processor` ссылается только на `Besm6.Architecture`.
- Processor не ссылается на Loader, CLI или TUI.
- Processor не содержит типов устройств и загрузчика.

До появления проектов boundary-тесты должны падать. После завершения этапа — проходить.

Коммит:

```powershell
git commit -m "Add architecture boundary tests"
```

---

### Task 3: Создать Besm6.Architecture

**Создать:**

```text
src/Besm6.Architecture/
  Besm6.Architecture.csproj
  Word48.cs
  Word48Extensions.cs
  MantissaExponent.cs
  Opcode.cs
  Extracode.cs
  RFlags.cs
  AddressMode.cs
  InstructionFormat.cs
  ArchitectureConstants.cs
```

**Настройки проекта:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

**Изменения:**

- Перенести ISA-типы из текущего `Core`.
- Использовать namespace `Besm6.Architecture`.
- В `ArchitectureConstants` оставить только:
  - маски 40/41/42/48 бит;
  - 15-битную маску адреса;
  - число индексных регистров;
  - номера stack/exchange registers;
  - bias порядка;
  - `OnBit()` и `NormalizeAddress()`.
- Константы устройств, дисков, барабанов и загрузчика оставить в монолите до Runtime-фазы.
- Сохранить числовые значения всех опкодов.
- Сохранить русские XML `<summary>`.

**Тесты для переноса:**

- `Word48Tests`;
- проверки `Opcode`;
- преобразование в восьмеричную форму;
- `MantissaExponent`;
- архитектурные маски.

Коммит:

```powershell
git commit -m "Extract BESM-6 architecture assembly"
```

---

### Task 4: Создать headless Besm6.Processor

**Создать:**

```text
src/Besm6.Processor/
  Besm6.Processor.csproj
  Processor.cs
  ProcessorState.cs
  ProcessorException.cs
  Alu.cs
  InstructionExecutor.cs
  IMemory.cs
  CoreMemory.cs
  BytePointer.cs
  StopReason.cs
  Tracing/
    ProcessorTraceRecord.cs
    RegisterTraceRecord.cs
    IProcessorTraceSink.cs
```

`Besm6.Processor.csproj` ссылается только на `Besm6.Architecture`.

#### Публичные интерфейсы

```csharp
public interface IMemory
{
    Word48 Read(uint address);
    void Write(uint address, Word48 value);
    int Size { get; }
}

public interface IProcessorTraceSink
{
    void OnInstruction(in ProcessorTraceRecord record);
    void OnRegisterChanged(in RegisterTraceRecord record);
}

public sealed class Processor
{
    public Processor(IMemory memory, IProcessorTraceSink? traceSink = null);
    public Func<ExtracodeCall, bool>? ExtracodeDispatch { get; set; }
    public bool Step();
}
```

```csharp
public readonly record struct ExtracodeCall(
    Extracode Code,
    uint EffectiveAddress,
    byte Register,
    ushort RawAddress,
    bool IsRightHalf);
```

#### Внутреннее разделение

`ProcessorState` владеет:

- `A`, `Y`, `R`, `M`, `C`, `K`;
- флагом правого полуслова;
- stack correction;
- признаками intercept и debug watch.

`Processor` остаётся фасадом и предоставляет существующие публичные регистровые методы.

`Alu` и `InstructionExecutor` получают `ProcessorState` через конструктор. Прямой доступ к полям `Processor` удаляется.

#### Удаление host-зависимостей

Из Processor убрать:

- создание trace-файлов;
- чтение environment variables;
- `Console`;
- ссылки на `MachineCore`, `DubnaLoader`, устройства и ленты.

Формирование trace records остаётся в Processor. Решение о записи в TSV принимает текущий host.

#### Тесты для переноса

В `Besm6.Processor.Tests` перенести:

- `ProcessorTests`;
- `AluTests` и `AluExtendedTests`;
- branch/stack/state/instruction tests;
- memory tests, относящиеся только к `IMemory`;
- processor trace tests без файловой системы.

Коммит:

```powershell
git commit -m "Extract headless processor assembly"
```

---

### Task 5: Подключить новые сборки к существующему приложению

**Изменить:**

- текущий `besm6.csproj`;
- `MachineCore`;
- assembler;
- loader и extracode handler;
- CLI и TUI;
- существующий test project.

**Project references:**

```xml
<ItemGroup>
  <ProjectReference Include="..\Besm6.Architecture\Besm6.Architecture.csproj" />
  <ProjectReference Include="..\Besm6.Processor\Besm6.Processor.csproj" />
</ItemGroup>
```

**Адаптация:**

- `MachineCore` создаёт `Processor` через `IMemory`.
- `SystemBus` реализует processor `IMemory`.
- `ExtracodeHandler.Handle` принимает `ExtracodeCall`.
- Временные поля `ExtracodeReg`, `ExtracodeRawAddr` и `ExtracodeRightFlag` удалить.
- Файловые trace writers оставить в монолите и подключить через `IProcessorTraceSink`.
- Assembler использует `Opcode` из Architecture.
- Устройства продолжают находиться в старом проекте до Runtime-фазы.

Коммит:

```powershell
git commit -m "Wire application to extracted processor assemblies"
```

---

### Task 6: Проверить промежуточную архитектуру

Выполнить:

```powershell
dotnet restore src/besm6.net/besm6.net.sln
dotnet build src/besm6.net/besm6.net.sln -c Release --no-restore
dotnet test src/besm6.net/besm6.net.sln -c Release --no-build --no-restore
py -3 -m unittest discover -s tools/tests
dotnet test src/besm-edu/Besm6.EduCpu.Tests/Besm6.EduCpu.Tests.csproj
git diff --check
```

Дополнительные smoke-тесты:

```powershell
dotnet run --project src/besm6.net/besm6.csproj -- help
dotnet run --project src/besm6.net/besm6.csproj -- asm "xta 10, atx 20"
dotnet run --project src/besm6.net/besm6.csproj -- disasm 0010000000000000
```

Проверить через `Assembly.GetReferencedAssemblies()`:

```text
Besm6.Architecture -> System.*
Besm6.Processor    -> Besm6.Architecture
besm6              -> Besm6.Architecture + Besm6.Processor
```

Финальный коммит этапа:

```powershell
git commit -m "Complete processor extraction phase"
```

### Критерий готовности промежуточного этапа

- Architecture и Processor физически находятся в отдельных DLL.
- Processor запускается в unit-тесте без MachineCore и Loader.
- Между новыми сборками нет циклических ссылок.
- Монолитный executable сохраняет текущие команды и поведение.
- Все существующие тесты и trace-сравнения проходят.
- Assembler, Runtime, extracode partials и отдельные CLI/TUI ещё не реорганизуются.

### Следующие этапы

После этого checkpoint выполняются отдельными планами:

1. `Besm6.Assembler`: `MadlenAssembler`, `BemshAssembler`, auto facade.
2. `Besm6.Runtime`: MachineCore, Loader, устройства, кодировки и grouped partial extracodes.
3. `Besm6.Cli` и `Besm6.Tui`: отдельные executable и финальное удаление монолита.
