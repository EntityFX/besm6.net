# Задача: найти ПЕРВОЕ расхождение `besm6.net` ↔ `besm6/dubna`

Репозитории:

```text
C# reference-under-test:
https://github.com/EntityFX/besm6.net
branch: cernlib-port

C++ ground truth:
https://github.com/besm6/dubna
```

Целевой тест сначала:

```text
CERNLIB a400
```

После исправления:

```text
CERNLIB z005
```

---

# 0. Основное правило

Не исследовать:

```text
PC 05762 loop
E61 loop
E63(0)
поздний мусорный вывод
финальный diff stdout
```

как первопричину.

Все они могут быть лишь следствием раннего расхождения.

Твоя единственная основная задача:

> Найти первую BESM-6 instruction, для которой архитектурное состояние C++ и C# было эквивалентно ДО инструкции и стало различным ПОСЛЕ неё.

После нахождения первой divergence:

1. определить конкретную причину;
2. сделать минимальный fix;
3. сделать regression test;
4. только потом продолжить сравнение.

---

# 1. Зафиксировать точную версию C#

Перед любой диагностикой вывести:

```bash
git status --short
git branch --show-current
git rev-parse HEAD
git log -5 --oneline
git diff -- src/besm6.net/Core/InstructionExecutor.cs
git diff
```

В отчёт записать:

```text
C# HEAD:
branch:
dirty: yes/no

P0 fixes:
[ ] extracode PC/half semantics
[ ] ACC/RMR preservation after handler
[ ] SetLogical after extracode
```

Не предполагать наличие исправлений по предыдущему отчёту.

Работать только с фактическим working tree.

---

# 2. Не доверять старому C# instruction trace

Текущий/старый trace мог выводить:

```text
PC
rightFlag
```

уже ПОСЛЕ предварительного advance/toggle.

Поэтому поле:

```text
R=R
```

могло означать состояние NEXT instruction, а не half только что исполняемой инструкции.

Это делает старый C++/C# line diff потенциально ложным.

Нужен новый canonical trace.

---

# 3. Ввести понятия PRE и POST state

В самом начале выполнения instruction, ДО изменения PC/half:

```csharp
uint execPc = pc;
bool execRight = rightFlag;

ulong accBefore = acc;
ulong rmrBefore = rmr;
int rauBefore = ...;
int modBefore = ...;

var mBefore = copy(M[0..15]);
```

Если имеются другие архитектурно значимые состояния — сохранить:

```text
Aex
applyMod
nextMod
interrupt/intercept state
corr_stack
```

После выполнения инструкции сохранить POST state.

---

# 4. Canonical trace format

Не использовать человекочитаемый disassembly как основной diff-format.

Сделать машинно-сравнимый CSV или TSV.

Предпочтительно TSV.

Одна строка = одна реально выполненная BESM-6 instruction.

Файл C#:

```text
trace_cs.tsv
```

Файл C++:

```text
trace_cpp.tsv
```

Минимальная схема:

```text
seq
pc
half
raw48
rk24
opcode
reg
addr
ea
acc_before
rmr_before
rau_before
mod_before
m0_before
m1_before
m2_before
m3_before
m4_before
m5_before
m6_before
m7_before
m8_before
m9_before
m10_before
m11_before
m12_before
m13_before
m14_before
m15_before
acc_after
rmr_after
rau_after
mod_after
pc_after
half_after
m0_after
m1_after
m2_after
m3_after
m4_after
m5_after
m6_after
m7_after
m8_after
m9_after
m10_after
m11_after
m12_after
m13_after
m14_after
m15_after
```

Дополнительно желательно:

```text
aex_before
aex_after
apply_mod_before
apply_mod_after
corr_stack_before
corr_stack_after
```

---

# 5. Все числовые поля должны иметь каноническое представление

Не сравнивать визуально:

```text
53542 oct
05762 hex
22370 dec
```

Все адреса в canonical trace хранить как:

```text
unsigned decimal integer
```

Можно дополнительно иметь:

```text
pc_oct
```

только для человека.

Но comparator работает исключительно по integer `pc`.

