import json
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import verify_guest_ratings as guest


class GuestRatingVerificationTests(unittest.TestCase):
    def test_clock_neutral_labels_and_legacy_model_labels_use_the_same_formula(self):
        text = "ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC\nGUEST CPU SECONDS: 1.52\nGUEST DMIPS: 1.872210"
        self.assertEqual(guest.validate(text, "dh", "good", 1),
                         guest.validate(text.replace("GUEST ", "GUEST MODEL "), "dh", "good", 1))

    def test_all_variants_have_unique_source_anchors_and_preserve_original_files(self):
        for case in guest.CASES:
            family, scenario, part, clock, source = guest.prepare(case)
            self.assertTrue(source.startswith("*name"), case)
            self.assertIn("call ctime", source.lower(), case)
            self.assertEqual(clock, scenario != "off", case)
            self.assertIn(f"GUEST {guest.UNITS[family]}", source, case)

    def test_guard_requires_executed_output_not_a_compiler_listing(self):
        self.assertEqual(guest.validate("MFLOPS UNAVAILABLE: ZERO KERNEL", "mp", "zero", 0)["guard"], "zero")
        with self.assertRaises(ValueError):
            guest.validate(" 1020 FORMAT ('MFLOPS UNAVAILABLE: ZERO KERNEL')", "mp", "zero", 0)

    def test_guard_rejects_a_numeric_rating_even_if_failure_message_is_present(self):
        with self.assertRaises(ValueError):
            guest.validate("MB/S UNAVAILABLE: CONTROL VALUES DO NOT MATCH\nGUEST MODEL MB/S: 1.0", "mem", "bad", 1)

    def test_dhrystone_formula_uses_guest_seconds_and_reference_1757(self):
        text = "ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC\nGUEST MODEL CPU SECONDS: 1.52\nGUEST MODEL DMIPS: 1.872210"
        self.assertEqual(guest.validate(text, "dh", "good", 1)["rating"], 1.872210)
        with self.assertRaises(ValueError):
            guest.validate(text.replace("1.872210", "2.0"), "dh", "good", 1)

    def test_positive_single_quantum_does_not_authorize_a_rating(self):
        with self.assertRaises(ValueError):
            guest.validate("ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC\nGUEST MODEL CPU SECONDS: 0.02\nGUEST MODEL DMIPS: 142.287990", "dh", "good", 1)

    def test_stop_and_final_state_are_required(self):
        report = {"finalState": {"k": 1}, "statistics": {"modelCycles": 2}, "memorySha256": "abc"}
        output = "≠\nresult\nHalted by STOP at 00001 after 2 instructions\nExecution stats: " + json.dumps(report)
        program, instructions, parsed = guest.extract(output, 0)
        self.assertEqual((program, instructions, parsed), ("result", 2, report))
        for invalid, code in [(output, 1), (output.replace("Halted by STOP", "instruction limit"), 0),
                              (output.split("Execution stats:")[0], 0)]:
            with self.assertRaises(ValueError):
                guest.extract(invalid, code)

    def test_ambiguous_source_substitution_is_rejected(self):
        for source in ("none", "DATA DATA"):
            with self.assertRaises(ValueError):
                guest.substitute(source, "DATA", "NEW")


if __name__ == "__main__":
    unittest.main()
