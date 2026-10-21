#!/usr/bin/env python3
"""Validate the published article bundle; optionally check external HTTP links."""
import argparse
import concurrent.futures
import json
import re
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import unquote

HERE = Path(__file__).resolve().parent


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--check-web', action='store_true')
    args = p.parse_args()
    article = (HERE / 'article.md').read_text()
    errors = []
    for forbidden in ['<!-- MEASURED_RESULTS -->', 'TODO', 'заполняется по журналам']:
        if forbidden in article:
            errors.append('Draft marker remains: ' + forbidden)
    local_count = 0
    urls = set()
    for target in re.findall(r'\]\(([^)]+)\)', article):
        if target.startswith('http'):
            urls.add(target)
        else:
            local_count += 1
            file = HERE / unquote(target.split('#')[0])
            if not file.exists():
                errors.append('Missing link: ' + target)
    # A conservative count includes code, table cells, and link destinations.
    words = len(re.findall(r'\b[\w-]+\b', article))
    if words / 160 > 40:
        errors.append('Article exceeds 40 minutes at 160 words/min')
    results = json.loads((HERE / 'data/run-2026-10-13/results.json').read_text())
    keys = {(r['benchmark'], r['part'], r['mode'], r['repeat']) for r in results}
    if len(results) != 48 or len(keys) != 48 or not all(r['validated'] for r in results):
        errors.append('Measurement coverage invalid')
    from measure import validate
    for r in results:
        stem = f'{r["benchmark"]}-{r["part"]}-{r["mode"]}-{r["repeat"]}'
        output = (HERE / 'data/run-2026-10-13' / (stem + '.out')).read_text()
        secs, rating, unit, extra = validate(output.rsplit('≠', 1)[-1], r['benchmark'], r['part'])
        if (secs, rating, unit, extra) != (r['guest_seconds'], r['rating'], r['unit'], r['extra']):
            errors.append('Saved measurement differs from raw log: ' + stem)
    # Computed values, excluding timer fields, must agree across modes/repeats.
    patterns = {'dhrystone': r'^(?:IGLOB|BLOB|C[12]GLB|A[12]GLOB.*?|PGLB.*?|NEXTP.*?|I[123]LOC|ELOC|LOCAL STRING[12]):.*$',
                'memspeed': r'^(?:FIRST RESULT|LAST RESULT|CHECKSUM)=.*$',
                'mpmflops': r'^FIRST RESULT=.*$'}
    baselines = {}
    for r in results:
        key = (r['benchmark'], r['part'])
        if r['benchmark'] == 'whetstone':
            values = [s['value'] for s in r['extra']['sections']]
        else:
            stem = f'{r["benchmark"]}-{r["part"]}-{r["mode"]}-{r["repeat"]}'
            output = (HERE / 'data/run-2026-10-13' / (stem + '.out')).read_text().rsplit('≠',1)[-1]
            values = re.findall(patterns[r['benchmark']], output, re.M)
            if not values:
                errors.append('No values compared: ' + stem)
        if key in baselines and baselines[key] != values:
            errors.append('Computed values changed: ' + str(key))
        baselines[key] = values
    web = []
    if args.check_web:
        def check(url):
            try:
                req = urllib.request.Request(url, headers={'User-Agent':'Mozilla/5.0'}, method='HEAD')
                with urllib.request.urlopen(req, timeout=20) as response:
                    return dict(url=url, http=response.status, resolved=response.url)
            except urllib.error.HTTPError as e:
                return dict(url=url, http=e.code, error=str(e))
            except Exception as e:
                return dict(url=url, http=None, error=str(e))
        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
            web = list(pool.map(check, sorted(urls)))
    report = dict(local_links=local_count, words_including_code_and_urls=words,
                  estimated_minutes_at_160_wpm=round(words/160,1), estimated_minutes_at_180_wpm=round(words/180,1),
                  validated_measurements=len(results), cross_mode_numeric_values_unchanged=not errors,
                  errors=errors, external_links=web)
    (HERE / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
    print(json.dumps({k:v for k,v in report.items() if k != 'external_links'}, ensure_ascii=False, indent=2))
    if web:
        print('HTTP link results:', [(r['http'],r['url']) for r in web])
    if errors:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
