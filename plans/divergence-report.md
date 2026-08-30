# Отчёт: поиск расхождений C# vs C++ (BESM-6), тесты a400 / z005

> **Актуализация 30.08.2026.** Описанные ниже ошибки возврата из экстракода,
> сохранения ACC и режима РАУ исправлены. Текущий полный тестовый прогон не доходит
> до a400/z005 из-за неверного пути `ref/tests`; актуальный статус и оставшиеся
> блокеры собраны в [simulator-readiness-report.md](simulator-readiness-report.md).
> Основная нерешённая задача этого направления — после восстановления CERN fixture
> повторно найти первую фактическую точку расхождения. Остальной текст сохранён как
> журнал предыдущей диагностики.

Дата: 2026-08-27
Ветка работы: сохранение состояния процессора + обработка RAU-режима после экстракодов.

---

## 1. TL;DR

- Внесена правка: после каждого экстракода в `InstructionExecutor.cs` теперь вызывается
  `_p.SetLogical()` (точное соответствие C++ `core.set_logical()` в `processor.cpp:639-640`).
- **Тесты a400 и z005 всё равно падают.** Ошибка:
  `Loop detected: PC stuck in range 05762-05763 for 20K+ instructions` (детектор циклов
  в `DubnaLoader.cs`, `LoopWindow = 20000`, `LoopRange = 16`).
- Вывод C# **совпадает** с expected **до строки 31** (`*NO LOAD LIST`), но **не достигает**
  строки 32 (`*EXECUTE`) и вывода тела теста (строки 33-46).
- Ключевой факт (после коррекции арифметики): C# и C++ зацикливаются **на одном и том же
  адресе** `22370-22372 dec` (`53542-53544 oct` = `05762-05764 hex`), но **получают из памяти
  разные инструкции** на этих адресах. Расходится **состояние/память**, а не поток программы.
- Вывод: `SetLogical()` - **необходим, но недостаточен**. Корень - более раннее расхождение
  (RAU-режим / ACC / стек-указатель M[15]).

---

## 2. Что сделано

### 2.1. `SetLogical()` после экстракода
Файл: `src/besm6.net/Core/InstructionExecutor.cs`

C++ (`ref/processor.cpp:637-640`) для всех экстракодов (050-077, 0200, 0210):
```
Aex        = ADDR(addr + core.M[reg]);
core.M[14] = Aex;
extracode(opcode);
core.set_logical();   // <-- ВСЕГДА после экстракода
break;
```
C# - теперь:
```
if (_p.ExtracodeHandler != null && _p.ExtracodeHandler((int)opcode, aex))
{
    acc = _p._acc.Value;   // ре-чт (обработчик мог менять ACC/RMR)
    rmr = _p._rmr.Value;
    _p.SetLogical();       // <-- добавлено, = C++ set_logical()
    break;
}
```
`SetLogical()` в C# эквивалентен C++ `set_logical()`
(`core.ALU.mode = core.ALU.mode2 = Logical`).

### 2.2. Проверены (соответствуют C++):
- `Tsikl` / vlm (0370): `m[reg]=Addr(m[reg]+1)` + ветка, `if(m[reg]==0) break` - идентично C++.
- `Uim` / sti (041): идентично C++ `processor.cpp:556-576`. **OK.**
- `Schi` / ita (042): идентично C++ `processor.cpp:578-583`. **OK.**
- `Vchob` / x-a (006): соответствует C++ `processor.cpp:238-249`. **OK.**

---

## 3. Результаты тестов

```
dotnet test .../Besm6.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CernLibTests"
```
```
Failed Beacon_MatchesExpectFile (1,"a400")
  result: Error at 05762: Loop detected: PC stuck in range 05762-05763 for 20K+ instructions.
Failed Beacon_MatchesExpectFile (2,"z005")   (то же самое)
Failed: 2, Passed: 0, Skipped: 1
```
Артефакты: `tests-run/cernlib/{actual,diff}_a400.txt`, `{actual,diff}_z005.txt`.

