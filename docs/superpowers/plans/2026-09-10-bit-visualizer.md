# BESM-6 Bit Visualizer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Russian-language .NET 8 Windows Forms application that visually decodes bit patterns for BESM-6 numbers and instructions, modern integers, IEEE-754 values, and radix conversions while accumulating a shared educational log.

**Architecture:** Extend the existing platform-neutral `Besm6.Architecture` assembly with immutable input/result models and pure decoders. Add one `Besm6.BitVisualizer.WinForms` production project whose reusable bit-grid control and five independent pages call the core and publish reports to one shared log. Keep all formulas and validation out of WinForms code.

**Tech Stack:** C# 12, .NET 8, Windows Forms, `System.Numerics.BigInteger`, MSTest 4, existing `Word48` and `InstructionCodec`.

**Spec:** `docs/superpowers/specs/2026-09-10-bit-visualizer-design.md`

## Global Constraints

- Target C#/.NET 8 and Windows Forms on Windows.
- Deliver exactly two production assemblies for this application: existing `Besm6.Architecture.dll` as core and new `Besm6.BitVisualizer.WinForms.dll` as UI.
- Reuse the existing `Word48` and `InstructionCodec` types without copying them.
- Display all UI and calculation explanations in Russian.
- Provide five tabs: native BESM-6 float, modern integers, IEEE-754, 24-bit BESM-6 instruction, and radix converter.
- Accumulate every calculation and validation error in one shared, labeled log.
- Render bits from most significant to least significant in aligned groups of eight with labeled, accessible semantic colors.
- Preserve unrelated working-tree changes.
- Follow red-green-refactor: every production behavior starts with a failing test.

---

### Task 1: Universal bit and report primitives

**Files:**
- Create: `src/Besm6.Architecture/Visualization/BitPattern.cs`
- Create: `src/Besm6.Architecture/Visualization/BitField.cs`
- Create: `src/Besm6.Architecture/Visualization/CalculationReport.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/BitPatternTests.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/CalculationReportTests.cs`

**Interfaces:**
- Produces: `BitPattern(int width, BigInteger unsignedValue)`, `BitPattern.FromMsbFirst(IEnumerable<bool>)`, `Width`, `UnsignedValue`, `GetBit(int)`, `ToBinaryString()`, `ToOctalString()`, and `ToHexString()`.
- Produces: `BitFieldKind`, `BitField(string Name, int MostSignificantBit, int LeastSignificantBit, BitFieldKind Kind)`.
- Produces: `CalculationReport(string Operation, string Input, string Result, IReadOnlyList<string> Steps)` and `Render()`.

- [ ] **Step 1: Write failing primitive tests**

```csharp
[TestMethod]
public void FromMsbFirst_PreservesVisualOrder()
{
    BitPattern bits = BitPattern.FromMsbFirst([true, false, true, true]);
    Assert.AreEqual(4, bits.Width);
    Assert.AreEqual(new BigInteger(11), bits.UnsignedValue);
    Assert.AreEqual("1011", bits.ToBinaryString());
    Assert.IsTrue(bits.GetBit(3));
    Assert.IsTrue(bits.GetBit(0));
}

[TestMethod]
public void Constructor_RejectsValueOutsideWidth()
{
    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BitPattern(8, 256));
}

[TestMethod]
public void Formatting_PadsToDeclaredWidth()
{
    var bits = new BitPattern(12, 0x2A);
    Assert.AreEqual("000000101010", bits.ToBinaryString());
    Assert.AreEqual("0052", bits.ToOctalString());
    Assert.AreEqual("02A", bits.ToHexString());
}
```

- [ ] **Step 2: Run tests and confirm missing-type failures**

Run: `dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj --filter "FullyQualifiedName~BitPatternTests|FullyQualifiedName~CalculationReportTests"`

Expected: compilation fails because the visualization types do not exist.

- [ ] **Step 3: Implement immutable primitives**

Implement `BitPattern` with `BigInteger`, reject width below 1, negative values, values at or above `BigInteger.One << width`, and bit indexes outside `0..Width-1`. Build base-2/8/16 strings without narrowing the value and pad them to `ceil(width / bitsPerDigit)`.

Implement the semantic enum values `Value`, `Sign`, `Exponent`, `Fraction`, `Register`, `Format`, `Opcode`, and `Address`. Validate `BitField` names and inclusive bounds.

