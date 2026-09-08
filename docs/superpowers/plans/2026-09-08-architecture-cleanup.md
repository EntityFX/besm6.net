# Архитектурная декомпозиция BESM-6 — план реализации

> **Для агентных работников:** ОБЯЗАТЕЛЬНЫЙ под-скилл: используйте superpowers:subagent-driven-development (рекомендуется) или superpowers:executing-plans для пошаговой реализации этого плана. Шаги используют синтаксис чекбоксов (`- [ ]`) для отслеживания прогресса.

**Goal:** Разделить монолитный executable на шесть независимых production-сборок с однонаправленными зависимостями, не меняя семантику процессора, экстракодов, загрузчика и форматов файлов.

**Architecture:**

```
Besm6.Architecture
      ↑         ↑
Besm6.Processor Besm6.Assembler
      ↑         ↑
      Besm6.Runtime
       ↑       ↑
 Besm6.Cli   Besm6.Tui
```

Зависимости направлены строго вниз: каждый слой ссылается только на сборки ниже себя, а циклы между production-сборками запрещены.

**Tech Stack:** C# 12, .NET 8 (net8.0), nullable reference types, `TreatWarningsAsErrors`, MSTest 4, PowerShell verification commands, существующий Python `unittest` trace comparator.

## Структура решения

```
Besm6.sln
src/
  Besm6.Architecture/
  Besm6.Processor/
  Besm6.Assembler/
  Besm6.Runtime/
  Besm6.Cli/
  Besm6.Tui/
tests/
  Besm6.Architecture.Tests/
  Besm6.Processor.Tests/
  Besm6.Assembler.Tests/
  Besm6.Runtime.Tests/
  Besm6.Cli.Tests/
  Besm6.Tui.Tests/
  Besm6.Integration.Tests/
```

Все проекты используют `net8.0`, nullable reference types и `TreatWarningsAsErrors`.

### Besm6.Architecture

Не имеет зависимостей на другие проекты решения. Содержит только модель архитектуры:

- `Word48`, `MantissaExponent`;
- `Opcode`, `Extracode`, `RFlags`;
- `AddressMode`, `InstructionFormat`;
- архитектурные маски и константы;
- `DecodedInstruction` и общий `InstructionCodec` для кодирования и декодирования полуслов;
- русские XML-комментарии `<summary>` для регистров, инструкций и публичных типов.

Константы устройств, дисков, загрузчика и путей переносятся в Runtime и не остаются в архитектурной сборке.

### Besm6.Processor

Headless-сборка процессора, зависящая только от `Besm6.Architecture`.

Содержит:

- `Processor` как публичный фасад;
- `ProcessorState` как единое внутреннее состояние регистров;
- `IMemory` и `CoreMemory`;
- арифметическое устройство;
- декодирование и исполнение инструкций;
- перехваты, трассировку и причины останова.

Из сборки исключаются любые прямые обращения к:

- `Console`, файлам и каталогам;
- environment variables;
- загрузчику, лентам и устройствам;
- CLI и TUI.

Трассировка передаётся наружу через typed callbacks:

```csharp
public Action<InstructionTraceRecord>? InstructionTrace { get; set; }
public Action<RegisterTraceRecord>? RegisterTrace { get; set; }
```

Экстракоды вызываются через:

```csharp
public Func<ExtracodeCall, bool>? ExtracodeDispatch { get; set; }
```

`ExtracodeCall` содержит код, исполнительный адрес, индексный регистр, исходный адрес и признак правого полуслова. Временные свойства `ExtracodeReg`, `ExtracodeRawAddr` и `ExtracodeRightFlag` удаляются.

### Besm6.Assembler

Зависит только от `Besm6.Architecture`.

Новый публичный API:

```csharp
public enum AssemblyDialect
{
    Auto,
    Madlen,
    Bemsh
}

public interface IBesm6Assembler
{
    AssemblyDialect Dialect { get; }
    ulong AssembleWord(string source);
    AssemblyResult AssembleProgram(
        IEnumerable<string> source,
        int baseAddress = 512);
}

public sealed class MadlenAssembler : IBesm6Assembler;
public sealed class BemshAssembler : IBesm6Assembler;
public sealed class AutoDetectingAssembler : IBesm6Assembler;
```

Поведение диалектов:

- `MadlenAssembler` принимает английские мнемоники и числовые формы;
- `BemshAssembler` принимает русские мнемоники и директивы БЭМШ;
- `AutoDetectingAssembler` сохраняет текущее совместимое поведение;
- `*madlen` и `*bemsh` выбирают строгий класс;
- `*assem` использует автоматическое определение;
- псевдоним `*32` продолжает обозначать EXT;
- кодирование инструкций выполняется через общий `InstructionCodec`.

`ProgramAssembler` разбивается на общий lexer/parser, обработчик символов и две реализации диалектов. Дублировать кодирование опкодов в разных классах нельзя.

Старые типы `Besm6.Asm.Assembler`, `ProgramAssembler`, `AsmResult`, `OpcodeTable` и `Disassembler` остаются на один релиз как `[Obsolete]`-адаптеры. Они делегируют новой реализации и не содержат собственной логики.

### Besm6.Runtime

Зависит от Architecture, Processor и Assembler.

Содержит:

- `MachineCore`;
- загрузчик Dubna и job parser;
- устройства, `SystemBus`, ленты, барабаны, puncher и plotter;
- runtime assets и конфигурацию;
- COSY, KOI-7, ГОСТ-10859 и TEXT-кодеки;
- модельные часы и планировщик;
- реализацию экстракодов;
- файловые writers для канонической и диагностической трассировки.

`Debugger` удаляется из процессорной сборки. Интерактивная логика переходит в TUI.

## Разбиение крупных классов

### Processor и InstructionExecutor

`Processor` разделяется по ответственности:

- `Processor` — публичный API и жизненный цикл;
- `ProcessorState` — регистры и внутренние флаги;
- `ProcessorMemoryAccess` — чтение, запись и memory watch;
- `ProcessorDebugWatch` — отладочные перехваты;
- `ProcessorTraceController` — формирование trace records;
- `ProcessorBitOperations` — pack, unpack, count и highest bit.

Монолитный switch `InstructionExecutor` заменяется диспетчером и специализированными internal-классами:

- `MemoryInstructionExecutor` — коды 000–037;
- `IndexInstructionExecutor` — коды 040–047;
- `ControlInstructionExecutor` — длинные команды 0220–0370;
- `ExtracodeInstructionExecutor` — подготовка и передача `ExtracodeCall`;
- `ExecutionFrame` — локальное изменяемое состояние одной инструкции;
- `InstructionOutcome` — Continue или Stop.

Стековая коррекция, применение C, переход между полусловами и финальная запись регистров выполняются централизованно, один раз на инструкцию.

### Alu

Разделить на:

- `Alu` — публичный фасад;
- `AdditiveOperations`;
- `MultiplicativeOperations`;
- `ShiftOperations`;
- `NormalizationAndRounding`.

Все компоненты работают с `ProcessorState`; математическая семантика и исключения не меняются.

### DubnaLoader

Разделить на сервисы:

- `DubnaLoader` — публичная оркестрация;
- `JobProgramLoader` — raw и assembler sections;
- `TapeMountService` — поиск, монтаж и освобождение лент;
- `MonsysBootstrapper` — подготовка MONSYS;
- `ExecutionLoop` — лимиты, hang/loop detection и останов;
- `LoadResult` — отдельный файл.

### ExtracodeHandler

Оставить sealed partial, но разнести реализацию по подсистемам:

- `ExtracodeHandler.cs` — поля, конструктор, dispatch, hang detection и trace;
- `ExtracodeHandler.Math.cs` — E50–E56;
- `ExtracodeHandler.Storage.cs` — E57 и физический I/O;
- `ExtracodeHandler.Print.cs` — E64;
- `ExtracodeHandler.Terminal.cs` — E70/E71, puncher и plotter;
- `ExtracodeHandler.System.cs` — E63, E65, E67, E72, E75 и E76.

Крупные алгоритмы E50 и E64 дополнительно выносятся в самостоятельные internal-классы:

- `E50Parser`, `E50Formatter`;
- `E64GostFormatter`, `E64DubnaFormatter`;
- `E64OctalFormatter`, `E64HexFormatter`;
- `E64RealFormatter`, `E64InstructionFormatter`.

Partial-файлы отвечают только за маршрутизацию и состояние конкретной подсистемы.

### CosyCodec

Разделить на:

- `CosyEncoder`;
- `CosyDecoder`;
- `Koi7Codec`;
- `Gost10859Codec`;
- `TextCodec`;
- `EncodingTables`.