### 3.1. Сравнение вывода a400
`expect_a400.txt` - 46 строк. C# (`actual_a400.txt`) выдаёт корректно строки **1-31**:
```
26  *NAME A400
27  *TAPE:12/******,32
28  *LIBRARY:1,2,3,5,12,23
29  *CALL SETFTN:ONE,LONG
30  *NO LIST
31  *NO LOAD LIST
    <-- C# ПАДАЕТ здесь (цикл); строки 32-46 (*EXECUTE, тело BOOLEAN ARITHMETIC) НЕ достигнуты
```
C++ доходит до конца (в `expect_a400.txt` строки 33-46 = тело теста).

---

## 4. Разбор циклов (ВАЖНО, с коррекцией адресов)

> ⚠️ Ранее я ошибся в арифметике `8^4` (писал 32768, правильно 4096) и неверно заключил,
> что циклы C# и C++ на разных адресах. **Правильно: это ОДИН и тот же адрес.**

`53542 oct = 5*4096 + 3*512 + 5*64 + 4*8 + 2 = 22370 dec = 0x5762 hex`.
`53544 oct = 22372 dec = 0x5764 hex`.

### 4.1. Цикл в C++ (`ref` trace `tests-run/cpp_a400_i.txt`)
Адреса в octal:
```
53536 R: vzm (12)
53537 L: ita 12          (042) M[12] <- ACC
53537 R: x-a 323(1)      (006) ACC  += mem(323+M1)
53540 L: sti 12          (041) M[12] <- ADDR(ACC);  ACC <- mem(--M15)
53540 R: uj 141(1)       (пб)
53542 L: utm 1(3)        (0250) M[3] <- M[1]
53542 R: atx -1(3)       (000)  M[3] -= 1
53543 L: utc 141(1)      (0220) ACC  <- M[1]
53543 R: vlm (12)        (0370) если M[12]!=0: M[12]++, ветка на 53542
53542 … (тело, итер. 2)
53543 R: vlm (12)        -> M[12]==0, ВЫХОД
53544 L: uj 101(1)       (пб)
```
**C++ делает ровно 2 итерации** и выходит: `M[12]` стартует `= 32767 (0x7FFF)`, после `+1 -> 0`.
### 4.2. Цикл в C# (`instr_trace.log` в `bin/Release/net8.0`)
Адреса в hex (`pc:X5`):
```
0575F L: vzm  (10)  (22367)
0575F R: ita  (10)  (22367) M[10] <- ACC
05760 L: x-a  211(1) (22368)
05760 R: sti  (10)  (22368) M[10] <- ADDR(ACC)   [acc=01FFFFFFFFFF, rau=13]
05761 L: uj   97(1) (22369)
05762 R: utm  1(3)  (22370)  <-- PRAVAYA polovina
05763 L: зп   32767(3) (22371)
05763 R: utc  97(1) (22371)
05764 L: vlm  (10) -> 22370  <-- ЦИКЛ (M[10]), НЕ выходит
05764 R: uj 65(1)   (выход при M[10]==0 - не достигается)
```

### 4.3. Несоответствие на ОДНОМ адресе (главное расхождение)
Сопоставляем **один и тот же адрес** `22370-22372 dec`:

| Адрес | C++ (ref) | C# (besm6.net) |
|-------|-----------|----------------|
| 22370 (53542 / 0x5762) L | `utm 1(3)` | *(в цикле не выполняется)* |
| 22370 R | `atx -1(3)` | `utm 1(3)` |
| 22371 (53543 / 0x5763) L | `utc 141(1)` | `зп 32767(3)` |
| 22371 R | `vlm (12)` | `utc 97(1)` |
| 22372 (53544 / 0x5764) L | `uj 101(1)` (выход) | `vlm (10)` (цикл) |

