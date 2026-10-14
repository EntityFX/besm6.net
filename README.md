# besm6.net — симулятор ЭВМ БЭСМ-6

Архитектура программной модели, компоненты и правила исполнения: [ARCHITECTURE.md](ARCHITECTURE.md).

Текущая приёмка архитектурной консолидации: [отчёт](reports/architecture-consolidation-report.md).

Точный и быстрый симулятор советской ЭВМ **БЭСМ-6** (1960-е) на C#/.NET.
Проект воспроизводит архитектуру, набор команд, память, супервизор и систему
программирования «Дубна» так, чтобы исторические задания на Фортране, Алголе
и Ассемблере побитово совпадали с реальной машиной, а по скорости обгоняли
наивную реализацию.

- **Зачем:** запускать настоящие программы и системное ПО БЭСМ-6 (`.dub`
  задания, компиляторы Фортрана/Алгола, бенчмарки Dhrystone/Whetstone/
  STREAM/MP-MFLOPS), проверять их на побитовое соответствие историческим
  эталонам (CERNlib, golden-набор) и исследовать аппаратное поведение
  процессора, памяти и устройств.
- **На чём:** C#, `net8.0`, без внешних рантайм-зависимостей. Собирается и
  работает на Windows, Linux и macOS.

## Что внутри

| Проект | Роль |
|---|---|
| `src/Besm6.Architecture` | Модель слова (48 бит), команд, АУ, режимов. Без зависимостей. |
| `src/Besm6.Assembler` | Ассемблер БЭСМ-6: текст → восьмеричное слово. |
| `src/Besm6.Processor` | Функциональный процессор: исполнение команд, тики, события. |
| `src/Besm6.Runtime` | Загрузчик, экстракоды, супервизор, устройства, память. |
| `src/Besm6.Cli` | Консольный симулятор `besm6` (`run`, `asm`, `disasm`, `check`). |
| `src/Besm6.Tui` | Текстовый интерфейс (пульт, регистры, память). |
| `src/Besm6.BitVisualizer*` | Визуализация разрядов слова (WinForms — только Windows). |
| `src/besm-edu` | Учебная модель простого CPU для экспериментов. |

Граф зависимостей строгий и без циклов:
`Architecture → (Processor → Architecture) → (Assembler) → (Runtime) → CLI/TUI`.

## Требования

