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

**Spec:** Архитектурные требования сохранены в разделах «Структура решения»,
«Разбиение крупных классов», «CLI и TUI», «Тестирование и критерии приёмки» этого
файла; `plans/refactor.md` используется только как исторический Phase 1 reference,
а не как инструкция к выполнению полного плана.

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

## Global Constraints

- Архитектурная чистка не должна менять семантику процессора, экстракодов, загрузчика и форматов файлов.
- `src/besm-edu` остаётся отдельным и не переводится на новый Processor.
- Старые assembler API сохраняются только как obsolete-адаптеры на один релиз.
- Старые имена регистров и опкодов не возвращаются.
- `pc`/`pc_a` в TSV остаются совместимыми полями формата, пользовательские подписи используют K.
- Один production-класс не должен превышать 300 строк логики. Исключение — data-only таблицы кодировок без исполняемой логики.
- В production-файле допускается один публичный тип; небольшие private/internal helper-типы могут находиться рядом с владельцем.
- Новые внешние NuGet-зависимости не добавляются.
- Итог поставляется одной feature-веткой и одним PR с последовательными проверяемыми коммитами.
- `BIT49` и другие execution-only constants принадлежат Processor; в Architecture
  остаются только архитектурные маски 40/41/42/48 бит, 15-битный адрес, число и
  номера специальных индексных регистров, bias порядка, `OnBit` и `NormalizeAddress`.

---

## Исходный checkpoint и уже выполненная работа

План актуализирован для ветки `codex/architecture-cleanup` на коммите `9edaa16`.
В истории уже существуют отдельные коммиты для создания верхнего решения,
выделения Architecture/Processor, части разбиения Processor/ALU и первого API
Assembler (`484a5fc`…`9edaa16`). Их нельзя дублировать или переписывать. Коммит
переименования регистров и опкодов `65aa2b0` уже является предком ветки и также
не должен создаваться повторно.

Текущий checkpoint нельзя считать зелёным для продолжения рефакторинга:

- `dotnet test Besm6.sln -c Release` запускает только 110 новых тестов;
- `dotnet test src/besm6.net/besm6.net.sln -c Release --no-restore` не компилирует
  прежний `Besm6.Tests` после переноса типов;
- `Besm6.Architecture` физически содержит `IDevice` в namespace `Besm6.Core`;
- общие `DecodedInstruction` и `InstructionCodec` ещё отсутствуют;
- `Besm6.Processor` всё ещё обращается к environment variables и файлам трассировки,
  содержит временные поля экстракода и монолитный switch;
- часть старых assembler API всё ещё содержит собственную реализацию.

Рабочее дерево содержит посторонние пользовательские изменения в `book/`, `tmp/`
и `tools/validate-book-svg.ps1`. Во всех командах `git add` ниже перечисляются только
точные пути архитектурной задачи; `git add -A` запрещён.

## Карта целевых файлов

```text
src/Besm6.Architecture/
  DecodedInstruction.cs         decoded-модель одного полуслова
  InstructionCodec.cs           единая битовая упаковка/распаковка

src/Besm6.Processor/
  Processor.cs                  публичный фасад и Step/Reset
  ProcessorState.cs             единственный владелец изменяемого CPU-state
  ProcessorTraceController.cs   формирование typed trace records
  ProcessorSnapshot.cs
  InstructionTraceRecord.cs
  RegisterTraceRecord.cs
  ExtracodeCall.cs
  ExecutionFrame.cs
  InstructionOutcome.cs
  InstructionExecutor.cs        только decode/dispatch/finalize
  MemoryInstructionExecutor.cs
  IndexInstructionExecutor.cs
  ControlInstructionExecutor.cs
  ExtracodeInstructionExecutor.cs

src/Besm6.Assembler/
  Lexer.cs                       токенизация без знания диалекта
  Parser.cs                      разбор строк и полуслов
  SymbolTable.cs                 двухпроходные labels/base address
  AssemblyDialectDetector.cs     *madlen/*bemsh/*assem и auto
  Disassembler.cs               новый API дизассемблера
  Legacy/*.cs                    namespace Besm6.Asm, только adapters

src/Besm6.Runtime/
  MachineCore.cs, SystemBus.cs
  Configuration/RuntimeOptions.cs
  Configuration/JsonRuntimeOptionsLoader.cs
  Devices/*.cs
  Loading/DubnaLoader.cs
  Loading/JobProgramLoader.cs
  Loading/TapeMountService.cs
  Loading/MonsysBootstrapper.cs
  Loading/ExecutionLoop.cs
  Loading/LoadResult.cs
  Encoding/CosyEncoder.cs, CosyDecoder.cs, Koi7Codec.cs
  Encoding/Gost10859Codec.cs, TextCodec.cs, EncodingTables.cs
  Extracodes/ExtracodeHandler*.cs
  Extracodes/E50Parser.cs, E50Formatter.cs, E64*Formatter.cs
  Tracing/CanonicalTraceWriter.cs, DiagnosticTraceWriter.cs

src/Besm6.Cli/
  Program.cs, CliApplication.cs, Commands/*.cs

src/Besm6.Tui/
  Program.cs, TuiApplication.cs, TuiApp.cs, TuiController.cs
  TuiCommandParser.cs, TuiRenderer.cs, TuiSessionState.cs
```

Ниже перечислены только оставшиеся действия от checkpoint `9edaa16`.

---

### Task 1: Восстановить полный зелёный regression baseline

**Files:**

- Modify: `Besm6.sln`
- Modify: `src/besm6.net/tests/Besm6.Tests/Besm6.Tests.csproj`
- Modify: старые processor-test files только для namespace/import исправлений
- Create: `tests/golden/architecture-cleanup/README.md`
- Verify: `src/besm6.net/tests/raw/*`, `tools/tests/*`

**Interfaces:**

- Consumes: существующие `Besm6.Architecture`, `Besm6.Processor`,
  `Besm6.Assembler` и временный executable `src/besm6.net/besm6.csproj`.
- Produces: один верхний solution, который до каждого следующего коммита запускает
  и новые boundary-тесты, и полный старый regression-набор.

- [ ] **Step 1: Зафиксировать чистую область архитектурной работы**

```powershell
git status --short --branch
git diff --name-only
git ls-files --others --exclude-standard
```

Ожидание: активна `codex/architecture-cleanup`; пользовательские файлы из `book/`,
`tmp/` и `tools/validate-book-svg.ps1` видны, но не попадают ни в один последующий
`git add`.

- [ ] **Step 2: Добавить временный монолит и полный test project в `Besm6.sln`**

```powershell
dotnet sln Besm6.sln add src/besm6.net/besm6.csproj
dotnet sln Besm6.sln add src/besm6.net/tests/Besm6.Tests/Besm6.Tests.csproj
```

Они остаются в solution только до финального переноса тестов и удаления монолита.

- [ ] **Step 3: Исправить ссылки старого test project**

В `Besm6.Tests.csproj` добавить явные project references и global usings:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\Besm6.Architecture\Besm6.Architecture.csproj" />
  <ProjectReference Include="..\..\..\Besm6.Processor\Besm6.Processor.csproj" />
  <ProjectReference Include="..\..\..\Besm6.Assembler\Besm6.Assembler.csproj" />
  <ProjectReference Include="..\..\besm6.csproj" />
