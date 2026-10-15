# Профилирование Dhrystone FOREX в режиме virtual

Дата: **15 октября 2026**. Реальные измерения и EventPipe-профили; оптимизации не выполнялись.

## Основной результат

Основные затраты находятся в обычном пути исполнения гостевых команд: `InstructionExecutor.ExecuteFetchedInstruction`, `InstructionExecutor.Execute` и `ExecutionLoop.RunCore`. Вместе они дают **82.74% собственных выборок** основного потока на 50000 итераций и **88.97% прироста собственных выборок** относительно 5000 итераций. Это включает работу заинлайненных методов и не является доказательством стоимости одной конкретной строки.

Найден конкретный ограничитель быстрого пути: MONSYS/гостевая среда оставляет активным **memory watchpoint на запись по адресу 22272 (053400 восьмеричное)**. Условие `CanExecuteUnobservedBlock` запрещает быстрый блок при любом активном memory watchpoint. Профиль показывает обычные `Execute`/`ExecuteFetchedInstruction`, а независимая диагностическая проба подтверждает состояние watchpoint на обеих границах CTIME и после STOP. Отключение watchpoint не проверялось и не предлагается как безопасная оптимизация.

GC не является основным ограничителем этого сценария: в отдельном allocation-прогоне зарегистрирована одна GC поколения 2 и менее 1 мс суммарных наблюдавшихся приостановок исполнения. Изменение арифметики или устранение выделений памяти не выглядит первым шагом по этим данным.

## Воспроизводимость и условия

- Коммит: `7df33e46eda6475af1784b35d1110bb66de501ee`; приложение и его публичные API не изменялись. Пользовательские изменения статьи и изображений сохранены.
- Release, target `net8.0`, runtime **Microsoft.NETCore.App 8.0.26**, SDK **10.0.202**; Windows **10.0.17763**, Intel(R) Core(TM) i5-4570TE CPU @ 2.70GHz, 2 ядра / 4 логических процессора.
- `dotnet-trace` **10.0.750501+ad724c4b85e7ae775e42f393c6e95a7557a57954**, установлен локально; стандартные JIT/Tiered PGO и настройки GC не переопределялись.
- Исходное задание — `examples/dhrystone-forex.dub`, 5000 итераций. Временная копия на 50000 итераций отличается только `DATA NUMBER/.../`.
- Память Dubna и стандартная конфигурация; фиксированная дата, явный путь к лентам. Режим `virtual`, гостевые часы включены, статистика включена, instruction limit 200000000. Опкодный `--profile`, trace и register hooks не включались.
- Первичный замер: по одному предварительному запуску каждого размера, затем по пять запусков без профилировщика с чередованием размеров. Каждый CLI запускается отдельным процессом: предварительный запуск прогревает файловые кэши, но не JIT следующего процесса.
- Затем по три независимых профиля стеков каждого размера и отдельный `gc-verbose` прогон на 50000 итераций. Время профилируемых запусков не используется для оценки производительности.
- Дополнительно пять длинных запусков без `--stats`; они выполнены отдельной серией, поэтому разницу следует считать диагностической оценкой, а не доказанным эффектом оптимизации.

```powershell
dotnet build src/Besm6.Cli/Besm6.Cli.csproj -c Release --no-restore
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-forex.dub --speed virtual --guest-clock --stats --no-wall-clock --limit 200000000
```

Полные параметры, CPU, хеши DLL и состояние дерева на момент измерения сохранены в `environment.json` рядом с трассами. Сборка CLI прошла без ошибок и предупреждений.

## Измерения без профилировщика

CPU time — сумма времени CPU всех потоков дочернего процесса, wall time — от запуска процесса до его завершения. `ExecutionStatistics.ElapsedSeconds` измеряет выполнение гостевого задания, включая компиляцию FOREX, но не весь жизненный цикл CLI. CTIME охватывает только участок Dhrystone, заданный самой программой.

