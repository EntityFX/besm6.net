#!/usr/bin/env bash
set -euo pipefail

# Target-computer benchmark. Requires .NET SDK, .NET 8 runtime and Python 3.
repo_root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd -- "$repo_root"
quick=false
skip_build=false
output="tests-run/cpu-target-$(date +%Y%m%d-%H%M%S)"
while (($#)); do
    case "$1" in
        --quick) quick=true; shift ;;
        --skip-build) skip_build=true; shift ;;
        --output)
            if (($# < 2)); then echo "--output requires a directory." >&2; exit 2; fi
            output="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: bash tools/run_cpu_benchmark.sh [--quick] [--skip-build] [--output DIRECTORY]"
            exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done
command -v dotnet >/dev/null || { echo "Install .NET SDK with the .NET 8 runtime." >&2; exit 2; }
if command -v python3 >/dev/null; then python=python3
elif command -v python >/dev/null; then python=python
else echo "Install Python 3." >&2; exit 2
fi
"$python" -c 'import sys; assert sys.version_info.major == 3, "Python 3 is required"'
if [[ -e "$output" ]]; then echo "Choose a fresh output directory; earlier measurements are retained." >&2; exit 2; fi
if [[ "$skip_build" == false ]]; then
    dotnet build src/Besm6.Cli/Besm6.Cli.csproj -c Release --verbosity minimal
fi
cli_dll="src/Besm6.Cli/bin/Release/net8.0/besm6.dll"
if [[ ! -f "$cli_dll" ]]; then echo "Release CLI is missing." >&2; exit 2; fi
jobs=(dh0 dh5000 dh10000 mp0 mp1 mp2 mp3)
if [[ "$quick" == true ]]; then jobs=(dh10000 mp3); fi
if "$python" tools/benchmark_speed.py --dll "$cli_dll" --jobs "${jobs[@]}" \
    --modes max original --warmups 1 --repeats 5 --timeout 600 --output "$output"; then
    benchmark_exit=0
else
    benchmark_exit=$?
fi
"$python" - "$output/results.json" "$benchmark_exit" <<'PY'
import json, pathlib, sys
path = pathlib.Path(sys.argv[1])
if not path.is_file():
    raise SystemExit("No benchmark result was produced.")
report = json.loads(path.read_text(encoding="utf-8"))
if "error" in report:
    raise SystemExit("Benchmark validation failed; see the local result and logs.")
if int(sys.argv[2]) not in (0, 1):
    raise SystemExit("Benchmark process failed.")
print("job       speed      loop seconds  model seconds  tempo met")
for row in report["measurements"]:
    print(f"{row['job']:<9} {row['variant']:<10} {row['loop_seconds']:>12.6f} "
          f"{row['model_seconds']:>14.6f} {row['original_tempo_met']}")
late = [row for row in report["measurements"]
        if row["variant"] == "original" and row["original_tempo_met"] is False]
print(f"Host falls behind the model tempo on {len(late)} workloads." if late
      else "Original model tempo met on all selected workloads.")
print("Keep results.json and results.csv. Raw logs remain local.")
PY