</ItemGroup>
<ItemGroup>
  <Using Include="Besm6.Architecture" />
  <Using Include="Besm6.Processor" />
</ItemGroup>
```

Удалить ошибочный/дублирующий reference или `Using`, но пока не перемещать тестовые
файлы: этот шаг только восстанавливает прежний oracle.

- [ ] **Step 4: Запустить полный набор и сохранить числовой baseline**

```powershell
dotnet test Besm6.sln -c Release --logger "trx;LogFileName=architecture-cleanup-baseline.trx"
py -3 -m unittest discover -s tools/tests
```

Ожидание: все существующие 506 .NET-сценариев обнаружены; остаются ровно четыре
обоснованных skip, Python tests проходят. Если фактический discovery отличается,
сначала сверить data-driven строки и причины skip; не менять expected count молча.

- [ ] **Step 5: Описать воспроизводимый baseline**

В `tests/golden/architecture-cleanup/README.md` записать команды, число discovered,
passed/skipped, имена четырёх skip и то, что TSV сравнивается с сохранением полей
`pc`/`pc_a`.

- [ ] **Step 6: Commit**

```powershell
git add Besm6.sln src/besm6.net/tests/Besm6.Tests tests/golden/architecture-cleanup/README.md
git diff --cached --check
git commit -m "Restore full architecture cleanup regression baseline"
```

---

### Task 2: Завершить чистую Architecture и единый InstructionCodec

**Files:**

- Create: `src/Besm6.Architecture/DecodedInstruction.cs`
- Create: `src/Besm6.Architecture/InstructionCodec.cs`
- Create: `src/besm6.net/Core/IDevice.cs` (временное местоположение до Runtime)
- Delete: `src/Besm6.Architecture/IDevice.cs`
- Modify: `src/Besm6.Processor/InstructionExecutor.cs`
- Modify: `src/Besm6.Assembler/AssemblyEngine.cs`
- Modify: `src/Besm6.Assembler/Disassembler.cs` после его создания в Task 5
- Modify: `tests/Besm6.Architecture.Tests/AssemblyBoundaryTests.cs`
- Create: `tests/Besm6.Architecture.Tests/InstructionCodecTests.cs`

**Interfaces:**

- Consumes: `Opcode`, `InstructionFormat`, `ArchitectureConstants`.
- Produces:

```csharp
public readonly record struct DecodedInstruction(
    byte Register,
    Opcode Opcode,
    ushort Address,
    InstructionFormat Format);

public static class InstructionCodec
{
    public static DecodedInstruction DecodeHalf(uint halfWord);
    public static uint EncodeHalf(DecodedInstruction instruction);
    public static (DecodedInstruction Left, DecodedInstruction Right) DecodeWord(ulong word);
    public static ulong EncodeWord(DecodedInstruction left, DecodedInstruction right);
}
```

- [ ] **Step 1: Написать failing tests для короткого, расширенного и длинного формата**

```csharp
[TestMethod]
public void ExtendedShortAddress_RoundTrips()
{
    var source = new DecodedInstruction(3, Opcode.Xta, 0x7FFB, InstructionFormat.Short);
    Assert.AreEqual(source, InstructionCodec.DecodeHalf(InstructionCodec.EncodeHalf(source)));
}

[TestMethod]
public void LongInstruction_RoundTrips()
{
    var source = new DecodedInstruction(15, Opcode.Vlm, 0x4321, InstructionFormat.Long);
    Assert.AreEqual(source, InstructionCodec.DecodeHalf(InstructionCodec.EncodeHalf(source)));
}
```

Также проверить левое/правое полуслово полного 48-битного слова, `reg=0/15`, адреса
`0`, `0xFFF`, `0x7000`, `0x7FFF` и исключение для непредставимого короткого адреса
`0x1000..0x6FFF`.

- [ ] **Step 2: Убедиться, что tests падают из-за отсутствия codec**

```powershell
dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj -c Release --filter "FullyQualifiedName~InstructionCodecTests"
```

Expected: compile failure для отсутствующих `DecodedInstruction`/`InstructionCodec`.

- [ ] **Step 3: Реализовать единственную битовую схему**

`DecodeHalf` должен использовать текущую машинную схему:

```csharp
byte register = (byte)((halfWord >> 20) & 0xF);
bool isLong = (halfWord & (1u << 19)) != 0;
Opcode opcode = isLong
    ? (Opcode)((halfWord >> 12) & 0xF8)
    : (Opcode)((halfWord >> 12) & 0x3F);
ushort address = (ushort)(halfWord & (isLong ? 0x7FFFu : 0xFFFu));
if (!isLong && (halfWord & (1u << 18)) != 0)
    address |= 0x7000;
```

`EncodeHalf` выполняет обратное преобразование, валидирует `Register <= 15`, адрес
`<= 0x7FFF`, формат opcode и не допускает второй реализации упаковки в Processor
или Assembler.

- [ ] **Step 4: Удалить устройство из архитектурной сборки**

Переместить `IDevice` во временный `src/besm6.net/Core/IDevice.cs`, сохранив namespace
`Besm6.Core`, затем удалить `src/Besm6.Architecture/IDevice.cs`. В boundary test
добавить проверку, что в Architecture нет namespace `Besm6.Core` и типов с именами
`IDevice`, `Tape`, `Disk`, `Loader`, `ConsoleDevice`.

- [ ] **Step 5: Перевести текущий decode Processor на `InstructionCodec.DecodeHalf`**

На этом шаге не менять порядок advance K, C или stack correction; заменить только
извлечение `reg/opcode/addr/format`.

- [ ] **Step 6: Выполнить focused и full tests**

```powershell
dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj -c Release
dotnet test Besm6.sln -c Release --no-restore
```

- [ ] **Step 7: Commit**

```powershell
git add src/Besm6.Architecture src/Besm6.Processor/InstructionExecutor.cs src/besm6.net/Core/IDevice.cs tests/Besm6.Architecture.Tests
git diff --cached --check
git commit -m "Complete architecture instruction codec"
```

---

### Task 3: Сделать Processor действительно headless и ввести typed contracts

**Files:**

- Create: `src/Besm6.Processor/ProcessorState.cs`
- Create: `src/Besm6.Processor/ProcessorSnapshot.cs`
- Create: `src/Besm6.Processor/InstructionTraceRecord.cs`
- Create: `src/Besm6.Processor/RegisterTraceRecord.cs`
- Create: `src/Besm6.Processor/ExtracodeCall.cs`
- Create: `src/Besm6.Processor/ProcessorTraceController.cs`
- Move: `src/besm6.net/Core/StopReason.cs` → `src/Besm6.Processor/StopReason.cs`
- Modify: `src/Besm6.Processor/Processor.cs`
- Modify: `src/Besm6.Processor/ProcessorMemoryAccess.cs`
- Modify: `src/Besm6.Processor/ProcessorDebugWatch.cs`
- Modify: ALU operation classes to consume `ProcessorState`
- Create: `src/besm6.net/Tracing/CanonicalTraceWriter.cs` (временный host)
- Create: `src/besm6.net/Tracing/DiagnosticTraceWriter.cs` (временный host)
- Modify: `src/besm6.net/Core/MachineCore.cs`
- Modify: `src/besm6.net/Loader/DubnaLoader.cs`
- Modify: `src/besm6.net/Loader/ExtracodeHandler.cs`
- Create: `tests/Besm6.Processor.Tests/ProcessorTraceContractTests.cs`
- Create: `tests/Besm6.Processor.Tests/ProcessorDependencyTests.cs`

**Interfaces:**

- Consumes: `DecodedInstruction`, `Word48`, `Extracode`, `IMemory`.
- Produces:

```csharp
public readonly record struct ExtracodeCall(
    Extracode Code,
    uint EffectiveAddress,
    byte Register,
    ushort RawAddress,
    bool IsRightHalf);

