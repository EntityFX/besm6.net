#!/usr/bin/env python3
"""Summarize evented dotnet-trace Speedscope profiles of the processor thread."""
import argparse
from collections import Counter
import json
import pathlib


def aggregate(profile, frames):
    if profile["type"] != "evented":
        raise ValueError("Expected an evented Speedscope profile")
    stack = []
    previous = profile["startValue"]
    own, inclusive = Counter(), Counter()
    total = 0.0
    for event in profile["events"]:
        delta = event["at"] - previous
        if delta < 0:
            raise ValueError("Events are not chronological")
        if stack and delta:
            names = [frames[i]["name"] for i in stack
                     if frames[i]["name"] not in ("CPU_TIME", "UNMANAGED_CODE_TIME")]
            total += delta
            if names:
                own[names[-1]] += delta
                for name in set(names):
                    inclusive[name] += delta
        if event["type"] == "O":
            stack.append(event["frame"])
        elif event["type"] == "C":
            if not stack or stack.pop() != event["frame"]:
                raise ValueError("Unbalanced stack")
        else:
            raise ValueError("Unknown event type")
        previous = event["at"]
    if stack:
        raise ValueError("Unclosed stack")
    return total, own, inclusive


def summarize(path):
    source = json.loads(path.read_text(encoding="utf-8-sig"))
    candidates = []
    for profile in source["profiles"]:
        total, own, inclusive = aggregate(profile, source["shared"]["frames"])
        processor = sum(time for name, time in own.items() if "Besm6.Core." in name)
        candidates.append((processor, profile, total, own, inclusive))
    processor, profile, total, own, inclusive = max(candidates, key=lambda row: row[0])
    if not total or not processor:
        raise ValueError("No processor thread samples")

    def rows(counter, limit):
        return [{"method": name, "sample_time": duration, "percent": duration / total * 100}
                for name, duration in counter.most_common(limit)]

    return {"file": str(path.resolve()), "thread": profile["name"], "unit": profile["unit"],
            "observed_stack_time": total, "own": rows(own, 40),
            "processor_own": rows(Counter({k: v for k, v in own.items() if "Besm6.Core." in k}), 40),
            "processor_inclusive": rows(Counter({k: v for k, v in inclusive.items()
                                               if "Besm6.Core." in k}), 40)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", nargs="+", type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    args = parser.parse_args()
    results = [summarize(path) for path in args.files]
    args.output.write_text(json.dumps(results, indent=2), encoding="utf-8")
    for result in results:
        print(result["file"], result["thread"])
        for row in result["processor_own"][:8]:
            print(f"  {row['percent']:6.2f}% {row['method']}")


if __name__ == "__main__":
    main()
