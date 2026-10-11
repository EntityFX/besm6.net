import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from benchmark_hardware_processor import compare
from inspect_processor_jit import summarize


class Stage6MeasurementTests(unittest.TestCase):
    def test_a_large_gain_cannot_hide_a_workload_regression_outside_tolerance(self):
        rows = [{"variant": variant, "workload": job, "loop_seconds": seconds}
                for variant, job, seconds in [("baseline", "control", 1), ("candidate", "control", .5),
                    ("baseline", "memory", 1), ("candidate", "memory", 1.11)]]
        result = compare(rows, "baseline", 10)["candidate"]
        self.assertTrue(result["gain_at_least_5_percent"])
        self.assertFalse(result["regressions_within_limit"])
        rows[-1]["loop_seconds"] = 1.09
        self.assertTrue(compare(rows, "baseline", 10)["candidate"]["regressions_within_limit"])

    def test_gain_is_geometric_and_is_separate_from_regression_gate(self):
        rows = [{"variant": variant, "workload": job, "loop_seconds": seconds}
                for variant, job, seconds in [("baseline", "control", 1), ("candidate", "control", .5),
                    ("baseline", "memory", 1), ("candidate", "memory", 2)]]
        result = compare(rows, "baseline", 100)["candidate"]
        self.assertAlmostEqual(0, result["geometric_mean_gain_percent"])
        self.assertFalse(result["gain_at_least_5_percent"])
        self.assertTrue(result["regressions_within_limit"])

    def test_jit_generic_call_targets_keep_type_arguments_and_drop_native_addresses(self):
        result = summarize("; Assembly listing for method Example:Run():this (Tier1)\n"
            "    call [Example:Apply[Value](byref):int]\n"
            "    call [0x1234]\n; Total bytes of code 42\n")
        self.assertEqual(42, result[0]["code_bytes"])
        self.assertEqual(["Example:Apply[Value](byref):int"], result[0]["calls"])

    def test_missing_jit_output_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "No matching"):
            summarize("No assembly was emitted")
