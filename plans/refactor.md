# BESM-6 Architecture Cleanup — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Выделить архитектурные типы и процессор в независимые сборки, сохранив поведение монолитного приложения.

**Architecture:** Первый промежуточный этап создаёт `Besm6.Architecture` и `Besm6.Processor`. Assembler, Runtime, CLI и TUI пока остаются в существующем executable и переводятся на новые сборки через project references.

**Tech Stack:** .NET 8, C#, MSTest.

**Plan file:** `docs/superpowers/plans/2026-09-08-architecture-cleanup-phase-1.md`

## Статус (2026-09-08)

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

## Ограничения

- Не менять семантику инструкций, экстракодов и загрузчика.
- Текущие переименования регистров и опкодов входят в ту же feature-ветку.
- Все 506 существующих .NET-тестов должны сохраниться.
- `src/besm-edu` не изменять.
- Пользовательские untracked-файлы `examples/` не добавлять.
- Каждый этап завершать отдельным коммитом и зелёной сборкой.

---

## Task 1: Зафиксировать текущую базу

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

## Task 2: Добавить проверки архитектурных границ

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

## Task 3: Создать Besm6.Architecture

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

## Task 4: Создать headless Besm6.Processor

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

### Публичные интерфейсы

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

### Внутреннее разделение

`ProcessorState` владеет:

- `A`, `Y`, `R`, `M`, `C`, `K`;
- флагом правого полуслова;
- stack correction;
- признаками intercept и debug watch.

`Processor` остаётся фасадом и предоставляет существующие публичные регистровые методы.

`Alu` и `InstructionExecutor` получают `ProcessorState` через конструктор. Прямой доступ к полям `Processor` удаляется.

### Удаление host-зависимостей

Из Processor убрать:

- создание trace-файлов;
- чтение environment variables;
- `Console`;
- ссылки на `MachineCore`, `DubnaLoader`, устройства и ленты.

Формирование trace records остаётся в Processor. Решение о записи в TSV принимает текущий host.

### Тесты для переноса

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

## Task 5: Подключить новые сборки к существующему приложению

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

## Task 6: Проверить промежуточную архитектуру

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

## Критерий готовности промежуточного этапа

- Architecture и Processor физически находятся в отдельных DLL.
- Processor запускается в unit-тесте без MachineCore и Loader.
- Между новыми сборками нет циклических ссылок.
- Монолитный executable сохраняет текущие команды и поведение.
- Все существующие тесты и trace-сравнения проходят.
- Assembler, Runtime, extracode partials и отдельные CLI/TUI ещё не реорганизуются.

## Следующие этапы

После этого checkpoint выполняются отдельными планами:

1. `Besm6.Assembler`: `MadlenAssembler`, `BemshAssembler`, auto facade.
2. `Besm6.Runtime`: MachineCore, Loader, устройства, кодировки и grouped partial extracodes.
3. `Besm6.Cli` и `Besm6.Tui`: отдельные executable и финальное удаление монолита.
