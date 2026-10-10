# besm6.net — симулятор ЭВМ БЭСМ-6

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

# Со статистикой исполнения и профилем горячего пути
dotnet $DLL run examples/algol.dub --stats --profile

# Ассемблировать и дизассемблировать отдельные команды
dotnet $DLL asm "сч 5"
dotnet $DLL disasm 05010

# Пакетно проверить все .dub в каталоге
dotnet $DLL check examples --limit 100000
```

Полный список ключей `run` — `dotnet $DLL help run`.

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
reports/      отчёты этапов и сохранённые измерения (csv/json)
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
