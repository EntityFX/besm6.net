# Организация исходников библиотек

Физические папки отражают ответственность кода внутри DLL. Переносы
не вводят новых пространств имён, сборок или зависимостей между проектами.
API и семантика команд остаются прежними. `bin/` и `obj/` — продукты сборки,
их расположение не меняется.

| Библиотека | Основные папки |
|---|---|
| [Architecture](../src/Besm6.Architecture/README.md) | Isa, Numerics, State, Memory, Machine |
| [Processor](../src/Besm6.Processor/README.md) | Cpu, Execution, Arithmetic, Memory, Supervisor, Diagnostics |
| [Runtime](../src/Besm6.Runtime/README.md) | Machine, Configuration, Execution, Modeling, Timing, Loading, Extracodes, Devices, Storage, Encoding, Diagnostics, Profiling |
| [Assembler](../src/Besm6.Assembler/README.md) | Api, Assembly, Parsing, Detection, Disassembly, Legacy |
| [BitVisualizer](../src/Besm6.BitVisualizer/README.md) | Model, Decoding, Formatting, Isa |

## Единая машина и два владельца исполнения

`Processor/Cpu` содержит общие регистры и фасад CPU. `Processor/Execution`
содержит единственную семантику команд, `Processor/Arithmetic` — общий
вычислитель АУ (арифметического устройства). Эти узлы не моделируют
ожидание хоста или аппаратную занятость ресурсов.

`Runtime/Machine` собирает CPU, память и устройства одной машины.
`Runtime/Execution` владеет функциональной симуляцией: тиками, быстрыми
блоками и планировщиком. `Runtime/Modeling` владеет аппаратным исполнением
того же CPU; `Runtime/Timing` содержит календарь, стадии АУ, память и
книжные спецификации времени. Общий координатор запрещает вложенное
исполнение. Новые аппаратные механизмы размещаются в Modeling/Timing,
а общая вычислительная семантика — в Processor.

`Processor/Supervisor` хранит привилегированное состояние УУ
(устройства управления) и общие правила входа в прерывание.
`Runtime/Devices` реализует устройства и обмен с ними.
Папки не заменяют ограничения зависимостей: Architecture не зависит от
Processor/Runtime, Processor — от Runtime; Runtime связывает компоненты.

144 файла перенесены без изменения содержимого. Актуальные ссылки
документации обновлены; исторические отчёты и планы относятся к своим
коммитам и сохраняют старые пути.

Приёмка: 2932 теста Release, CERN 397/397, golden 3/3; три прежних пропуска.
[Карта переносов и результаты](../reports/source-layout-results.json).