| Итерации / статистика | Прогоны | Полный wall time: медиана [min–max], с | CPU time: медиана [min–max], с | CTIME: медиана [min–max], с | DMIPS: медиана |
|---|---:|---:|---:|---:|---:|
| 5000 / stats | 5 | 3.528 [3.188–3.563] | 3.359 [3.234–3.609] | 0.600 [0.600–0.840] | 4.742933 |
| 50000 / stats | 5 | 9.802 [9.367–14.394] | 9.453 [9.188–12.422] | 6.700 [6.160–10.840] | 4.247403 |
| 50000 / no-stats | 5 | 8.805 [8.228–9.453] | 8.438 [8.297–8.875] | 5.620 [5.480–6.640] | 5.063630 |

Медианы интервала `ExecutionStatistics.ElapsedSeconds`: **3.137 с** и **9.430 с** соответственно. Остальное время полного процесса включает создание машины, подготовку запуска и завершающую работу CLI.

Дополнительные 45000 итераций: **6.275 с wall**, **6.094 с CPU**, **6.100 с CTIME** по разности медиан. Приближённая стоимость итерации — **139.44 мкс wall / 135.42 мкс CPU**. Это разностная оценка, а не точные временные метки тела цикла.

В коротком запуске CTIME составляет около **17.0%** полного wall time, в длинном — **68.4%**. Линейная экстраполяция постоянной части даёт около **2.831 с**; туда входят startup, MONSYS/FOREX, инициализация гостевой программы, вывод и завершение. Это не отдельное измерение времени компилятора.

Без `--stats` медиана CPU длинного запуска ниже на **10.7%**, wall — на **10.2%**. Причина по коду: в virtual без статистики `countCycles` становится false, и сессия учёта команд не создаётся. Точные доли Begin/Complete по этому sampling-профилю не разделены из-за инлайнинга; сравнение серий чувствительно к изменению нагрузки хоста.

Полные пять значений каждого варианта находятся в `timings.json`; предварительные запуски отмечены `warmup=true` и исключены из таблицы. Разброс wall time существенен, поэтому проценты улучшения по одному запуску здесь не выводятся.

## Горячие методы .NET

Источник — `dotnet-sampled-thread-time,dotnet-common`, три трассы каждого размера. Выбран поток с исполнением `ExecutionLoop.RunCore`; веса рассчитываются по evented Speedscope в миллисекундах. Таблицы показывают суммарный вес метода во всех трёх трассах / суммарный наблюдавшийся вес потока. Диапазон — доли в отдельных трассах.

**Это доли выборок стеков потока, не аппаратные проценты CPU и не количество вызовов.** Own/self включает заинлайненный код. Inclusive включает потомков; его строки нельзя складывать. `CPU_TIME` — техническая рамка, исключена; `UNMANAGED_CODE_TIME` сохранён отдельной категорией, а не приписан managed-методу.

Наблюдавшийся вес основного потока в среднем: **3372.185 мс** для 5000 и **10700.444 мс** для 50000 итераций.

### 20 методов с наибольшим собственным временем, 50000 итераций

