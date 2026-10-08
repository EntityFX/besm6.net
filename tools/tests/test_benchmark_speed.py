import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("benchmark_speed", Path(__file__).resolve().parents[1] / "benchmark_speed.py")
bench = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bench)


class BenchmarkSpeedTests(unittest.TestCase):
    def memory_output(self, part=1):
        values = {0: ("1", "1", "4096", 0),
                  1: ("0.000244140625", "1", "2048.5", 12),
                  2: ("1.244140625", "1001", "2052596", 18),
                  3: ("1.1220703125", "501", "1028346", 18)}
        first, last, total, traffic = values[part]
        return (f"compiler echo ALL VALUES MATCH: YES\n≠\nMEMSPEED PART {part}\n"
                f"WORDS=4096\nREPEAT PASSES=1000\nBYTES/ELEMENT={traffic}\n"
                f"DATA TRAFFIC BYTES={4096*1000*traffic}\nFIRST RESULT={first}\n"
                f"LAST RESULT={last}\nCHECKSUM={total}\nBAD ELEMENTS=0\n"
                "ALL VALUES MATCH: YES\nHalted by STOP at 0118 after 100 instructions\n")

    def test_memory_kernels_match_independent_endpoints_and_sum(self):
        for part in range(4):
            bench.parse_run(self.memory_output(part), f"mem{part}", 0)
        with self.assertRaises(ValueError):
            bench.parse_run(self.memory_output(2).replace("CHECKSUM=2052596", "CHECKSUM=4096"), "mem2", 0)
        with self.assertRaises(ValueError):
            bench.parse_run(self.memory_output().replace("BAD ELEMENTS=0", "BAD ELEMENTS=1"), "mem1", 0)

    def test_memory_traffic_uses_guest_words_and_rejects_compiler_echo(self):
        with self.assertRaises(ValueError):
            bench.parse_run(self.memory_output().replace("BYTES/ELEMENT=12", "BYTES/ELEMENT=16"), "mem1", 0)
        with self.assertRaises(ValueError):
            bench.parse_run(self.memory_output().replace("ALL VALUES MATCH: YES\nHalted", "ALL VALUES MATCH: NO\nHalted"), "mem1", 0)

    def output(self, part=1, result="99.909962369711-02"):
        flops = 9000 * 30 * bench.OPWD[part]
        message = "BASELINE RUN: NO KERNEL EXECUTED" if not part else "CALCULATIONS PERFORMED"
        return ("compiler echo: ALL SAME: YES CALCULATIONS PERFORMED\n≠\n"
                f"MP-MFLOPS PART {part}\nWORDS= 9000\nOPS/WORD= {bench.OPWD[part]}\n"
                f"REPEAT PASSES= 30\nFLOPS= {flops}\nFIRST RESULT= {result}\n"
                f"{message}\nALL SAME: YES\nHalted by STOP at 0118 after 10 instructions\n")

    def test_fortran_exponent_without_e(self):
        self.assertEqual(bench.fortran_number("99.856051500828-02"), bench.Decimal("0.99856051500828"))
        self.assertEqual(bench.fortran_number("86.399999999848+05"), bench.Decimal("8639999.9999848"))

    def test_completed_numerically_valid_run(self):
        result = bench.parse_run(self.output(), "mp1", 0)
        self.assertEqual(result["halt_instructions"], 10)

    def test_rejects_limit_and_nonzero_exit(self):
        for text, code in ((self.output().replace("Halted by STOP", "Instruction limit"), 0), (self.output(), 2)):
            with self.assertRaises(ValueError):
                bench.parse_run(text, "mp1", code)

    def test_rejects_compiler_echo_as_validation(self):
        with self.assertRaises(ValueError):
            bench.parse_run(self.output().replace("ALL SAME: YES\nHalted", "ALL SAME: NO\nHalted"), "mp1", 0)

    def test_rejects_uniform_but_wrong_result(self):
        with self.assertRaises(ValueError):
            bench.parse_run(self.output(result="1.000000"), "mp1", 0)

    def test_baseline_is_expected_to_skip_calculations(self):
        bench.parse_run(self.output(0, "0.999999"), "mp0", 0)

    def test_parity_rejects_memory_and_register_differences(self):
        left = {"halt_instructions": 1, "program_sha256": "a", "report": {
            "finalState": {"a": 0}, "memorySha256": "a", "statistics": {"completedInstructions": 1, "modelCycles": 2}}}
        right = {**left, "report": {**left["report"], "memorySha256": "b"}}
        with self.assertRaises(ValueError):
            bench.parity(left, right)
        right["report"]["memorySha256"] = "a"
        right["report"]["finalState"] = {"a": 1}
        with self.assertRaises(ValueError):
            bench.parity(left, right)

    def test_unobserved_runtime_state_compares_to_instrumented_calibration(self):
        state = {"finalState": {"a": 7}, "memorySha256": "abc"}
        left = {"halt_instructions": 1, "program_sha256": "a", "report": state}
        right = {"halt_instructions": 1, "program_sha256": "a", "report": None,
                 "runtime": {**state, "elapsedSeconds": 0.1}}
        bench.parity(left, right)
        right["runtime"]["finalState"] = {"a": 8}
        with self.assertRaises(ValueError):
            bench.parity(left, right)

    def test_workload_scores_use_matching_baselines_and_separate_time_units(self):
        rows = [{"job": "mp0", "variant": "max", "process_seconds": 0.9, "loop_seconds": 0.6},
                {"job": "mp1", "variant": "max", "process_seconds": 1.2, "loop_seconds": 0.8}]
        calibration = {job + ":max": {"report": {"statistics": {"modelCycles": cycles}}}
                       for job, cycles in [("mp0", 1000000), ("mp1", 10000000)]}
        metrics = bench.workload_metrics(rows, calibration)["mp1:max"]
        self.assertAlmostEqual(0.6, metrics["model_score"])
        self.assertAlmostEqual(1.8, metrics["host_process_score"])
        self.assertAlmostEqual(2.7, metrics["host_loop_score"])
        rows[1]["process_seconds"] = 0.8
        self.assertIsNone(bench.workload_metrics(rows, calibration)["mp1:max"]["host_process_score"])


if __name__ == "__main__":
    unittest.main()