Implement `CalculationReport.Render()` exactly as operation, `Вход: ...`, numbered steps, and `Результат: ...`, separated by new lines. Copy incoming collections to arrays so callers cannot mutate results.

- [ ] **Step 4: Run targeted tests until green**

Run the Task 1 test command. Expected: all selected tests pass with zero warnings.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- src/Besm6.Architecture/Visualization tests/Besm6.Architecture.Tests/Visualization
git commit -m "feat(architecture): add bit visualization primitives"
```

---

### Task 2: Signed and unsigned integer decoding

**Files:**
- Create: `src/Besm6.Architecture/Visualization/IntegerDecoder.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/IntegerDecoderTests.cs`

**Interfaces:**
- Consumes: `BitPattern`, `BitField`, and `CalculationReport` from Task 1.
- Produces: `IntegerInterpretation { Unsigned, Signed }`.
- Produces: `IntegerDecodeResult(BigInteger Value, IntegerInterpretation Interpretation, IReadOnlyList<BitField> Fields, CalculationReport Report)`.
- Produces: `IntegerDecoder.Decode(BitPattern bits, IntegerInterpretation interpretation)`.

- [ ] **Step 1: Write failing decoder tests**

```csharp
[DataTestMethod]
[DataRow(8, "11111111", "255")]
[DataRow(16, "1000000000000000", "32768")]
[DataRow(64, "1111111111111111111111111111111111111111111111111111111111111111", "18446744073709551615")]
public void Decode_UnsignedUsesAllPositiveWeights(int width, string binary, string expected)
{
    var result = IntegerDecoder.Decode(Bits(binary), IntegerInterpretation.Unsigned);
    Assert.AreEqual(BigInteger.Parse(expected), result.Value);
    Assert.AreEqual(IntegerInterpretation.Unsigned, result.Interpretation);
}

[DataTestMethod]
[DataRow("10000000", "-128")]
[DataRow("11111111", "-1")]
[DataRow("01111111", "127")]
public void Decode_SignedUsesTwosComplement(string binary, string expected)
{
    var result = IntegerDecoder.Decode(Bits(binary), IntegerInterpretation.Signed);
    Assert.AreEqual(BigInteger.Parse(expected), result.Value);
    StringAssert.Contains(result.Report.Render(), "дополнительном коде");
}
```

The local `Bits` helper maps each character to `c == '1'` and calls `BitPattern.FromMsbFirst`.

- [ ] **Step 2: Run tests and confirm RED**

Run: `dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj --filter FullyQualifiedName~IntegerDecoderTests`

Expected: compilation fails because `IntegerDecoder` is missing.

- [ ] **Step 3: Implement integer decoding and educational steps**

Accept only widths 8, 16, 32, and 64. For signed values compute:

```csharp
BigInteger signWeight = BigInteger.One << (bits.Width - 1);
BigInteger value = bits.GetBit(bits.Width - 1)
    ? bits.UnsignedValue - (BigInteger.One << bits.Width)
    : bits.UnsignedValue;
```

List each set-bit contribution in descending bit order. Use negative sign weight only when signed mode has its top bit set. Return one `Sign` field plus one `Value` field in signed mode; return a single `Value` field in unsigned mode.

- [ ] **Step 4: Run targeted tests until green**

Run the Task 2 command and then the Task 1 command. Expected: all pass.

- [ ] **Step 5: Commit Task 2**

```powershell
git add -- src/Besm6.Architecture/Visualization/IntegerDecoder.cs tests/Besm6.Architecture.Tests/Visualization/IntegerDecoderTests.cs
git commit -m "feat(architecture): decode signed and unsigned integers"
```

---

### Task 3: IEEE-754 decoding

**Files:**
- Create: `src/Besm6.Architecture/Visualization/Ieee754Decoder.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/Ieee754DecoderTests.cs`

**Interfaces:**
- Consumes: Task 1 primitives.
- Produces: `Ieee754Class { Normal, Subnormal, PositiveZero, NegativeZero, PositiveInfinity, NegativeInfinity, NaN }`.
- Produces: `Ieee754DecodeResult(double Value, Ieee754Class Classification, int RawExponent, BigInteger Fraction, IReadOnlyList<BitField> Fields, CalculationReport Report)`.
- Produces: `Ieee754Decoder.Decode(BitPattern bits)` for widths 16, 32, and 64.

- [ ] **Step 1: Write failing known-pattern and special-value tests**

```csharp
[DataTestMethod]
[DataRow("0011110000000000", 1.0, Ieee754Class.Normal)]
[DataRow("00111111100000000000000000000000", 1.0, Ieee754Class.Normal)]
[DataRow("1011111111110000000000000000000000000000000000000000000000000000", -1.0, Ieee754Class.Normal)]
public void Decode_KnownPatterns(string binary, double expected, Ieee754Class expectedClass)
{
    var result = Ieee754Decoder.Decode(Bits(binary));
    Assert.AreEqual(expected, result.Value);
    Assert.AreEqual(expectedClass, result.Classification);
}