| № | Метод | Own, % | Диапазон по трём трассам, % | Средний вес, мс |
|---:|---|---:|---:|---:|
| 1 | [InstructionExecutor.ExecuteFetchedInstruction](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L116) | 40.42 | 39.95–41.01 | 4325.4 |
| 2 | [InstructionExecutor.Execute](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L63) | 27.42 | 26.49–28.72 | 2934.3 |
| 3 | [ExecutionLoop.RunCore](../../src/Besm6.Runtime/Execution/ExecutionLoop.cs#L160) | 14.89 | 14.45–15.62 | 1593.7 |
| 4 | [FunctionalProcessorSimulation.ExecuteBlock](../../src/Besm6.Runtime/Execution/FunctionalProcessorSimulation.cs#L144) | 3.20 | 3.09–3.40 | 342.2 |
| 5 | 0&) | 3.15 | 3.01–3.33 | 336.8 |
| 6 | [IndexInstructionExecutor.Execute](../../src/Besm6.Processor/Execution/IndexInstructionExecutor.cs#L17) | 1.01 | 0.95–1.09 | 108.2 |
| 7 | [ControlInstructionExecutor.ExecuteOther](../../src/Besm6.Processor/Execution/ControlInstructionExecutor.cs#L65) | 0.70 | 0.52–0.87 | 75.3 |
| 8 | [AdditiveOperations.Add](../../src/Besm6.Processor/Arithmetic/AdditiveOperations.cs#L27) | 0.35 | 0.25–0.42 | 37.4 |
| 9 | [InstructionExecutor.BeginFetchedInstruction](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L139) | 0.34 | 0.19–0.61 | 36.3 |
| 10 | [MultiplicativeOperations.Multiply](../../src/Besm6.Processor/Arithmetic/MultiplicativeOperations.cs#L26) | 0.31 | 0.21–0.52 | 33.4 |
| 11 | [InstructionExecutor.ExecuteCore](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L80) | 0.26 | 0.13–0.45 | 27.3 |
| 12 | 0&) | 0.23 | 0.22–0.26 | 25.1 |
| 13 | [InstructionExecutor.FetchInstruction](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L87) | 0.20 | 0.08–0.26 | 21.2 |
| 14 | [InstructionExecutor.Dispatch](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L174) | 0.17 | 0.13–0.27 | 18.4 |
| 15 | [MemoryInstructionExecutor.LoadWord](../../src/Besm6.Processor/Execution/MemoryInstructionExecutor.cs#L251) | 0.16 | 0.11–0.21 | 16.9 |
| 16 | [NormalizationAndRounding.NormalizeAndRound](../../src/Besm6.Processor/Arithmetic/NormalizationAndRounding.cs#L36) | 0.15 | 0.11–0.18 | 16.2 |
| 17 | [InstructionExecutor.ExecuteUnobservedBlock](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L194) | 0.15 | 0.12–0.22 | 15.9 |
| 18 | [ControlInstructionExecutor.SetEffectiveAddress](../../src/Besm6.Processor/Execution/ControlInstructionExecutor.cs#L143) | 0.15 | 0.08–0.21 | 15.6 |
| 19 | [MemoryInstructionExecutor.PrepareStack](../../src/Besm6.Processor/Execution/MemoryInstructionExecutor.cs#L259) | 0.13 | 0.07–0.24 | 13.4 |
| 20 | System.IO.TextWriter+SyncTextWriter.Write | 0.13 | 0.09–0.16 | 13.4 |

Отдельно `UNMANAGED_CODE_TIME`: **4.22%**. Без native/ETW-профиля нельзя корректно распределить этот вес между JIT, runtime и системными функциями.

### 20 методов с наибольшим включённым временем, 50000 итераций

| № | Метод | Inclusive, % | Own того же метода, % |
|---:|---|---:|---:|
| 1 | [Program.Main](../../src/Besm6.Cli/Program.cs#L1) | 99.43 | 0.00 |
| 2 | [CliApplication.Run](../../src/Besm6.Cli/CliApplication.cs#L1) | 99.42 | 0.00 |
| 3 | [CliApplication.Dispatch](../../src/Besm6.Cli/CliApplication.cs#L1) | 99.31 | 0.00 |
| 4 | [RunCommand.Execute](../../src/Besm6.Cli/RunCommand.cs#L1) | 99.26 | 0.00 |
| 5 | [DubnaLoader.RunScript](../../src/Besm6.Runtime/Loading/DubnaLoader.cs#L118) | 96.84 | 0.00 |
| 6 | [DubnaLoader.RunJob](../../src/Besm6.Runtime/Loading/DubnaLoader.cs#L160) | 96.82 | 0.00 |
| 7 | [DubnaLoader.BootAndRun](../../src/Besm6.Runtime/Loading/DubnaLoader.cs#L192) | 96.77 | 0.00 |
| 8 | [ExecutionLoop.Run](../../src/Besm6.Runtime/Execution/ExecutionLoop.cs#L105) | 96.42 | 0.00 |
| 9 | [ExecutionLoop.RunCore](../../src/Besm6.Runtime/Execution/ExecutionLoop.cs#L160) | 96.35 | 14.89 |
| 10 | [FunctionalProcessorSimulation.ExecuteBlock](../../src/Besm6.Runtime/Execution/FunctionalProcessorSimulation.cs#L144) | 81.35 | 3.20 |
| 11 | [InstructionExecutor.Execute](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L63) | 76.94 | 27.42 |
| 12 | [InstructionExecutor.ExecuteFetchedInstruction](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L116) | 48.87 | 40.42 |
| 13 | [MachineCore.ExecuteBlock](../../src/Besm6.Runtime/Machine/MachineCore.cs#L278) | 5.96 | 0.00 |
| 14 | [FunctionalProcessorSimulation.Step](../../src/Besm6.Runtime/Execution/FunctionalProcessorSimulation.cs#L101) | 4.40 | 0.11 |
| 15 | 0&) | 3.91 | 3.15 |
| 16 | [Processor.Step](../../src/Besm6.Processor/Cpu/Processor.cs#L275) | 2.63 | 0.01 |
| 17 | [InstructionExecutor.ExecuteCore](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L80) | 2.51 | 0.26 |
| 18 | [InstructionExecutor.Dispatch](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L174) | 1.06 | 0.17 |
| 19 | [IndexInstructionExecutor.Execute](../../src/Besm6.Processor/Execution/IndexInstructionExecutor.cs#L17) | 1.01 | 1.01 |
| 20 | [RunOptions.LoadConfiguration](../../src/Besm6.Cli/RunOptions.cs#L166) | 0.79 | 0.00 |

Высокий inclusive у `Main`/`RunScript` показывает владельца всей работы, а не самостоятельное узкое место этих обёрток.

### Разностный профиль: 50000 минус 5000

Для каждого метода вычтено среднее собственное время коротких трасс из среднего длинных. Доля рассчитана относительно прироста общего наблюдавшегося времени потока. Это приближение стоимости дополнительных итераций: JIT, дискретность выборок, изменение нагрузки и поведение компилятора не вычитаются идеально.

| Метод / категория | Прирост own, мс | Доля прироста, % |
|---|---:|---:|
| [InstructionExecutor.ExecuteFetchedInstruction](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L116) | 3195.9 | 43.61 |
| [InstructionExecutor.Execute](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L63) | 2170.5 | 29.62 |
| [ExecutionLoop.RunCore](../../src/Besm6.Runtime/Execution/ExecutionLoop.cs#L160) | 1153.2 | 15.74 |
| 0&) | 242.8 | 3.31 |
| [FunctionalProcessorSimulation.ExecuteBlock](../../src/Besm6.Runtime/Execution/FunctionalProcessorSimulation.cs#L144) | 221.8 | 3.03 |
| [IndexInstructionExecutor.Execute](../../src/Besm6.Processor/Execution/IndexInstructionExecutor.cs#L17) | 90.5 | 1.23 |
| [ControlInstructionExecutor.ExecuteOther](../../src/Besm6.Processor/Execution/ControlInstructionExecutor.cs#L65) | 58.7 | 0.80 |
| UNMANAGED_CODE_TIME | 57.2 | 0.78 |
| [MultiplicativeOperations.Multiply](../../src/Besm6.Processor/Arithmetic/MultiplicativeOperations.cs#L26) | 30.0 | 0.41 |
| [AdditiveOperations.Add](../../src/Besm6.Processor/Arithmetic/AdditiveOperations.cs#L27) | 24.6 | 0.34 |

Эти данные подтверждают рост затрат обычного интерпретатора вместе с числом итераций, а не только его участие в загрузке/компиляции. Отрицательные разности не обрезались: они сохранены в `profile-summary.json` и отражают шум/постоянные затраты.

## Цепочка исполнения и ограничитель быстрого пути

```text
ExecutionLoop.RunCore
  → FunctionalProcessorSimulation.ExecuteBlock
    → обычный Step → InstructionExecutor.Execute
      → ExecuteCore / FetchInstruction
      → ExecuteFetchedInstruction → Dispatch → обработчики → FinalizeInstruction
```

В профиле основная цепочка иногда не содержит `Step`/`ExecuteCore`: JIT заинлайнил промежуточные вызовы. Это не другая семантика исполнения.

- [Проверка допуска быстрого пути](../../src/Besm6.Processor/Cpu/Processor.cs#L283): `!DebugMemoryArmed` обязательна даже при редком/несрабатывающем в данном участке watchpoint.
- [Выбор между быстрым блоком и Step](../../src/Besm6.Runtime/Execution/FunctionalProcessorSimulation.cs#L154): при запрете быстрых блоков остаётся обычное исполнение каждой команды.
- [Обычный интерпретатор](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L63) выполняет общий fetch/dispatch/finalize и проверки; существующий специализированный цикл находится [здесь](../../src/Besm6.Processor/Execution/InstructionExecutor.cs#L208).
- [Установка watchpoint экстракодом E67](../../src/Besm6.Runtime/Extracodes/HostedSystemService.Operations.cs#L181), [проверка и срабатывание](../../src/Besm6.Processor/Diagnostics/ProcessorDebugWatch.cs#L83), [проверки памяти](../../src/Besm6.Processor/Memory/ProcessorMemoryAccess.cs#L60).

Дополнительная проба — отдельный процесс без sampling. Через reflection только в диагностическом runner прочитаны закрытые признаки, а обёртка делегата гостевых часов зафиксировала их при CTIME. Runtime и его видимость типов не менялись; поинструкционные hooks не подключались. Эта проба не входит в измерения производительности или профили. Четыре запроса — commercial и CPU time для двух вызовов CTIME.

| Признак | Наблюдение на всех четырёх запросах CTIME |
|---|---|
| `DebugMemoryArmed` | true |
| Адрес / режим watchpoint | 22272 = 053400 / 1, запись |
| `DebugFetchArmed` / `DebugWatchSuppressed` | false / false |
| Plain CoreMemory | true |
| Instruction trace hook / trace controller | отсутствует / отключён |
| `CanExecuteUnobservedBlock` | false |

Внутри вызова CTIME `InstructionAccounting.HasPending=true` закономерно: текущий экстракод ещё не завершён. Поэтому это не доказательство зависшей сессии учёта. После STOP учёт снят, а memory watchpoint остаётся активен и продолжает запрещать быстрый путь. Контрольные значения в диагностическом прогоне также подтверждены.

Причина запрета подтверждена; возможное ускорение после изменения допуска **не измерено**. Просто удалить условие `DebugMemoryArmed` нельзя: быстрый цикл должен корректно обрабатывать срабатывание watchpoint, перенос управления, незавершённую команду и счётчики.

## Аллокации и GC

Отдельная трасса `gc-verbose`, 50000 итераций: **8.17 МиБ** веса AllocationTick, **30** событий. Это sampled/взвешенная оценка всего процесса под сборщиком событий, не точный счётчик всех выделений и не только тело Dhrystone.

| Тип | Вес AllocationTick, байт | Событий |
|---|---:|---:|
| `System.Byte[]` | 5737832 | 14 |
| `CachedInstruction[]` | 1048576 | 1 |
| `System.Char[]` | 314912 | 3 |
| `Besm6.Architecture.Numerics.Word48[]` | 262168 | 1 |
| `EventMetadata[]` | 235328 | 2 |
| `System.String` | 217632 | 2 |
| `System.Reflection.RuntimeParameterInfo` | 213768 | 2 |
| `RuntimeTypeCache` | 109384 | 1 |
| `System.Type[]` | 107080 | 1 |
| `System.Text.Unicode.UnicodeRange` | 106344 | 1 |

Зарегистрирована **1 GC** (поколение 2, причина AllocLarge), длительность между GC Start/Stop **1.403 мс**. Сумма двух наблюдавшихся SuspendEE/RestartEE-интервалов — **0.783 мс**; длительность GC и stop-the-world не смешиваются. События были около 395 мс после старта трассы.

CLI сообщает **2602608 байт** в каждом из пяти измерений на 5000 и каждом из пяти измерений на 50000 итераций. Выделения в измеряемом потоке/интервале исполнения не растут при увеличении итераций в десять раз. Этот счётчик имеет более узкую область измерения, чем AllocationTick всего процесса, и не включает все startup-выделения.

Главные типы — массивы загрузки/инфраструктуры и массив decode cache. `Byte[]` нельзя отнести к конкретному месту выделения только по таблице типов; для этого нужен allocation stack. Размер кэша сам по себе не делает его приоритетной целью: он создаётся один раз, а не на каждую гостевую команду.

## Рекомендации по приоритету

| Приоритет | Участок / действие | Доказательство и ожидаемый эффект | Риск / проверка |
|---|---|---|---|
| P0 | Исследовать поддержку активного memory watchpoint в специализированном блоке CPU | Подтверждённый запрет быстрого пути; первые три метода дают 88.97% прироста выборок. Потенциал высокий по охвату, численное ускорение пока неизвестно | Высокий риск семантики: срабатывание внутри блока, store/load режимы, перенос K/полуслово, точный completed count и ticks, прерывание команды. Не отключать watchpoint, не ослаблять публичные контракты |
| P1 | Для обычного пути получить JIT disassembly `Execute`/`ExecuteFetchedInstruction` и специализировать доказанную горячую работу | Own 27.42% и 40.42%; проверить повторные обращения к состоянию, инлайнинг, проверки и dispatch. Уменьшение собственных затрат второго метода на 10% даёт лишь около 4% общего выигрыша по Amdahl, а не ускорение в разы | Средний/высокий: сохранить read-only State, роли интерфейсов, self-modifying code, C/ApplyC, исключения и диагностические границы. Не объявлять интерфейсы причиной торможения без disassembly/A-B |
| P2 | Отдельно оптимизировать учёт команд при обязательном `--stats` | Диагностические серии с/без stats показывают разницу CPU; Begin/Complete выполняются в обычном интерпретаторе на каждую команду. Выигрыш требует парного A/B на одном хосте | Средний: STOP, fault/intercept, отменённые инструкции, границы CycleLimit и точная статистика |
| P3 | Для полного короткого запуска отдельно исследовать постоянную часть MONSYS/FOREX и startup | Только около 17% короткого wall time приходится на CTIME; ускорение одного тела цикла не устраняет большую часть задержки короткого запуска | Не считать всё постоянное время компилятором. Перед кэшированием проверять повторное использование образов/состояния и эквивалентность гостевой загрузки |
| Ниже приоритета | Переписывание АУ, GC tuning, уменьшение одноразового decode cache | Собственное время отдельных арифметических методов невелико, паузы GC менее 1 мс, выделения почти не растут с N | Не ухудшать арифметическую точность и производительность других workloads ради неподтверждённой причины |

Следующий шаг после согласования оптимизаций — один ограниченный A/B-эксперимент по P0, затем повторные профили и те же unprofiled измерения. Проверки: несрабатывающий и срабатывающий watchpoint в середине блока, оба полуслова, изменения hooks/памяти и событие планировщика; сравнение обычного и специализированного исполнения по регистрам, памяти, завершённым командам и ticks. Тесты выполнять в конце блока реализации.

## Артефакты и команды профилирования

Все сырые материалы находятся в `tests-run/dhrystone-virtual-profile-20261015/` и исключены из Git:

- `environment.json`, `timings.json` — окружение, хеши сборок, все unprofiled измерения;
- `sampling-{5000,50000}-{1,2,3}.nettrace`, `.speedscope.json`, `*-summary.json` — шесть профилей и таблицы всех методов/стеков;
- `profile-summary.json` — объединённые собственные/включённые и разностные веса;
- `allocations-50000.nettrace`, `allocations-50000-summary.json` — типы аллокаций и события GC;
- `gate-probe.json`, `probe/` — независимая проверка условий быстрого пути;
- `reader/` — временный .NET-анализатор; синтетическая проверка весов стека и рекурсивного inclusive прошла;
- `baseline-*.log`, `sampling-*.log`, `allocations-50000.log` — вывод CLI с контрольными результатами.

Временные PowerShell-сценарии в `tests-run/`: `dhrystone-profile-measure.ps1`, `dhrystone-profile-collect.ps1`, `dhrystone-profile-analyze.ps1`, `dhrystone-profile-report.ps1`. Для нового независимого измерения следует выбрать свежий каталог результатов, сохранив параметры и версии инструментов.

```powershell
# Воспроизведение на примере копии задания на 50000 итераций:
tests-run/dhrystone-profile-tools/dotnet-trace.exe collect --profile dotnet-sampled-thread-time,dotnet-common --buffersize 64 --output tests-run/dh50000.nettrace --show-child-io -- dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run tests-run/dhrystone-virtual-profile-20261015/dh50000.dub --config tests-run/dhrystone-virtual-profile-20261015/config.json --speed virtual --guest-clock --stats --no-wall-clock --limit 200000000
tests-run/dhrystone-profile-tools/dotnet-trace.exe convert tests-run/dh50000.nettrace --format Speedscope
# Для аллокаций повторить collect с --profile gc-verbose в отдельный файл.
```

Методика инструмента: [официальная документация dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace). В установленной версии `cpu-sampling` относится к Linux collect-linux; использованный Windows EventPipe-профиль называется `dotnet-sampled-thread-time` и не измеряет аппаратное CPU-время методов.

## Проверка и ограничения

- Во всех 12 первичных unprofiled запусках, пяти дополнительных без stats, шести sampling-прогонах и allocation-прогоне подтверждены все 18 числовых и 4 строковых контрольных значения. Предварительные запуски входят в эти 12 и не входят в медианы. Дополнительная диагностическая проба также прошла контроль.
- Таймерные слова памяти и число команд форматирования времени могут различаться в virtual; полного совпадения memory SHA-256 между такими запусками не требуется.
- Отдельный процесс на каждый запуск сохраняет реальную стоимость startup/JIT CLI. Профиль не является измерением полностью прогретого непрерывного эмулятора.
- Нет точных меток CTIME в трассе: разностные веса являются оценкой, а не изолированным профилем тела цикла. Inlining и неполные native-стеки ограничивают точность атрибуции строкам исходника.
- Закрытые признаки прочитаны reflection только отдельным runner; это диагностическая зависимость от данного коммита, а не предлагаемое расширение API или friend-доступа.
- Полный набор тестов приложения не запускался: приложение не изменялось. Проверены сборка CLI, анализатор и фактическое выполнение задания. Оптимизации и коммиты не выполнялись.

## Продолжение: реализация оптимизации

После этого исходного профилирования добавлена opt-in настройка
`memoryWatchpointBatching` и сохранены проверки watchpoints.
[Изменения, A/B-замеры и регрессионная проверка](dhrystone-memory-watchpoint-batching.md).
