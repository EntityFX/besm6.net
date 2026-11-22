"""Compact parameterized Fortran workloads and independent Python controls.

These are new scaling families, not silently modified versions of examples/ai.
All generated programs finish computation before their first formatted PRINT.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
import re
from pathlib import Path


@dataclass(frozen=True)
class Case:
    algorithm: str
    level: int
    parameters: dict[str, int]

    @property
    def id(self):
        return f"{self.algorithm}-l{self.level}"


def cases():
    definitions = {
        "perceptron": [(4, 2, 20), (64, 4, 20), (256, 12, 40), (512, 24, 100)],
        "knn": [(16, 1, 3), (128, 4, 8), (512, 8, 32), (1024, 12, 128)],
        "kmeans": [(12, 1, 2, 10), (64, 4, 4, 10), (256, 8, 8, 10), (512, 16, 8, 20)],
        "naive_bayes": [(16, 2, 2, 4), (64, 8, 4, 64), (128, 12, 8, 512), (256, 16, 8, 8192)],
        "decision_stump": [(8,), (64,), (256,), (1024,)],
        "genetic": [(4, 8), (32, 64), (128, 512), (512, 1024)],
        "annealing": [(1, 30), (4, 500), (16, 5000), (64, 30000)],
        "qlearning": [(4, 20), (16, 200), (64, 1000), (128, 4000)],
        "astar": [(5,), (10,), (25,), (45,)],
        "chess_mate": [(1,), (64,), (256,), (2048,)],
        "mlp": [(2000,)],
    }
    names = {
        "perceptron": ("samples", "features", "epochs"),
        "knn": ("samples", "features", "queries"),
        "kmeans": ("samples", "features", "clusters", "iterations"),
        "naive_bayes": ("samples", "features", "classes", "queries"),
        "decision_stump": ("samples",), "genetic": ("population", "generations"),
        "annealing": ("dimensions", "iterations"), "qlearning": ("states", "episodes"),
        "astar": ("side",), "chess_mate": ("positions",),
        "mlp": ("epochs",),
    }
    return [Case(a, level, dict(zip(names[a], values)))
            for a, levels in definitions.items() for level, values in enumerate(levels)]


def deck(name, body):
    # FORTRAN-GDR rejects literal constants in PRINT lists. Use implicit INTEGER
    # temporaries without changing the algorithm or requiring a SIMH-only fork.
    portable = []
    for line in body.strip(chr(10)).splitlines():
        match = re.match(r"(\s*print\s+\d+,)(.*)", line, re.I)
        if match:
            items = re.split(r",(?![^()]*\))", match[2])
            for index, item in enumerate(items):
                if re.fullmatch(r"\s*\d+\s*", item):
                    var = f"iz{index+1:03d}"
                    portable.append(f"      {var}={item.strip()}")
                    items[index] = var
            line = match[1] + ",".join(items)
        portable.append(line)
    body = "\n".join(portable)
    result = f"*name scale {name}\n*fortran\n{body.strip(chr(10))}\n*execute\n*end file\n"
    for line in result.splitlines():
        if len(line) > 72 and not line.startswith(("*", "c", "C")):
            raise ValueError(f"Fixed-form line too long ({len(line)}): {line}")
    return result


def rng(seed=12345):
    while True:
        seed = (seed * 75 + 74) % 65537
        yield seed


def perceptron(p):
    n, d, epochs = p.values()
    source = deck("perceptron", f"""
      program perc
      real x({n},{d}),w({d}),b,z,err,cs
      integer y({n}),i,j,ep,seed,pred,nok
      seed=12345
      do 10 i=1,{n}
        do 11 j=1,{d}
          seed=seed*75+74
          seed=seed-65537*(seed/65537)
          x(i,j)=-1.
          if (seed .ge. 32768) x(i,j)=1.
   11   continue
        y(i)=0
        if (x(i,1)+x(i,2) .gt. 1.) y(i)=1
   10 continue
      do 12 j=1,{d}
        w(j)=0.
   12 continue
      b=0.
      do 30 ep=1,{epochs}
        do 20 i=1,{n}
          z=b
          do 21 j=1,{d}
            z=z+w(j)*x(i,j)
   21     continue
          pred=0
          if (z .gt. 0.) pred=1
          err=(y(i)-pred)*0.125
          do 22 j=1,{d}
            w(j)=w(j)+err*x(i,j)
   22     continue
          b=b+err
   20   continue
   30 continue
      nok=0
      do 40 i=1,{n}
        z=b
        do 41 j=1,{d}
          z=z+w(j)*x(i,j)
   41   continue
        pred=0
        if (z .gt. 0.) pred=1
        if (pred .eq. y(i)) nok=nok+1
   40 continue
      cs=b
      do 50 j=1,{d}
        cs=cs+j*w(j)
   50 continue
      print 100, {n},{d},{epochs},nok,cs
      stop
  100 format('RESULT',4i12,f18.6)
      end
