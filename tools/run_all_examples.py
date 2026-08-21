#!/usr/bin/env python3
"""Прогон всех .dub примеров и сбор статистики."""
import subprocess, os, time, json, sys, glob

CWD = r"e:\Projects\besm6.net"
DLL = os.path.join(CWD, r"src\besm6.net\bin\Debug\net8.0\besm6.dll")
EXAMPLES_DIR = os.path.join(CWD, "examples")
LIMIT = "500000"  # 500K инструкций

def find_dub_files():
    files = []
    for root, dirs, fnames in os.walk(EXAMPLES_DIR):
        # Пропускаем tapes/ (это не примеры) и README.md
        rel_root = os.path.relpath(root, EXAMPLES_DIR)
        if rel_root.startswith("tapes"):
            continue
        for f in fnames:
            if f.endswith(".dub"):
                files.append(os.path.join(root, f))
    files.sort()
    return files

def run_one(path):
    rel = os.path.relpath(path, CWD)
    cmd = ["dotnet", DLL, "run", path, "--limit", LIMIT]
    start = time.time()
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=120, cwd=CWD)
        elapsed = time.time() - start
        out = r.stdout + r.stderr
        # Классификация результата
        if "Halted by STOP" in out:
            status = "OK"
        elif "limit" in out.lower() and "reached" in out.lower():
            status = "TIMEOUT"
        elif r.returncode != 0:
            status = "ERROR"
        else:
            status = "OK"
        # Извлечь число инструкций
        import re
        m = re.search(r"after (\d+) instructions", out)
        instrs = m.group(1) if m else "?"
        return {"file": rel, "status": status, "instrs": instrs, "secs": round(elapsed,1), "rc": r.returncode}
    except subprocess.TimeoutExpired:
        return {"file": rel, "status": "HANG", "instrs": "?", "secs": 120, "rc": -1}
    except Exception as e:
        return {"file": rel, "status": "EXC", "instrs": "?", "secs": 0, "rc": -1, "err": str(e)}

def main():
    files = find_dub_files()
    print(f"Found {len(files)} .dub files")
    results = []
    for i, f in enumerate(files):
        print(f"[{i+1}/{len(files)}] {os.path.relpath(f, CWD)}", end=" ... ", flush=True)
        r = run_one(f)
        print(r["status"], f"({r['instrs']} instr, {r['secs']}s)")
        results.append(r)
    # Статистика
    ok = sum(1 for r in results if r["status"] == "OK")
    to = sum(1 for r in results if r["status"] == "TIMEOUT")
    err = sum(1 for r in results if r["status"] == "ERROR")
    hang = sum(1 for r in results if r["status"] == "HANG")
    exc = sum(1 for r in results if r["status"] == "EXC")
    print(f"\n=== STATS: {ok} OK, {to} TIMEOUT, {err} ERROR, {hang} HANG, {exc} EXC | total {len(results)} ===")
    # Сохранить JSON
    outpath = os.path.join(CWD, "examples_report.json")
    with open(outpath, "w") as f2:
        json.dump(results, f2, indent=2, ensure_ascii=False)
    print(f"Report saved to {outpath}")

if __name__ == "__main__":
    main()