Для слов:

```text
raw48 = 12 hex digits
ACC   = 12 hex digits
RMR   = 12 hex digits
RK    = 6 hex digits
```

Пример:

```text
raw48=0123456789AB
rk24=ABCDEF
acc_before=FFFFFFFFFFFF
```

M-registers хранить как integer `0..32767`.

---

# 6. half должен означать EXECUTED half

Строго:

```text
half=L
```

означает:

> текущая RK была выбрана из старших 24 bits `raw48`.

```text
half=R
```

означает:

> текущая RK была выбрана из младших 24 bits `raw48`.

Не выводить сюда значение CPU half flag после advance.

Нужно разделять:

```text
half
half_after
```

---

# 7. raw48 обязателен

Для каждой инструкции логировать исходное 48-bit machine word:

```text
raw48
```

Это позволяет мгновенно разделить два класса ошибок:

### Case A

```text
C++ raw48 == C# raw48
C++ rk24  == C# rk24
```

Но state after различается.

Значит ошибка в execution semantics.

### Case B

```text
C++ raw48 != C# raw48
```

Значит ошибка возникла раньше:

```text
loader
I/O
memory write
memory corruption
wrong address
bootstrap
```

В этом случае decoder не трогать.

---

# 8. Отдельный memory-write trace

Создать:

```text
mem_cs.tsv
mem_cpp.tsv
```

Формат:

```text
seq
pc
half
address
old48
new48
kind
```

`kind` например:

```text
CPU
EXTRACODE
BOOT
DRUM
DISK
TAPE
MONSYS
OTHER
```

Если возможно, добавить:

```text
source_opcode
source_reg
source_addr
```

Каждая операция изменения BESM memory должна проходить через единый trace hook.

---

# 9. Самое важное: comparator НЕ должен начинать с позднего loop

Сначала сравнить трассы от первой instruction.

Но нельзя бездумно делать:

```python
zip(trace_cpp, trace_cs)
```

потому что возможны разные internal trace events.

Canonical trace должен содержать только реально исполненные machine instructions.

Если это выполнено, `seq` должен в идеале совпадать.

---

# 10. Алгоритм сравнения instruction traces

Написать отдельный небольшой tool:

```text
tools/trace-diff/
```

или Python script:

```text
tools/diff_trace.py
```

Он читает:

```text
trace_cpp.tsv
trace_cs.tsv
```

и ищет первую divergence.

Псевдокод:

```python
for i in range(min(len(cpp), len(cs))):

    a = cpp[i]
    b = cs[i]

    compare identity:
        pc
        half
        raw48
        rk24
        opcode
        reg
        addr

    if identity differs:
        report CONTROL/FETCH divergence
        stop

    compare PRE architectural state

    if pre differs:
        report:
            state divergence already existed BEFORE this instruction

        then inspect previous instruction
        stop

    compare POST architectural state

    if post differs:
        report:
            FIRST EXECUTION DIVERGENCE
        stop
```

---

# 11. Сравнение PRE state

Сравнивать:

```text
ACC
RMR
RAU
MOD
M0..M15
Aex
applyMod
```

Но если поле не существует/неэквивалентно в одном simulator, сначала документировать mapping.

Не сравнивать internal implementation detail, если он не имеет архитектурного эквивалента.

---

# 12. Классификация divergence

Comparator должен выдавать один из типов.

## TYPE 1 — FETCH

```text
same expected control flow
different raw48
```

Пример отчёта:

```text
FIRST DIVERGENCE: FETCH

seq: 17293
PC: 22370
PC oct: 53542
half: L

C++ raw48 = ...
C#  raw48 = ...

Memory content differs before execution.
```

Дальше автоматически искать:

```text
last write to address 22370
```

в `mem_cpp.tsv` и `mem_cs.tsv`.

---

## TYPE 2 — CONTROL FLOW

```text
previous POST differed in PC/half
```

Пример:

```text
Instruction identity before divergence:

PC=...
half=...
opcode=...
ACC=...

After:

C++ PC=...
C#  PC=...
```

Это почти всегда:

```text
branch
extracode return
modifier
interrupt
half transition
```