""")
    random = rng()
    x = [[1.0 if next(random) >= 32768 else -1.0 for _ in range(d)] for _ in range(n)]
    y = [int(row[0] + row[1] > 1) for row in x]
    w, b = [0.0] * d, 0.0
    for _ in range(epochs):
        for row, target in zip(x, y):
            z = b
            for j in range(d): z += w[j] * row[j]
            error = (target - int(z > 0)) * .125
            for j in range(d): w[j] += error * row[j]
            b += error
    correct = 0
    for row, target in zip(x, y):
        z = b
        for j in range(d): z += w[j] * row[j]
        correct += int(int(z > 0) == target)
    checksum = b
    for j in range(d): checksum += (j + 1) * w[j]
    return source, [n, d, epochs, correct, checksum]


def knn(p):
    n, d, queries = p.values()
    source = deck("knn", f"""
      program knn
      real x({n},{d}),q({d}),dist({n}),delta
      integer y({n}),used({n}),i,j,iq,k,best,seed,vote
      integer pos,cs,pred
      seed=12345
      do 10 i=1,{n}
        do 11 j=1,{d}
          seed=seed*75+74
          seed=seed-65537*(seed/65537)
          x(i,j)=seed-16*(seed/16)
   11   continue
        y(i)=0
        if (x(i,1) .ge. 8.) y(i)=1
   10 continue
      pos=0
      cs=0
      do 50 iq=1,{queries}
        do 12 j=1,{d}
          seed=seed*75+74
          seed=seed-65537*(seed/65537)
          q(j)=seed-16*(seed/16)
   12   continue
        do 20 i=1,{n}
          dist(i)=0.
          used(i)=0
          do 21 j=1,{d}
            delta=x(i,j)-q(j)
            dist(i)=dist(i)+delta*delta
   21     continue
   20   continue
        vote=0
        do 30 k=1,3
          best=0
          do 31 i=1,{n}
            if (used(i) .ne. 0) goto 31
            if (best .eq. 0) best=i
            if (dist(i) .lt. dist(best)) best=i
   31     continue
          used(best)=1
          vote=vote+y(best)
   30   continue
        pred=0
        if (vote .gt. 1) pred=1
        pos=pos+pred
        cs=cs+iq*pred
   50 continue
      print 100, {n},{d},{queries},pos,cs
      stop
  100 format('RESULT',5i12)
      end
""")
    random = rng()
    x = [[float(next(random) % 16) for _ in range(d)] for _ in range(n)]
    y = [int(row[0] >= 8) for row in x]
    pos = cs = 0
    for iq in range(1, queries + 1):
        q = [float(next(random) % 16) for _ in range(d)]
        distances = []
        for row in x:
            distance = 0.0
            for j in range(d):
                delta = row[j] - q[j]
                distance += delta * delta
            distances.append(distance)
        used = [False] * n; vote = 0
        for _ in range(3):
            best = -1
            for i in range(n):
                if used[i]: continue
                if best < 0: best = i
                if distances[i] < distances[best]: best = i
            used[best] = True; vote += y[best]
        pred = int(vote > 1)
        pos += pred; cs += iq * pred
    return source, [n, d, queries, pos, cs]


def kmeans(p):
    n, d, clusters, iterations = p.values()
    source = deck("kmeans", f"""
      program kmeans
      real x({n},{d}),cent({clusters},{d})
      real sums({clusters},{d}),dist,best,delta
      integer count({clusters}),labels({n}),i,j,k,it,c,cs
      do 10 i=1,{n}
        c=i-1-{clusters}*((i-1)/{clusters})
        do 11 j=1,{d}
          k=i*7+j*3
          x(i,j)=10.*c+(k-16*(k/16))*0.0625
   11   continue
   10 continue
      do 12 k=1,{clusters}
        do 13 j=1,{d}
          cent(k,j)=x(k,j)
   13   continue
   12 continue
      do 50 it=1,{iterations}
        do 20 k=1,{clusters}
          count(k)=0
          do 21 j=1,{d}
            sums(k,j)=0.
   21     continue
   20   continue
        do 30 i=1,{n}
          c=1
          best=1.e10
          do 31 k=1,{clusters}
            dist=0.
            do 32 j=1,{d}
              delta=x(i,j)-cent(k,j)
              dist=dist+delta*delta
   32       continue
            if (dist .ge. best) goto 31
            best=dist
            c=k
   31     continue
          labels(i)=c
          count(c)=count(c)+1
          do 33 j=1,{d}
            sums(c,j)=sums(c,j)+x(i,j)
   33     continue
   30   continue
        do 40 k=1,{clusters}
          if (count(k) .eq. 0) goto 40
          do 41 j=1,{d}
            cent(k,j)=sums(k,j)/count(k)
   41     continue
   40   continue
   50 continue
      cs=0
      do 60 i=1,{n}
        cs=cs+labels(i)
   60 continue
      print 100, {n},{d},{clusters},cs,cent(1,1)
      print 110, cent({clusters},{d})
      stop
  100 format('RESULT',4i12,f18.6)
  110 format('EXTRA',f18.6)
      end
