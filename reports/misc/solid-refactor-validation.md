# Проверка SOLID-рефакторинга

Дата: 14 октября 2026. База сравнения: `a961912`.
Изменения касаются Architecture, Processor, Runtime и их потребителей.

## Изменения и доказательства

| Требование | Реализация | Проверка |
|---|---|---|
| Единые инварианты регистров и безопасное наблюдение | шестибитная маска R в ProcessorState; отдельное read-only State; неизменяемый ProcessorSnapshot; M без публичного изменяемого массива | RegisterStateContractTests; существующие арифметические, регистровые и JSON trace-тесты |
| Раздельные роли процессора | IProcessor, IRegisterOps, IAlu, IProcessorDebug, IInstructionAccountingSource; исполнители получают функциональный/подготовленный протокол | RegisterStateContractTests.ExecutionRoleDoesNotRequireEditingArithmeticOrDiagnostics; FunctionalExecutionPortTests; тесты исполнения и учёта |
| Протокол подготовленного исполнения | отдельная PreparedInstruction, владелец и поколение; общий интерпретатор; issue/transfer/publish/retire через внутренний контракт | PreparedInstructionTests, включая чужие/устаревшие/повторные аренды; PreparedProcessorOwnershipTests; аппаратные тесты памяти и АУ |
| Разделение экстракодов | фасад ExtracodeHandler; самостоятельные math/clock/print/terminal/storage/system-сервисы, trace и hang detector; сервисы без MachineCore | HostedServiceContractTests; существующие экстракодные и загрузочные тесты; Dhrystone FOREX |
| Раздельные роли памяти | IHostMemory, IOperandMemory, IInstructionMemory, ICommandMemory; CTX получает командный порт непосредственно | MemoryRoleContractTests: coherent replacement, отсутствие изменения recency, защита/контроль, приписка и алиасы CTX; существующие MemoryModelTests и MappedMemoryTests |
| Границы сборок | watch-состояние и управляющее исключение внутри Processor; удалены friend-доступы CLI; Runtime сохраняет доступ к протоколам Processor | AssemblyBoundaryTests; успешная сборка CLI и всего решения |
| Стандартная конфигурация и гостевая семантика | Dubna остаётся конфигурацией по умолчанию; единые вычислители и фиксированный ABI экстракодов | MemoryModelTests.DefaultRemainsDubnaAndSelectionIsPerMachine; полный набор тестов; контрольные результаты Dhrystone |

Публичный API изменён намеренно: IProcessor больше не наследует роли редактора,
АУ и диагностики; State и снимки доступны только для чтения; CPU принимает
IOperandMemory, SystemBus — IHostMemory. Миграция потребителей и описание
контрактов находятся в [source-layout.md](../../docs/source-layout.md).
Новая архитектура не обещает одинаковых побочных эффектов у разных моделей
памяти: приписка, защита, контроль и видимость буферов сохраняют свою семантику.

## Проверка по блокам

Тесты запускались в конце завершённых блоков, без запуска после отдельных правок.

| Блок | Пройдено | Ошибок | Пропущено |
|---|---:|---:|---:|
| Состояние и регистры | 3314 | 0 | 395 |
| Контракты исполнения | 3316 | 0 | 395 |
| Сервисы экстракодов | 3320 | 0 | 395 |
| Роли памяти и границы сборок; итоговая полная проверка | 3329 | 0 | 395 |

Итоговые команды:

```powershell
dotnet build Besm6.sln -c Release --no-restore --verbosity minimal
dotnet test Besm6.sln -c Release --no-build --no-restore --verbosity minimal
```

Сборка: 0 предупреждений и 0 ошибок. Проверены все девять тестовых проектов.
Пропуски обусловлены отсутствующим внешним CERN-корпусом и уже отключёнными
диагностическими/эталонными случаями; полный внешний CERN-корпус этой проверкой
не подтверждён. Коммиченные интеграционные случаи выполнены.
Локальные журналы: `tests-run/solid-block{1,2,3,4}-tests.log`,
`tests-run/solid-block4-build.log` (директория исключена из Git).

## Dhrystone FOREX: original и virtual

Запускались две Release-сборки на одном хосте: базовая `a961912` в отдельном
рабочем дереве и текущая. Порядок пар чередовался. По одному прогревочному
запуску каждой версии/режима исключено из статистики. Всего 26 запусков,
включая прогревы: по 3 измерения original и по 8 измерений virtual для каждой версии.

```powershell
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-forex.dub --speed original --guest-clock --stats --no-wall-clock --limit 200000000
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-forex.dub --speed virtual --guest-clock --stats --no-wall-clock --limit 200000000
```

| Версия / режим | Медиана wall time, с | Диапазон, с | Модельное время, с |
|---|---:|---:|---:|
| База / original | 5.779213 | 4.306181–6.515155 | 4.0894883 |
| Рефакторинг / original | 4.094313 | 4.089760–6.726621 | 4.0894883 |
| База / virtual | 3.512210 | 3.208149–6.121533 | 4.0894816–4.0894883 |
| Рефакторинг / virtual | 3.567504 | 3.276004–6.234251 | 4.0894816–4.0894883 |

Медиана virtual после рефакторинга выше базы на 1.57%; заметного ухудшения на
фоне наблюдаемого разброса не установлено. Это измерение на загруженном хосте,
а не доказательство равенства производительности или ускорения original.
Virtual текущей версии по медиане быстрее original примерно на 12.9%.
Выделения памяти на запуск остаются около 2.6 МБ в обеих версиях.

Во всех 26 запусках: 5000 итераций, BAD CONTROL VALUES = 0,
все 18 числовых и 4 строковых значения соответствуют Dhrystone 2.1.
Финальные регистры обеих версий и обоих режимов совпадают (за исключением
счётчика clockTicks при разном количестве команд форматирования времени).
В original совпадает также полный SHA-256 памяти:
`68D66C59FC6DD065557F9F6C71CFDF235F9AE50514256FE2E5B68103359756A2`.

Original использует модельные гостевые часы (Dhrystone измеряет 1.06 с).
Virtual использует время хоста, округлённое к 20 мс; CTIME/DMIPS и содержащие
их слова памяти зависят от длительности запуска. Форматирование этих значений
может добавить 30 команд: 24606699 либо 24606729 учтённых команд в virtual;
24606729 в original. Поэтому полного совпадения хешей памяти virtual не ожидается.
Политика часов сохранена и подтверждена существующими тестами гостевых часов.

Подробные локальные результаты: `tests-run/solid-dhrystone-results.json`,
`tests-run/solid-dhrystone-summary.json`,
`tests-run/solid-dhrystone-{baseline,refactored}-{original,virtual}-{round}.log`.
