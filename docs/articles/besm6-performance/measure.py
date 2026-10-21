#!/usr/bin/env python3
"""Run the article's native FOREX workloads serially; keep raw evidence."""
import argparse
import csv
import hashlib
import json
import math
import platform
import re
import statistics
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DLL = ROOT / 'src/Besm6.Cli/bin/Release/net8.0/besm6.dll'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def number(output, label):
    match = re.search(r'^' + re.escape(label) + r'\s*([-+\d.Ee]+)', output, re.M)
    if not match:
        raise ValueError('Missing field: ' + label)
    return float(match[1])


def close(actual, expected, tolerance=2e-6):
    if not math.isclose(actual, expected, rel_tol=tolerance, abs_tol=tolerance):
        raise ValueError(f'{actual} != {expected}')


def validate(output, family, part):
    if 'Halted by STOP' not in output:
        raise ValueError('Guest termination missing')
    if number(output, 'BAD CONTROL VALUES=' if family != 'memspeed' else 'BAD ELEMENTS=') != 0:
        raise ValueError('Guest controls failed')
    secs = number(output, 'GUEST CPU SECONDS:')
    if secs < .04:
        raise ValueError('Interval below timer guard')
    if family == 'dhrystone':
        if 'ALL 18 CONTROL VALUES MATCH' not in output or 'ALL 4 STRING VALUES MATCH: YES' not in output:
            raise ValueError('Dhrystone controls missing')
        expected = {'IGLOB:': 5, 'A2GLOB 8/7:': 5010, 'I1LOC:': 5, 'I2LOC:': 13, 'I3LOC:': 7}
        for label, value in expected.items():
            close(number(output, label), value)
        rating = number(output, 'GUEST DMIPS:')
        close(rating, 5000 / (1757 * secs))
        return secs, rating, 'DMIPS', None
    if family == 'whetstone':
        refs = [-1.13333333059052, -1.13333328610525, 1, 12, .50000005785012, .99999980904064, 3, .75792040197525]
        work = [153600, 1075200, 828000, 2520000, 665600, 4315200, 1478400, 297600]
        sections = []
        for i, ref in enumerate(refs, 1):
            m = re.search(r'^N' + str(i) + r'\s+.+?\s+(-?\d+\.\d+)\s+(\d+\.\d+)\s+(\d+\.\d+)\s*$', output, re.M)
            if not m:
                raise ValueError(f'Missing section N{i}')
            value, rate, seconds = map(float, m.groups())
            close(value, ref, 5e-6)
            close(rate, work[i-1] / (1e6 * seconds))
            sections.append(dict(section=i, value=value, rate=rate, seconds=seconds))
        close(secs, sum(s['seconds'] for s in sections))
        rating = number(output, 'GUEST MWIPS:')
        close(rating, 80 / secs)
        gm = number(output, 'GUEST MFLOPS GEOMEAN N1 N2 N6:')
        close(gm, math.prod(sections[i]['rate'] for i in [0, 1, 5]) ** (1/3))
        return secs, rating, 'MWIPS', dict(sections=sections, mflops_geomean=gm)
    close(number(output, 'WORDS='), 4096 if family == 'memspeed' else 9000)
    close(number(output, 'REPEAT PASSES='), 1000 if family == 'memspeed' else 30)
    if family == 'memspeed':
        work = 4096 * 1000 * (12 if part == 1 else 18)
        expected = [(1/4096, 1, 2048.5), (1+1000/4096, 1001, 2052596), (1+500/4096, 501, 1028346)][part-1]
        for label, value in zip(['FIRST RESULT=', 'LAST RESULT=', 'CHECKSUM='], expected):
            close(number(output, label), value, 1e-9)
        close(number(output, 'DATA TRAFFIC BYTES='), work)
        unit = 'MB/S'
    else:
        ops = [2, 8, 32][part-1]
        work = 9000 * 30 * ops
        close(number(output, 'OPS/WORD='), ops)
        close(number(output, 'FLOPS='), work)
        value = .999999
        a = [.000020, .000011, .000012, .000013, .000014, .000015, .000016, .000017, .000018, .000019, .000021]
        b = [.999980, 1.000011, .999992, 1.000013, .999994, 1.000015, .999996, 1.000017, 1.000018, 1.000019, 1.000021]
        for _ in range(30):
            value = (value + .000020) * .999950 if part == 1 else sum((1 if j % 2 == 0 else -1) * (value + a[j]) * b[j] for j in range(3 if part == 2 else 11))
        close(number(output, 'FIRST RESULT='), value, 1e-9)
        unit = 'MFLOPS'
    rating = number(output, 'GUEST ' + unit + ':')
    close(rating, work / (1e6 * secs))
    return secs, rating, unit, None


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--repeats', type=int, default=3)
    p.add_argument('--modes', nargs='+', choices=['original', 'virtual', 'max'], default=['original', 'virtual'])
    args = p.parse_args()
    if args.repeats < 1:
        p.error('--repeats must be positive')
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    config = dict(tapes=str(ROOT / 'tapes'), useWallClock=False, forexAsFortran=False,
                  originalTimeFactor=.670839, originalMathFactor=1.769005)
    (out / 'config.json').write_text(json.dumps(config, indent=2))
    env = dict(commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
               platform=platform.platform(), cpu=Path('/proc/cpuinfo').read_text().split('model name')[1].split('\n')[0].strip(': \t'),
               runtime=subprocess.check_output(['dotnet', '--list-runtimes'], text=True),
               sdk=subprocess.check_output(['dotnet', '--version'], text=True).strip(),
               dll_sha256=sha(DLL), config=config,
               tapes={f.name: sha(f) for f in sorted((ROOT / 'tapes').iterdir()) if f.suffix in ['.9', '.12', '.37', '.739', '.7']})
    (out / 'environment.json').write_text(json.dumps(env, ensure_ascii=False, indent=2))
    cases = [('dhrystone', 0, 'dhrystone-forex.dub'), ('whetstone', 0, 'whetstone-longbottom-forex.dub')]
    cases += [(family, part, family+'-forex.dub') for family in ['memspeed', 'mpmflops'] for part in [1, 2, 3]]
    results = []
    for family, part, source in cases:
        text = (ROOT / 'examples' / source).read_text()
        if part:
            text = text.replace('data kpart/1/', f'data kpart/{part}/')
        deck = out / f'{family}-{part}.dub'
        deck.write_text(text)
        for repeat in range(1, args.repeats + 1):
            for mode in args.modes:
                name = f'{family}-{part}-{mode}-{repeat}'
                print('Running ' + name, flush=True)
                cmd = ['dotnet', str(DLL), 'run', str(deck), '--config', str(out / 'config.json'),
                       '--speed', mode, '--guest-clock', '--stats', '--no-wall-clock', '--no-hang-detect', '--limit', '2000000000']
                start = time.perf_counter()
                run = subprocess.run(cmd, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=900)
                wall = time.perf_counter() - start
                (out / (name+'.out')).write_text(run.stdout)
                if run.returncode:
                    raise RuntimeError(f'{name}: exit {run.returncode}; see log')
                if 'F O R E X' not in run.stdout:
                    raise ValueError('Native FOREX compiler missing')
                program = run.stdout.rsplit('≠', 1)[-1]
                secs, rating, unit, extra = validate(program, family, part)
                stats = json.loads(re.search(r'^Execution stats: (.+)$', run.stdout, re.M)[1])
                row = dict(benchmark=family, part=part, mode=mode, repeat=repeat, guest_seconds=secs,
                           rating=rating, unit=unit, process_wall_seconds=wall, validated=True,
                           deck_sha256=sha(deck), command=cmd, extra=extra, stats=stats)
                results.append(row)
                (out / 'results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2))
                print(f'Validated: {secs:.4f} s, {rating:.6f} {unit}', flush=True)
    columns = ['benchmark', 'part', 'mode', 'repeat', 'guest_seconds', 'rating', 'unit', 'process_wall_seconds', 'validated']
    with (out / 'results.csv').open('w') as f:
        writer = csv.DictWriter(f, columns, extrasaction='ignore'); writer.writeheader(); writer.writerows(results)
    summary = []
    for family, part, _ in cases:
        for mode in args.modes:
            rows = [r for r in results if (r['benchmark'], r['part'], r['mode']) == (family, part, mode)]
            summary.append(dict(benchmark=family, part=part, mode=mode, repeats=len(rows), unit=rows[0]['unit'],
                                seconds_median=statistics.median(r['guest_seconds'] for r in rows),
                                rating_median=statistics.median(r['rating'] for r in rows),
                                rating_min=min(r['rating'] for r in rows), rating_max=max(r['rating'] for r in rows)))
    (out / 'summary.json').write_text(json.dumps(summary, indent=2))
    print(f'Completed {len(results)} validated runs', flush=True)


if __name__ == '__main__':
    main()