public sealed class Processor
{
    public Action<InstructionTraceRecord>? InstructionTrace { get; set; }
    public Action<RegisterTraceRecord>? RegisterTrace { get; set; }
    public Func<ExtracodeCall, bool>? ExtracodeDispatch { get; set; }
    public bool Step();
}
```

`ProcessorSnapshot` содержит A, Y, R, C, K, right-half, ApplyC, AEX, intercept
count/address и копию шестнадцати M-регистров. `InstructionTraceRecord` содержит
sequence, raw 48-bit word, raw 24-bit instruction, decoded instruction и pre/post
snapshots. `RegisterTraceRecord` содержит имя регистра и значение в совместимом
формате текущего диагностического writer.

- [ ] **Step 1: Написать failing contract tests**

```csharp
[TestMethod]
public void ExtracodeDispatch_ReceivesCompleteCall()
{
    ExtracodeCall? observed = null;
    var memory = new CoreMemory();
    var instruction = new DecodedInstruction(
        2,
        (Opcode)(int)Extracode.E50,
        Convert.ToUInt16("1234", 8),
        InstructionFormat.Short);
    memory.Write(1, new Word48((ulong)InstructionCodec.EncodeHalf(instruction) << 24));
    var cpu = new Processor(memory);
    cpu.ExtracodeDispatch = call => { observed = call; return true; };
    cpu.Step();
    Assert.IsTrue(observed.HasValue);
    Assert.AreEqual(
        new ExtracodeCall(Extracode.E50, 0x29C, 2, 0x29C, false),
        observed.Value);
}
```

Использовать существующий helper кодирования вместо строкового assembler, если это
устраняет лишнюю зависимость теста Processor от Assembler. Отдельно проверить, что
callback instruction trace вызывается ровно один раз на реально исполненное
полуслово и содержит pre/post state.

- [ ] **Step 2: Добавить dependency guard**

`ProcessorDependencyTests` проверяет `Assembly.GetReferencedAssemblies()` и запрещает
ссылки на `System.Console`, `System.IO.FileSystem`, `Besm6.Runtime`, `Besm6.Cli`,
`Besm6.Tui`. Исходный grep должен стать пустым:

```powershell
rg -n "Environment\.|Console\.|File\.|Directory\.|StreamWriter" src/Besm6.Processor
```

- [ ] **Step 3: Вынести всё mutable state в `ProcessorState`**

Переместить `_k`, `_a`, `_y`, `_m`, `_c`, `_r`, `_interceptCount`, `_interceptAddr`,
`_rightInstrFlag`, `_applyC`, `_corrStack`, `_rk`, `_aex` и debug-watch поля.
`Processor` сохраняет прежние публичные свойства/методы и делегирует state, чтобы
не ломать существующие тесты и Runtime.

- [ ] **Step 4: Вынести формирование records в `ProcessorTraceController`**

Контроллер получает `ProcessorState`, создаёт immutable snapshots и вызывает только
callbacks. Он не читает environment и не форматирует TSV. Имена полей `pc`/`pc_a`
остаются обязанностью writer, а пользовательские сообщения продолжают использовать K.

- [ ] **Step 5: Перенести файловую трассировку во временный host**

`CanonicalTraceWriter` подписывается на `InstructionTrace` и воспроизводит текущий
header/строки byte-for-byte. `DiagnosticTraceWriter` подписывается на оба callback.
Параметры `BESM6_CANON_TRACE`, `BESM6_CANON_TRACE_LIMIT`, `BESM6_INSTR_TRACE` читает
только временный executable/host.

- [ ] **Step 6: Заменить временный extrаcode handshake**

`InstructionExecutor` создаёт один `ExtracodeCall` и вызывает `ExtracodeDispatch`.
Удалить `ExtracodeHandler`, `ExtracodeReg`, `ExtracodeRawAddr`,
`ExtracodeRightFlag` из `Processor`; изменить `DubnaLoader.InstallExtracodeHook()` и
`ExtracodeHandler.Handle` на приём record.

- [ ] **Step 7: Сравнить trace до/после**

Запустить существующие `ProcessorTraceRegressionTests`, затем:

```powershell
py -3 tools/diff_trace.py tests/golden/architecture-cleanup/before.tsv tests/golden/architecture-cleanup/after.tsv
```

Если repository не хранит эти два больших файла, генерировать их по командам из
README в temp directory; в git сохранять только малый fixture, достаточный для
`pc`/`pc_a` и legacy-column compatibility.

- [ ] **Step 8: Full verification and commit**

```powershell
dotnet test tests/Besm6.Processor.Tests/Besm6.Processor.Tests.csproj -c Release
dotnet test Besm6.sln -c Release --no-restore
git add src/Besm6.Processor src/besm6.net/Core/MachineCore.cs src/besm6.net/Loader/DubnaLoader.cs src/besm6.net/Loader/ExtracodeHandler.cs src/besm6.net/Tracing tests/Besm6.Processor.Tests tests/golden/architecture-cleanup
git diff --cached --check
git commit -m "Complete headless processor contracts"
```

---

### Task 4: Разделить исполнение инструкций и централизовать finalize

**Files:**

- Create: `src/Besm6.Processor/ExecutionFrame.cs`
- Create: `src/Besm6.Processor/InstructionOutcome.cs`
- Create: `src/Besm6.Processor/MemoryInstructionExecutor.cs`
- Create: `src/Besm6.Processor/IndexInstructionExecutor.cs`
- Create: `src/Besm6.Processor/ControlInstructionExecutor.cs`
- Create: `src/Besm6.Processor/ExtracodeInstructionExecutor.cs`
- Modify: `src/Besm6.Processor/InstructionExecutor.cs`
- Modify: `src/Besm6.Processor/Alu.cs`
- Modify: `src/Besm6.Processor/AdditiveOperations.cs`
- Modify: `src/Besm6.Processor/MultiplicativeOperations.cs`
- Modify: `src/Besm6.Processor/ShiftOperations.cs`
- Modify: `src/Besm6.Processor/NormalizationAndRounding.cs`
- Modify: processor regression tests moved/created in prior tasks

**Interfaces:**

- Consumes: `ProcessorState`, `ProcessorMemoryAccess`, `InstructionCodec`, typed trace
  and extracode contracts.
- Produces:

```csharp
internal enum InstructionOutcome { Continue, Stop }

