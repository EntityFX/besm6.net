# План рефакторинга: полнофункциональный симулятор БЭСМ-6 на C# + загрузка примеров Dubna

## 1. Цели и область работ

1. Полнофункциональная работа машины БЭСМ-6 на C# (полный набор команд,
   память, периферия, прерывания, экстракоды, ОС-загрузка).
2. Загрузка программного кода из примеров Dubna (`.dub` job-скрипты).
3. Проекты ассемблеров (Madlen, BEMSH и общая инфраструктура ассемблирования).
4. Рефакторинг архитектуры: консолидация движков, много-проектное решение.

## 2. Ключевая проблема текущей архитектуры

В проекте ДВА параллельных исполнительных ядра, из которых работает только
устаревшее:

| Компонент | Назначение | Используется MachineCore? |
|---|---|---|
| `Processor.cs` | Точный порт `dubna/processor.cpp` (все команды) | НЕТ |
| `ControlUnit` + `ArithmeticUnit` + `InstructionDecoder` | Старый конвейер | ДА |

Следствия:
- тесты `ProcessorTests` проверяют `Processor`, а рантайм гоняет `ControlUnit` →
  тесты не отражают поведение машины;
- `AluTests` падают (неверные кодировки чисел + баги ALU, см. `plans/fix-alu.md`);
- отладчик/`Run()` привязаны к `ControlUnit`.

**Решение (утверждено пользователем):** единственный исполнительный движок —
`Processor` (порт `dubna/processor.cpp`, сверка ALU с `dubna/arithmetic.cpp`).
Устаревший конвейер (`ControlUnit`, `ArithmeticUnit`, `InstructionDecoder` и
связанный с ними код) **полностью удалить**. `MachineCore` переводится на
`Processor`. Приоритет работ: **1) загрузка программ Dubna, 2) ассемблеры.**

## 3. Целевая структура решения (много-проектный `.sln`)

```text
Besm6.sln
src/
  Besm6.Core/                 # ядро машины (class library, без зависимостей от IO ОС)
    Cpu/                      #   Processor, RegisterBank, ALU (порт Dubna)
    Memory/                   #   CoreMemory, BufferMemory, SOM, MMU
    Devices/                  #   IDevice, Disk, Drum, Tape, Teletype, Console, Plotter, Puncher
    System/                   #   Bus, DMA, InterruptController, DeviceManager
    Types/                    #   Word48, MantissaExponent, OperationCode, Enums
  Besm6.Loader/               # порт машины Dubna: загрузка .dub, монтаж лент,
                              #   обработка экстракодов e57/e64, COSY, кодировки ГОСТ
    Job/                      #   JobParser, ControlCard
    Session/                  #   Session, load_script
    Extracodes/               #   e57_mount, e64_output, ...
    Encoding/                 #   GOST-10859, ISO, ASCII конвертеры
    Disks/                    #   DiskImage, ZoneImage, tape-id
  Besm6.Asm.Core/             # общая инфраструктура ассемблера (токены, таблица команд)
  Besm6.Asm.Madlen/           # ассемблер МАДЛЕН
  Besm6.Asm.Bemsh/            # ассемблер БЭМШ
  Besm6.Disasm/               # дизассемблер
  Besm6.Cli/                  # консольный фронтенд (Program.cs, Debugger)
tests/
  Besm6.Tests/                # тесты ядра (ALU, процессор)
  Besm6.Loader.Tests/         # тесты загрузчика .dub / сессии
  Besm6.Asm.Tests/            # тесты ассемблеров
  Besm6.Cli.Tests/            # (опц.) e2e: запуск примеров .dub и сверка вывода
dubna/                        # эталонный C++ (не изменяем, только референс)
```

Зависимости (строго по слоям): `Cli -> Loader -> Core`; `Loader -> Core`;
`Asm.* -> Asm.Core` (не зависит от Core). Тесты ссылаются на свои библиотеки.

## 4. Рефакторинг ядра (Этап A)

