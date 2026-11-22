#!/usr/bin/env python3
"""Export a fail-closed, single-host scaling data set and its raw evidence."""
from __future__ import annotations
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import statistics
from benchmark_ai_scaling import compare, extract_results

ORDER = ["perceptron", "knn", "kmeans", "naive_bayes", "decision_stump", "genetic", "annealing", "qlearning", "astar", "mlp", "chess_mate"]
NAMES = ["Перцептрон", "Метод k ближайших соседей", "Метод k-средних", "Наивный байесовский классификатор", "Решающий пень", "Генетический алгоритм", "Имитация отжига", "Q-обучение", "Поиск A*", "Многослойный перцептрон", "Минишахматы"]
OLD_IDS = ["01_perceptron", "02_knn", "03_kmeans", "04_naive_bayes", "05_decision_stump", "06_genetic", "07_annealing", "08_qlearning", "09_astar", "10_mlp_xor", "11_chess_mate"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--include-evidence", action="store_true", help="include full printer/console profiles for an external archive, never the compact Git export")
    args = parser.parse_args()
    work, out = args.work.resolve(), args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    if any(out.iterdir()): raise RuntimeError("Choose an empty export directory")
    def load(path): return json.loads(path.read_text())
    def save(path, data):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+"\n")
    manifest = load(work/"manifest-0-1-2-3.json")
    native = {row["id"]: row for row in load(work/"native-results.json")}
    host = load(work/"host-benchmark-results.json")
    host_rows = {row["id"]: row for row in host["rows"]}
    parity = load(work/"original-mode-validation.json")
    if len(manifest) != 41 or len(host_rows) != 11 or len(parity) != 12 or not all(p["equal"] for p in parity):
        raise RuntimeError("Incomplete manifest, timings, or original-mode validation")
    all_rows = []; model_rows = {}; simh_rows = []; gdr_rows = []
    def pick_simh(case, translator):
        prefix = case["id"] + ("-fortran" if translator == "fortran" else "")
        candidates = []
        for path in sorted((work/"simh-runs").glob(prefix+"-attempt-*/result.json")):
            row = load(path)
            if row["sourceSha256"] == case["sourceSha256"] and row.get("translatorControlCard", "ftn") == translator:
                candidates.append(row)
        successes = [row for row in candidates if row["verified"]]
        return (successes or candidates)[-1] if candidates else None
    for case in manifest:
        source = work/"jobs"/(case["id"]+".dub")
        if hashlib.sha256(source.read_bytes()).hexdigest() != case["sourceSha256"]: raise RuntimeError("Stale source: "+case["id"])
        model = load(work/"profiles"/(case["id"]+"-unpaced.json"))
        if model["sourceSha256"].lower() != case["sourceSha256"] or not model["success"] or not model["observerCountMatchesStatistics"]:
            raise RuntimeError("Invalid model result: "+case["id"])
        if compare(case["expected"], extract_results(model["output"])): raise RuntimeError("Model control mismatch: "+case["id"])
        if not native[case["id"]]["verified"] or native[case["id"]]["sourceSha256"] != case["sourceSha256"]:
            raise RuntimeError("Invalid native control: "+case["id"])
        simh = pick_simh(case, "fortran")
        if simh is None or not simh["verified"]: raise RuntimeError("SIMH check is not complete: "+case["id"])
        gdr = pick_simh(case, "ftn")
        simh_rows.append(simh)
        if gdr: gdr_rows.append(gdr)
        model_rows[case["id"]] = model
        all_rows.append({**case, **{k:model[k] for k in ("originalModelSeconds", "beforeProgramSeconds", "computeSeconds", "computeInstructions", "computeFraction", "completedInstructions", "allocatedUserWordsFromLinkMap")}, "verifiedPython": True, "verifiedGnuFortran": True, "verifiedSimhDubna": True})
        if args.include_evidence:
            for directory in ("jobs", "native"):
                target = out/directory/source.with_suffix(".dub" if directory=="jobs" else ".f").name
                target.parent.mkdir(exist_ok=True)
                shutil.copy2(work/directory/target.name, target)
            save(out/"model-profiles"/(case["id"]+".json"), model)
            shutil.copytree(Path(simh["runDirectory"]), out/"simh-fortran"/case["id"], dirs_exist_ok=True, ignore=shutil.ignore_patterns("*.bin", "boot_dispak.b6"))
            if gdr:
                shutil.copytree(Path(gdr["runDirectory"]), out/"simh-gdr"/case["id"], dirs_exist_ok=True, ignore=shutil.ignore_patterns("*.bin", "boot_dispak.b6"))
    rows = []
    for algorithm, name, identifier in zip(ORDER, NAMES, OLD_IDS):
        selected = max((case for case in manifest if case["algorithm"] == algorithm), key=lambda c:c["level"])
        m, h = model_rows[selected["id"]], host_rows[selected["id"]]
        if not h["verified"] or h["sourceSha256"] != selected["sourceSha256"]: raise RuntimeError("Stale host timing: "+algorithm)
        row = {"id":identifier, "case_id":selected["id"], "name":name, "parameters":selected["parameters"], "source_sha256":selected["sourceSha256"],
               "besm_compute_s":m["computeSeconds"], "besm_exec_s":m["originalModelSeconds"]-m["beforeProgramSeconds"],
               "besm_io_s":m["originalModelSeconds"]-m["beforeProgramSeconds"]-m["computeSeconds"],
               "besm_preparation_s":m["beforeProgramSeconds"], "besm_total_s":m["originalModelSeconds"],
               "besm_instructions_per_pass":m["computeInstructions"], "besm_compute_fraction":m["computeFraction"], "loaded_user_words":m["allocatedUserWordsFromLinkMap"],
               "python_solve_s":h["pythonComputeSeconds"], "python_process_s":h["pythonProcessSeconds"],
               "fortran_solve_s":h["fortranComputeSeconds"], "fortran_process_s":h["fortranProcessSeconds"], "fortran_compile_s":h["fortranCompilationSeconds"],
               "verified_python":True, "verified_gfortran":True, "verified_simh_dubna":True}
        for key, numerator, denominator in [
            ("speedup_besm_python_solve","besm_compute_s","python_solve_s"),
            ("speedup_besm_fortran_solve","besm_compute_s","fortran_solve_s"),
            ("speedup_fortran_python_solve","python_solve_s","fortran_solve_s"),
            ("speedup_besm_python_total","besm_total_s","python_process_s"),
            ("speedup_besm_fortran_total","besm_total_s","fortran_process_s"),
            ("speedup_python_fortran_process","python_process_s","fortran_process_s")]:
            row[key] = row[numerator]/row[denominator]
        rows.append(row)
    summary = {key:{"min":min(r[key] for r in rows),"max":max(r[key] for r in rows),"median":statistics.median(r[key] for r in rows)} for key in rows[0] if key.startswith("speedup_")}
    comparison = {"schema":"besm6.ai-comparison.scaled-linux.v1", "generated_at":datetime.now(timezone.utc).isoformat(), "dataset":"ai-scaling-linux-2026-11-23", "host":host["metadata"],
                  "runtime_base_commit":"737349e10e87f339cec369340be384a531edfb8a", "runtime_assembly_sha256":hashlib.sha256((work/"probe"/"Besm6.Runtime.dll").read_bytes()).hexdigest(), "model":"HostedOriginalTiming, originalTimeFactor=0.670839, originalMathFactor=1.769005", "windows_pilots_included":False,
                  "methods":{"besm":"model time at program entry, first formatted output, and halt; includes data setup and control totals", "modern_kernel":"process CPU time; median of five adaptive repetition series", "modern_process":"wall time; median of ten starts", "native_compile":"wall time; median of three -O0 compilations and links"},
                  "verification":{"cases":41,"besm_python":41,"gnu_python":41,"simh_dubna_python":41,"gdr_cases":len(gdr_rows),"gdr_matches":sum(r["verified"] for r in gdr_rows),"original_mode_pairs":len(parity)}, "summary":summary,"rows":rows}
    save(out/"comparison.json", comparison)
    with (out/"comparison.csv").open("w",encoding="utf-8",newline="") as stream:
        fields=["id","case_id","name","besm_preparation_s","besm_compute_s","besm_io_s","besm_total_s","besm_compute_fraction","python_solve_s","fortran_solve_s","python_process_s","fortran_process_s","fortran_compile_s","speedup_besm_python_solve","speedup_besm_fortran_solve"]
        writer=csv.DictWriter(stream,fieldnames=fields,extrasaction="ignore")
        writer.writeheader();writer.writerows(rows)
    save(out/"all-sizes.json", all_rows)
    save(out/"manifest.json", manifest)
    save(out/"native-results.json", list(native.values()))
    save(out/"host-benchmark-results.json", host)
    def compact_check(row):
        return {key:row[key] for key in ("id","algorithm","level","parameters","sourceSha256","expected","observed","verified","problems","simhCommit","translatorControlCard","compilerDates") if key in row}
    save(out/"simh-fortran-results.json", list(map(compact_check,simh_rows)))
    save(out/"simh-gdr-results.json", list(map(compact_check,gdr_rows)))
    paired=[]
    for pair in parity:
        record={"id":pair["id"],"equal":pair["equal"]}
        for mode in ("original","unpaced"):
            raw=load(work/"profiles"/(pair["id"]+"-"+mode+".json"))
            record[mode]={key:raw[key] for key in ("originalModelSeconds","computeSeconds","completedInstructions","sourceSha256","memorySha256")}
            record[mode]["outputSha256"]=hashlib.sha256(raw["output"].encode()).hexdigest()
        paired.append(record)
    save(out/"original-mode-validation.json", paired)
    save(out/"environment.json", {**host["metadata"],"dotnetRuntime":"8.0.21","evidenceStoredOutsideGit":True})
    for name in ("image-sha256.txt","simh-commit.txt"):
        shutil.copy2(work/name, out/name)
    if args.include_evidence:
        for source in (work/"profiles").glob("*-original.json"):
            save(out/"original-mode-profiles"/source.name, load(source))
        for name in ("linux-environment.txt","tools-tests.txt"):
            shutil.copy2(work/name,out/name)
        shutil.copytree(work/"host-benchmark",out/"host-kernels",dirs_exist_ok=True,ignore=shutil.ignore_patterns("*.o","program","driver"))
    if (work/"diagnostics").exists():
        if args.include_evidence:
            shutil.copytree(work/"diagnostics"/"simh-runs",out/"diagnostics"/"simh-runs",dirs_exist_ok=True,ignore=shutil.ignore_patterns("*.bin","boot_dispak.b6"))
        path=work/"diagnostics"/"simh-results.json"
        if path.exists(): save(out/"diagnostic-results.json",list(map(compact_check,load(path))))
    frozen = {str(path.relative_to(out)):hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(out.rglob("*")) if path.is_file() and path.name!="sha256.json"}
    save(out/"sha256.json", frozen)
    print(json.dumps(comparison["verification"], indent=2))
    print("Exported",len(frozen),"files to",out)


if __name__ == "__main__": main()