internal sealed class ExecutionFrame
{
    internal required DecodedInstruction Instruction { get; init; }
    internal required uint RawInstruction { get; init; }
    internal required uint EffectiveAddress { get; set; }
    internal required bool WasRightHalf { get; init; }
}
```

Каждый специализированный executor имеет `InstructionOutcome Execute(ExecutionFrame frame)`
и не выполняет глобальный finalize самостоятельно.

- [ ] **Step 1: Добавить characterization tests для единственного finalize**

Проверить отдельными тестами: pre-decrement/post-correction stack для STX/XTS/ATI,
переход L→R и R→next K, применение и очистку C, STOP без лишнего advance, branch с
явной сменой half, arithmetic intercept. Эти тесты должны проходить до разбиения.

- [ ] **Step 2: Создать frame/outcome и пустые специализированные executors**

На первом micro-step dispatcher продолжает направлять все команды в старый switch;
новые executors компилируются, но не вызываются. Запустить Processor tests.

- [ ] **Step 3: Перенести memory opcodes 000–037**

Переместить case-блоки без редактирования арифметики. После удаления каждого блока
запустить фильтр `ProcessorTests|AluTests|ProcessorInstructionStateTests`.

- [ ] **Step 4: Перенести index opcodes 040–047**

Запустить `ProcessorStackSemanticsTests|ProcessorStateRegressionTests` и убедиться,
что stack correction остаётся только в общем finalize.

- [ ] **Step 5: Перенести long control opcodes 0220–0370**

Запустить `ProcessorBranchModeTests` и тесты C/right-half transitions.

- [ ] **Step 6: Перенести E50–E77 и long E20/E21 dispatch**

`ExtracodeInstructionExecutor` только вычисляет call и интерпретирует bool result;
реализация экстракодов остаётся в host/Runtime.

- [ ] **Step 7: Оставить в `InstructionExecutor` один pipeline**

Порядок: fetch → decode → pre-trace → advance-half/K → execute selected family →
single stack/C/final-register finalize → post-trace → return outcome. Удалить старый
switch и любые вторые применения этих переходов.

- [ ] **Step 8: Переключить ALU components на `ProcessorState`**

`Alu` остаётся фасадом; четыре operation-класса получают state в конструкторе и не
держат ссылку на `Processor`. Математические выражения и тексты исключений не менять.

- [ ] **Step 9: Проверить размер и регрессии**

```powershell
Get-ChildItem src/Besm6.Processor -Filter *.cs | ForEach-Object { [pscustomobject]@{Lines=(Get-Content $_).Count; Name=$_.Name} } | Sort-Object Lines -Descending
dotnet test tests/Besm6.Processor.Tests/Besm6.Processor.Tests.csproj -c Release
dotnet test Besm6.sln -c Release --no-restore
```

Ни один production-класс с исполняемой логикой не превышает 300 строк.

- [ ] **Step 10: Commit**

```powershell
git add src/Besm6.Processor tests/Besm6.Processor.Tests src/besm6.net/tests/Besm6.Tests
git diff --cached --check
git commit -m "Split processor instruction execution"
```

---

### Task 5: Завершить Assembler, dialect routing и obsolete adapters

**Files:**

- Create: `src/Besm6.Assembler/Lexer.cs`
- Create: `src/Besm6.Assembler/Parser.cs`
- Create: `src/Besm6.Assembler/SymbolTable.cs`
- Create: `src/Besm6.Assembler/AssemblyDialectDetector.cs`
- Create: `src/Besm6.Assembler/Disassembler.cs`
- Modify: `src/Besm6.Assembler/AssemblyEngine.cs`
- Modify: `src/Besm6.Assembler/OpcodeTable.cs`
- Create/Move: `src/Besm6.Assembler/Legacy/Assembler.cs`
- Create/Move: `src/Besm6.Assembler/Legacy/ProgramAssembler.cs`
- Create/Move: `src/Besm6.Assembler/Legacy/AsmResult.cs`
- Create/Move: `src/Besm6.Assembler/Legacy/OpcodeTable.cs`
- Create/Move: `src/Besm6.Assembler/Legacy/Disassembler.cs`
- Delete after move: `src/besm6.net/Asm/*.cs`
- Modify: `tests/Besm6.Assembler.Tests/AssemblerDialectTests.cs`
- Move/Modify: old `AssemblerTests.cs` into `tests/Besm6.Assembler.Tests`

**Interfaces:**

- Consumes: `InstructionCodec`, `Opcode`, `DecodedInstruction`.
- Produces: заданный `IBesm6Assembler` API и новый
  `Besm6.Assembler.Disassembler`, используемый CLI/TUI; namespace `Besm6.Asm`
  остаётся доступен из той же DLL только через `[Obsolete]` wrappers.

- [ ] **Step 1: Расширить acceptance tests**

Добавить точные cases:

```csharp
[DataRow(AssemblyDialect.Madlen, "xta 10", true)]
[DataRow(AssemblyDialect.Madlen, "сч 10", false)]
[DataRow(AssemblyDialect.Bemsh, "сч 10", true)]
[DataRow(AssemblyDialect.Bemsh, "xta 10", false)]
```

Отдельно проверить Auto для обоих языков, `*madlen`, `*bemsh`, `*assem`, равенство
`*32` и `ext`, labels, отрицательные адреса, registers, raw words, round-trip и parity
каждого legacy adapter с новым API.

- [ ] **Step 2: Разделить tokenizer/parser/symbol resolution**

`Lexer` возвращает токены без таблицы opcode; `Parser` строит instruction/directive
модель; `SymbolTable` выполняет два прохода и base address. `AssemblyEngine` становится
оркестратором менее 300 строк.

- [ ] **Step 3: Реализовать directive selection**

`AssemblyDialectDetector` рассматривает первую assembler directive:

```csharp
"*madlen" => AssemblyDialect.Madlen,
"*bemsh"  => AssemblyDialect.Bemsh,
"*assem"  => AssemblyDialect.Auto
```

Строгий assembler отвергает mnemonic другого языка; Auto сохраняет нынешнее mixed
поведение. Директивы выбора не создают машинного слова.

- [ ] **Step 4: Удалить ручную упаковку инструкций**

Все пути mnemonic/numeric/label создают `DecodedInstruction` и вызывают
`InstructionCodec.EncodeHalf/EncodeWord`. `rg` не должен находить битовую упаковку
`<< 12`, `<< 20`, `<< 24` в assembler, кроме самого `InstructionCodec` в Architecture.

- [ ] **Step 5: Реализовать новый Disassembler поверх codec**

Сохранить текущий текстовый формат Madlen, отрицательные адреса, omission нулевого
правого полуслова и octal formatting. Код decode берётся только из `InstructionCodec`.

- [ ] **Step 6: Переместить legacy API в Assembler DLL**

Каждый тип в namespace `Besm6.Asm` получает `[Obsolete]` и одну строку делегирования.
Таблицы и parser logic в Legacy отсутствуют. Это сохраняет совместимость после
удаления монолитного executable.

- [ ] **Step 7: Перевести всех текущих consumers**

`DubnaLoader`, CLI и TUI используют `IBesm6Assembler`/новый Disassembler напрямую;
legacy namespace остаётся только в adapter parity tests.

- [ ] **Step 8: Verify and commit**

```powershell
dotnet test tests/Besm6.Assembler.Tests/Besm6.Assembler.Tests.csproj -c Release
dotnet test Besm6.sln -c Release --no-restore
rg -n "<<\s*(12|20|24)" src/Besm6.Assembler
git add src/Besm6.Assembler src/besm6.net/Asm src/besm6.net/Loader/DubnaLoader.cs src/besm6.net/Cli src/besm6.net/Tui tests/Besm6.Assembler.Tests src/besm6.net/tests/Besm6.Tests
git diff --cached --check
git commit -m "Complete dialect-aware assembler extraction"
```

---

### Task 6: Создать Runtime assembly и перенести host responsibilities

**Files:**

- Create: `src/Besm6.Runtime/Besm6.Runtime.csproj`
- Move: `src/besm6.net/Core/MachineCore.cs` → `src/Besm6.Runtime/MachineCore.cs`
- Move: `SystemBus.cs`, device classes, event scheduler and simulation clock
- Move: loader/tape/assets files into `src/Besm6.Runtime/Loading` and `Devices`
- Create: `src/Besm6.Runtime/Configuration/RuntimeOptions.cs`
- Create: `src/Besm6.Runtime/Configuration/JsonRuntimeOptionsLoader.cs`
- Create: `src/Besm6.Runtime/MachineFactory.cs`
- Move: temporary trace writers into `src/Besm6.Runtime/Tracing`
- Create: `tests/Besm6.Runtime.Tests/Besm6.Runtime.Tests.csproj`
- Create: `tests/Besm6.Runtime.Tests/RuntimeOptionsTests.cs`
- Modify: `Besm6.sln`

**Interfaces:**

- Consumes: Architecture, Processor, Assembler only.
- Produces:

```csharp
public sealed class RuntimeOptions
{
    public string? Tapes { get; set; }
    public string? Disk { get; set; }
    public string? Drum { get; set; }
    public long DefaultLimit { get; set; } = 20_000_000;
    public long CheckLimit { get; set; } = 5_000;
    public string LoadBaseOctal { get; set; } = "1000";
    public int MemorySize { get; set; } = 32_768;
    public bool UseWallClock { get; set; } = true;
}

public sealed class JsonRuntimeOptionsLoader
{
    public RuntimeOptions Load(string? path = null);
    public string ResolvePath(RuntimeOptions options, string relative);
}
```

- [ ] **Step 1: Создать project и boundary test до переноса**

Project references должны быть ровно:

```xml
<ProjectReference Include="..\Besm6.Architecture\Besm6.Architecture.csproj" />
<ProjectReference Include="..\Besm6.Processor\Besm6.Processor.csproj" />
<ProjectReference Include="..\Besm6.Assembler\Besm6.Assembler.csproj" />
```

Добавить Runtime и Runtime.Tests в `Besm6.sln`; test сначала падает, поскольку
`MachineCore` ещё находится в монолите.

- [ ] **Step 2: Перенести machine/memory bus/device contracts**

Использовать `git mv`; привести namespace к `Besm6.Runtime`. `SystemBus` реализует
`Besm6.Processor.IMemory`; `IDevice` окончательно переносится из временного монолита.
Processor не получает обратной ссылки на Runtime.

- [ ] **Step 3: Перенести devices, clock и scheduler**

Переместить ConsoleDevice, DeviceManager, DeviceType, DiskDevice,
MagneticDrumDevice, TeletypeDevice, Puncher, Plotter, EventScheduler,
SimulationClock и runtime StopReason. Проверить соответствующие существующие tests.

- [ ] **Step 4: Перенести loader, tape, job parser, assets и math без разбиения**

На этом micro-step меняются пути/namespace/project references, но не тела больших
алгоритмов. Это отделяет механический move от последующих структурных изменений.

- [ ] **Step 5: Ввести общий options loader**

Перенести свойства текущего `Config` в `RuntimeOptions`; JSON names и default values
сохранить. Относительные пути разрешать в порядке: directory config-файла → current
directory → app base → standard tapes lookup. Явно указанный отсутствующий config
вызывает `FileNotFoundException`, неявно отсутствующий использует defaults.

- [ ] **Step 6: Перенести machine factory и trace writers**

Оба будущих frontend получают одинаковые `RuntimeOptions`, runtime assets validation,
MachineCore/DubnaLoader и trace wiring из Runtime. Никакой Runtime type не использует
CLI/TUI namespace.

- [ ] **Step 7: Настроить runtime content**

`Besm6.Runtime.csproj` включает `tapes/**` и default `besm6.json` с
`CopyToOutputDirectory="PreserveNewest"` и `CopyToPublishDirectory="PreserveNewest"`.
RuntimeAssetsTests проверяют наличие/sha и диагностируют все missing assets одним
исключением.

- [ ] **Step 8: Verify and commit**

```powershell
dotnet test tests/Besm6.Runtime.Tests/Besm6.Runtime.Tests.csproj -c Release
dotnet test Besm6.sln -c Release --no-restore
git add Besm6.sln src/Besm6.Runtime src/besm6.net tests/Besm6.Runtime.Tests
git diff --cached --check
git commit -m "Extract BESM-6 runtime assembly"
```

---

### Task 7: Разделить DubnaLoader на orchestration services

**Files:**

- Modify: `src/Besm6.Runtime/Loading/DubnaLoader.cs`
- Create: `src/Besm6.Runtime/Loading/JobProgramLoader.cs`
- Create: `src/Besm6.Runtime/Loading/TapeMountService.cs`
- Create: `src/Besm6.Runtime/Loading/MonsysBootstrapper.cs`
- Create: `src/Besm6.Runtime/Loading/ExecutionLoop.cs`
- Create: `src/Besm6.Runtime/Loading/LoadResult.cs`
- Modify/Move: Loader and MachineCore tests into `tests/Besm6.Runtime.Tests`

**Interfaces:**

- Consumes: `MachineCore`, `IBesm6Assembler`, runtime assets, tape devices and typed
  Processor callbacks.
- Produces: `DubnaLoader` как совместимый публичный orchestration facade; internal
  services independently testable through InternalsVisibleTo Runtime.Tests.

- [ ] **Step 1: Закрепить tests по каждой ответственности**

Перенести существующие cases и сгруппировать filters: raw/assembler sections;
mount/release/file search; MONSYS bootstrap; instruction limit; wall-clock limit;
hang/loop detection; halt/failure result. До split все должны проходить.

- [ ] **Step 2: Вынести `LoadResult` в отдельный файл без изменения API**

Сохранить factories `Halt`, `StoppedByLimit`, `Failed`, `Success`, `LimitExceeded`,
K/instruction count и строковое представление.

- [ ] **Step 3: Вынести tape lifecycle**

`TapeMountService` владеет dictionaries/sets file-backed tapes и реализует mount,
release, file search/mount, scratch mount, required/script tapes. `DubnaLoader`
делегирует существующие public methods.

- [ ] **Step 4: Вынести program loading**

`JobProgramLoader` отвечает за raw words, assembler sections, base/start address и
запись script на drum. Он получает `IBesm6Assembler`, а не создаёт concrete assembler.

- [ ] **Step 5: Вынести MONSYS bootstrap**

`MonsysBootstrapper` обеспечивает required tape, подготовку drum и начальный K;
вызовы `BootMsDubna`/`BootAndRun` facade сохраняют прежний порядок.

- [ ] **Step 6: Вынести bounded execution**

`ExecutionLoop` единолично применяет instruction/wall-clock/hang/loop limits,
arithmetic intercept и формирует `LoadResult`. Progress output передаётся callback,
не пишется напрямую в Console.

- [ ] **Step 7: Оставить facade менее 300 строк**

`DubnaLoader` хранит dependencies/options, связывает services и предоставляет
совместимые entry points. Дублированных dictionaries и limit loops не остаётся.

- [ ] **Step 8: Verify and commit**

```powershell
dotnet test tests/Besm6.Runtime.Tests/Besm6.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~Loader|FullyQualifiedName~Tape|FullyQualifiedName~ExecutionLoop|FullyQualifiedName~Bootstrap"
dotnet test Besm6.sln -c Release --no-restore
git add src/Besm6.Runtime/Loading tests/Besm6.Runtime.Tests
git diff --cached --check
git commit -m "Split Dubna runtime loading services"
```

---

### Task 8: Разделить COSY/KOI-7/ГОСТ/TEXT codecs

**Files:**

- Create: `src/Besm6.Runtime/Encoding/CosyEncoder.cs`
- Create: `src/Besm6.Runtime/Encoding/CosyDecoder.cs`
- Create: `src/Besm6.Runtime/Encoding/Koi7Codec.cs`
- Create: `src/Besm6.Runtime/Encoding/Gost10859Codec.cs`
- Create: `src/Besm6.Runtime/Encoding/TextCodec.cs`
- Create: `src/Besm6.Runtime/Encoding/EncodingTables.cs`
- Create or Modify: `src/Besm6.Runtime/Encoding/CosyCodec.cs` internal facade
- Move/Modify: encoding tests into `tests/Besm6.Runtime.Tests`

**Interfaces:**

- Consumes: текущие tables/algorithms из монолитного `CosyCodec` без изменения bytes.
- Produces: focused internal codecs; существующий `CosyCodec` делегирует, если Runtime
  code ещё использует facade.

- [ ] **Step 1: Зафиксировать byte-for-byte fixtures**

Для каждого codec добавить round-trip и known-vector cases: ASCII, кириллица,
control characters, COSY old/end-file markers, max length, malformed/truncated line,
`BytesToWord` boundaries.

- [ ] **Step 2: Вынести immutable tables**

`EncodingTables` содержит только data/build-table routines. Исключение лимита 300
строк применяется только если файл действительно data-only и не маршрутизирует codec.

- [ ] **Step 3: Вынести KOI-7, GOST-10859 и TEXT conversions**

Каждый codec принимает/возвращает те же char/byte values. Неподдерживаемые символы и
fallback остаются идентичными current behavior.

- [ ] **Step 4: Разделить COSY encode/decode**

`CosyEncoder` отвечает только за line packing; `CosyDecoder` — markers и decoding.
Ни один из них не знает о loader, tape path или Console.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test tests/Besm6.Runtime.Tests/Besm6.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~Encoding|FullyQualifiedName~Cosy|FullyQualifiedName~Koi|FullyQualifiedName~Gost|FullyQualifiedName~Text"
dotnet test Besm6.sln -c Release --no-restore
git add src/Besm6.Runtime/Encoding tests/Besm6.Runtime.Tests
git diff --cached --check
git commit -m "Split BESM-6 runtime codecs"
```

---

### Task 9: Разнести ExtracodeHandler и выделить E50/E64 algorithms

**Files:**

- Modify: `src/Besm6.Runtime/Extracodes/ExtracodeHandler.cs`
- Create: `ExtracodeHandler.Math.cs`
- Create: `ExtracodeHandler.Storage.cs`
- Create: `ExtracodeHandler.Print.cs`
- Create: `ExtracodeHandler.Terminal.cs`
- Create: `ExtracodeHandler.System.cs`
- Create: `E50Parser.cs`, `E50Formatter.cs`
- Create: `E64GostFormatter.cs`, `E64DubnaFormatter.cs`
- Create: `E64OctalFormatter.cs`, `E64HexFormatter.cs`
- Create: `E64RealFormatter.cs`, `E64InstructionFormatter.cs`
- Modify/Move: extracode tests into `tests/Besm6.Runtime.Tests`

**Interfaces:**

- Consumes: `ExtracodeCall`, MachineCore/SystemBus, codecs, devices, clock.
- Produces: `public sealed partial class ExtracodeHandler` с одним dispatch method;
  internal algorithms без прямой Console/file dependency.

- [ ] **Step 1: Сформировать тестовые группы по машинным кодам**

Создать отдельные test classes E50–E56, E57, E63/E64/E65/E67, E70/E71,
E72/E75/E76. Существующие `ExtracodeHandlerContractTests`, `E65E71Tests` и edge
cases перераспределить без удаления assertions.

- [ ] **Step 2: Сохранить dispatch в root partial**

Root file содержит поля, constructor, `Handle(ExtracodeCall)`, hang detection и
typed trace. Switch только маршрутизирует в subsystem method; алгоритмов format/I/O
в root нет.

- [ ] **Step 3: Разнести subsystem partials механически**

Math: E50–E56; Storage: E57 и physical I/O; Print: E64; Terminal: E70/E71,
puncher/plotter; System: E63/E65/E67/E72/E75/E76. После каждого перемещения запускать
соответствующий test filter, не редактируя вычисления одновременно.

- [ ] **Step 4: Выделить E50 parser/formatter**

Parser принимает входные слова/символы и возвращает parse result; formatter строит
выходные слова. Floating-point conversion остаётся в существующей математике.

- [ ] **Step 5: Выделить E64 formatters**

Каждый internal formatter отвечает за один формат и получает data/context явно.
`ExtracodeHandler.Print.cs` выбирает formatter, но не содержит его циклы форматирования.

- [ ] **Step 6: Regression verification**

```powershell
dotnet test tests/Besm6.Runtime.Tests/Besm6.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~Extracode|FullyQualifiedName~E50|FullyQualifiedName~E57|FullyQualifiedName~E64|FullyQualifiedName~E65|FullyQualifiedName~E70"
dotnet test Besm6.sln -c Release --no-restore
```

Ожидание: output bytes/text, register effects, memory writes, K/right-half и stop
reasons полностью совпадают; никаких улучшений семантики в этом коммите.

- [ ] **Step 7: Commit**

```powershell
git add src/Besm6.Runtime/Extracodes tests/Besm6.Runtime.Tests
git diff --cached --check
git commit -m "Split runtime extracode subsystems"
```

---

### Task 10: Создать независимый Besm6.Cli executable

**Files:**

- Create: `src/Besm6.Cli/Besm6.Cli.csproj`
- Create: `src/Besm6.Cli/Program.cs`
- Create: `src/Besm6.Cli/CliApplication.cs`
- Move: command files into `src/Besm6.Cli/Commands`
- Delete: legacy `TuiCommand.cs`
- Create: `tests/Besm6.Cli.Tests/Besm6.Cli.Tests.csproj`
- Create/Move: `tests/Besm6.Cli.Tests/CliContractTests.cs`
- Modify: `Besm6.sln`

**Interfaces:**

- Consumes: Runtime and Assembler. It must not reference TUI.
- Produces: executable assembly name `besm6` with commands `run`, `check`, `asm`,
  `disasm`, `help`.

- [ ] **Step 1: Написать CLI process-contract tests**

Проверить real process exit/stdout/stderr: no args → help/0; help → 0; unknown → 1;
missing run arg → 1; instruction limit → documented nonzero; `tui` → unknown command;
asm/disasm round-trip. Использовать опубликованный/собранный dll, не reflection private Main.

- [ ] **Step 2: Создать project и deterministic entry point**

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net8.0</TargetFramework>
  <AssemblyName>besm6</AssemblyName>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

Project references: Runtime и Assembler; Architecture допускается только если CLI
форматирует architecture value напрямую. Processor reference должен приходить через
Runtime, а TUI reference отсутствует.

- [ ] **Step 3: Вынести testable application runner**

```csharp
public static class CliApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error);
}
```

`Program.Main` только вызывает runner с `Console.Out/Error`. Commands получают writers
явно; no-args вызывает тот же HelpCommand и возвращает 0.

- [ ] **Step 4: Перенести команды и удалить TUI command**

Сохранить options/exit codes current `run/check/asm/disasm`; перевести factory/config
на Runtime API и disassembly на new Assembler API. Help больше не обещает interactive
debugger при пустом input.

- [ ] **Step 5: Verify dependencies and behavior**

```powershell
dotnet test tests/Besm6.Cli.Tests/Besm6.Cli.Tests.csproj -c Release
dotnet run --project src/Besm6.Cli/Besm6.Cli.csproj --
dotnet run --project src/Besm6.Cli/Besm6.Cli.csproj -- tui
dotnet test Besm6.sln -c Release --no-restore
```

- [ ] **Step 6: Commit**

```powershell
git add Besm6.sln src/Besm6.Cli src/besm6.net/Cli tests/Besm6.Cli.Tests
git diff --cached --check
git commit -m "Create standalone BESM-6 CLI"
```

---

### Task 11: Создать независимый Besm6.Tui executable и разделить UI

**Files:**

- Create: `src/Besm6.Tui/Besm6.Tui.csproj`
- Create: `src/Besm6.Tui/Program.cs`
- Create: `src/Besm6.Tui/TuiApplication.cs`
- Move/Modify: `src/besm6.net/Tui/TuiApp.cs` → `src/Besm6.Tui/TuiApp.cs`
- Create: `src/Besm6.Tui/TuiController.cs`
- Create: `src/Besm6.Tui/TuiCommandParser.cs`
- Create: `src/Besm6.Tui/TuiRenderer.cs`
- Create: `src/Besm6.Tui/TuiSessionState.cs`
- Create: `tests/Besm6.Tui.Tests/Besm6.Tui.Tests.csproj`
- Create: parser/controller/renderer/application tests
- Modify: `Besm6.sln`

**Interfaces:**

- Consumes: Runtime and Assembler. It must not reference CLI.
- Produces: executable assembly name `besm6-tui`; optional `.dub`, `--config path`,
  `--help`; interactive run/step/reset/memory/assembler/disassembler.

- [ ] **Step 1: Написать failing parser/renderer/help tests**

Проверить parse для `run`, `step`, `reset`, memory read/write, asm/disasm, invalid
command; renderer snapshot содержит A/Y/R/M/C/K и ANSI panel; renderer не открывает
files и не выполняет CPU step; `--help` не создаёт terminal loop.

- [ ] **Step 2: Создать executable project без Spectre.Console**

Project references: Runtime и Assembler; AssemblyName `besm6-tui`; никаких package
references сверх уже используемых test packages.

- [ ] **Step 3: Выделить session state и command parser**

`TuiSessionState` хранит running/status/current address/instruction count; parser
возвращает typed command и не обращается к Runtime/Console.

- [ ] **Step 4: Выделить controller**

Controller выполняет команды через MachineCore/DubnaLoader/assembler, обновляет
session state и возвращает status/result. ANSI strings и drawing logic отсутствуют.

- [ ] **Step 5: Выделить pure renderer**

Renderer получает read-only session/machine snapshot и возвращает строку панели.
File/Directory/Console calls запрещены; существующая ANSI layout сохраняется snapshot-тестом.

- [ ] **Step 6: Оставить в TuiApp только loop**

Loop читает line/key, передаёт parser/controller, вызывает renderer. `TuiApplication`
разбирает `--config`, optional `.dub`, `--help`; оба frontend используют один
`JsonRuntimeOptionsLoader`.

- [ ] **Step 7: Verify and commit**

```powershell
dotnet test tests/Besm6.Tui.Tests/Besm6.Tui.Tests.csproj -c Release
dotnet run --project src/Besm6.Tui/Besm6.Tui.csproj -- --help
dotnet test Besm6.sln -c Release --no-restore
git add Besm6.sln src/Besm6.Tui src/besm6.net/Tui tests/Besm6.Tui.Tests
git diff --cached --check
git commit -m "Create standalone BESM-6 TUI"
```

---

### Task 12: Разнести все tests и удалить монолитный executable

**Files:**

- Move: architecture tests → `tests/Besm6.Architecture.Tests`
- Move: CPU/ALU/memory tests → `tests/Besm6.Processor.Tests`
- Move: assembler tests → `tests/Besm6.Assembler.Tests`
- Move: loader/device/codec/clock/extracode tests → `tests/Besm6.Runtime.Tests`
- Move: CLI tests → `tests/Besm6.Cli.Tests`
- Move: TUI tests → `tests/Besm6.Tui.Tests`
- Create: `tests/Besm6.Integration.Tests/Besm6.Integration.Tests.csproj`
- Move: CERNlib, diagnostics, bootstrap and end-to-end `.dub` tests/assets → Integration
- Delete: `src/besm6.net/tests/Besm6.Tests`
- Delete: `src/besm6.net/besm6.csproj`, `Program.cs`, obsolete solution
- Delete: redundant `Besm6.slnx` if it still exists; `Besm6.sln` is the canonical solution
- Modify: `Besm6.sln`

**Interfaces:**

- Consumes: все шесть production assemblies.
- Produces: семь test projects с ownership по assembly; Integration может ссылаться
  на production assemblies, но production никогда не ссылается на tests.

- [ ] **Step 1: Составить file-to-project manifest**

Перед `git mv` записать список всех current `.cs` tests и назначение. Особые случаи:
`SystemBusMemoryTests` → Runtime; `ConfigTests` → Runtime; `BootstrapDubnaTests`,
`IntegrationTests`, `DiagMainTest`, `Diagnostics`, `CernLib*` → Integration.

- [ ] **Step 2: Перенести тесты через `git mv` без удаления methods/data rows**

После каждого project запускать его отдельно. Shared CERN helpers/manifest/raw assets
остаются рядом с Integration test assembly и копируются в output через csproj.

- [ ] **Step 3: Обновить AssemblyBoundaryTests для всех production assemblies**

Построить directed graph из `GetReferencedAssemblies()` и assert:

```text
Architecture -> none BESM-6
Processor    -> Architecture
Assembler    -> Architecture
Runtime      -> Architecture, Processor, Assembler
Cli          -> Runtime (+ Assembler/Architecture только при прямом использовании)
Tui          -> Runtime (+ Assembler/Architecture только при прямом использовании)
```

Явно запретить Runtime→Cli/Tui, Cli↔Tui и любой цикл DFS-проверкой.

- [ ] **Step 4: Сверить test inventory**

```powershell
dotnet test Besm6.sln -c Release --logger "trx;LogFileName=architecture-cleanup-final.trx"
```

Сравнить discovered fully-qualified names/data rows с baseline Task 1. Ожидание:
506 сценариев сохранены и четыре прежних skip имеют те же причины.

- [ ] **Step 5: Удалить монолит только после parity**

Удалить `src/besm6.net` production/test project и `src/besm6.net/besm6.net.sln`;
не удалять raw fixtures до их подтверждённого переноса. Удалить temporary entries из
верхнего solution и проверить, что в solution ровно шесть production и семь test projects.

- [ ] **Step 6: Verify and commit**

```powershell
dotnet restore Besm6.sln
dotnet build Besm6.sln -c Release --no-restore
dotnet test Besm6.sln -c Release --no-build --no-restore
git add Besm6.sln src/Besm6.Architecture src/Besm6.Processor src/Besm6.Assembler src/Besm6.Runtime src/Besm6.Cli src/Besm6.Tui tests src/besm6.net
git diff --cached --check
git commit -m "Reorganize BESM-6 test suites"
```

---

### Task 13: Обновить CI, tracked tools, docs и автоматизировать shape checks

**Files:**

- Modify: `.github/workflows/ci.yml`
- Modify: `tools/check_warnings.py`
- Modify: `tools/run_all_examples.py`
- Modify: `tools/generate_cern_failure_trace.ps1`
- Create: `tools/check_architecture.py`
- Create: `tools/tests/test_check_architecture.py`
- Modify: `docs/runtime-assets.md`, `tapes/README.md`
- Modify: live book/practicum links and tracked reports/tests with old paths
- Modify: historical plans only where commands would otherwise be copied and fail;
  keep an explicit note that their original layout was pre-decomposition

**Interfaces:**

- Consumes: финальный `Besm6.sln`, Cli/Tui projects and test layout.
- Produces: CI and scripts with no operational references to `src/besm6.net` or
  `besm6.net.sln`; architecture checker без third-party Python dependencies.

- [ ] **Step 1: Написать failing architecture-checker tests**

Проверить project-reference graph, запрещённые reverse edges, production class
line budget и правило одного public top-level type на production file. Data-only
`EncodingTables.cs` — единственное документированное line-budget exception.

- [ ] **Step 2: Реализовать checker стандартной библиотекой Python**

Parser читает csproj XML, строит graph, обнаруживает cycles и scans C# declarations.
CLI возвращает 0 при успехе, 1 и список exact files/edges при нарушении:

```powershell
py -3 tools/check_architecture.py --solution Besm6.sln
```

- [ ] **Step 3: Обновить CI**

CI выполняет restore/build/test верхнего solution, Python tests/checker, publish CLI
и TUI, edu tests. Test-result upload указывает новые каталоги `tests/**/TestResults`.

- [ ] **Step 4: Обновить tracked tools**

Defaults становятся `Besm6.sln`, `src/Besm6.Cli/Besm6.Cli.csproj` и
`tests/Besm6.Integration.Tests/Besm6.Integration.Tests.csproj`. Python trace tool
сохраняет поддержку current и legacy TSV headers.

- [ ] **Step 5: Обновить документацию и ссылки**

Исправить ссылки на Word48/Processor/MachineCore, runtime assets, build/test commands
и directory overview. Не менять пользовательские untracked files в `examples/`.

- [ ] **Step 6: Проверить отсутствие старых operational paths**

```powershell
git grep -n -E "src/besm6\.net|besm6\.net\.sln"
```

Допустимы только явно помеченные исторические пояснения; ни одна executable command,
CI path или live source link не содержит старого layout.

- [ ] **Step 7: Verify and commit**

```powershell
py -3 -m unittest discover -s tools/tests
py -3 tools/check_architecture.py --solution Besm6.sln
dotnet test Besm6.sln -c Release
git add .github/workflows/ci.yml tools docs book tapes reports tests/golden plans/SuperPlan.md plans/refactor.md
git diff --cached --check
git commit -m "Update tooling for decomposed BESM-6 solution"
```

Перед commit проверить `git diff --cached --name-only` и убрать любые пользовательские
файлы, не перечисленные в этой задаче.

---

### Task 14: Финальная Release/publish проверка и handoff

**Files:**

- Verify only: all production/test projects, published outputs and smoke fixtures
- Modify only if verification exposes a defect; defect fix получает отдельный focused commit

**Interfaces:**

- Consumes: целевое решение после Tasks 1–13.
- Produces: доказательство критериев приёмки для одного PR; не создаёт новый feature scope.

- [ ] **Step 1: Убедиться, что рабочая область не смешана с пользовательскими файлами**

```powershell
git status --short --branch
git diff --check
git log --oneline --decorate origin/main..HEAD
```

- [ ] **Step 2: Выполнить канонический Release pipeline**

```powershell
dotnet restore Besm6.sln
dotnet build Besm6.sln -c Release --no-restore
dotnet test Besm6.sln -c Release --no-build --no-restore
py -3 -m unittest discover -s tools/tests
py -3 tools/check_architecture.py --solution Besm6.sln
dotnet test src/besm-edu/Besm6.EduCpu.Tests/Besm6.EduCpu.Tests.csproj -c Release
```

- [ ] **Step 3: Publish оба executable в изолированные каталоги**

```powershell
dotnet publish src/Besm6.Cli/Besm6.Cli.csproj -c Release --no-build --output artifacts/publish/besm6
dotnet publish src/Besm6.Tui/Besm6.Tui.csproj -c Release --no-build --output artifacts/publish/besm6-tui
```

Проверить, что оба output содержат нужные runtime assets/config, CLI не содержит
`Besm6.Tui.dll`, TUI не содержит `Besm6.Cli.dll`, Spectre.Console отсутствует.

- [ ] **Step 4: Выполнить smoke tests опубликованных binaries**

```powershell
dotnet artifacts/publish/besm6/besm6.dll
dotnet artifacts/publish/besm6/besm6.dll help
dotnet artifacts/publish/besm6/besm6.dll asm "xta 10, atx 20"
dotnet artifacts/publish/besm6/besm6.dll disasm 0010000000000000
dotnet artifacts/publish/besm6-tui/besm6-tui.dll --help
```

Запустить один raw `.dub`, один assembler `.dub`, CERNlib matrix и diagnostic suite
через Integration tests; сравнить canonical trace с baseline через `diff_trace.py`.

- [ ] **Step 5: Проверить критерии структуры вручную**

```powershell
Get-ChildItem src -Directory | Select-Object Name
Get-ChildItem tests -Directory | Select-Object Name
rg -n "Environment\.|Console\.|File\.|Directory\." src/Besm6.Processor
rg -n "Spectre\.Console|ProjectReference.*Besm6\.(Cli|Tui)" src -g "*.csproj" -g "*.cs"
```

Ожидание: шесть production projects, семь test projects, пустые запрещённые scans,
506 .NET scenarios и четыре прежних skip.

- [ ] **Step 6: Review commits and open one PR**

Не squash последовательные проверяемые commits до review. PR описывает dependency
graph, obsolete compatibility window, test inventory, trace parity и publish smoke.
Пользовательские untracked/modified files остаются вне PR.