---

## TYPE 3 — REGISTER STATE

Например:

```text
M15 differs
```

Вывести только различающиеся поля:

```text
M15:
 C++ before=21953 after=21954
 C#  before=21953 after=22273
```

Не печатать 100 одинаковых полей.

---

## TYPE 4 — ACC/RMR

Вывести:

```text
ACC before
RMR before
operation
operands
memory operand
ACC after
RMR after
RAU after
```

---

## TYPE 5 — RAU

Если единственное отличие:

```text
RAU_after
```

особенно важно.

Отчёт:

```text
Instruction arithmetic result is identical,
but resulting ALU mode differs.

C++: LOGICAL
C#:  ADDITIVE
```

Дальше найти mapping этой instruction к:

```text
set_logical
set_additive
set_multiplicative
```

в C++.

---

## TYPE 6 — MODIFIER

Если отличается:

```text
MOD
effective address
applyMod
```

сразу анализировать lifecycle modifier.

---

# 13. Автоматический поиск причины memory divergence

Если первый instruction mismatch вызван:

```text
raw48_cpp != raw48_cs
```

пусть script:

1. берёт `PC` адрес;
2. ищет последнюю запись в этот address ДО divergence:

```text
mem_cpp.tsv
mem_cs.tsv
```

3. печатает:

```text
C++ last writer
C# last writer
```

Если одна сторона вообще никогда его не записывала — это чрезвычайно важный результат.

Пример:

```text
Address: 22370

C++ last write:
 seq=9182
 PC=...
 kind=DISK
 old=...
 new=...

C# last write:
 seq=9053
 PC=...
 kind=CPU
 old=...
 new=...
```

Это сразу переводит debugging из decoder в I/O/memory subsystem.

---

# 14. Если traces начинают расходиться по длине

Не пытаться "ресинхронизировать" через поиск похожего PC далеко впереди.

Это скрывает первопричину.

Первая ситуация:

```text
cpp[i].identity != cs[i].identity
```

уже является divergence.

Нужно анализировать:

```text
cpp[i-1].POST
cs[i-1].POST
```

Именно предыдущая instruction определила разный следующий control flow.

---

# 15. Нужен небольшой контекст вокруг divergence

Comparator выводит:

```text
5 instructions before
FIRST DIVERGENCE
5 instructions after
```

Но AFTER имеет только вспомогательное значение.

Фокус всегда на:

```text
i-1
i
```

---

# 16. Формат итогового сообщения comparator

Пример:

```text
====================================================
FIRST BESM-6 DIVERGENCE
====================================================

Sequence: 48372

Executed instruction:
PC dec : 1037
PC oct : 02015
Half   : R
RK     : 123456
Opcode : ...
Reg    : ...
Addr   : ...

PRE STATE:
                 C++             C#
ACC              ...             ...
RMR              ...             ...
RAU              ...             ...
MOD              ...             ...
M15              ...             ...

PRE STATE MATCH: YES

POST STATE DIFFERENCES:

M15:
    C++ = 21953
    C#  = 22273

PC:
    C++ = ...
    C#  = ...

All other compared state matches.

Classification:
REGISTER/CONTROL DIVERGENCE

Likely implementation:
InstructionExecutor.cs / <opcode implementation>

Reference:
processor.cpp / <case>

====================================================
```

---

# 17. Bootstrap validation ДО CERNLIB diff

Отдельно проверить после `BootMsDubna()` raw memory:

```text
02010..02023 oct
```

Сравнить C++ и C#.

Ожидаемая последовательность:

```text
02010 L: vtm -5(1)
02010 R: *70 3002

02011 L: xta 377
02011 R: atx 3010

02012 L: xta 363
02012 R: atx 100

02013 L: vtm 53401(17)
02013 R: utc

02014 L: *70 3010(1)
02014 R: utc

02015 L: vlm 2014(1)
02015 R: ita 17

02016 L: atx 716
02016 R: *70 717

02017 L: xta 17
02017 R: ati 16

02020 L: atx 2(16)
02020 R: arx 30101

02021 L: atx 17
02021 R: xta 3000

02022 L: atx (16)
02022 R: vtm 1673(15)

02023 L: uj (17)
02023 R: utc
```