[DataTestMethod]
[DataRow("1000000000000000", Ieee754Class.NegativeZero)]
[DataRow("0111110000000000", Ieee754Class.PositiveInfinity)]
[DataRow("0111111000000000", Ieee754Class.NaN)]
[DataRow("0000000000000001", Ieee754Class.Subnormal)]
public void Decode_ClassifiesBinary16Specials(string binary, Ieee754Class expected)
{
    Assert.AreEqual(expected, Ieee754Decoder.Decode(Bits(binary)).Classification);
}
```

- [ ] **Step 2: Run IEEE tests and confirm RED**

Run: `dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj --filter FullyQualifiedName~Ieee754DecoderTests`

Expected: compilation fails because the decoder types are missing.

- [ ] **Step 3: Implement format metadata, classification, formula, and runtime cross-check**

Map widths to exponent/fraction/bias tuples `(5,10,15)`, `(8,23,127)`, and `(11,52,1023)`. Extract fields with `BigInteger` masks. Compute runtime values using `UInt16BitsToHalf`, `Int32BitsToSingle`, and `Int64BitsToDouble`. Build separate report branches for zero, infinity, NaN, subnormal, and normal values, using invariant-culture round-trip formatting.

- [ ] **Step 4: Run targeted and primitive tests until green**

Run the Task 3 command followed by all `Visualization` tests. Expected: all pass.

- [ ] **Step 5: Commit Task 3**

```powershell
git add -- src/Besm6.Architecture/Visualization/Ieee754Decoder.cs tests/Besm6.Architecture.Tests/Visualization/Ieee754DecoderTests.cs
git commit -m "feat(architecture): add IEEE-754 visual decoder"
```

---

### Task 4: BESM-6 native float and instruction decoding

**Files:**
- Create: `src/Besm6.Architecture/Visualization/Besm6FloatDecoder.cs`
- Create: `src/Besm6.Architecture/Visualization/Besm6InstructionDecoder.cs`
- Create: `src/Besm6.Architecture/Visualization/OpcodeInfoCatalog.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/Besm6FloatDecoderTests.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/Besm6InstructionDecoderTests.cs`

**Interfaces:**
- Consumes: `Word48`, `InstructionCodec`, `DecodedInstruction`, and Task 1 primitives.
- Produces: `Besm6FloatDecodeResult(Word48 Word, double Value, int RawExponent, int Exponent, long Mantissa, IReadOnlyList<BitField> Fields, CalculationReport Report)`.
- Produces: `Besm6InstructionDecodeResult(DecodedInstruction Instruction, string Mnemonic, string Description, IReadOnlyList<BitField> Fields, CalculationReport Report)`.
- Produces: `Besm6FloatDecoder.Decode(BitPattern)` and `Besm6InstructionDecoder.Decode(BitPattern)`.

- [ ] **Step 1: Write failing BESM-6 tests**

```csharp
[TestMethod]
public void FloatDecoder_UsesCanonicalWord48Conversion()
{
    Word48 word = Word48.FromDouble(-3.5);
    var result = Besm6FloatDecoder.Decode(new BitPattern(48, word.Value));
    Assert.AreEqual(word.ToDouble(), result.Value);
    Assert.AreEqual(48, result.Fields.Max(f => f.MostSignificantBit) + 1);
    StringAssert.Contains(result.Report.Render(), "2^40");
}

[TestMethod]
public void InstructionDecoder_DecodesShortCommandAndExplainsFields()
{
    var source = new DecodedInstruction(3, Opcode.APlusX, 0x123, InstructionFormat.Short);
    uint raw = InstructionCodec.EncodeHalf(source);
    var result = Besm6InstructionDecoder.Decode(new BitPattern(24, raw));
    Assert.AreEqual(source, result.Instruction);
    Assert.AreEqual("СЛ", result.Mnemonic);
    StringAssert.Contains(result.Description, "сложение");
    StringAssert.Contains(result.Report.Render(), "маска");
}

