# Поддержка ядра MONSYS для ALGOL/FORTRAN/B примеров

## Контекст

MVP raw-words путь (E64/E50/E70) работает: 71/71 тестов, 3 .dub файла end-to-end.
Однако 80+ примеров в `examples/` (ALGOL, FORTRAN, B, quines, games) требуют:
1. Загрузки MONSYS с диска (dense tape layout)
2. Исполнения MONSYS boot-sequence (внутренние инструкции ОС)
3. Вызова компилятора (BEMSH/EXFOR/B-compiler)
4. Исполнения скомпилированного кода

## Текущее состояние

```
algol.dub → MONSYS загружается → E75-цикл (intercept_count) → PC=02001 "Illegal instruction 002 рег/mod"
```

«Illegal instruction 002» — это внутренняя инструкция MONSYS (рег/mod = register modify?),
не распознанная C# Processor. C++ dubna/machine.cpp обрабатывает её как часть OS-ядра.

## Задачи (по приоритету)

### 1. Диагностика "Illegal instruction 002" ✅ (диагностика завершена)

**Находка (важное открытие):**
- C++ `dubna/processor.cpp` **ТОЖЕ** бросает `throw Exception("Illegal instruction 002 рег/mod")`.
  Это **НЕ баг C#-порта** — это общий gap обоих эмуляторов (C++ reference тоже не запускает ALGOL).
- `book/opcodes.md`: `002 рег | mod` = "Обращение к спец. регистрам (привилегированная)", группа **Л, П** (супервизор).
- В C++ `apply_mod_reg`/`MOD` — это **не инструкция 002**, а побочный эффект: ставится в конце
  `Step()` когда `next_mod != 0` (processor.cpp:679-682), применяется в decode (line 153: `addr = ADDR(addr + core.MOD)`).
- C# **уже** имеет всю инфраструктуру: `Processor._mod`, `_applyModReg`, `Mod` getter, и применение в
  `InstructionExecutor.cs:71-72` (`if (applyMod) addr = Addr(addr + mod)`).

**Вывод:** инструкция `002` — привилегированная (режим супервизора). Она **не должна** попадаться
в обычном потоке MONSYS. Если PC=02001 декодируется как 002, это либо:
  (a) реальная привилегированная инструкция MONSYS → нужен режим супервизора + её семантика; ЛИБО
  (b) **артефакт**: MONSYS делает неверный переход и попадает в мусорную/незагруженную область памяти
      (неверный PC / сдвиг dense-tape layout) → тогда дело в загрузке, а не в процессоре.

**Результат:** инструкция `002 рег/mod` — **реальная привилегированная инструкция MONSYS** (не мусор).
C++ **тоже** не реализует её (processor.cpp:178: `throw`). **Следующий шаг: реализовать `case 002` в C#.**

### 2. E75 intercept_count ✅
- [x] `Processor.InterceptCount` (property + `ConsumeIntercept()`)
- [x] `E75()`: `if (addr == 16) cpu.InterceptCount = 1` (020 oct)
- [ ] Обработка в ALU (overflow/div-zero → `ConsumeIntercept()` + jump to intercept vector)

### 3. E50 014 (parse) + 017 (format)
- [ ] Требуют записи в RMR (Register for Memory Reference)
- [ ] Требуют байтового доступа к памяти (BytePointer)
- [ ] C++ `e50_parse` — токенизатор входной строки (используется BEMSH для чтения исходников)
- [ ] C++ `e50_format_real` — форматирование чисел (используется для вывода)
- [ ] Нужен: `Processor.SetRmr(long)` + `Memory.ReadByte(addr)` / `Memory.WriteByte(addr, byte)`

### 4. Внутренние инструкции MONSYS
- [ ] `dubna/machine.cpp` — найти все OS-специфичные инструкции
- [ ] Классифицировать: (a) уже есть в C# Processor, (b) missing
- [ ] Реализовать missing (reg/mod, semaphores, task-switching, ...)

### 5. BEMSH (ALGOL compiler)
- [ ] BEMSH — это программа в памяти, а не C++ код
- [ ] Если MONSYS-ядро работает, BEMSH должен заработать сам
- [ ] Проверить: `examples/algol.dub` + `examples/quine/algol.dub`

### 6. EXFOR / B compiler
- [ ] Аналогично — программы в памяти
- [ ] Проверить: `examples/exfor.dub`, `examples/b/fibonacci.dub`

## Оценка

| Задача | Оценка |
|--------|--------|
| 1. Diagnose 002 | 2-4 ч |
| 2. E75 intercept | 1-2 ч |
| 3. E50 014/017 | 4-6 ч (RMR + byte I/O) |
| 4. MONSYS инструкции | 4-8 ч |
| 5-6. Компилеры | 2-4 ч (если ядро OK) |
| **Итого** | **~2-3 дня** |

## Зависимости

```
1 (diagnose 002) ──→ 4 (MONSYS инструкции) ──→ 5 (BEMSH) ──→ ALGOL работает
                    ──→ 6 (EXFOR)  ──→ FORTRAN работает
                    ──→ B-compiler ──→ B работает
3 (E50 parse) ──→ 5 (BEMSH читает source)
3 (E50 format) ──→ вывод результатов
2 (E75 intercept) ──→ runtime safety (overflow)
```

## Выходные критерии

- [ ] `dotnet besm6 run examples/algol.dub` → ALGOL программа выполняется, выдаёт результат
- [ ] `dotnet besm6 run examples/quine/algol.dub` → выводит сам себя
- [ ] `dotnet besm6 run examples/exfor.dub` → FORTRAN программа выполняется
- [ ] 71/71 юнит-тестов + новые интеграционные тесты