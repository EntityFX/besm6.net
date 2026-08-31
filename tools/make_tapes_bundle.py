#!/usr/bin/env python3
"""Собрать переносимый bundle runtime-ассетов БЭСМ-6 для CI (Task A5 incr. 3).

Ассеты (`tapes/`, `besm6_disk.bin`, `besm6_drum.bin`) — gitignored и не попадают в
чистое CI-окружение; fast-примеры (name/algol/bemsh) не запускаются без MONSYS-ленты.
Этот скрипт упаковывает их в единый tar.gz, который можно:

  1. выложить как GitHub Release asset / в bucket и дать CI URL —
     задайте repo secret ``BESM6_TAPES_BUNDLE_URL`` (см. .github/workflows/ci.yml);
  2. закоммитить в Git LFS в ``tapes/`` + ``besm6_disk.bin`` + ``besm6_drum.bin``
     (полная детерминизация: CI тогда не нужен bundle, ассеты приходят с checkout).

Структура архива (распаковка в корень репо — туда же, куда CI кладёт его):

    tapes/<файл>          (monsys.9, librar.12, librar.37, bemsh.739, b.7, lib1.txt, ...)
    besm6_disk.bin
    besm6_drum.bin

Использование:

    python tools/make_tapes_bundle.py                    # -> <root>/tapes_bundle.tar.gz
    python tools/make_tapes_bundle.py -o dist/asety.tgz  # -> явный путь вывода
"""
from __future__ import annotations

import argparse
import sys
import tarfile
from pathlib import Path

HERE = Path(__file__).resolve()
ROOT = HERE.parent.parent


def collect_assets(root: Path):
    """Собрать список ассетов: каталог ``tapes/`` + образы диска/барабана.

    Возвращает список (путь, arcname); отсутствующие ассеты — в stderr как ВНИМАНИЕ.
    """
    entries = []
    tapes_dir = root / "tapes"
    if tapes_dir.is_dir():
        entries.append((tapes_dir, "tapes"))
    else:
        print(f"ВНИМАНИЕ: каталог {tapes_dir} не найден — ленты не попадут в bundle.", file=sys.stderr)
    for name in ("besm6_disk.bin", "besm6_drum.bin"):
        p = root / name
        if p.is_file():
            entries.append((p, name))
        else:
            print(f"ВНИМАНИЕ: {p} не найден.", file=sys.stderr)
    return entries


def make_bundle(root: Path, out: Path) -> int:
    entries = collect_assets(root)
    if not any(p.exists() for p, _ in entries):
        print("ОШИБКА: не найдено ни одного ассета для упаковки.", file=sys.stderr)
        return 3
    out = Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with tarfile.open(out, "w:gz") as tar:
        for path, arcname in entries:
            if path.is_dir():
                for f in sorted(path.iterdir()):
                    if f.is_file():
                        tar.add(f, arcname=f"{arcname}/{f.name}")
                        count += 1
            else:
                tar.add(path, arcname=arcname)
                count += 1
    print(f"Bundle: {out} ({count} файлов, {out.stat().st_size // 1024} KiB)")
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("-o", "--output", default=None, help="Путь вывода (по умолчанию <root>/tapes_bundle.tar.gz).")
    p.add_argument("--root", default=None, help="Корень проекта (по умолчанию от расположения скрипта).")
    args = p.parse_args(argv)

    root = Path(args.root).resolve() if args.root else ROOT
    out = Path(args.output) if args.output else root / "tapes_bundle.tar.gz"
    return make_bundle(root, out)


if __name__ == "__main__":
    sys.exit(main())