[TestMethod]
public void InstructionDecoder_LongFormatReturnsDynamicFieldLayout()
{
    var source = new DecodedInstruction(2, Opcode.Stop, 0x4567, InstructionFormat.Long);
    var result = Besm6InstructionDecoder.Decode(new BitPattern(24, InstructionCodec.EncodeHalf(source)));
    Assert.AreEqual(InstructionFormat.Long, result.Instruction.Format);
    Assert.IsTrue(result.Fields.Any(f => f.Kind == BitFieldKind.Format));
    Assert.IsTrue(result.Fields.Any(f => f.Kind == BitFieldKind.Address && f.LeastSignificantBit == 0));
}
```

- [ ] **Step 2: Run BESM-6 tests and confirm RED**

Run: `dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj --filter "FullyQualifiedName~Besm6FloatDecoderTests|FullyQualifiedName~Besm6InstructionDecoderTests"`

Expected: compilation fails because the new decoders do not exist.

- [ ] **Step 3: Implement native float extraction through `Word48`**

Require width 48. Create `new Word48((ulong)bits.UnsignedValue)`, extract raw exponent from bits 47–41, and sign-extend the lower 41 bits:

```csharp
long mantissa = (long)(word.Value & ((1UL << 41) - 1));
if ((mantissa & (1L << 40)) != 0)
    mantissa |= ~((1L << 41) - 1);
```

Return `word.ToDouble()` as the authoritative value and explain `mantissa / 2^40 × 2^(rawExponent - 64)`.

- [ ] **Step 4: Implement instruction adapter and complete opcode catalogue**

Require width 24 and decode with `InstructionCodec.DecodeHalf`. Provide Russian mnemonic/description entries for every named `Opcode`; for unnamed short codes 050–077 octal return a numeric `*NN` mnemonic and the description `Экстракод БЭСМ-6`. Return short fields for register 23–20, format 19, extension 18, opcode 17–12, address 11–0; return long fields for register 23–20, format 19, opcode 18–15, address 14–0.

- [ ] **Step 5: Run targeted tests and all existing architecture tests**

Run the Task 4 command, then `dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj`. Expected: all pass.

- [ ] **Step 6: Commit Task 4**

```powershell
git add -- src/Besm6.Architecture/Visualization/Besm6FloatDecoder.cs src/Besm6.Architecture/Visualization/Besm6InstructionDecoder.cs src/Besm6.Architecture/Visualization/OpcodeInfoCatalog.cs tests/Besm6.Architecture.Tests/Visualization/Besm6FloatDecoderTests.cs tests/Besm6.Architecture.Tests/Visualization/Besm6InstructionDecoderTests.cs
git commit -m "feat(architecture): explain BESM-6 words and instructions"
```

---

### Task 5: Arbitrary-precision radix conversion

**Files:**
- Create: `src/Besm6.Architecture/Visualization/RadixConverter.cs`
- Create: `tests/Besm6.Architecture.Tests/Visualization/RadixConverterTests.cs`

**Interfaces:**
- Consumes: `CalculationReport`.
- Produces: `RadixConversionResult(BigInteger Value, string Output, CalculationReport Report)`.
- Produces: `RadixConverter.Convert(string input, int sourceBase, int targetBase)` for bases 2–36.

- [ ] **Step 1: Write failing conversion and validation tests**

```csharp
[DataTestMethod]
[DataRow("11111111", 2, 10, "255")]
[DataRow("777", 8, 16, "1FF")]
[DataRow("-32768", 10, 16, "-8000")]
[DataRow("Z", 36, 2, "100011")]
public void Convert_ReturnsExpectedDigits(string input, int from, int to, string expected)
{
    var result = RadixConverter.Convert(input, from, to);
    Assert.AreEqual(expected, result.Output);
    Assert.IsTrue(result.Report.Steps.Count > 0);
}

