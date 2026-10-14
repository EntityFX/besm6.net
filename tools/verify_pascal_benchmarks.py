#!/usr/bin/env python3
"""Execute Pascal benchmarks and verify guest controls, ratings and guards."""
import argparse
from decimal import Decimal
import hashlib
import json
import pathlib
import re
import subprocess

import benchmark_speed as bench
import verify_guest_ratings as guest

ROOT = pathlib.Path(__file__).resolve().parents[1]
CASES = ('dh5000', 'dh10000', 'dh-zero', 'dh-off', 'dh-bad', 'dh-short',
         'whet-good', 'whet-zero', 'whet-off', 'whet-bad', 'whet-short')


def prepare(case):
    family = 'dh' if case.startswith('dh') else 'whet'
    name = 'dhrystone-pascal.dub' if family == 'dh' else 'whetstone-pascal.dub'
    source = (ROOT / 'examples' / name).read_text(encoding='utf-8')
    scenario = 'good' if case in ('dh5000', 'dh10000', 'whet-good') else case.split('-')[1]
    passes = 10000 if case == 'dh10000' else 5000 if family == 'dh' else 100
    if scenario in ('zero', 'short'):
        passes = 0 if scenario == 'zero' else 1
    original = 'numberofruns = 5000;' if family == 'dh' else 'nruns = 100;'
    replacement = f'numberofruns = {passes};' if family == 'dh' else f'nruns = {passes};'
    source = guest.substitute(source, original, replacement)
    if scenario == 'bad':
        source = guest.substitute(source, 'check(intglob = 5);', 'check(intglob = 6);') if family == 'dh' else guest.substitute(
            source, 'refval[1] := -1.13323943676865;', 'refval[1] := 0.0;')
    return family, scenario, passes, source