Старый `CosyCodec` остаётся внутренним фасадом Runtime либо obsolete-адаптером, если он используется извне.

### TUI

`TuiApp` разделить на:

- `TuiApp` — запуск и главный цикл;
- `TuiController` — команды и управление машиной;
- `TuiCommandParser`;
- `TuiRenderer`;
- `TuiSessionState`.

Рендерер не выполняет команды и не обращается к файловой системе. Контроллер не содержит ANSI-разметки.

## CLI и TUI

### Besm6.Cli

Отдельный executable с assembly name `besm6`.

Сохраняет команды:

- `run`;
- `check`;
- `asm`;
- `disasm`;
- `help`.

Команда `tui` удаляется. Запуск CLI без аргументов печатает help и завершается с кодом 0.

CLI не ссылается на проект TUI.

### Besm6.Tui

Отдельный executable с assembly name `besm6-tui`.

Поддерживает:

- загрузку `.dub`;
- `run`, `step`, `reset`;
- просмотр и изменение памяти;
- assembler/disassembler;
- отображение A, Y, R, M, C, K;
- аргументы `--config`, необязательный путь к `.dub` и `--help`.

Существующая ANSI-панель сохраняется. Неиспользуемая зависимость `Spectre.Console` удаляется.

Конфигурация машины оформляется как `RuntimeOptions` и загружается общим `JsonRuntimeOptionsLoader`, используемым обоими executable.

## Этапы реализации

1. Создать ветку `codex/architecture-cleanup` из текущего working tree. Текущие staged-изменения переименования сделать первым коммитом этой ветки, не отправляя отдельно в main.
2. Добавить characterization-тесты текущего поведения: опкоды, assembler round-trip, processor trace, экстракоды, CLI-коды возврата и загрузка `.dub`.
3. Создать `Besm6.sln` и `Besm6.Architecture`; перенести ISA-типы и общий codec. Обновить namespace и ссылки, добиться зелёного решения.
4. Создать `Besm6.Processor`; перенести CPU и память, удалить зависимости на Console/File/Loader, затем разбить Processor, ALU и InstructionExecutor.
5. Создать `Besm6.Assembler`; реализовать отдельные диалекты, auto-selection и obsolete-адаптеры. Перевести Runtime, CLI, TUI и тесты на новый API.
6. Создать `Besm6.Runtime`; перенести машину, устройства, загрузчик, кодеки, конфигурацию и runtime assets. Разбить `DubnaLoader` и `CosyCodec`.
7. Разнести `ExtracodeHandler` по подсистемным partial-файлам и выделить форматтеры E50/E64. На этом этапе запрещены изменения машинной семантики экстракодов.
8. Создать `Besm6.Cli` и `Besm6.Tui`, добавить отдельные entry points, удалить зависимость между фронтендами и старый монолитный executable.
9. Разнести существующие тесты по соответствующим test-проектам. CERNlib, diagnostics и end-to-end `.dub` оставить в `Besm6.Integration.Tests`.
10. Обновить tracked-скрипты и документацию, содержащие старые пути `src/besm6.net` и `besm6.net.sln`. Пользовательские untracked-файлы в `examples/` не добавлять и не изменять.
11. Выполнить Release build, все тесты, publish обоих executable и smoke-тесты. После ревью отправить одну feature-ветку и один PR.

Каждый этап завершается отдельным коммитом и полностью собираемым решением.

## Тестирование и критерии приёмки

### Границы сборок

Добавить тест, проверяющий `Assembly.GetReferencedAssemblies()`:

- Architecture не ссылается ни на одну сборку BESM-6;
- Processor и Assembler ссылаются только на Architecture;
- Runtime не ссылается на CLI/TUI;
- CLI и TUI не ссылаются друг на друга;
- production-сборки не имеют циклических ссылок.

### Assembler

Проверить:

- Madlen принимает `xta`, но отвергает `сч`;
- Bemsh принимает `сч`, но отвергает `xta`;
- Auto принимает оба диалекта;
- `*madlen`, `*bemsh` и `*assem` выбирают правильную реализацию;
- `*32` и `ext` дают код 032;
- round-trip assembler/disassembler сохраняет слово;
- obsolete-адаптеры возвращают те же результаты, что новые классы;
- labels, отрицательные адреса, регистры и raw-слова сохраняют текущее поведение.

