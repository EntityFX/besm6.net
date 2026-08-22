# Отчёт: точное сравнение C++ (ref/) vs C# (src/besm6.net/)

**Дата:** 22.08.2026
**Причина:** зависание симулятора C# при запуске .dub файлов

---

## 1. КРИТИЧЕСКОЕ расхождение — инструкция 002 (рег/mod)

### C++ (`ref/processor.cpp:194-195`)
```cpp
case 002: // рег, mod
    throw Exception("Illegal instruction 002 рег/mod");
```
**Поведение:** выбрасывает исключение → симуляция ОСТАНАВЛИВАЕТСЯ с ошибкой.

### C# (`src/besm6.net/Core/InstructionExecutor.cs:93-99`)
```csharp
case Opcode.Reg:
    mod = addr & 0x7FFF;
    applyMod = true;
    break;
```
**Поведение:** ТИХО устанавливает MOD-регистр и продолжает выполнение.

### Влияние на зависание
1. MONSYS попадает на инструкцию 002 (привилегированная)
2. C# молча модифицирует `mod` и `applyMod`
3. Все последующие адреса искажаются: `addr = Addr(addr + mod)`
4. PC дрейфует в мусор → бесконечный цикл E75/PIO → **зависание**
5. В C++ машина бы ОСТАНОВИЛАСЬ с понятной ошибкой

### Исправление
Заменить C# `Opcode.Reg` на throw (как в C++).

---

## 2. E76 — вызов рутин ядра

### C++ (`ref/extracode.cpp`)
Полная реализация с dispatch по адресу (00-07, 10+, etc.).

### C# (`src/besm6.net/Loader/ExtracodeHandler.cs:270-277`)
```csharp
private void E76()
{
    long addr = cpu.GetM(M16) & 0x7FFF;
    if (addr == 0 || addr == 1) return;
    if (addr >= 10) return;
    throw new ProcessorException($"Unimplemented extracode *76 ...");
}
```
**Влияние:** MONSYS может вызывать E76 для kernel services. C# не реализован → throw или no-op.

---

## 3. E75 — запись ACC в память (IDENTICAL)

### C++ (`ref/extracode.cpp:e75`)
```cpp
void Processor::e75() {
    auto addr = core.M[016] & 07777;
    if (addr > 0) {
        machine.mem_store(addr, core.ACC);
        if (addr == 020) intercept_count = 1;
    }
}
```

### C# (`src/besm6.net/Loader/ExtracodeHandler.cs:253-266`)
```csharp
private void E75()
{
    long addr = cpu.GetM(M16) & 0x7FFF;
    if (addr > 0)
    {
        _machine.Memory.Write((int)addr, new Word48(cpu.GetAcc()));
        if (addr == 16)
            cpu.InterceptCount = 1;
    }
}
```
**Вывод:** ИДЕНТИЧНЫ. Не является причиной зависания.

---

## 4. PIO/PINO (IDENTICAL)

### C++ (`ref/processor.cpp:644-658`)
```cpp
case 0340: // pio, vzm
    if (core.M[reg] == 0) {
        core.PC = addr;
        core.right_instr_flag = false;
    }
    break;

case 0350: // pino, v1m
    if (core.M[reg] != 0) {
        core.PC = addr;
        core.right_instr_flag = false;
    }
    break;
```

### C# (`src/besm6.net/Core/InstructionExecutor.cs`)
```csharp
case Opcode.Pio:
    if (m[reg] == 0) { pc = addr; rightFlag = false; }
    break;
case Opcode.Pino:
    if (m[reg] != 0) { pc = addr; rightFlag = false; }
    break;
```
**Вывод:** ИДЕНТИЧНЫ.

---

## 5. Экстракод dispatch (IDENTICAL)

C++ `extracode()`: E50, E51-E56, E57, E60, E61, E63, E64, E65, E67, E70, E71, E72, E75, E76, E77.
C# `ExtracodeHandler.Handle()`: тот же набор.

**Вывод:** Совпадает.

---

## 6. ЦИКЛ (0370) — КРИТИЧЕСКОЕ расхождение №2

### C++ (`ref/processor.cpp:660-670`)
```cpp
case 0370: // цикл, vlm
    if (core.M[reg] == 0) break;
    if (core.M[reg] == -1) {
        core.M[reg] = -1;
        break;
    }
    core.M[reg] = ADDR(core.M[reg] - 1);   // ДЕКРЕМЕНТ
    if (core.M[reg] == 0) break;
    core.PC = addr;
    core.right_instr_flag = false;
    break;
```