Если raw48 C++ == C# для всего диапазона:

> bootstrap packing hypothesis закрыта.

Добавить regression test.

Не возвращаться к ней без новых доказательств.

---

# 18. Initial CPU state test

До первой инструкции bootstrap получить:

```text
PC
half
ACC
RMR
RAU
MOD
M0..M15
```

В частности проверить:

```text
PC = 02010 oct
half = LEFT
```

Если начальное состояние разное, весь дальнейший trace бессмысленен.

---

# 19. P0 extracode regression tests

Нужны micro-tests отдельно от CERNLIB.

### Test A — extracode in LEFT half

Слово:

```text
LEFT:  test extracode
RIGHT: instruction that MUST NOT execute
```

Проверить:

```text
handler called once
RIGHT skipped
PC -> next word
next half = LEFT
```

### Test B — extracode in RIGHT half

Проверить правильный:

```text
PC
half
```

после возврата.

### Test C — ACC preservation

Handler:

```csharp
cpu.SetAcc(KNOWN_VALUE);
```

После Step:

```text
ACC == KNOWN_VALUE
```

### Test D — RMR preservation

То же для RMR.

### Test E — RAU

После extracode:

```text
RAU == Logical
```

---

# 20. RAU audit

После first-diff infrastructure сделать автоматический audit C++ `processor.cpp`.

Собрать все места:

```text
set_logical()
set_additive()
set_multiplicative()
```

Создать таблицу:

```text
opcode
mnemonic
C++ resulting mode
C# resulting mode
status
```

Пример:

```text
006  x-a      ADDITIVE        ADDITIVE       OK
...
```

Не исправлять инструкцию только потому, что кажется подозрительной.

Исправлять только подтверждённый mismatch или доказанную audit-разницу.

---

# 21. Stack audit

Особенно проверить `M[017]` C++ = `M[15]` C#.

Найти ВСЕ строки C++, где изменяется:

```cpp
core.M[017]
```

Классифицировать:

```text
normal execution
stack operand
XTS
STX
intercept
exception
corr_stack rollback
extracode
```

Сравнить с C#.

Результат оформить:

```text
C++ location
operation
expected semantics
C# location
status
```

---

# 22. corr_stack

Не оставлять фразу:

```text
C# порт не реализует corr_stack
```

без анализа того, нужен ли он на first divergence path.

Если first divergence проходит через instruction/intercept, где C++ имеет:

```cpp
corr_stack = 1;
```

а C# этого не имеет — сделать точный regression test.

Но не внедрять большую stack rewrite без воспроизводимого failing test.

---

# 23. Modifier audit

Сравнить:

```text
MOD
applyMod
nextMod
```

по instruction boundary.

Особенно проверить:

```text
UTC
WTC
VTM
UTM
```

и инструкции, использующие модифицированный effective address.

Нужен micro-test:

```text
modifier instruction
normal instruction
another normal instruction
```

проверяющий, что modifier действует ровно столько instructions, сколько должен.

---

# 24. I/O differential trace

Если первая divergence возникает непосредственно после disk/drum/tape extracode, создать:

```text
io_cpp.tsv
io_cs.tsv
```

Формат:

```text
seq
extracode
subfunction
unit
zone
sector
mem_addr
word_count
direction
first_word
last_word
hash
```

Для block transfer считать hash по полным 48-bit words.

Если параметры совпадают, но hash разный:

```text
data source / tape image / byte decoding
```

Если hash одинаков, но memory после transfer различается:

```text
destination/write semantics
```

---

# 25. Не использовать stdout как архитектурный comparator

stdout полезен только как верхнеуровневый regression:

```text
expect_a400
expect_z005
```

Он не нужен для поиска первой divergence.

То, что output совпадает до:

```text
*NO LOAD LIST
```

лишь устанавливает верхнюю границу ошибки.

---

# 26. Этапы работы

Работать именно в таком порядке.

## PHASE A

```text
git state
P0 verification
```

