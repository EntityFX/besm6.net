#!/usr/bin/env python3
"""Small, independently specified controls for SIMH/compiler discrepancies."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import tarfile
from ai_scaling_cases import deck, rng


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--work", type=Path, required=True)
    args = parser.parse_args()
    work = args.work.resolve()
    root = work / "diagnostics"
    for name in ("native", "jobs"):
        (root / name).mkdir(parents=True, exist_ok=True)
    for name in ("simh-source", "images", "tools", "cli", "probe"):
        target = root / name
        if not target.exists(): target.symlink_to(work / name, target_is_directory=True)
    shutil.copy2(work / "simh-commit.txt", root)
    manifest = []
    def native_source(name):
        frozen = work / "scaling-inputs-final.tar.gz"
        if frozen.exists():
            with tarfile.open(frozen) as archive:
                return archive.extractfile("native/" + name + ".f").read().decode()
        return (work / "native" / (name + ".f")).read_text()
    def add(name, body, expected):
        source = deck(name, body)
        (root / "jobs" / (name + ".dub")).write_text(source)
        (root / "native" / (name + ".f")).write_text(source.split("*fortran\n", 1)[1].split("*execute", 1)[0])
        manifest.append({"id": name, "algorithm": name, "level": 0, "parameters": {}, "expected": expected, "sourceSha256": hashlib.sha256(source.encode()).hexdigest()})
    random = rng(); expected = []
    for i in range(1, 65):
        seed = next(random); low = seed % 16; high = int(seed >= 32768)
        expected.extend([i, seed, low, float(low), float(low), seed / 65537., high, high])
    add("diag_rng", """
      program rng
      integer seed,low,i,ih,jh
      real a,b,r
      seed=12345
      do 10 i=1,64
        seed=seed*75+74
        seed=seed-65537*(seed/65537)
        low=seed-16*(seed/16)
        a=low
        b=seed-16*(seed/16)
        r=seed/65537.
        ih=0
        jh=0
        if (seed .ge. 32768) ih=1
        if (r .ge. 0.5) jh=1
        print 100, i,seed,low,a,b,r,ih,jh
   10 continue
      stop
  100 format('RESULT',3i10,3f16.6,2i5)
      end
""", expected)
    expected = []
    for bit in range(4): expected.extend([bit, 2**bit, float(2**bit)])
    add("diag_pow", """
      program powers
      integer bit,n
      real x
      do 10 bit=0,3
        n=2**bit
        x=float(n)
        print 100, bit,n,x
   10 continue
      stop
  100 format('RESULT',2i12,f18.6)
      end
""", expected)
    add("diag_min", """
      program minsel
      integer g(25),f(25),op(25),i,v,u,best
      do 10 i=1,25
        g(i)=32760
        f(i)=32760
        op(i)=0
   10 continue
      g(1)=0
      f(1)=8
      op(1)=1
      u=0
      best=32760
      do 20 v=1,25
        if (op(v) .eq. 0) goto 20
        if (f(v) .ge. best) goto 20
        best=f(v)
        u=v
   20 continue
      print 100, u,best,op(1),f(1),g(1)
      stop
  100 format('RESULT',5i12)
      end
""", [1, 8, 1, 8, 0])

    for algorithm in ("perceptron", "knn"):
        native = native_source(algorithm + "-l1")
        marker = "      do 12 j" if algorithm == "perceptron" else "      pos=0"
        body = native.split(marker, 1)[0]
        n, d = (64, 4) if algorithm == "perceptron" else (128, 4)
        random = rng()
        data = [[(1 if next(random) >= 32768 else -1) if algorithm == "perceptron" else next(random) % 16 for _ in range(d)] for _ in range(n)]
        labels = [int(row[0]+row[1] > 1) if algorithm == "perceptron" else int(row[0] >= 8) for row in data]
        expected = []
        for i in range(8): expected.extend([i+1, labels[i], *map(float, data[i])])
        # Existing scalar types in each source: cs is REAL for perceptron,
        # delta is REAL for kNN. Use an explicitly declared new scalar instead.
        program = re.search(r"(?m)^      program[^\n]*\n", body)
        body = body[:program.end()] + "      real diagcs\n      integer diagn\n" + body[program.end():]
        body += """      do 80 i=1,8
        print 500, i,y(i),x(i,1),x(i,2),x(i,3),x(i,4)
   80 continue
      diagn=0
      diagcs=0.
"""
        body += f"""      do 90 i=1,{n}
        diagn=diagn+y(i)
        do 91 j=1,{d}
          diagcs=diagcs+i*j*x(i,j)
   91   continue
   90 continue
      print 510, diagn,diagcs
      stop
  500 format('RESULT',2i10,4f12.1)
  510 format('RESULT',i10,f18.6)
      end
"""
        expected.extend([sum(labels), float(sum((i+1)*(j+1)*row[j] for i,row in enumerate(data) for j in range(d)))])
        add("diag_" + algorithm + "_data", body, expected)

    native = native_source("naive_bayes-l0")
    native = native.replace("      cs=0", """      print 500, cnt(1),cnt(2),hits(1,1),hits(1,2)
      print 501, hits(2,1),hits(2,2)
      print 502, prob(1,1),prob(1,2),prob(2,1),prob(2,2)
      cs=0""", 1)
    native = native.replace("   61     continue", "   61     continue\n          print 503, iq,c,score", 1)
    native = native.replace("      end", """  500 format('RESULT',4i12)
  501 format('RESULT',2i12)
  502 format('RESULT',4f16.6)
  503 format('RESULT',2i10,f18.6)
      end""", 1)
    expected = [8, 8, 2, 2, 6, 2, .3, .3, .7, .3]
    for iq in range(1, 5):
        for c, probabilities in enumerate(((.3, .3), (.7, .3)), 1):
            score = .5
            for j in range(2): score *= probabilities[j] if ((iq-1) >> j) & 1 else 1-probabilities[j]
            expected.extend([iq, c, score])
    expected.extend([16, 2, 2, 4, 6, 16])
    add("diag_bayes", native, expected)

    native = native_source("astar-l0")
    native = native.replace("        if (u .eq. 0)", """        if (it .ne. 1) goto 950
        print 500, it,u,best,g(1),f(1),op(1)
  950   continue
        if (u .eq. 0)""", 1)
    native = native.replace("      end", "  500 format('RESULT',6i12)\n      end", 1)
    add("diag_astar", native, [1, 1, 8, 0, 8, 1, 5, 1, 9, 605])
    (root / "manifest-0-1-2-3.json").write_text(json.dumps(manifest, indent=2))
    print(root)


if __name__ == "__main__":
    main()
