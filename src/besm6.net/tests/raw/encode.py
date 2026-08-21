import sys

def encode_short(reg, opcode, addr):
    """Short instruction: bit19=0, opcode bits 17-12, addr bits 11-0, reg bits 23-20"""
    if opcode >= 64:
        # needs long encoding
        return None
    return (reg << 20) | (opcode << 12) | (addr & 0xFFF)

def encode_long(reg, opcode, addr):
    """Long instruction: bit19=1, opcode bits 19-12, addr bits 14-0, reg bits 23-20"""
    # opcode here is the 8-bit opcode value (0x80-0xF8)
    return (reg << 20) | ((opcode & 0xF8) << 12) | (addr & 0x7FFF)

def decode_half(rk, side):
    reg = (rk >> 20) & 0xF
    if (rk & (1 << 19)):
        addr = rk & 0x7FFF
        opcode = (rk >> 12) & 0xF8
        kind = "LONG"
    else:
        addr = rk & 0xFFF
        opcode = (rk >> 12) & 0x3F
        kind = "SHORT"
    print(f"  {side}: {kind} reg={reg} addr={addr} opcode={opcode} (0x{opcode:X})")
    return opcode

# ===== HELLO Program =====
# Layout (base = 512 dec = 01000 oct):
# 512: [E64 addr=513] [STOP]
# 513: Pointer: startReg=0 startAddr=515 endReg=0 endAddr=0
# 514: Info: format=0(GOST) finish=1 (bit 24)
# 515: GOST text: H(0x2D) I(0x28) END(0x7A)

print("=== HELLO ===")
# Word at 512: E64 instr (left) + STOP (right)
e64_half = encode_short(reg=0, opcode=52, addr=513)
stop_half = encode_long(reg=0, opcode=0xD8, addr=0)
word_512 = (e64_half << 24) | stop_half
print(f"W[512]: hex={word_512:012X} oct={word_512:o}")
print(f"  E64 half:  hex={e64_half:X} oct={e64_half:o}")
print(f"  STOP half: hex={stop_half:X} oct={stop_half:o}")
# Verify decode
rk = word_512 >> 24
print("  Decode LEFT:")
decode_half(rk, "LEFT")
rk2 = word_512 & 0xFFFFFF
print("  Decode RIGHT:")
decode_half(rk2, "RIGHT")

# Word at 513: Pointer
# startReg=0(startAddr=515) endReg=0(endAddr=0) flags=0
# (0<<44)|(515<<29)|(0<<25)|(0<<10)|0
ptr = (515 << 29) | 0
print(f"W[513] Pointer: hex={ptr:012X} oct={ptr:o}")

# Word at 514: Info (format=0=GOST, finish=1)
# finish is bit 24: (0<<44)|(0<<37)|(0<<25)|(1<<24)
info = (1 << 24)
print(f"W[514] Info: hex={info:012X} oct={info:o}")

# Word at 515: GOST text
# H = 0x2D, I = 0x28, END = 0x7A
# 6 bytes per word: byte0=MSB (bits 47-40), byte1=bits 39-32, ...
text = (0x2D << 40) | (0x28 << 32) | (0x7A << 24)
print(f"W[515] Text: hex={text:012X} oct={text:o}")

# Output all 16-octal-digit words
print("\n=== .dub raw words ===")
for name, w in [("512", word_512), ("513", ptr), ("514", info), ("515", text)]:
    print(f"`{w:016o}")

# ===== MATH Program =====
# Layout (base = 512 dec = 01000 oct):
# 512: [E50 addr=514] [E64 addr=515]
# 513: E50 arg: addr field = 0 (sqrt of operand in ACC)
# 514: E64 control addr = 515
# 515: Pointer: startReg=0 startAddr=517 endReg=0 endAddr=0
# 516: Info: format=3 (Real) digits=6 width=10 finish=1
# 517: Result value (in memory, will be set by E50)

print("\n=== MATH ===")
# E50: opcode=40 (050 oct), addr=0 (value in acc)
e50_half = encode_short(reg=0, opcode=40, addr=0)
# E64: opcode=52, addr=515
e64_math = encode_short(reg=0, opcode=52, addr=515)
word_math = (e50_half << 24) | e64_math
print(f"W[512]: hex={word_math:012X} oct={word_math:016o}")

# After E50, the result is in ACC. E64 PrintReal reads from memory.
# We need to store ACC to memory first. Use ZP (opcode 0): store acc to addr+m[reg]
# Actually let's use a different approach: E50 stores result to ACC,
# then we need to write ACC to memory before E64 can read it.
# ZP = opcode 0: aex = addr + m[reg]; store acc to aex
# addr = 517, reg = 0: store acc to 517
zp_half = encode_short(reg=0, opcode=0, addr=517)
# So word at 512: [E50 addr=0] [ZP addr=517]
word_512 = (e50_half << 24) | zp_half
print(f"W[512] (E50+ZP): hex={word_512:012X} oct={word_512:016o}")

