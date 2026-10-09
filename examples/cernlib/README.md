# CERNlib beacon fixtures (committed subset)

This directory holds a **small, committed subset** of the CERNlib CERN acceptance
corpus so the beacon and printing regression cases can run in a clean CI checkout, where the full
`ref/` tree (22.6 MB) is git-ignored and therefore absent.

The full corpus lives under the local `ref/tests/lib{1,2}/` (git-ignored).
The beacon cases are byte-exact copies of the corresponding `ref/tests` files.
The three printing cases are pinned directly to upstream commit
[`9cc404f4241e00c2d98e386a85cdf56eca7a74a9`](https://github.com/besm6/dubna/commit/9cc404f4241e00c2d98e386a85cdf56eca7a74a9)
(7 October 2026), which corrects their stale expected output:

| File                              | Source                     |
|-----------------------------------|----------------------------|
| `lib1/a400.f`, `lib1/expect_a400.txt` | `ref/tests/lib1/a400.f`, `expect_a400.txt` |
| `lib2/z005.f`, `lib2/expect_z005.txt` | `ref/tests/lib2/z005.f`, `expect_z005.txt` |
| `lib2/i312a.f`, `lib2/expect_i312a.txt` | upstream commit `9cc404f` |
| `lib2/j531a.f`, `lib2/expect_j531a.txt` | upstream commit `9cc404f` |
| `lib2/j531b.f`, `lib2/expect_j531b.txt` | upstream commit `9cc404f` |

The pinned sources are identical to the local reference sources. Expected files
include the GOST decimal exponent symbol `⏨` and the reference's corrected E64
rounding. They were not generated from C# output. See
[the evidence and documentation](../../reports/cernlib-golden-refresh.md) and
[SHA-256 checksums](../../reports/cernlib-golden-refresh.json).

## How tests find the data

`CernLibFixture.ResolveCaseDirectory` resolves each source/expected pair:

1. `BESM6_CERN_DATA` env variable (explicit override);
2. `<repo>/examples/cernlib/libN` when both source and expected files exist;
3. `RefTestsDir`: `<repo>/ref/tests`, then `<repo>/ref/dubna/tests`, then this subset;
4. a sentinel path — absent cases classify as `MissingSource` and the test
   is reported `Inconclusive` (skip), so the matrix degrades gracefully instead
of crashing when the corpus is absent. A partial committed pair never mixes
with files from the optional reference snapshot. An explicit existing data
directory is authoritative, including missing cases; it is not overridden.

The MONSYS + CERNlib runtime tapes (`monsys.9`, `librar.12`, `librar.37`) are
tracked under `tapes/` (Bundled, MIT), so the beacon cases execute in CI with
only the committed subset available.

## Regenerating the subset

```powershell
# copy the beacon files from the local corpus (byte-exact)
foreach ($f in @('lib1\*a400*','lib2\*z005*')) {
    Get-Item "ref\tests\$f" | Copy-Item -Destination "examples\cernlib\" -Force
}
```

Keep the copies byte-exact — they are MIT reference fixtures and must not be
re-encoded. Refresh the pinned printing files from the stated upstream revision,
not from an older local `ref/` snapshot. `.gitattributes` preserves their LF endings.