[TestMethod]
public void Convert_RejectsDigitOutsideSourceBase()
{
    var error = Assert.ThrowsExactly<FormatException>(() => RadixConverter.Convert("102", 2, 10));
    StringAssert.Contains(error.Message, "2");
}
```

- [ ] **Step 2: Run radix tests and confirm RED**

Run: `dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj --filter FullyQualifiedName~RadixConverterTests`

Expected: compilation fails because `RadixConverter` is missing.

- [ ] **Step 3: Implement parsing, conversion, and both explanation phases**

Trim input, consume one optional leading minus, map `0-9A-Z` digits, and reject empty magnitude or invalid digits. Parse using `value = value * sourceBase + digit`. Format zero directly; otherwise repeatedly call `BigInteger.DivRem` on the absolute value, collect remainder digits, reverse them, and restore the sign. Record every accumulation and division step.

- [ ] **Step 4: Run radix and all visualization tests until green**

Run the Task 5 command, then all tests matching `FullyQualifiedName~Visualization`. Expected: all pass.

- [ ] **Step 5: Commit Task 5**

```powershell
git add -- src/Besm6.Architecture/Visualization/RadixConverter.cs tests/Besm6.Architecture.Tests/Visualization/RadixConverterTests.cs
git commit -m "feat(architecture): add explained radix conversion"
```

---

### Task 6: WinForms shell, bit grid, and shared log

**Files:**
- Create: `src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj`
- Create: `src/Besm6.BitVisualizer.WinForms/Program.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Theme.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Controls/BitGridControl.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Controls/SharedLogControl.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/MainForm.cs`
- Create: `tests/Besm6.BitVisualizer.WinForms.Tests/Besm6.BitVisualizer.WinForms.Tests.csproj`
- Create: `tests/Besm6.BitVisualizer.WinForms.Tests/BitGridControlTests.cs`
- Create: `tests/Besm6.BitVisualizer.WinForms.Tests/SharedLogControlTests.cs`

**Interfaces:**
- Consumes: `BitPattern`, `BitField`, and `CalculationReport`.
- Produces: `BitGridControl.Configure(int width, IReadOnlyList<BitField> fields)`, `ToBitPattern()`, `SetValue(BigInteger)`, `Clear()`, `BitCount`, and `GetDisplayedBitIndex(int visualIndex)`.
- Produces: `SharedLogControl.Append(CalculationReport report, DateTime timestamp)`, `AppendError(string category, string input, string message, DateTime timestamp)`, `Text`, and `ClearLog()`.
- Produces: `MainForm.Publish(CalculationReport)` and `PublishError(...)` used by pages.

- [ ] **Step 1: Create test project and write failing control tests**

```csharp
[STATestMethod]
public void Configure_CreatesBitsFromMostToLeastSignificant()
{
    using var grid = new BitGridControl();
    grid.Configure(24, [new BitField("Значение", 23, 0, BitFieldKind.Value)]);
    Assert.AreEqual(24, grid.BitCount);
    Assert.AreEqual(23, grid.GetDisplayedBitIndex(0));
    Assert.AreEqual(0, grid.GetDisplayedBitIndex(23));
}