### Processor

Проверить:

- все существующие ALU и instruction tests;
- stack correction и переход между полусловами;
- C применяется и очищается в прежние моменты;
- экстракод получает полный `ExtracodeCall`;
- trace TSV до и после рефакторинга совпадает;
- processor можно создать и использовать без Runtime, Console и файлов.

### Runtime и экстракоды

Проверить отдельно группы:

- E50–E56;
- E57;
- E63/E64/E65/E67;
- E70/E71;
- E72/E75/E76;
- MONSYS bootstrap;
- tape/file mount;
- wall-clock и deterministic режимы;
- instruction, wall-clock, hang и loop limits;
- COSY/KOI-7/ГОСТ/TEXT round-trip.

### CLI/TUI и интеграция

Проверить:

- все CLI-команды и exit codes;
- отсутствие команды `besm6 tui`;
- `besm6` без аргументов выводит help;
- `besm6-tui --help` не входит в интерактивный цикл;
- TUI parser и renderer тестируются без реальной консоли;
- все текущие 506 .NET-сценариев сохранены с прежними четырьмя обоснованными skip;
- CERNlib и диагностические сценарии сохраняют результаты;
- Python trace tools продолжают принимать новый и legacy TSV.

Финальные команды:

```bash
dotnet restore Besm6.sln
dotnet build Besm6.sln -c Release --no-restore
dotnet test Besm6.sln -c Release --no-build --no-restore
py -3 -m unittest discover -s tools/tests
dotnet publish src/Besm6.Cli/Besm6.Cli.csproj -c Release --no-build
dotnet publish src/Besm6.Tui/Besm6.Tui.csproj -c Release --no-build
dotnet test src/besm-edu/Besm6.EduCpu.Tests/Besm6.EduCpu.Tests.csproj
```

## Ограничения и принятые решения

- Архитектурная чистка не должна менять семантику процессора, экстракодов, загрузчика и форматов файлов.
- `src/besm-edu` остаётся отдельным и не переводится на новый Processor.
- Старые assembler API сохраняются только как obsolete-адаптеры на один релиз.
- Старые имена регистров и опкодов не возвращаются.
- `pc`/`pc_a` в TSV остаются совместимыми полями формата, пользовательские подписи используют K.
- Один production-класс не должен превышать 300 строк логики. Исключение — data-only таблицы кодировок без исполняемой логики.
- В production-файле допускается один публичный тип; небольшие private/internal helper-типы могут находиться рядом с владельцем.
- Новые внешние NuGet-зависимости не добавляются.
- Итог поставляется одной feature-веткой и одним PR с последовательными проверяемыми коммитами.

---

### Task 1: Подготовка ветки и characterization-тесты

**Files:**
- Git: создать ветку `codex/architecture-cleanup` из текущего working tree, текущие staged-изменения — первый коммит ветки.
- Create: characterization-тесты опкодов, assembler round-trip, processor trace, экстракоды, CLI-коды возврата, загрузка `.dub`.

- [ ] **Step 1: Создать ветку и первый коммит со staged-изменениями**
- [ ] **Step 2: Добавить characterization-тесты текущего поведения**
- [ ] **Step 3: Убедиться, что тесты зелёные на монолитном executable**

---

### Task 2: Besm6.sln и Besm6.Architecture

**Files:**
- Create: `Besm6.sln`, `src/Besm6.Architecture/*`, `tests/Besm6.Architecture.Tests/*`.
- Modify: namespace и ссылки потребителей ISA-типов.

- [ ] **Step 1: Перенести ISA-типы и общий `InstructionCodec` в архитектурную сборку**
- [ ] **Step 2: Обновить namespace и ссылки, добиться зелёного решения**
- [ ] **Step 3: Добавить тест границ сборок (assembly boundaries)**

---

### Task 3: Besm6.Processor

**Files:**
- Create: `src/Besm6.Processor/*`.
- Modify: удалить зависимости CPU/памяти на Console, File, Loader, CLI, TUI.

- [ ] **Step 1: Перенести CPU и память, исключить внешние зависимости**
- [ ] **Step 2: Разбить `Processor` на вспомогательные классы**
- [ ] **Step 3: Разбить ALU на подсистемы**
- [ ] **Step 4: Заменить монолитный switch `InstructionExecutor` диспетчером и специализированными классами**
- [ ] **Step 5: Добавить typed callbacks и `ExtracodeDispatch`**
- [ ] **Step 6: Убедиться, что trace TSV совпадает до/после рефакторинга**