1. Зафиксировать `Processor` как единственный движок:
   - довести ALU до соответствия `dubna/arithmetic.cpp` (исправить
     `MantissaExponent`: знаковая мантисса `long`, маска экспоненты `0x7F`,
     sign-extension, корректная упаковка результата);
   - исправить восьмеричные литералы в тестах до 17 символов;
   - перенести `Run/Step/Reset/LoadProgram` из `MachineCore` на `Processor`.
2. Удалить из активного пути `ControlUnit`, `InstructionDecoder` (или свести к
   тонкой обёртке-декоратору трассировки поверх `Processor`), решить судьбу
   `ArithmeticUnit`.
3. Выстроить `MachineCore` заново: память -> шина -> устройства -> процессор,
   с интерфейсами `ICpu`, `IMemory`, `IDevice` из `Interfaces.cs`.
4. Обновить `Debugger` и `Program.cs` под новый `MachineCore`.

## 5. Периферия и образы дисков (Этап B)

Согласовать `DeviceManager`, `DiskDevice`, `MagneticDrumDevice`, `TeletypeDevice`
с моделью Dubna:
- образ диска = набор зон (`PAGE_NWORDS`), монтаж по tape-id, readonly/write;
- COSY-файлы на барабане (текст <-> слова), конвертация в текст при монтировании;
- новые устройства: магнитные ленты (mount/unmount), плоттер (grafor),
  перфоратор/считыватель карт, АЦПУ;
- ГОСТ-10859 / ISO-7 кодировки (перенос `gost10859.h`/`encoding.*`).

## 6. Загрузчик примеров Dubna (Этап C)

Порт `machine.cpp`/`session.cpp`/`cosy.cpp`/`extracode.cpp`/`e57.cpp`/`e64.cpp`:
- парсер job-скрипта `.dub`: карты `*name`, `*tape:N/имя,Z`, `*library:N`,
  `*trans-main:ADDR`, `*execute`, `*end file`, `*call`, `*read`, `*no load list`;
- монтаж MONSYS и библиотек (таблица библиотек из `cli_test.cpp`);
- статический загрузчик + резидентный монитор (диспетчер);
- экстракоды: `e57` (монтаж лент/файлов/scratch), `e64` (вывод), `e63`, ...
- поддержка языковых трансляторов из образов: БЭМШ, МАДЛЕН, Фортран, Алгол,
  Паскаль, B (для `examples/`).
- CLI-команда `besm6 run examples/b/hello.dub`.

Критерий приёмки: золотые тесты — сверка вывода с `dubna/tests/expect_algol.txt`
и `session_test.cpp`/`e64_test.cpp` наборами.

## 7. Ассемблеры (Этап D)

- `Besm6.Asm.Core`: таблица команд БЭСМ-6 (мнемоника -> opcode/формат), токенизатор.
- `Besm6.Asm.Madlen`: МАДЛЕН-ассемблер (порт логики, используемой в `besm6_asm`).
- `Besm6.Asm.Bemsh`: БЭМШ-ассемблер.
- `Besm6.Disasm`: обратный транслятор для отладчика/дампов памяти.
- CLI: `besm6 asm file.mad -> file.bin`, `besm6 asm --dialect bemsh`, `besm6 disasm addr`.

## 8. Верификация и приёмка (Этап E)

- Прогнать примеры Dubna, сравнить вывод с эталонным (золотые файлы).
- Согласовать юнит-тесты рантайма и движка (убрать расхождение).
- Прогнать `fix-alu.md` исправления, зафиксировать регрессии.

## 9. Открытые вопросы к уточнению

- Состав ассемблеров: только МАДЛЕН и БЭМШ или ещё системный + дизассемблер.
- Глубина загрузки: полный путь «ОС-сессия + монитор MONSYS» или достаточно
  простых бинарных примеров в первую очередь.
- CLI-ориентированность или требуется GUI.
- Наличие образов лент/дисков для компиляторов (MONSYS, librar, b) в репозитории.