[STATestMethod]
public void Append_AccumulatesLabeledReports()
{
    using var log = new SharedLogControl();
    log.Append(new CalculationReport("Первая", "0", "0", ["Шаг"]), new DateTime(2026, 9, 10, 12, 0, 0));
    log.Append(new CalculationReport("Вторая", "1", "1", ["Шаг"]), new DateTime(2026, 9, 10, 12, 1, 0));
    StringAssert.Contains(log.Text, "[12:00:00] Первая");
    StringAssert.Contains(log.Text, "[12:01:00] Вторая");
}
```

- [ ] **Step 2: Run UI tests and confirm RED**

Run: `dotnet test tests/Besm6.BitVisualizer.WinForms.Tests/Besm6.BitVisualizer.WinForms.Tests.csproj`

Expected: compilation fails because the UI project and controls are incomplete.

- [ ] **Step 3: Implement project, theme, and reusable controls**

Set UI target to `net8.0-windows`, `OutputType` to `WinExe`, `UseWindowsForms` to true, and reference `Besm6.Architecture`. Set the test target to `net8.0-windows`, enable Windows Forms, use the repository MSTest package versions, and reference UI and Architecture.

Build `BitGridControl` with an `AutoScroll` panel of `TableLayoutPanel` byte groups. Each group contains eight fixed-size cells; each cell contains a bit-number label and centered checkbox. Use `AccessibleName` containing field name and bit number. Map semantic kinds to the spec palette in `Theme` and expose no decoding logic.

Build `SharedLogControl` from a toolbar and read-only monospaced `RichTextBox`. Append two blank lines between reports, prefix the timestamp, preserve history, select the newly appended block, and scroll it into view. Implement Copy through `Clipboard.SetText` only when text exists.

- [ ] **Step 4: Implement `MainForm` shell**

Use a horizontal `SplitContainer` with tabs in panel 1 and shared log in panel 2, minimum size 1280×800, DPI autoscaling, Russian title, and stable minimum panel sizes. Add temporary empty tab pages with the five final names so the shell is runnable.

- [ ] **Step 5: Run UI tests until green and build UI project**

Run the Task 6 test command and `dotnet build src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj -c Release`. Expected: tests and build pass with zero warnings.

- [ ] **Step 6: Commit Task 6**

```powershell
git add -- src/Besm6.BitVisualizer.WinForms tests/Besm6.BitVisualizer.WinForms.Tests
git commit -m "feat(ui): add visualizer shell and bit controls"
```

---

### Task 7: Five functional pages wired to the common log

**Files:**
- Create: `src/Besm6.BitVisualizer.WinForms/Pages/CalculationPageBase.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Pages/Besm6FloatPage.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Pages/IntegerPage.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Pages/Ieee754Page.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Pages/InstructionPage.cs`
- Create: `src/Besm6.BitVisualizer.WinForms/Pages/RadixConverterPage.cs`
- Modify: `src/Besm6.BitVisualizer.WinForms/MainForm.cs`
- Create: `tests/Besm6.BitVisualizer.WinForms.Tests/PageContractTests.cs`

**Interfaces:**
- Consumes: all core decoders and Task 6 controls.
- Produces: `CalculationPageBase.ReportCreated` and `CalculationPageBase.ErrorCreated` events.
- Produces public read-only testing properties: each page's `BitCount`, chosen width/mode, displayed result, and action method `Calculate()` or `Decode()`.

- [ ] **Step 1: Write failing page contract tests**

```csharp
[STATestMethod]
public void MainForm_ContainsFiveFunctionalTabsAndOneSharedLog()
{
    using var form = new MainForm();
    CollectionAssert.AreEqual(
        new[] { "Число БЭСМ-6", "Целые числа", "IEEE-754", "Команда БЭСМ-6", "Системы счисления" },
        form.TabNames.ToArray());
    Assert.IsNotNull(form.SharedLog);
}

[STATestMethod]
public void IntegerPage_SignedCheckBoxChangesResultAndSignColoring()
{
    using var page = new IntegerPage();
    page.SetWidth(8);
    page.SetBits(new BigInteger(0xFF));
    page.Signed = false;
    page.Calculate();
    Assert.AreEqual("255", page.ResultText);
    page.Signed = true;
    page.Calculate();
    Assert.AreEqual("-1", page.ResultText);
    Assert.AreEqual(BitFieldKind.Sign, page.MostSignificantBitKind);
}

[STATestMethod]
public void EachPage_PublishesOneReportForOneCalculation()
{
    using var form = new MainForm();
    foreach (CalculationPageBase page in form.CalculationPages)
    {
        int before = form.LogEntryCount;
        page.RunDefaultCalculation();
        Assert.AreEqual(before + 1, form.LogEntryCount, page.Name);
    }
}
```

- [ ] **Step 2: Run page tests and confirm RED**

Run: `dotnet test tests/Besm6.BitVisualizer.WinForms.Tests/Besm6.BitVisualizer.WinForms.Tests.csproj --filter FullyQualifiedName~PageContractTests`

Expected: compilation fails because pages and final shell properties are missing.

- [ ] **Step 3: Implement common page layout and report events**

`CalculationPageBase` creates a scrollable vertical table with title/help text, input area, action row, and result card. It catches only expected `ArgumentException`/`FormatException` around user actions, displays an inline red validation label, and raises an error event. Unexpected exceptions are raised as neutral diagnostic errors while stack details are included only under `#if DEBUG`.

- [ ] **Step 4: Implement bit-based pages**

`Besm6FloatPage` fixes 48 bits with exponent and mantissa colors and invokes `Besm6FloatDecoder`.

`IntegerPage` offers widths 8/16/32/64 and a `Знаковое число (дополнительный код)` checkbox. Width change clears bits while preserving the checkbox. Mode change recolors the top bit. It invokes `IntegerDecoder` with the selected mode.