# Word at 513: [E64 addr=515] [STOP]
e64_w = encode_short(reg=0, opcode=52, addr=515)
stop_w = encode_long(reg=0, opcode=0xD8, addr=0)
word_513 = (e64_w << 24) | stop_w
print(f"W[513] (E64+STOP): hex={word_513:012X} oct={word_513:016o}")

# Word at 515: Pointer (startAddr=517)
ptr_math = (517 << 29)
print(f"W[515] Pointer: hex={ptr_math:012X} oct={ptr_math:016o}")

# Word at 516: Info (format=3=Real, digits=6, width=10, finish=1)
# format in bits 44-47, digits in bits 25-31, width in bits 13-19, finish in bit 24
info_math = (3 << 44) | (6 << 25) | (10 << 13) | (1 << 24)
print(f"W[516] Info: hex={info_math:012X} oct={info_math:016o}")

# Word at 517: value (initial sqrt operand, e.g. 2.0 in BESM-6 format)
# BESM-6 real format: sign bit (47) + exponent (46-39) + mantissa (38-0)
# 2.0 = 1.0 * 2^1: sign=0, exponent offset = 2^8 (assumed), mantissa = 0.5 normalized
# Actually, BESM-6 uses excess-128 exponent.
# Let's use a simple value. The E50 will compute sqrt(acc).
# We need to pre-load ACC before E50 runs.
# The program starts at 512, so ACC is initially 0.
# We need a way to set ACC. ATX (opcode 8): acc = m[reg] + addr
# Actually let's just use the program to load a value.
# ATX = opcode 8: loads memory into acc
# Or just store a known value at 517, and load it first.
# 
# Simpler: use A+X (opcode 4) to add memory value to acc, starting from 0.
# Or use the initial value approach: the memory at 517 already has a value.
# 
# Let me restructure:
# 512: [ATX addr=517] [E50 addr=0]  -- load value from 517, compute sqrt
# 513: [ZP addr=518] [E64 addr=519] -- store result, print
# 514: [STOP] [NOP]
# 515: Pointer: startAddr=518
# 516: Info: format=3 (Real)
# 517: Initial value (2.0)
# 518: Result (written by ZP)
# 519: (E64 control block here)

print("\n=== MATH v2 (simpler) ===")
# ATX = opcode 8: acc += memory[addr + m[reg]], but we want to SET acc
# Actually looking at the instruction set:
# ATX (opcode 8): acc = m[reg] + addr ... no that's not right
# Let me just pre-set memory and use a different approach.
# 
# For the MVP, let's just print a constant using E64 Real format.
# Put value 4.0 (sqrt=2.0) at memory, load into ACC, compute, store, print.
# 
# Actually, the simplest approach for math.dub:
# Just use E64 to print a pre-computed constant (e.g. "2.0" via PrintReal format)
# and verify E64 Real output works.

# For the math test, let's just verify E64 can print a real number
# stored in memory. The program:
# 512: [E64 addr=514] [STOP]
# 513: (unused)
# 514: Pointer: startAddr=515
# 515: Info: format=3(Real) digits=4 width=10 finish=1
# 516: Value: 2.0 in BESM-6 float format

# BESM-6 float: bit 47 = sign, bits 46-39 = exponent (8 bits, excess-128), bits 38-0 = mantissa
# 2.0 = -1.0 * 2^1 ... actually:
# BESM-6: value = (-1)^sign * (1 + mantissa/2^39) * 2^(exp-128)
# 2.0 = 1.0 * 2^1: sign=0, exp = 128+1 = 129, mantissa = 0
# word = (129 << 39) | 0
val_2 = (129 << 39) | 0
print(f"Value 2.0: hex={val_2:012X} oct={val_2:016o}")

e64_m2 = encode_short(reg=0, opcode=52, addr=514)
stop_m2 = encode_long(reg=0, opcode=0xD8, addr=0)
word_m_512 = (e64_m2 << 24) | stop_m2
print(f"MATH W[512] (E64+STOP): oct={word_m_512:016o}")
print(f"MATH W[514] Pointer: oct={(516<<29):016o}")
info_m = (3 << 44) | (4 << 25) | (10 << 13) | (1 << 24)
print(f"MATH W[515] Info: oct={info_m:016o}")
print(f"MATH W[516] Value: oct={val_2:016o}")

# ===== IO Program =====
print("\n=== IO (E70 disk I/O) ===")
# E70 is complex (disk read/write). For MVP, let's just verify it doesn't crash.
# Simple: [E70 addr=0] [STOP]
# E70: opcode = 56 (070 oct)
e70_half = encode_short(reg=0, opcode=56, addr=0)
stop_io = encode_long(reg=0, opcode=0xD8, addr=0)
word_io = (e70_half << 24) | stop_io
print(f"IO W[512]: oct={word_io:016o}")