""")
    x = [[10 * (i % clusters) + (((i + 1) * 7 + (j + 1) * 3) % 16) / 16 for j in range(d)] for i in range(n)]
    cent = [row[:] for row in x[:clusters]]
    labels = [0] * n
    for _ in range(iterations):
        sums = [[0.0] * d for _ in range(clusters)]; count = [0] * clusters
        for i, row in enumerate(x):
            distances = []
            for center in cent:
                distance = 0.0
                for j in range(d):
                    delta = row[j] - center[j]
                    distance += delta * delta
                distances.append(distance)
            c = min(range(clusters), key=lambda k: distances[k]); labels[i] = c + 1; count[c] += 1
            for j in range(d): sums[c][j] += row[j]
        for k in range(clusters):
            if count[k]: cent[k] = [v / count[k] for v in sums[k]]
    return source, [n, d, clusters, sum(labels), cent[0][0], cent[-1][-1]]


def naive_bayes(p):
    n, d, classes, queries = p.values()
    source = deck("bayes", f"""
      program bayes
      integer x({n},{d}),y({n}),cnt({classes})
      integer hits({classes},{d}),q({d})
      real prob({classes},{d}),score,best
      integer i,j,c,bit,code,flip,iq,pred,cs,ps
      do 10 i=1,{n}
        c=1+(i-1)-{classes}*((i-1)/{classes})
        y(i)=c
        do 11 j=1,{d}
          bit=(j-1)-3*((j-1)/3)
          code=(c-1)/(2**bit)
          x(i,j)=code-2*(code/2)
          flip=(i-1)/{classes}+j*3
          flip=flip-4*(flip/4)
          if (flip .eq. 0) x(i,j)=1-x(i,j)
   11   continue
   10 continue
      do 20 c=1,{classes}
        cnt(c)=0
        do 21 j=1,{d}
          hits(c,j)=0
   21   continue
   20 continue
      do 30 i=1,{n}
        c=y(i)
        cnt(c)=cnt(c)+1
        do 31 j=1,{d}
          hits(c,j)=hits(c,j)+x(i,j)
   31   continue
   30 continue
      do 40 c=1,{classes}
        do 41 j=1,{d}
          prob(c,j)=(hits(c,j)+1.)/(cnt(c)+2.)
   41   continue
   40 continue
      cs=0
      ps=0
      do 70 iq=1,{queries}
        code=iq-1
        do 50 j=1,{d}
          q(j)=code-2*(code/2)
          code=code/2
   50   continue
        best=-1.
        pred=1
        do 60 c=1,{classes}
          score=1./{classes}
          do 61 j=1,{d}
            if (q(j) .eq. 0) goto 62
            score=score*prob(c,j)
            goto 61
   62       score=score*(1.-prob(c,j))
   61     continue
c---- Explicit tolerance makes equal-likelihood tie handling portable.
          if (score .le. best*(1.+1.e-5)) goto 60
          best=score
          pred=c
   60   continue
        ps=ps+pred
        cs=cs+iq*pred
   70 continue
      print 100, {n},{d},{classes},{queries},ps,cs
      stop
  100 format('RESULT',6i12)
      end
