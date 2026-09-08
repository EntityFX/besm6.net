# CERNlib beacon fixtures (committed subset)

This directory holds a **small, committed subset** of the CERNlib CERN acceptance
corpus so the two beacon cases can run in a clean CI checkout, where the full
`ref/` tree (22.6 MB) is git-ignored and therefore absent.

The full corpus lives under the local `ref/tests/lib{1,2}/` (git-ignored).
Each case below is a byte-exact copy of the corresponding `ref/tests` files:

| File                              | Source                     |
|-----------------------------------|----------------------------|
| `lib1/a400.f`, `lib1/expect_a400.txt` | `ref/tests/lib1/a400.f`, `expect_a400.txt` |
| `lib2/z005.f`, `lib2/expect_z005.txt` | `ref/tests/lib2/z005.f`, `expect_z005.txt` |

## How tests find the data

`CernLibFixture.RefTestsDir` resolves the CERN data directory in priority order:

1. `BESM6_CERN_DATA` env variable (explicit override);
2. `<repo>/ref/tests` — full corpus (dev / local snapshot);
3. `<repo>/ref/dubna/tests` — alt local layout;
4. `<repo>/examples/cernlib` — this committed subset (present in CI);
5. a sentinel path — every case then classifies as `MissingSource` and the test
   is reported `Inconclusive` (skip), so the matrix degrades gracefully instead
   of crashing when the corpus is absent.

The MONSYS + CERNlib runtime tapes (`monsys.9`, `librar.12`, `librar.37`) are
tracked under `tapes/` (Bundled, MIT), so the beacon cases execute in CI with
only the four data files above committed.

## Regenerating the subset

```powershell
# copy the beacon files from the local corpus (byte-exact)
foreach ($f in @('lib1\*a400*','lib2\*z005*')) {
    Get-Item "ref\tests\$f" | Copy-Item -Destination "examples\cernlib\" -Force
}
```

Keep the copies byte-exact — they are MIT reference fixtures and must not be
re-encoded.