def validate(program, family, scenario, passes):
    unit = 'DMIPS' if family == 'dh' else 'MWIPS'
    score = re.search(r'^GUEST ' + unit + r':\s*(\S+)', program, re.M)
    if scenario != 'good':
        reason = ('ZERO PASSES' if scenario == 'zero' else
                  'CONTROL VALUES DO NOT MATCH' if scenario == 'bad' else
                  'ENABLE --GUEST-CLOCK OR RUN LONGER')
        if score or not re.search(r'^' + unit + r' UNAVAILABLE: ' + re.escape(reason) + r'\s*$', program, re.M):
            raise ValueError('Pascal rating guard failed')
        return {'rating': None, 'guard': scenario}
    if not re.search(r'^BAD CONTROL VALUES=\s*0\s*$', program, re.M):
        raise ValueError('Pascal self-check failed')
    time = re.search(r'^GUEST CPU SECONDS:\s*(\S+)', program, re.M)
    if not score or not time:
        raise ValueError('Pascal guest time/rating missing')
    seconds, rating = bench.fortran_number(time[1]), bench.fortran_number(score[1])
    if seconds < Decimal('.04') or rating <= 0:
        raise ValueError('Insufficient Pascal guest interval')
    if family == 'dh':
        required = ['ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC',
                    'ALL 4 STRING VALUES MATCH: YES']
        if not all(re.search(r'^' + re.escape(text) + '$', program, re.M) for text in required):
            raise ValueError('Dhrystone complete controls missing')
        values = {'INTGLOB': 5, 'ARR1GLOB 8': 7, 'ARR2GLOB 8/7': passes+10,
                  'PTRGLOB.DISCR': 0, 'PTRGLOB.ENUM': 2, 'PTRGLOB.INT': 17,
                  'NEXTPTR.DISCR': 0, 'NEXTPTR.ENUM': 1, 'NEXTPTR.INT': 18,
                  'INT1LOC': 5, 'INT2LOC': 13, 'INT3LOC': 7, 'ENUMLOC': 1}
        for label, expected in values.items():
            found = re.search(r'^' + re.escape(label) + r':\s*(-?\d+)\b', program, re.M)
            if not found or int(found[1]) != expected:
                raise ValueError('Dhrystone independent control: ' + label)
        for label, value in (('BOOLGLOB', 'TRUE'), ('CH1GLOB', 'A'), ('CH2GLOB', 'B')):
            if not re.search(r'^' + label + r':\s*' + value + r'\b', program, re.M):
                raise ValueError('Dhrystone boolean/character control')
        strings = {'PTRGLOB.STR': 'DHRYSTONE PROGRAM, SOME STRING',
                   'NEXTPTR.STR': 'DHRYSTONE PROGRAM, SOME STRING',
                   'LOCAL STRING1': "DHRYSTONE PROGRAM, 1'ST STRING",
                   'LOCAL STRING2': "DHRYSTONE PROGRAM, 2'ND STRING"}
        normalized = program.replace('′', "'")
        for label, expected in strings.items():
            if not re.search(r'^' + re.escape(label) + r':\s*' + re.escape(expected) + r'\s*$', normalized, re.M):
                raise ValueError('Dhrystone independent string control: ' + label)
        expected_score = Decimal(passes)/(seconds*1757)
    else:
        if not re.search(r'^ALL 8 CONTROL VALUES MATCH: YES$', program, re.M):
            raise ValueError('Whetstone complete controls missing')
        reference = ('-1.1332394367686494', '-1.1332181986811034', '0', '12',
                     '.49999052352563617', '.99999988878912238', '3', '.75001003274361189')
        for i, expected in enumerate(reference, 1):
            found = re.search(r'^N' + str(i) + r' VALUE\s*(\S+)', program, re.M)
            if not found or abs(bench.fortran_number(found[1])-Decimal(expected)) > Decimal('1e-8'):
                raise ValueError('Whetstone independent section control')
        checksum = re.search(r'^CHECK SUM\s*(\S+)', program, re.M)
        if not checksum or abs(bench.fortran_number(checksum[1])-Decimal('14.9835428096086')) > Decimal('1e-7'):
            raise ValueError('Whetstone independent checksum')
        expected_score = Decimal(15869*passes*10)/(seconds*1000000)
    if abs(rating-expected_score) > max(Decimal('1e-7'), abs(expected_score)*Decimal('2e-7')):
        raise ValueError('Pascal score formula failed')
    return {'rating': float(rating), 'seconds': float(seconds), 'unit': unit, 'guard': None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dll', type=pathlib.Path, required=True)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--cases', nargs='+', choices=CASES, default=list(CASES))
    parser.add_argument('--modes', nargs='+', choices=('max','original','virtual'), default=['max','original','virtual'])
    args = parser.parse_args()
    args.dll = args.dll.resolve()
    args.output = args.output.resolve()
    if args.output.exists():
        parser.error('Use a new output directory; earlier evidence is retained')
    args.output.mkdir(parents=True)
    (args.output/'logs').mkdir()
    config = args.output/'config.json'
    config.write_text(json.dumps({'tapes': str(ROOT/'tapes'), 'useWallClock': False, 'guestClock': False}), encoding='utf-8')
    result = {'validated': False, 'scope': 'Pascal controls, formulas, guards; exact state parity only with guest clock disabled',
              'environment': bench.report_environment(), 'build': bench.report_build_identity(args.dll), 'cases': []}
    def save():
        (args.output/'results.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    try:
        for case in args.cases:
            family, scenario, passes, source = prepare(case)
            deck = args.output/(case+'.dub')
            deck.write_text(source, encoding='utf-8')
            reference = None
            for speed in args.modes:
                command = ['dotnet', str(args.dll), 'run', str(deck), '--config', str(config),
                           '--speed', speed, '--stats', '--no-wall-clock', '--limit', '200000000']
                if scenario != 'off': command.append('--guest-clock')
                run = subprocess.run(command, cwd=ROOT, capture_output=True, encoding='utf-8', errors='replace', timeout=180)
                output = run.stdout+run.stderr
                (args.output/'logs'/(case+'-'+speed+'.out')).write_text(output, encoding='utf-8')
                program, instructions, report = guest.extract(output, run.returncode)
                rated = validate(program, family, scenario, passes)
                parity = {'program_sha256': hashlib.sha256(program.encode()).hexdigest(),
                          'halt_instructions': instructions, 'finalState': report['finalState'],
                          'memorySha256': report['memorySha256'], 'cycles': report['statistics']['modelCycles']}
                if scenario == 'off':
                    if reference is not None and parity != reference: raise ValueError('Pascal model-speed parity failed')
                    reference = parity
                result['cases'].append({'case': case, 'speed': speed, 'passes': passes,
                    'source_sha256': hashlib.sha256(source.encode()).hexdigest(), **parity, **rated})
                save()
                print(case, speed, 'passed', flush=True)
        result['validated'] = True
        save()
        return 0
    except Exception:
        result['error'] = 'Pascal verification failed; full diagnostics remain local'
        save()
        raise


if __name__ == '__main__':
    raise SystemExit(main())