- [.NET SDK 8.0+](https://dotnet.microsoft.com/download) (сборка целится в `net8.0`).
- Для окон Windows дополнительно ничего не требуется; для побитовых
  golden-замеров удобен Python 3.12 (`tools/run_all_examples.py`).

## Сборка

```bash
# Восстановить и собрать всё решение
dotnet build Besm6.sln -c Release

# Только консольный симулятор
dotnet run --project src/Besm6.Cli -c Release -- <аргументы>
```

> Проекты `Besm6.BitVisualizer.WinForms*` компилируются только под Windows.
> На Linux/macOS для сборки остального решения они отключены автоматически.

## Использование (CLI)

После сборки исполняемый файл:
`src/Besm6.Cli/bin/Release/net8.0/besm6.dll`. Вызов через `dotnet <путь> <команда>`.

```bash
DLL=src/Besm6.Cli/bin/Release/net8.0/besm6.dll

# Выполнить задание .dub (файл с программой на Фортране/Алголе/Ассемблере)
dotnet $DLL run examples/algol.dub

# Режимы скорости: max — быстрый (по умолчанию), original — темп как на железе
dotnet $DLL run examples/algol.dub --speed original

# Производительность эмуляции на текущем хосте через гостевой CTIME
dotnet $DLL run examples/dhrystone-dmips.dub --speed virtual --guest-clock --stats --no-wall-clock

# Со статистикой исполнения и профилем горячего пути
dotnet $DLL run examples/algol.dub --stats --profile

# Ассемблировать и дизассемблировать отдельные команды
dotnet $DLL asm "сч 5"
dotnet $DLL disasm 05010

# Пакетно проверить все .dub в каталоге
dotnet $DLL check examples --limit 100000
```

Полный список ключей `run` — `dotnet $DLL help run`.

Hosted-запуск `--speed original` использует статистическую модель УУ/АУ и МОЗУ
для гостевого таймера и замедления хоста. Стоимость математических экстракодов
оценена отдельно. `max` сохраняет прежний табличный таймер, `virtual` — таймер
хоста. [Источники, допущения и ограничения модели](docs/original-timing.md).
Калибровку original можно переопределить в JSON полями `originalTimeFactor`
и `originalMathFactor`; [пример настроек](examples/original-timing.json).

### Формат `.dub`

`.dub` — задание для системы «Дубна»: заголовок `*name`, тело программы
(Фортран/Алгол/Ассемблер в кавычках или управляющие строки) и `*execute`.
Более 50 примеров лежат в `examples/` (включая порты Dhrystone, Whetstone,
STREAM, MP-MFLOPS и матрицу CERNlib).

## Тесты и приёмка

```bash
# Полный прогон юнит-, интеграционных и CLI-тестов
dotnet test Besm6.sln -c Release

# CERNlib-матрица (долгая, ~18 мин) — как в CI
dotnet test tests/Besm6.Integration.Tests -c Release

# Golden-приёмка примеров (побитовое сравнение с эталонами)
python tools/run_all_examples.py \
  --dll src/Besm6.Cli/bin/Release/net8.0/besm6.dll \
  --suite fast --output tests-run/golden
```

Актуальное состояние: **~2500+ тестов зелёные**, **CERNlib 397/397**,
golden-набор **3/3**, максимальная регрессия скорости в пределах допуска 5%.
Подробная карта готовности по этапам — в [`docs/besm6-readiness.md`](docs/besm6-readiness.md).

## Структура репозитория

```
src/          исходный код проекта (.NET)
tests/        юнит-, интеграционные и golden-тесты
examples/     .dub-задания (исторические программы и бенчмарки)
tools/        python-инструменты (golden-прогон, дизассемблер, разбор ОС)
book/         книжные материалы по БЭСМ-6 и Фортрану-Дубна (источники)
tapes/        образы лент ОС «Дубна» и разбор
docs/         спецификации архитектуры, памяти, приёмки по этапам
reports/      текущая приёмка и необходимые исторические контрольные данные
plans/        планы разработки
```

## Документация

- [docs/besm6-machine.md](docs/besm6-machine.md) — устройство, симулятор, скорость.
- [docs/besm6-commands.md](docs/besm6-commands.md) — команды, признаки, супервизор.
- [docs/besm6-memory.md](docs/besm6-memory.md) — память, приписка, буферы.
- [docs/besm6-hardware-time.md](docs/besm6-hardware-time.md) — аппаратное время и стадии АУ.
- [docs/besm6-readiness.md](docs/besm6-readiness.md) — готовность и границы по этапам.
- [docs/ose-dubna.md](docs/ose-dubna.md) — восстановление кода ОС «Дубна».
- [docs/dubna-languages.md](docs/dubna-languages.md) — Фортран-Дубна, портирования.

## Лицензия

Проект исследовательский и распространяется «как есть»; книжные материалы в
`book/` принадлежат их авторам и сохранены в справочных целях.

## Гостевые показатели производительности

`examples/memspeed-forex.dub` и `examples/mpmflops-forex.dub` выполняются
настоящим ФОРЕКС 4.13 и используют блочный `IF` для выбора ядра. Значение
`data kpart/1/` выбирает первый тест; замените 1 на 2 или 3 для остальных
ядер, на 0 — для запуска без ядра. Число итераций и арифметические
выражения сохранены из проверенных портов `*ftn`.

Программы самостоятельно печатают рейтинг после проверки результатов:
Memspeed — `GUEST MB/S` (copy/add/multiply-add, 6 байт на слово БЭСМ-6),
MPMFLOPS — `GUEST MFLOPS` (2/8/32 операции на слово).
CTIME измеряет только исполнение ядра; компиляция, подготовка, проверки
и печать находятся вне измеряемого интервала. При неверных контрольных
значениях, выключенном таймере или времени менее 40 мс рейтинг не выдаётся.

```powershell
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/memspeed-forex.dub --guest-clock --speed original --stats --no-wall-clock --limit 200000000
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/mpmflops-forex.dub --guest-clock --speed virtual --stats --no-wall-clock --limit 200000000
./tools/verify_forex_kernels.ps1 -CheckGuards
```

Проверочный скрипт запускает все шесть ядер в `original`/`virtual`/`max`,
сверяет независимые численные результаты, объём данных, число операций,
формулы рейтингов и конечное состояние `original`/`max`. Защитные сценарии
проверяются в `max`: нулевое/недопустимое ядро, короткая нагрузка,
выключенный таймер и испорченный элемент. Для отдельного семейства:
`-Benchmarks memspeed` или `-Benchmarks mpmflops`.

`examples/dhrystone-forex.dub` — Dhrystone 2.1 для настоящего ФОРЕКС 4.13:
восемь подпрограмм и три функции, строки `CHARACTER*30`, подстроки,
сравнение строк, блочный `IF`, глобальные данные через `COMMON` и
двумерный целочисленный массив. Записи представлены полями массивов,
указатели — индексами двух узлов. Все 18 основных значений и четыре строки
проверяются после измерения. В отличие от старого варианта `*ftn`, тело
содержит реальные вызовы процедур; показатели разных портов зависят от
представления данных и сгенерированного кода.

```powershell
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-forex.dub --guest-clock --speed original --stats --no-wall-clock --limit 200000000
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-forex.dub --guest-clock --speed virtual --stats --no-wall-clock --limit 200000000
./tools/verify_forex_dhrystone.ps1 -CheckGuards
```

Скрипт проверяет 5000 и 10000 проходов в `original`/`virtual`/`max`,
независимые контрольные значения и строки, формулу DMIPS, одинаковое
состояние `original`/`max` и защитные сценарии: нулевая/короткая нагрузка,
выключенный таймер, неверное числовое значение и испорченная строка.

Оригинальные ядра Whetstone Роя Лонгботтома (`whets.for`, 1996) адаптированы в
`examples/whetstone-longbottom.dub`: 8 проходов × 100, массивы и обращения по
индексам, чередование T, секционные MFLOPS/MOPS. N1 выполняется десятикратно,
его время нормализуется делением на 10. MWIPS = 800 / (10 × сумма нормализованных
времён); итоговый MFLOPS — геометрическое среднее MFLOPS секций N1, N2 и N6.
REAL БЭСМ-6 имеет 48 бит (40 бит мантиссы), поэтому это не IEEE single precision.
Автокалибровка длительности и интерактивный ввод сведений о хосте заменены
фиксированной нагрузкой. Прежний `whetstone-mwips.dub` сохраняет старую методику;
его MWIPS нельзя напрямую сравнивать с новым портом.

```powershell
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/whetstone-longbottom.dub --guest-clock --speed original --stats --no-wall-clock --limit 2000000000
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/whetstone-longbottom.dub --guest-clock --speed virtual --stats --no-wall-clock --limit 2000000000
./tools/verify_longbottom_whetstone.ps1 -Modes original,virtual,max -CheckGuards
```

Проверочный скрипт сверяет независимые контрольные значения восьми секций,
количество операций и формулы рейтингов, совпадение численных значений `original`/`max`,
отказ от итоговых рейтингов при выключенном таймере и неверном результате.

Вариант с картами `*name форекс` / `*forex`:
`examples/whetstone-longbottom-forex.dub`. По умолчанию карта `*forex`
передаётся монитору без изменения и выбирает настоящий **ФОРЕКС 4.13
от 11.09.85**, уже находящийся на штатных лентах. Для обратной совместимости
можно явно включить прежнюю подмену на `*fortran` через JSON-конфигурацию:

```json
{"forexAsFortran": true}
```

Передайте этот файл через `--config`; без настройки (или при `false`)
работает настоящий ФОРЕКС. Свойство также доступно в API как
`Config.ForexAsFortran` и `DubnaLoader.ForexAsFortran`.

Проверено: настоящий ФОРЕКС выполняет `Hello, World!`, присваивания,
арифметику и Whetstone. Прежняя ошибка `Illegal instruction 002 рег/mod`
была вызвана тремя машинными словами, заменёнными пробелами в bundled `monsys.9`;
образ восстановлен из upstream, контрольная сумма обновлена.
Результаты прежних прогонов с `forexAsFortran: true` относятся к
Фортрану-Дубна в режиме совместимости, а не к настоящему ФОРЕКС.

Для режима совместимости циклы N3/N6/N7 разбиты на 100 блоков,
чтобы границы DO не превышали 32767; число операций сохранено, затраты
на дополнительные циклы входят во время секций. Допуск контрольных значений
5E-6 учитывает накопленное округление Фортрана-Дубна.

```powershell
./tools/verify_longbottom_whetstone.ps1 -Deck examples/whetstone-longbottom-forex.dub -ControlTolerance 0.000005 -Modes original,virtual,max -CheckGuards
# Явный режим совместимости с Фортраном-Дубна:
./tools/verify_longbottom_whetstone.ps1 -Deck examples/whetstone-longbottom-forex.dub -ForexAsFortran -ControlTolerance 0.000005 -Modes original,virtual,max -CheckGuards
```

Число операций и рейтинг вычисляет гостевая программа; специальные параметры рейтингов CLI больше не используются. Для hosted-таймера CTIME требуется `--guest-clock`:

```bash
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-dmips.dub --guest-clock --speed max --no-wall-clock
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/mpmflops-mflops.dub --guest-clock --speed max --no-wall-clock
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/whetstone-mwips.dub --guest-clock --speed max --no-wall-clock
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/memspeed-mbps.dub --guest-clock --speed max --no-wall-clock

# Pascal: native records/strings in Dhrystone, arrays/procedures in Whetstone
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/dhrystone-pascal.dub --guest-clock --speed virtual --no-wall-clock
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll run examples/whetstone-pascal.dub --guest-clock --speed virtual --no-wall-clock
```

В `max` и `original` вывод содержит модельные DMIPS, MFLOPS, MWIPS или MB/s. В `virtual` гостевой CTIME использует фактическое время исполнительного цикла на хосте: рейтинг показывает производительность эмуляции и зависит от компьютера и его нагрузки. Для этого замените `--speed max` на `--speed virtual`; `--guest-clock` остаётся обязательным. Таймер имеет разрешение 20 мс и обнуляется при каждом запуске. Загрузка и компиляция находятся вне времени тела теста; выключите трассировку и профиль для измерений.

`--stats` дополнительно показывает `elapsedSeconds`, `modelSeconds` и `modelToWallRatio`: модельное время, делённое на фактическое. Значение меньше единицы означает, что эмуляция медленнее модели. Аппаратный `virtual` выполняется без ожиданий как `max`; аппаратные часы ОС и сроки устройств остаются модельными. `DATE*` независим от гостевого таймера.

Исходные нетаймированные задания сохраняются для сравнений. Формулы и защитные условия проверяются `tools/verify_guest_ratings.py`; точное совпадение таймерных результатов требуется между `max` и `original`, но не с `virtual`. Отдельный план и результаты: [plans/Virtual.md](plans/Virtual.md).

Pascal-версии используют библиотеку №22 и второй выход CTIME (счётное время CPU). Dhrystone проверяет 18 основных значений и четыре строки; Whetstone — восемь секций и сумму. Проверить все три скорости, контрольные значения и отказ от рейтинга при выключенном таймере, нулевой/короткой нагрузке и неверном результате:

```bash
python tools/verify_pascal_benchmarks.py --dll src/Besm6.Cli/bin/Release/net8.0/besm6.dll --output tests-run/pascal-check
```

Параметры нагрузки находятся в исходниках: `numberofruns=5000` и `nruns=100`. Проверочный инструмент создаёт варианты во временном рабочем каталоге; исходные задания не переписывает. Рейтинг публикуется после контроля и при времени не менее 40 мс. Whetstone суммирует квантованные интервалы секций, поэтому для сравнительных измерений нужны повторные прогоны и достаточно длинная нагрузка.
