"""Structural and semantic controls; run on the Linux experiment host."""
import re
import unittest
from ai_scaling_cases import cases, generate
from benchmark_ai_scaling_host import native_kernel, python_kernel
from benchmark_ai_scaling import compare


class ScalingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.small = {case.algorithm: case for case in cases() if case.level == 0}
        cls.generated = {name: generate(case) for name, case in cls.small.items()}

    def test_case_matrix(self):
        matrix = cases()
        self.assertEqual(len(matrix), 41)
        self.assertEqual(len({case.algorithm for case in matrix}), 11)
        self.assertEqual(sum(case.level == 3 for case in matrix), 10)
        self.assertEqual(len({case.id for case in matrix}), len(matrix))

    def test_fixed_form_and_portable_print(self):
        for name, (source, _) in self.generated.items():
            with self.subTest(name=name):
                for line in source.splitlines():
                    if not line.startswith(("*", "c", "C")):
                        self.assertLessEqual(len(line), 72)
                    match = re.search(r"\bprint\s+\d+\s*,(.*)", line, re.I)
                    if match:
                        for item in re.split(r",(?![^()]*\))", match[1]):
                            self.assertFalse(item.strip().isdigit(), line)

    def test_host_kernel_excludes_source_generation(self):
        for name, case in self.small.items():
            with self.subTest(name=name):
                function, standalone = python_kernel(name)
                self.assertNotIn("deck(", standalone)
                self.assertNotIn("read_text", standalone)
                self.assertEqual(compare(self.generated[name][1], function(case.parameters), float_atol=1e-12, float_rtol=1e-12), [])

    def test_mlp_has_one_output_after_all_predictions(self):
        source = self.generated["mlp"][0]
        self.assertEqual(len(re.findall(r"(?mi)^[ \t]*print\b", source)), 1)
        self.assertIn("outval(i)=o", source)
        self.assertLess(source.index("outval(i)=o"), source.index("print 120"))

    def test_no_whole_program_repeat_padding(self):
        for source, _ in self.generated.values():
            self.assertNotIn("irep", source.lower())

    def test_native_timing_wrapper_removes_labeled_prints(self):
        for name, (source, _) in self.generated.items():
            body = source.split("*fortran\n", 1)[1].split("*execute", 1)[0]
            wrapped = native_kernel(body)
            self.assertIsNone(re.search(r"(?mi)^\s*(?:\d+\s+)?print\s", wrapped), name)
            self.assertIn("subroutine solvebench(sink)", wrapped)
            if name == "chess_mate": self.assertIn("  200 continue", wrapped)

    def test_annealing_schedule_is_positive(self):
        source = self.generated["annealing"][0]
        self.assertNotIn("t=t*0.8", source.lower())
        self.assertIn("t=5./(1.+49.*it/", source)
        self.assertAlmostEqual(self.generated["annealing"][1][-1], .1)


if __name__ == "__main__":
    unittest.main()