---

### Task 4: Besm6.Assembler

**Files:**
- Create: `src/Besm6.Assembler/*`, `tests/Besm6.Assembler.Tests/*`.
- Modify: перевод Runtime, CLI, TUI и тестов на новый API; obsolete-адаптеры.

- [ ] **Step 1: Реализовать `IBesm6Assembler`, `MadlenAssembler`, `BemshAssembler`, `AutoDetectingAssembler`**
- [ ] **Step 2: Разбить `ProgramAssembler` на lexer/parser, обработчик символов и два диалекта**
- [ ] **Step 3: Добавить obsolete-адаптеры, делегирующие новой реализации**
- [ ] **Step 4: Перевести потребителей и тесты на новый API**
- [ ] **Step 5: Проверить все assembler-критерии приёмки**

---

### Task 5: Besm6.Runtime

**Files:**
- Create: `src/Besm6.Runtime/*`, `tests/Besm6.Runtime.Tests/*`.
- Modify: перенос машины, устройств, загрузчика, кодеков, конфигурации, runtime assets.

- [ ] **Step 1: Перенести машину, устройства, загрузчик, кодеки и конфигурацию**
- [ ] **Step 2: Разбить `DubnaLoader` на сервисы**
- [ ] **Step 3: Разбить `CosyCodec` на компоненты**
- [ ] **Step 4: Перенести константы устройств, дисков и загрузчика из Architecture**
- [ ] **Step 5: Проверить все Runtime-критерии приёмки**

---

### Task 6: ExtracodeHandler partial и форматтеры E50/E64

**Files:**
- Modify: `ExtracodeHandler.cs`, новые `ExtracodeHandler.*.cs`, internal-классы форматтеров.
- Constraint: без изменения машинной семантики экстракодов.

- [ ] **Step 1: Разнести реализацию по подсистемным partial-файлам**
- [ ] **Step 2: Выделить `E50Parser`, `E50Formatter` и форматтеры E64 в internal-классы**
- [ ] **Step 3: Проверить группы экстракодов отдельно**
- [ ] **Step 4: Сравнить поведение до/после (семантика неизменна)**

---

### Task 7: Besm6.Cli и Besm6.Tui

**Files:**
- Create: `src/Besm6.Cli/*`, `src/Besm6.Tui/*`, `tests/Besm6.Cli.Tests/*`, `tests/Besm6.Tui.Tests/*`.
- Modify: удалить команду `tui`, монолитный executable, зависимость фронтендов друг от друга.

- [ ] **Step 1: Создать `Besm6.Cli` с assembly name `besm6`**
- [ ] **Step 2: Создать `Besm6.Tui` с assembly name `besm6-tui`, удалить Spectre.Console**
- [ ] **Step 3: Разбить `TuiApp` на контроллер, parser, renderer и session state**
- [ ] **Step 4: Ввести `RuntimeOptions` и общий `JsonRuntimeOptionsLoader`**
- [ ] **Step 5: Проверить все CLI/TUI-критерии приёмки**

---

### Task 8: Разнесение тестов и обновление путей

**Files:**
- Modify: распределение тестов по test-проектам; tracked-скрипты и документация с путями `src/besm6.net` и `besm6.net.sln`.
- Constraint: untracked-файлы в `examples/` не изменять.

- [ ] **Step 1: Разнести существующие тесты по соответствующим test-проектам**
- [ ] **Step 2: CERNlib, diagnostics и end-to-end `.dub` оставить в `Besm6.Integration.Tests`**
- [ ] **Step 3: Обновить tracked-скрипты и документацию со старыми путями**

---

### Task 9: Финальная сборка, тесты, publish и PR

**Files:**
- Verify: Release build, все тесты, publish обоих executable, smoke-тесты, Python trace tools.

- [ ] **Step 1: `dotnet restore` / `build` / `test` всего решения в Release**
- [ ] **Step 2: Запустить Python trace-сравнение и edu-тесты**
- [ ] **Step 3: `dotnet publish` обоих executable и smoke-тесты**
- [ ] **Step 4: Ревью, одна feature-ветка и один PR с последовательными проверяемыми коммитами**