## PHASE B

```text
fix trace semantics
```

## PHASE C

```text
bootstrap raw memory test
initial CPU state test
```

## PHASE D

```text
generate C++ canonical trace
generate C# canonical trace
```

## PHASE E

```text
automatic first divergence
```

## PHASE F

```text
minimal root-cause fix
micro regression
```

## PHASE G

```text
repeat first-divergence tool
```

Если новая divergence появилась позже — повторять.

Цель:

```text
divergence position monotonically moves forward
```

пока `a400` не завершится.

Только после этого:

```text
z005
```

---

# 27. Очень важно: не делать giant patch

Каждый исправленный архитектурный mismatch:

```text
1 divergence
1 root cause
1 minimal patch
1 regression test
```

Не исправлять одновременно:

```text
RAU
stack
modifier
I/O
decoder
```

иначе невозможно доказать, какая правка была правильной.

---

# 28. После каждой правки

Минимум:

```bash
dotnet test <project> --filter "FullyQualifiedName~ProcessorTests"
dotnet test <project> --filter "FullyQualifiedName~Besm6MathAndTapeTests"
dotnet test <project> --filter "FullyQualifiedName~CernLibTests"
```

Если реальные имена/пути отличаются — сначала определить правильные существующие test projects, не придумывать новые.

---

# 29. Критерий успеха первого debugging pass

НЕ:

```text
a400 полностью работает
```

Первый milestone гораздо уже:

> Получен доказанный first divergence report.

В нём должны быть:

```text
C# HEAD + dirty diff

sequence number

PC:
 decimal
 octal

half

raw48

RK

instruction

PRE state C++
PRE state C#

подтверждение:
PRE STATE MATCH = YES

POST state C++
POST state C#

точные differing fields

C++ implementation location

C# implementation location

root cause

minimal proposed patch
```

---

# 30. Definition of Done для root-cause fix

Исправление считается доказанным только если:

```text
1. Есть micro/regression test, который FAILS до fix.
2. Он PASSES после fix.
3. Existing processor tests проходят.
4. Первая C++/C# divergence после fix переместилась дальше.
```

Идеально:

```text
old first divergence: seq 48372
new first divergence: seq 69105
```

Это объективное доказательство прогресса.

---

# 31. Definition of Done для a400

Только когда:

```text
no architectural divergence until normal completion
```

и:

```text
actual a400 output == expected a400 output
```

можно считать a400 исправленным.

После этого тем же toolchain запускать `z005`.

---

# 32. Запрещённые выводы без доказательств

Не писать:

```text
"probably MONSYS bug"
"likely E61"
"looks like RAU"
"probably stack"
"decoder seems wrong"
```

без first divergence evidence.

Допустимый формат:

```text
Observed:
...

Proof:
...

First differing architectural field:
...

Instruction responsible:
...

C++ semantics:
...

C# semantics:
...

Therefore:
...
```

---

# 33. Что от тебя требуется прямо сейчас

Сейчас НЕ пытайся сразу чинить CERNLIB.

Выполни только:

```text
A. зафиксировать HEAD/working tree;
B. подтвердить P0;
C. исправить trace, чтобы PC/half были PRE-execution;
D. проверить raw bootstrap 02010..02023;
E. получить initial CPU state;
F. получить canonical traces a400;
G. автоматически найти первую divergence.
```

После этого остановись и дай отчёт.

Не делай speculative fixes до получения первой divergence.

## Финальный формат ответа

```text
# Differential tracing report

## Repository state

## P0 status

## Trace correctness fix

## Bootstrap verification

## Initial state

## First divergence

Sequence:
PC decimal:
PC octal:
Half:
Instruction:
Raw48:

### PRE
C++:
C#:

### POST
C++:
C#:

### Differing fields

## Root cause

C++:
file:line
semantics

C#:
file:line
semantics

## Proposed minimal patch

## Regression test

## Test results

## Next divergence after patch
```

Основной принцип всей работы:

> Не отлаживать место, где машина окончательно сломалась. Отлаживать первую инструкцию, после которой две машины перестали быть одной и той же BESM-6.