`Ieee754Page` offers `binary16 (Half)`, `binary32 (float)`, and `binary64 (double)`, clears bits on format change, configures sign/exponent/fraction fields, and invokes `Ieee754Decoder`.

`InstructionPage` fixes 24 bits, watches bit 19 to switch the short/long field layout immediately, and invokes `Besm6InstructionDecoder` only from its button.

Each page shows compact result labels and raises the complete report exactly once.

- [ ] **Step 5: Implement radix page**

Use one input textbox, two numeric selectors constrained to 2–36, four quick-base buttons per selector, a swap button, a read-only result textbox, and an inline error label. Invoke `RadixConverter.Convert`, preserve the original input on error, and raise the report once on success.

- [ ] **Step 6: Replace placeholder tabs and wire common log**

Construct each page once in `MainForm`, add it to the correctly named tab, subscribe both events to the shared log, and expose read-only test views without duplicating UI state. Set initial splitter distance after `OnShown` so minimum panel sizes are respected.

- [ ] **Step 7: Run all UI tests and build UI**

Run `dotnet test tests/Besm6.BitVisualizer.WinForms.Tests/Besm6.BitVisualizer.WinForms.Tests.csproj` and `dotnet build src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj -c Release`. Expected: all pass with no warnings.

- [ ] **Step 8: Commit Task 7**

```powershell
git add -- src/Besm6.BitVisualizer.WinForms tests/Besm6.BitVisualizer.WinForms.Tests
git commit -m "feat(ui): add five bit visualizer pages"
```

---

### Task 8: Solution integration, documentation, and end-to-end verification

**Files:**
- Modify: `Besm6.sln`
- Create: `docs/bit-visualizer.md`
- Modify: `src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj`

**Interfaces:**
- Consumes: completed core, UI, and test projects.
- Produces: discoverable solution projects and user launch instructions.

- [ ] **Step 1: Add solution-level failing presence check**

Before adding projects, run:

```powershell
dotnet sln Besm6.sln list | Select-String 'Besm6.BitVisualizer'
```

Expected: no matches, proving the new projects are not yet integrated.

- [ ] **Step 2: Add projects to the solution without rewriting unrelated entries**

Run:

```powershell
dotnet sln Besm6.sln add src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj --solution-folder src
dotnet sln Besm6.sln add tests/Besm6.BitVisualizer.WinForms.Tests/Besm6.BitVisualizer.WinForms.Tests.csproj --solution-folder tests
```

- [ ] **Step 3: Write launch and feature documentation**

Document prerequisites, `dotnet run --project src/Besm6.BitVisualizer.WinForms`, the purpose of all five tabs, the signed checkbox, color legend, common-log accumulation, and Copy/Clear actions. State that all calculations occur locally.

- [ ] **Step 4: Run fresh automated verification**

Run:

```powershell
dotnet test tests/Besm6.Architecture.Tests/Besm6.Architecture.Tests.csproj -c Release
dotnet test tests/Besm6.BitVisualizer.WinForms.Tests/Besm6.BitVisualizer.WinForms.Tests.csproj -c Release
dotnet build Besm6.sln -c Release
```

Expected: every command exits 0 with no failed tests, errors, or warnings.

- [ ] **Step 5: Launch and visually inspect the application**

Run `dotnet run --project src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj -c Release`. Verify five visible tabs, exact bit counts 48/8–64/16–64/24, readable category colors, aligned byte groups, resize behavior, signed-mode recoloring, immediate instruction-format recoloring, one successful operation per page, five labeled entries in the shared log, Copy, Clear, and one invalid radix input recorded as an error.

- [ ] **Step 6: Review the complete requirement diff**

Run `git diff --check` and `git status --short`. Inspect only files created or intentionally modified by this plan, and confirm unrelated pre-existing changes remain untouched.

- [ ] **Step 7: Commit integration and documentation**

```powershell
git add -- Besm6.sln docs/bit-visualizer.md src/Besm6.BitVisualizer.WinForms/Besm6.BitVisualizer.WinForms.csproj
git commit -m "docs: integrate BESM-6 bit visualizer"
```

- [ ] **Step 8: Final completion audit**

Re-read `docs/superpowers/specs/2026-09-10-bit-visualizer-design.md` line by line. For every criterion, identify a passing test, successful build output, or visual observation. Do not report completion while any criterion lacks evidence.