**Вывод:** на этих адресах у C# и у C++ **разное содержимое слов памяти**. Либо (а) это
**разные участки программы** (поток ушёл в разную ветку ранее и попал в другой под-цикл
с vlm на M[10] вместо M[12]), либо (б) **память/реестры M[] расходятся** из-за раннего
разбега RAU/ACC/M[15]. Оба механизма дают одну картину (зацикливание на 22370-22372),
поэтому **следующий шаг - найти ТОЧКУ**, где C# впервые отклоняется от C++.

---

## 5. Старт совпадает, расхождение позже

Начало обоих трейсов - один адрес `1032 dec` (`00408 hex = 02010 oct`), оба выполняют
`vtm`/`*70` и входят в vlm(1)-цикл «Load 50 names from TRP» на `1036-1037 dec`:
```
C++ : 02014 L: *70  | 02015 L: vlm 2014(1)   (повторы, корректный выход)
C#  : 0040C R: 56   | 0040D R: Tsikl(1)      (повторы)
```
Далее:
- C# строки 21-32 (`1038-1043 dec`): `ita15, зп, *70, xta, уи, зп, слц, зп, сл, зп, втбр, пб M[15]`,
  затем **прыжок в M[15]=22273 dec (0x5701)** -> мусорная зона `зп 0(0)` (строки 33-60+).
- C++ строки 26-37 (`1037-1043 dec`): `ita17, atx716, *70, xta17, ati16, atx2(16), arx3001,
  atx17, xta3000, atx(16), vtm1673(15), uj(17)` -> возврат в `53401 oct = 21953 dec`.

Расхождение **M[15] (стек)**: C# `M[15]=22273 dec`, C++ `M[17 oct]=M[15 dec]=21953 dec`.
Разность `320 dec` - к этому моменту C# уже «накопил» лишние/другие `зп/зпм`.

---

## 6. Рабочие гипотезы (в порядке приоритета)

1. **RAU-режим на условных переходах (Po/пе).** `Po`/`Pe` выбирают ветку по RAU
   (Additive-знак BIT41, Multiplicative-нуль BIT48, Logical-нуль). Если C# в момент
   `Po/пе` в другом RAU-режиме, чем C++ - ветка другая -> другой M[15]/M[12]/M[10] ->
   разный vlm-цикл. `SetLogical()` после экстракода лечит **часть** случаев, но не все.
2. **Пропущенный `set_*` в иных местах** (не только после экстракода). Сверить **все**
   `set_logical/set_additive/set_multiplicative` в `processor.cpp` (строки 191,209,222,235,
   248,261,274,288,302,318,331,345,358,371,385,399,415,443,456,469,484,503,527,533,541,574,
   582,640) с аналогами C#.