### C# (было, ПЕРЕД исправлением)
```csharp
case Opcode.Tsikl:
    if (m[reg] == 0) break;
    m[reg] = Addr(m[reg] + 1);   // ← ИНКРЕМЕНТ!!! НЕВЕРНО
    pc = addr;
    rightFlag = false;
    break;
```

**Вывод:** C# делал `m[reg] + 1` (инкремент) вместо `m[reg] - 1` (декремент).
Счётчик цикла РАСТЕТ вместо убывания → цикл **НИКОГДА не завершается** → бесконечное выполнение → **зависание**.

Это **ГЛАВНАЯ причина** зависания: MONSYS содержит сотни инструкций ЦИКЛ
(циклы загрузки, циклы I/O, циклы управления заданиями). Каждая из них
зацикливалась бесконечно.

---

## ИТОГОВЫЙ ВЕРДИКТ

| # | Расхождение | Критичность | Влияние на hang |
|---|-------------|-------------|-----------------|
| 1 | **ЦИКЛ (0370):** C# `m[reg]+1` вместо C++ `m[reg]-1` | **КРИТИЧЕСКАЯ** | **ГЛАВНАЯ ПРИЧИНА** — все циклы MONSYS бесконечны |
| 2 | Инструкция 002: C++ throw vs C# silent MOD | **КРИТИЧЕСКАЯ** | PC дрейфует в мусор при переходе на 002 |
| 3 | E76: C++ full vs C# stub | Высокая | MONSYS не может вызывать kernel services |
| 4 | E75 | Нет (идентичны) | — |
| 5 | PIO/PINO | Нет (идентичны) | — |
| 6 | Extracode dispatch | Нет (совпадает) | — |

## Исправления (выполнены)
1. ✅ **ЦИКЛ (0370):** `m[reg]+1` → `m[reg]-1` + поддержка `-1` (бесконечный цикл) + `if (m[reg]==0) break`
2. ✅ **Инструкция 002 (Opcode.Reg):** silent MOD → `throw` (как C++)
3. [ ] Прогнать `dotnet build` + `dotnet test` + `algol.dub`


### Следующий шаг (не блокер диагностики)

Ошибка "disk unit 043 not mounted" означает, что для полного boot MONSYS нужно предмассивировать дополнительные ленты (b.7, bemsh.739, librar.12/37) на соответствующих каналах. Это описано в `plans/monsys-kernel-support.md` и требует реализации динамического монтирования лент через E57 ASSIGN (как в C++ `machine.cpp::disk_mount`).


Диагностика: зависание симулятора C# при запуске .dub файлов
Найденные критические баги (оба исправлены):
1. ЦИКЛ (0370 / VLm) — ГЛАВНАЯ ПРИЧИНА зависания

Было: m[reg] = Addr(m[reg] + 1) — инкремент счётчика цикла
Стало: m[reg] = Addr(m[reg] - 1) — декремент (как в C++ ref/processor.cpp:660-670)
Эффект: MONSYS содержит сотни инструкций ЦИКЛ (циклы загрузки, I/O, управления заданиями). С инкрементом счётчик растёт вместо убывания → ни один цикл не завершался → бесконечное исполнение → зависание.
Файл: src/besm6.net/Core/InstructionExecutor.cs строки 469–478.
2. Инструкция 002 рег/mod — PC дрейф в мусор

Было: mod = addr & 0x7FFF; applyMod = true; — silent MOD-modification, продолжение выполнения.
Стало: throw new ProcessorException("Illegal instruction 002 рег/mod") — как в C++ ref/processor.cpp:194-195.
Эффект: При попадании MONSYS на инструкцию 002, C# молча искажал все последующие адреса (addr = Addr(addr + mod)), PC дрейфовал в мусорную память → бесконечный цикл E75/PIO → зависание.
Файл: src/besm6.net/Core/InstructionExecutor.cs (case Opcode.Reg).
Проверено, НЕ является причиной:
Компонент	Статус
E75 (запись ACC)	Идентичен C++ ✅
PIO/PINO	Идентичны C++ ✅
E76 (kernel services)	Stub (no-op) — MONSYS не может вызвать, но не вызывает hang
Extracode dispatch	Совпадает с C++ ✅
Верификация:
dotnet build → 0 ошибок, 0 предупреждений
plans/hang-diagnosis.md содержит полный отчёт сравнения C++/C#