""")
    count = [0] * classes; hits = [[0] * d for _ in range(classes)]
    for i in range(n):
        c = i % classes; count[c] += 1
        for j in range(d):
            value = (c >> (j % 3)) & 1
            if (i // classes + (j + 1) * 3) % 4 == 0: value = 1 - value
            hits[c][j] += value
    prob = [[(hits[c][j] + 1) / (count[c] + 2) for j in range(d)] for c in range(classes)]
    ps = cs = 0
    for iq in range(1, queries + 1):
        q = [((iq - 1) >> j) & 1 for j in range(d)]
        best = -1.; pred = 1
        for c in range(classes):
            score = 1. / classes
            for j in range(d): score *= prob[c][j] if q[j] else 1 - prob[c][j]
            if score > best * (1 + 1e-5): best, pred = score, c + 1
        ps += pred; cs += iq * pred
    return source, [n, d, classes, queries, ps, cs]


def decision_stump(p):
    n = p["samples"]
    source = deck("stump", f"""
      program stump
      real x({n}),th
      integer y({n}),i,t,d,e,best,bt,bd,pred
      do 10 i=1,{n}
        x(i)=i
        y(i)=0
        if (i .gt. {n//2}) y(i)=1
   10 continue
      best={n+1}
      bt=0
      bd=0
      do 40 t=1,{n-1}
        th=t+0.5
        do 30 d=1,2
          e=0
          do 20 i=1,{n}
            pred=0
            if (x(i) .ge. th) pred=1
            if (d .eq. 2) pred=1-pred
            if (pred .ne. y(i)) e=e+1
   20     continue
          if (e .ge. best) goto 30
          best=e
          bt=t
          bd=d
   30   continue
   40 continue
      th=bt+0.5
      print 100, {n},bd,best,th
      stop
  100 format('RESULT',3i12,f18.6)
      end
""")
    x = [float(i) for i in range(1, n + 1)]
    y = [int(i > n // 2) for i in range(1, n + 1)]
    best, threshold, direction = n + 1, 0., 0
    for t in range(1, n):
        th = t + .5
        for d in (1, 2):
            errors = 0
            for i in range(n):
                pred = int(x[i] >= th)
                if d == 2: pred = 1 - pred
                if pred != y[i]: errors += 1
            if errors < best: best, threshold, direction = errors, th, d
    assert best == 0 and threshold == n / 2 + .5
    return source, [n, direction, best, threshold]


def genetic(p):
    population, generations = p.values()
    source = deck("genetic", f"""
      program genalg
      integer pop({population}),fit({population})
      integer i,gen,i1,i2,p1,p2,child,bx,bf,cs
      do 10 i=1,{population}
        child=37*i+7
        pop(i)=child-1024*(child/1024)
   10 continue
      do 50 gen=1,{generations}
        do 20 i=1,{population}
          fit(i)=pop(i)*pop(i)
   20   continue
        i1=1
        do 21 i=2,{population}
          if (fit(i) .gt. fit(i1)) i1=i
   21   continue
        i2=0
        do 22 i=1,{population}
          if (i .eq. i1) goto 22
          if (i2 .eq. 0) i2=i
          if (fit(i) .gt. fit(i2)) i2=i
   22   continue
        p1=pop(i1)
        p2=pop(i2)
        pop(1)=p1
        pop(2)=p2
        do 30 i=3,{population}
          child=(p1/32)*32+p2-32*(p2/32)
          if (i-2*(i/2) .eq. 0) goto 31
          child=(p2/32)*32+p1-32*(p1/32)
   31     child=child+2*i+gen
          pop(i)=child-1024*(child/1024)
   30   continue
   50 continue
      bx=pop(1)
      bf=bx*bx
      cs=0
      do 60 i=1,{population}
        cs=cs+pop(i)
        if (pop(i)*pop(i) .le. bf) goto 60
        bx=pop(i)
        bf=bx*bx
   60 continue
      print 100, {population},{generations},bx,bf,cs
      stop
  100 format('RESULT',5i12)
      end
""")
    pop = [(37 * i + 7) % 1024 for i in range(1, population + 1)]
    for gen in range(1, generations + 1):
        fit = [value * value for value in pop]
        i1 = 0
        for i in range(1, population):
            if fit[i] > fit[i1]: i1 = i
        i2 = -1
        for i in range(population):
            if i == i1: continue
            if i2 < 0: i2 = i
            if fit[i] > fit[i2]: i2 = i
        p1, p2 = pop[i1], pop[i2]
        pop[:2] = [p1, p2]
        for i in range(3, population + 1):
            child = (p1 // 32) * 32 + p2 % 32
            if i % 2: child = (p2 // 32) * 32 + p1 % 32
            pop[i - 1] = (child + 2 * i + gen) % 1024
    best = max(pop)
    return source, [population, generations, best, best * best, sum(pop)]


def annealing(p):
    dimensions, iterations = p.values()
    source = deck("anneal", f"""
      program anneal
      integer x({dimensions}),i,j,it,old,new,seed,cs
      real fx,fn,fb,t,r1,r2,delta
      do 10 j=1,{dimensions}
        x(j)=0
   10 continue
      fx=0.
      fb=fx
      seed=12345
      do 50 it=1,{iterations}
c---- Hyperbolic schedule: T stays positive and ends at 0.1.
        t=5./(1.+49.*it/{iterations})
        i=1+(it-1)-{dimensions}*((it-1)/{dimensions})
        seed=seed*75+74
        seed=seed-65537*(seed/65537)
        r1=seed/65537.
        old=x(i)
        new=old-1
        if (r1 .ge. 0.5) new=old+1
        if (new .lt. 0) new=0
        if (new .gt. 10) new=10
        x(i)=new
        fn=0.
        do 20 j=1,{dimensions}
          delta=x(j)-3.
          fn=fn+9.-delta*delta
   20   continue
        if (fn .ge. fx) goto 30
        seed=seed*75+74
        seed=seed-65537*(seed/65537)
        r2=seed/65537.
        if (r2 .lt. exp((fn-fx)/t)) goto 30
        x(i)=old
        goto 50
   30   fx=fn
        if (fx .gt. fb) fb=fx
   50 continue
      cs=0
      do 60 j=1,{dimensions}
        cs=cs+j*x(j)
   60 continue
      print 100, {dimensions},{iterations},cs,fb,fx,t
      stop
  100 format('RESULT',3i12,3f18.6)
      end
""")
    x = [0] * dimensions; fx = fb = 0.; random = rng()
    for it in range(1, iterations + 1):
        t = 5 / (1 + 49 * it / iterations)
        i = (it - 1) % dimensions
        old = x[i]
        x[i] = max(0, min(10, old + (1 if next(random) / 65537 >= .5 else -1)))
        fn = 0.0
        for value in x:
            delta = value - 3.0
            fn = fn + 9.0 - delta * delta
        accepted = fn >= fx
        if not accepted: accepted = next(random) / 65537 < math.exp((fn - fx) / t)
        if accepted: fx = fn; fb = max(fb, fx)
        else: x[i] = old
    return source, [dimensions, iterations, sum((i + 1) * value for i, value in enumerate(x)), fb, fx, t]


def qlearning(p):
    states, episodes = p.values()
    source = deck("qlearn", f"""
      program qlearn
      real q({states},2),r,r2,mq,cs
      integer s,a,ns,ep,st,reward,seed,moves,nright
      seed=12345
      moves=0
      do 10 s=1,{states}
        q(s,1)=0.
        q(s,2)=0.125
   10 continue
      do 100 ep=1,{episodes}
        s=1
        do 90 st=1,{states*4}
          seed=seed*75+74
          seed=seed-65537*(seed/65537)
          r=seed/65537.
          if (r .lt. 0.2) goto 20
          a=1
          if (q(s,2) .gt. q(s,1)) a=2
          goto 30
   20     seed=seed*75+74
          seed=seed-65537*(seed/65537)
          r2=seed/65537.
          a=1
          if (r2 .ge. 0.5) a=2
   30     ns=s-1
          if (a .eq. 2) ns=s+1
          if (ns .lt. 1) ns=1
          if (ns .gt. {states}) ns={states}
          reward=0
          if (ns .eq. {states}) reward=1
          mq=q(ns,1)
          if (q(ns,2) .gt. mq) mq=q(ns,2)
          q(s,a)=q(s,a)+0.5*(reward+0.875*mq-q(s,a))
          moves=moves+1
          s=ns
          if (s .eq. {states}) goto 100
   90   continue
  100 continue
      cs=0.
      nright=0
      do 110 s=1,{states-1}
        cs=cs+q(s,1)+q(s,2)
        if (q(s,2) .gt. q(s,1)) nright=nright+1
  110 continue
      print 200, {states},{episodes},moves,nright,cs
      stop
  200 format('RESULT',4i12,f18.6)
      end
""")
    q = [[0., .125] for _ in range(states)]; random = rng(); moves = 0
    for _ in range(episodes):
        s = 0
        for _ in range(states * 4):
            if next(random) / 65537 < .2: a = int(next(random) / 65537 >= .5)
            else: a = int(q[s][1] > q[s][0])
            ns = max(0, min(states - 1, s + (1 if a else -1)))
            reward = int(ns == states - 1)
            q[s][a] += .5 * (reward + .875 * max(q[ns]) - q[s][a])
            moves += 1; s = ns
            if s == states - 1: break
    cs = 0.
    for row in q[:-1]: cs = cs + row[0] + row[1]
    return source, [states, episodes, moves, sum(row[1] > row[0] for row in q[:-1]), cs]


def astar(p):
    side = p["side"]; nodes = side * side
    source = deck("astar", f"""
      program astar
      integer obs({side},{side}),g({nodes}),f({nodes})
      integer par({nodes}),op({nodes}),cl({nodes})
      integer path({nodes}),dr(4),dc(4)
      integer i,r,c,k,nr,nc,v,u,best,ng,l,it,ii,cs,found
      do 10 i=1,{nodes}
        g(i)=32760
        f(i)=32760
        par(i)=0
        op(i)=0
        cl(i)=0
   10 continue
      do 15 r=1,{side}
        do 14 c=1,{side}
          obs(r,c)=0
   14   continue
   15 continue
      obs(2,2)=1
      obs(2,3)=1
      obs(3,3)=1
      obs(4,2)=1
      dr(1)=-1
      dc(1)=0
      dr(2)=1
      dc(2)=0
      dr(3)=0
      dc(3)=-1
      dr(4)=0
      dc(4)=1
      g(1)=0
      f(1)={2*(side-1)}
      op(1)=1
      found=0
      l=0
      cs=0
      do 100 it=1,{nodes}
        u=0
        best=32760
        do 20 v=1,{nodes}
          if (op(v) .eq. 0) goto 20
          if (f(v) .ge. best) goto 20
          best=f(v)
          u=v
   20   continue
        if (u .eq. 0) goto 300
        if (u .eq. {nodes}) goto 200
        op(u)=0
        cl(u)=1
        r=(u-1)/{side}+1
        c=(u-1)-{side}*((u-1)/{side})+1
        do 30 k=1,4
          nr=r+dr(k)
          nc=c+dc(k)
          if (nr .lt. 1) goto 30
          if (nr .gt. {side}) goto 30
          if (nc .lt. 1) goto 30
          if (nc .gt. {side}) goto 30
          if (obs(nr,nc) .ne. 0) goto 30
          v=(nr-1)*{side}+nc
          if (cl(v) .ne. 0) goto 30
          ng=g(u)+1
          if (ng .ge. g(v)) goto 30
          g(v)=ng
          f(v)=ng+iabs(nr-{side})+iabs(nc-{side})
          par(v)=u
          op(v)=1
   30   continue
  100 continue
      goto 300
  200 found=1
      v={nodes}
  210 l=l+1
      path(l)=v
      if (v .eq. 1) goto 220
      v=par(v)
      goto 210
  220 do 230 ii=1,l
        i=l+1-ii
        cs=cs+ii*path(i)
  230 continue
  300 continue
      print 400, {side},found,l,cs
      stop
  400 format('RESULT',4i12)
      end
""")
    blocked = {(1, 1), (1, 2), (2, 2), (3, 1)}
    g = [32760] * nodes; f = g[:]; parent = [-1] * nodes
    opened = [False] * nodes; closed = opened[:]
    g[0] = 0; f[0] = 2 * (side - 1); opened[0] = True
    found = False
    for _ in range(nodes):
        u = -1; best = 32760
        for v in range(nodes):
            if not opened[v] or f[v] >= best: continue
            best = f[v]; u = v
        if u < 0: break
        if u == nodes - 1: found = True; break
        opened[u] = False; closed[u] = True
        r, c = divmod(u, side)
        for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            nr, nc = r + dr, c + dc
            if not (0 <= nr < side and 0 <= nc < side) or (nr, nc) in blocked: continue
            v = nr * side + nc
            if closed[v] or g[u] + 1 >= g[v]: continue
            g[v] = g[u] + 1; f[v] = g[v] + abs(nr-side+1) + abs(nc-side+1)
            parent[v] = u; opened[v] = True
    path = []
    if found:
        v = nodes - 1
        while v >= 0:
            path.append(v + 1)
            if v == 0: break
            v = parent[v]
        path.reverse()
    assert found and len(path) == side * 2 - 1
    return source, [side, 1, len(path), sum((i + 1) * v for i, v in enumerate(path))]


def chess_mate(p):
    positions = p["positions"]
    original = (Path(__file__).resolve().parent.parent / "examples/ai/11_chess_mate.dub").read_text()
    check = original[original.index("      subroutine chk"):original.index("*execute")]
    source = deck("chess", f"""
      program chess
      integer b(8,8),iw,ir,wr,wc,rr,rc,i,j,res,np
      integer nm,mt,mr,mc,tm,tt,cs
      np=0
      tm=0
      tt=0
      cs=0
      do 100 iw=1,64
        wr=(64-iw)/8+1
        wc=(64-iw)-8*((64-iw)/8)+1
        if (wr .ge. 7 .and. wc .ge. 7) goto 100
        do 90 ir=1,64
          rr=(ir-1)/8+1
          rc=ir-8*((ir-1)/8)
          if (rr .eq. wr .and. rc .eq. wc) goto 90
          if (rr .eq. 8 .and. rc .eq. 8) goto 90
          do 20 i=1,8
            do 10 j=1,8
              b(i,j)=0
   10       continue
   20     continue
          b(wr,wc)=1
          b(rr,rc)=2
          b(8,8)=-1
          call chk(b,8,8,res)
          if (res .ne. 0) goto 90
          call solve(b,rr,rc,nm,mt,mr,mc)
          np=np+1
          tm=tm+nm
          tt=tt+mt
          cs=cs+np*(nm+64*mt)
          cs=cs-65521*(cs/65521)
          if (np .eq. {positions}) goto 200
   90   continue
  100 continue
  200 print 300, np,tm,tt,cs
      stop
  300 format('RESULT',4i12)
      end
      subroutine solve(b,rr,rc,nmove,mate,mr,mc)
      integer b(8,8),dr(4),dc(4)
      integer rr,rc,nmove,mate,mr,mc
      integer i,j,k,d,nr,nc,res,res2,legal,sav
      dr(1)=-1
      dc(1)=0
      dr(2)=1
      dc(2)=0
      dr(3)=0
      dc(3)=-1
      dr(4)=0
      dc(4)=1
      nmove=0
      mate=0
      mr=0
      mc=0
      do 100 d=1,4
        nr=rr+dr(d)
        nc=rc+dc(d)
   40   continue
        if (nr .lt. 1) goto 100
        if (nr .gt. 8) goto 100
        if (nc .lt. 1) goto 100
        if (nc .gt. 8) goto 100
        if (b(nr,nc) .eq. 1) goto 100
        if (b(nr,nc) .eq. -1) goto 100
        b(rr,rc)=0
        b(nr,nc)=2
        nmove=nmove+1
        call chk(b,8,8,res)
        if (res .eq. 0) goto 80
        legal=0
        do 50 k=1,4
          i=8+dr(k)
          j=8+dc(k)
          if (i .lt. 1) goto 50
          if (i .gt. 8) goto 50
          if (j .lt. 1) goto 50
          if (j .gt. 8) goto 50
          if (b(i,j) .eq. 1) goto 50
          sav=b(i,j)
          b(8,8)=0
          b(i,j)=-1
          call chk(b,i,j,res2)
          if (res2 .eq. 0) legal=1
          b(i,j)=sav
          b(8,8)=-1
          if (legal .eq. 1) goto 60
   50   continue
   60   continue
        if (legal .ne. 0) goto 80
        mate=mate+1
        mr=nr
        mc=nc
   80   b(nr,nc)=0
        b(rr,rc)=2
        nr=nr+dr(d)
        nc=nc+dc(d)
        goto 40
  100 continue
      return
      end
{check.strip(chr(10))}
""")
    directions = [(-1, 0), (1, 0), (0, -1), (0, 1)]
    def checked(board, kr, kc):
        for i in range(max(0, kr-1), min(8, kr+2)):
            for j in range(max(0, kc-1), min(8, kc+2)):
                if board[i][j] == 1: return True
        for dr, dc in directions:
            i, j = kr + dr, kc + dc
            while 0 <= i < 8 and 0 <= j < 8:
                if board[i][j]:
                    if board[i][j] == 2: return True
                    break
                i += dr; j += dc
        return False
    count = total_moves = total_mates = checksum = 0
    for wk in reversed(range(64)):
        wr, wc = divmod(wk, 8)
        if wr >= 6 and wc >= 6: continue
        for rook in range(64):
            rr, rc = divmod(rook, 8)
            if (rr, rc) in [(wr, wc), (7, 7)]: continue
            board = [[0]*8 for _ in range(8)]
            board[wr][wc] = 1; board[rr][rc] = 2; board[7][7] = -1
            if checked(board, 7, 7): continue
            moves = mates = 0
            for dr, dc in directions:
                nr, nc = rr + dr, rc + dc
                while 0 <= nr < 8 and 0 <= nc < 8:
                    if board[nr][nc] in (1, -1): break
                    board[rr][rc] = 0; board[nr][nc] = 2; moves += 1
                    if checked(board, 7, 7):
                        legal = False
                        for er, ec in directions:
                            i, j = 7 + er, 7 + ec
                            if not (0 <= i < 8 and 0 <= j < 8) or board[i][j] == 1: continue
                            saved = board[i][j]; board[7][7] = 0; board[i][j] = -1
                            if not checked(board, i, j): legal = True
                            board[i][j] = saved; board[7][7] = -1
                            if legal: break
                        if not legal: mates += 1
                    board[nr][nc] = 0; board[rr][rc] = 2
                    nr += dr; nc += dc
            count += 1; total_moves += moves; total_mates += mates
            checksum = (checksum + count * (moves + 64 * mates)) % 65521
            if count == positions: return source, [count, total_moves, total_mates, checksum]
    raise AssertionError(f"Only {count} admissible positions")


def mlp(p):
    epochs = p["epochs"]
    original = (Path(__file__).resolve().parent.parent / "examples/ai/10_mlp_xor.dub").read_text()
    body = original.split("*fortran\n", 1)[1].split("*execute", 1)[0]
    body = body.replace("      integer i,ep", "      real outval(4)\n      integer i,ep")
    body = body.replace("ep=1,2000", f"ep=1,{epochs}")
    body, replaced = re.subn(r"(?mi)^[ \t]*print 100, x1\(i\), x2\(i\), y\(i\), o[ \t]*$", "        outval(i)=o", body)
    if replaced != 1: raise ValueError("Expected one MLP output assignment")
    body = body.replace("      print 110, loss", "      print 120, outval(1),outval(2),outval(3),outval(4),loss")
    body = body.replace("      end", "  120 format('RESULT',5f18.6)\n      end", 1)
    if len(re.findall(r"(?mi)^[ \t]*print\b", body)) != 1:
        raise ValueError("MLP must print only after completing all predictions")
    source = deck("mlp", body)
    x = [(0., 0.), (0., 1.), (1., 0.), (1., 1.)]; y = [0., 1., 1., 0.]
    w11, w12, w21, w22, v1, v2 = .5, .1, .3, .7, .2, .4
    b1, b2, b3, eta = -.1, .2, -.3, .5
    for _ in range(epochs):
        for (a, b), target in zip(x, y):
            h1 = 1. / (1. + math.exp(-(w11*a+w12*b+b1)))
            h2 = 1. / (1. + math.exp(-(w21*a+w22*b+b2)))
            o = 1. / (1. + math.exp(-(v1*h1+v2*h2+b3)))
            d3 = o - target
            d1 = d3*v1*h1*(1.-h1); d2 = d3*v2*h2*(1.-h2)
            v1 -= eta*d3*h1; v2 -= eta*d3*h2; b3 -= eta*d3
            w11 -= eta*d1*a; w12 -= eta*d1*b
            w21 -= eta*d2*a; w22 -= eta*d2*b
            b1 -= eta*d1; b2 -= eta*d2
    values = []; loss = 0.
    for (a, b), target in zip(x, y):
        h1 = 1. / (1. + math.exp(-(w11*a+w12*b+b1)))
        h2 = 1. / (1. + math.exp(-(w21*a+w22*b+b2)))
        o = 1. / (1. + math.exp(-(v1*h1+v2*h2+b3)))
        values.append(o); loss += (o-target)*(o-target)
    return source, values + [loss]


GENERATORS = {"perceptron": perceptron, "knn": knn, "kmeans": kmeans,
              "naive_bayes": naive_bayes, "decision_stump": decision_stump,
              "genetic": genetic, "annealing": annealing,
              "qlearning": qlearning, "astar": astar, "chess_mate": chess_mate,
              "mlp": mlp}


def generate(case):
    return GENERATORS[case.algorithm](case.parameters)