3. **Расхождение стека M[15]/corr_stack** (`PrepareStack`, `corr_stack=1` в C++ при
   `!addr && reg==017`). Если C# некорректно эмулирует поправку стека - `uj M[15]`
   уйдёт в мусор (наблюдается: C# прыгает в `0x5701` = зона `зп 0(0)`).
4. **Состояние ACC до `x-a`/`sti`** (значение, из которого `ADDR(ACC)` даёт счётчик vlm).
   Разный ACC -> разный M[12]/M[10] -> разное число итераций.

---

## 7. Инструменты и артефакты

- C++ trace: `tests-run/cpp_a400_i.txt` (2 952 294 строк), формат
  `<PC:5oct> <L|R>: <reg:2> <opcode:3oct> <addr:4oct> <mnemonic> [= exec]`.
- C# instr trace: `src/besm6.net/tests/Besm6.Tests/bin/Release/net8.0/instr_trace.log`
  (~30 МБ), включается `BESM6_INSTR_TRACE=1`, формат
  `<pc:X5hex> R=L|R op=<dec> reg=<dec> addr=<dec> acc=<hex12> rau=<hex> mod=<dec> m14=<dec> <Op>`.
  > Осторожно: **PC в C# hex, в C++ octal** - приводить к dec перед сравнением.
- C# ec trace: `.../ec_trace.log`, включает `BESM6_TRACE=1` (детектор повторений PC>20).
- Детектор циклов (C#): `DubnaLoader.cs:435-487` (`LoopWindow=20000`, `LoopRange=16`).
- Детектор «hang» (C#): `ExtracodeHandler.cs:143-159` (`NoOutputLimit=500` на E64/E74).

---

## 8. План следующих шагов

1. **Найти первую точку расхождения.** Свести оба трейса к `dec`-адресам и сделать
   построчный diff по `(address, half, opcode, reg)` начиная со строки ~20, где C# уже
   в `1038 dec` (`ita15`), а C++ - в `1037 dec` (`ita17`). Первая пара с разным opcode
   на одном адресе = корневая точка.
2. **Вывести RAU и M[] в C# trace.** Расширить строку trace: добавить `m0..m15` (хотя бы
   m10, m12, m13, m14, m15); `acc` уже есть. Сверить RAU в месте `Po/пе` с C++.
3. **Сверить все `set_*` режимы.** Таблица «инструкция -> режим после» C++ vs C#; закрыть
   несовпадения (особенно `Po/пе`, `Po`, `Pe`, `зп`, `зпм`, `уи`, `slt`).
4. **Проверить `PrepareStack` / `corr_stack`.** Убедиться, что C# корректно эмулирует
   `if(!addr && reg==017) M[017]=ADDR(M[017]-1); corr_stack=1;` во всех инструкциях,
   где это есть в C++.
5. **Отладочный «стоп-снимок»:** добавить опцию `BESM6_STOP_AT=<dec-addr>`, чтобы на
   точке расхождения дампнуть `ACC,RMR,PC,M[0..15],RAU,MOD` и сравнить с C++.
6. **Регрессия:** после каждой правки прогонять
   `dotnet test ... --filter "FullyQualifiedName~CernLibTests"` + `ProcessorTests` +
   `Besm6MathAndTapeTests`, чтобы не сломать уже работающие.

---

## 9. Что уже УВЕРЕННО известно (не гипотезы)

- vlm (цикл) в C# - корректный порт (инкремент + `if 0 break`).
- sti / ita / x-a - корректные порты.
- `SetLogical()` после экстракода - добавлен, совпадает с C++.
- a400/z005 **не** падают на «hang-детекторе» E64/E74; падают на **PC-детекторе**
  (DubnaLoader: 20K инструкций в диапазоне <16).
- Вывод C# корректен до `*NO LOAD LIST` (строка 31 из 46).
- Зацикливание - на адресе `22370-22372 dec`; vlm идёт по **M[10]** в C# и по **M[12]** в C++.
- Старт (`1032 dec`) и vlm(1)-цикл (`1036-1037 dec`) - **совпадают**.
- M[15] (стек) к моменту `uj M[15]` - **разный** (C# `22273`, C++ `21953`).


---

**Прорыв!** C++ `boot_ms_dubna` (`machine.cpp:946-962`) хранит **каждое слово с ДВЕМИ инструкциями** (левая/правая):
```
02010: vtm -5(1), *70 3002
02011: xta 377,  atx 3010
02012: xta 363,  atx 100
02013: vtm 53401(17), utc
02014: *70 3010(1), utc
02015: vlm 2014(1), ita 17
02016: atx 716, *70 717
02017: xta 17, ati 16
02020: atx 2(16), arx 30101
02021: atx 17, xta 3000
02022: atx (16), vtm 1673(15)
02023: uj (17), utc
```
А C#-трек на том же `1032 dec` показывает `R=R Uia M[1]` (ПРАВАЯ половина!), потом `1033 dec L=E70`. То есть в C# **инструкции разложены по другим половинам слова / порядок выполнения другой**. Сравниваю C# `BootMsDubna` с C++.



Да. Я посмотрел именно `EntityFX/besm6.net:cernlib-port` и сравнил критические места с текущим `besm6/dubna`. Уже нашлись несколько конкретных расхождений, причём два из них могут полностью объяснять нынешний уход MONSYS в `Э63(0)`.

### 1. Критическая ошибка: возврат из экстракода

В оригинальном `dubna` экстракод **всегда продолжает выполнение со следующего 48-битного машинного слова**. Если экстракод стоял в левой половине слова, правая половина **не исполняется**:

```cpp
// dubna/extracode.cpp
if (core.right_instr_flag) {
    core.PC += 1;
    core.right_instr_flag = false;
}
```

Это явно зафиксировано в референсе. 

В C# сейчас обычная логика `Execute()` сначала переключает половину слова:

```csharp
if (rightFlag)
{
    pc += 1;
    rightFlag = false;
}
else
{
    rightFlag = true;
}
```



Но при вызове `ExtracodeHandler` дополнительного перехода на следующее слово нет:

```csharp
if (_p.ExtracodeHandler != null &&
    _p.ExtracodeHandler((int)opcode, aex))
    break;
```



То есть получается:

```text
             C++ dubna                besm6.net сейчас

Э63 в LEFT   выполнить Э63            выполнить Э63
             PC := PC+1               PC тот же
             half := LEFT             half := RIGHT
                 ↓                         ↓
          следующее слово          ПРАВАЯ ПОЛОВИНА
                                   текущего слова
```

Это очень серьёзное расхождение управления потоком. Оно легко может привести к совершенно корректно декодируемому, но **никогда не предполагавшемуся** `Э63(0)` через несколько инструкций.

Поэтому я бы пока вообще не считал `Э63(0)` первопричиной.

---

### 2. Ещё более неприятное: результат экстракода в ACC затирается

В начале `InstructionExecutor.Execute()` делается копия:

```csharp
ulong acc = _p._acc.Value;
ulong rmr = _p._rmr.Value;
```



Обработчики экстракодов при этом работают непосредственно с `Processor`. Например `E63`:

```csharp
case 1: cpu.SetAcc(206L); return;
case 7: cpu.SetAcc(5L << 33); return;
...
```

а `E50` аналогично делает:

```csharp
cpu.SetAcc(Besm6Math.Sqrt(arg));
cpu.SetAcc(Besm6Math.Sin(arg));
...
```



Но после возврата из обработчика `InstructionExecutor` выполняет:

```csharp
_p._acc = Word48.FromInt48(acc);
_p._rmr = Word48.FromInt48(rmr);
```



То есть происходит:

```text
ACC = старое значение

E63:
    cpu.SetAcc(результат)
           ↓
Processor._acc = результат

возврат в Execute()
           ↓
Processor._acc = локальный acc
           ↓
ACC = СТАРОЕ ЗНАЧЕНИЕ
```

Это почти наверняка ломает не только CERNLIB, но и множество экстракодов вообще.

Например оригинальный C++ для `Э63(0502)` возвращает адрес дескриптора процесса через `ACC = 02000`.  В C# обработчик тоже устанавливает это значение, но текущий `InstructionExecutor` затем может его затереть.

### 3. После экстракода должен выставляться логический режим РАУ

В C++:

```cpp
Aex        = ADDR(addr + core.M[reg]);
core.M[14] = Aex;
extracode(opcode);
core.set_logical();
break;
```



В C# после обработчика `SetLogical()` отсутствует. 

Это тоже архитектурно значимо: последующая условная логика может интерпретировать ACC не в том режиме.

---

## Я бы первым делом исправил dispatch экстракодов

Минимально — примерно так:

```csharp
default:
    if (IsExtracode(opcode))
    {
        aex = Addr(addr + m[reg]);
        m[14] = aex;

        _p.ExtracodeReg = reg;
        _p.ExtracodeRawAddr = addr;
        _p.ExtracodeRightFlag = rightFlag;

        //
        // C++ Processor::extracode():
        // return from extracode to the next machine word.
        //
        if (rightFlag)
        {
            pc = Addr(pc + 1);
            rightFlag = false;
        }

        if (_p.ExtracodeHandler != null &&
            _p.ExtracodeHandler((int)opcode, aex))
        {
            //
            // Handler modifies Processor directly.
            // Synchronize local cached registers.
            //
            acc = _p._acc.Value;
            rmr = _p._rmr.Value;

            //
            // processor.cpp does core.set_logical()
            // after every extracode.
            //
            _p.SetLogical();

            break;
        }

        throw new ProcessorException(
            $"Extracode {(int)opcode} not implemented");
    }

    throw new ProcessorException($"Unknown instruction {opcode}");
```

Это исправляет сразу три вещи:

1. пропуск правой половины слова после экстракода в LEFT;
2. сохранение результата `SetAcc()/SetRmr()` обработчика;
3. `RAU = logical` после экстракода.

Я бы именно этот фикс сделал **до любых изменений `E63(0)`**.

### 4. Комментарий про «C++ тоже падает на a400/z005» выглядит ошибочным

Сейчас в вашей ветке написано:

> `C++ cernlib_test.cpp cernlib_a400/z005 тоже фейлят`



То же утверждение продублировано непосредственно возле `E63`. 

Но в текущем upstream `dubna`:

* CERNLIB-набор специально включается через `make test-all`;
* файл содержит **более 400 тестов**;
* `cernlib_a400` является активным тестом; 
* `cernlib_z005` тоже является активным тестом. 
* README прямо описывает `make test-all` как запуск тестов вместе с CERN Library tests. 

При этом действительно сам upstream `E63()` не имеет case `0` и бросит исключение, если реально получит такую подкоманду. 

Из этого следует важный вывод:

> **Нормальный upstream-путь a400/z005, скорее всего, вообще не должен доходить до `E63(0)`.**

Поэтому ваше наблюдение `0765 → 07 → 0502 → 00` очень похоже не на отсутствующую функцию MONSYS, а на **расхождение состояния CPU до этой точки**.

И первые два найденных бага как раз способны такое расхождение создать.

---

### 5. Есть ещё один настоящий gap: `corr_stack`

В C# `StackCorrection()` сейчас сознательно является заглушкой:

```csharp
public void StackCorrection()
{
    // C#-порт не реализует corr_stack
}
```



В C++ `corr_stack` сбрасывается для каждой инструкции и выставляется, например, при `STX`/`XTS`; это позволяет откатить изменение `M[017]`, если инструкция была прервана исключением/перехватом. Например:

```cpp
core.M[017] = ADDR(core.M[017] - 1);
corr_stack  = 1;
```

и наоборот для push. 

Это я бы поставил **P1 после extracode fix**. Для нормального линейного выполнения оно не всегда проявляется, но при MONSYS/overflow/intercept даст очень неприятные рассинхронизации стека.

---

## Что интересно: CERN fixture у вас сделан правильно

Я отдельно сверил prolog upstream:

```text
*name ...
*tape:12/librar,32
*library:1,2,3,5,12,23
*call setftn:one,long
*no list
*no load list
...
*end file
```

Это именно то, что использует upstream `test_cernlib()`. 

Так что сейчас я бы **не копал job setup, библиотечные tape или сам FORTRAN**. Сначала CPU/extracode semantics.

## Предлагаемый порядок доведения порта

```text
P0
 ├─ extracode → всегда next machine word
 ├─ не затирать ACC/RMR после handler
 └─ set_logical() после extracode
        │
        ▼
Повторить a400
        │
        ├── проходит дальше → отлично
        │
        └── diverges
              │
              ▼
        C++/C# instruction trace
        до ПЕРВОЙ отличающейся инструкции
              │
              ▼
P1
 └─ corr_stack / intercept semantics
              │
              ▼
P2
 └─ переносить весь CERNLIB test matrix
```

Причём я бы не сравнивал конечный stdout на этом этапе. Нужно сравнивать после каждой инструкции:

```text
PC
left/right
RK
ACC
RMR
RAU
MOD
M[0..15]
Aex
```

и остановиться **ровно на первом различии**. Тогда вместо отладки тысяч инструкций MONSYS мы получим одну конкретную несовместимость C# ↔ C++.

На текущем состоянии ветки мой главный кандидат на первопричину — **не `E63(0)`, а неверная семантика возврата из экстракода плюс затирание ACC после обработчика**.

Могу следующим сообщением пройти `InstructionExecutor.cs` целиком инструкция-за-инструкцией против `dubna/processor.cpp` и составить полный список несовпадений, а затем дать готовый patch для `cernlib-port` с регрессионными тестами на эти три ошибки.


---

I've gathered decisive evidence. Let me give you the verified picture and a concrete path forward.

## What I verified (ground truth, not assumption)

__1. Your three P0 fixes are already in the working tree__ — I confirmed via `git diff` on `InstructionExecutor.cs` (uncommitted, on top of `1a8bbc2`): the extracode PC-advance, ACC/RMR preservation, and `SetLogical()` are all present. You don't need to re-apply them.

__2. They fixed the `*63 0` crash but exposed a deeper problem.__ Running a400 + z005 *with* those fixes in place, both now fail with:

```javascript
Error at 05762: Loop detected: PC stuck in range 05762-05763 for 20K+ instructions.
MONSYS is in an I/O wait/abort spin-loop (channel-done not signaled).
instructions: 360000
```

- a400: output __494/1211 chars__, __character-0 divergence: actual starts with a NUL (`\u0000`)__ that `expect_a400.txt` lacks, and ends in garbage `ЖЖЖ`.
- z005: same loop error.

__3. The "known MONSYS kernel gap (same in C++ dubna reference)" is WRONG.__ I verified against the real C++ artifacts:

- `cpp_a400.txt`: C++ a400 __succeeds__ — full `BOOLEAN ARITHMETIC TEST A400`, compile/link, octal results, `Elapsed time: 0.044 s, Simulated: 2805935 instructions`.
- `cpp_a400_t.txt`: C++ __also loops on E61__ (`061 0000 *61 = 34464`) hundreds of times near the end (PC `34015`–`34033` octal) — then *exits* it and halts with `*74`. So an E61 loop is __legitimate MONSYS behavior__, not a bug.
- __Decisive:__ I searched the full 98MB C++ instruction trace for `^05762` — __zero hits__. C++ *never executes* PC `05762`. Yet C# parks there in a spin-loop.

## What this means

The failure is __not__ extracode semantics (that's already fixed) and __not__ a shared MONSYS gap. It's a __state divergence right after the MONSYS banner__: C# takes a branch into an I/O-wait loop at `05762` that C++ never enters. The leading NUL + `ЖЖЖ` garbage corroborate that C# is reading a wrong/zero word (a bad branch condition) at that point. So the real bug is a __state difference (ACC / M-register / flag) that flips a branch__ — exactly the class of bug your P0 fixes addressed, but in a different spot.

## Proposed next step (concrete)

1. __Generate a C# instruction trace__ for a400 (the `StepTrace` hook already exists in `DubnaLoader.cs`) and capture the last ~200 instructions before the `05762` loop.
2. __Compare against `cpp_a400_i.txt`__ at the matching banner-exit point to find the exact first divergent word (ACC / M / flag).
3. __Fix the divergence__ (likely in the branch/flag logic or a register left wrong) and re-run a400 + z005.

Before I sink into that (it's the real work), I want to confirm direction with you, since your message framed this as "apply P0 → re-run":

Cline has a question:

The P0 fixes are already applied and a400 now fails at a post-banner state divergence (C# spins at PC 05762, which C++ never reaches; C++ also legitimately loops on E61 then halts *74). How should I proceed?
---
Декодер C++ идентичен C#.
