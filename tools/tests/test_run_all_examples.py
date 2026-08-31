import importlib.util
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("run_all_examples", ROOT / "tools" / "run_all_examples.py")
rae = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(rae)


def make_result(status, file="examples/name.dub", rc=0, instrs="209174", secs=1.2,
                stdout="Halted by STOP after 209174 instructions", stderr=""):
    return {"file": file, "status": status, "instrs": instrs, "secs": secs, "rc": rc,
            "stdout": stdout, "stderr": stderr, "timed_out": False}


class PortabilityTests(unittest.TestCase):
    def test_default_root_is_repo_root(self):
        # tools/run_all_examples.py -> корень проекта (родитель tools/)
        self.assertEqual(ROOT, rae.default_root())

    def test_no_hardcoded_absolute_path_in_source(self):
        src = (ROOT / "tools" / "run_all_examples.py").read_text(encoding="utf-8")
        self.assertNotIn("e:\\projects", src.lower())
        self.assertNotIn("c:\\users", src.lower())
        self.assertNotIn("/home/", src)
        self.assertNotIn("/Users/", src)


class NormOnlyTests(unittest.TestCase):
    def test_none_stays_none(self):
        self.assertIsNone(rae.norm_only(None))

    def test_string_comma_separated(self):
        self.assertEqual(frozenset({"a", "b"}), rae.norm_only("a, b ,"))

    def test_list_input(self):
        self.assertEqual(frozenset({"x", "y"}), rae.norm_only(["x", "y"]))

    def test_empty_becomes_none(self):
        self.assertIsNone(rae.norm_only(""))
        self.assertIsNone(rae.norm_only(" , "))


class FindDubFilesTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        d = Path(self.tmp.name)
        (d / "name.dub").write_text("x", encoding="utf-8")
        (d / "algol.dub").write_text("x", encoding="utf-8")
        (d / "sub").mkdir()
        (d / "sub" / "c.dub").write_text("x", encoding="utf-8")
        (d / "tapes").mkdir()
        (d / "tapes" / "monsys.dub").write_text("x", encoding="utf-8")
        (d / "notes.md").write_text("x", encoding="utf-8")
        self.dir = d

    def tearDown(self):
        self.tmp.cleanup()

    def test_finds_dub_and_skips_tapes_and_non_dub(self):
        found = {p.name for p in rae.find_dub_files(self.dir)}
        self.assertEqual({"name.dub", "algol.dub", "c.dub"}, found)

    def test_filter_by_name(self):
        found = {p.name for p in rae.find_dub_files(self.dir, "name,algol")}
        self.assertEqual({"name.dub", "algol.dub"}, found)

    def test_filter_matches_subdir_stem(self):
        found = {p.name for p in rae.find_dub_files(self.dir, "c")}
        self.assertEqual({"c.dub"}, found)

    def test_missing_dir_returns_empty(self):
        self.assertEqual([], rae.find_dub_files(self.dir / "nope"))


class ClassifyTests(unittest.TestCase):
    def test_ok_on_stop(self):
        self.assertEqual("OK", rae.classify(0, "Halted by STOP after 5 instructions"))

    def test_halted_on_other_controlled_stop(self):
        self.assertEqual("HALTED", rae.classify(0, "Halted by EOF"))

    def test_no_stop_when_no_marker(self):
        self.assertEqual("NO_STOP", rae.classify(0, "no halt marker here"))

    def test_limit_on_exit_2(self):
        self.assertEqual("LIMIT", rae.classify(2, "limit reached"))

    def test_error_on_other_nonzero(self):
        self.assertEqual("ERROR", rae.classify(1, "Error: something"))
        self.assertEqual("ERROR", rae.classify(7, "whatever"))

    def test_timeout_overrides(self):
        self.assertEqual("HANG", rae.classify(0, "Halted by STOP", timed_out=True))


class ExtractInstrsTests(unittest.TestCase):
    def test_extracts_count(self):
        self.assertEqual("209174", rae.extract_instrs("Halted by STOP after 209174 instructions"))

    def test_absent_returns_none(self):
        self.assertIsNone(rae.extract_instrs("no count here"))
        self.assertIsNone(rae.extract_instrs(None))


class CompareGoldenTests(unittest.TestCase):
    def test_identical(self):
        self.assertTrue(rae.compare_golden("a\nb", "a\nb"))

    def test_crlf_vs_lf_normalized(self):
        self.assertTrue(rae.compare_golden("a\r\nb\r\n", "a\nb"))

    def test_blank_lines_and_trailing_spaces_ignored(self):
        self.assertTrue(rae.compare_golden("  a  \n\n  b\n", "a\nb"))

    def test_different_content(self):
        self.assertFalse(rae.compare_golden("a\nb", "a\nc"))


class BuildJsonReportTests(unittest.TestCase):
    def test_summary_and_public_projection(self):
        results = [
            make_result("OK", file="examples/name.dub"),
            make_result("ERROR", file="examples/bad.dub", rc=1, instrs=None,
                        stdout="", stderr="boom"),
        ]
        report = rae.build_json_report(results, {"suite": "fast"})
        self.assertEqual(2, report["summary"]["total"])
        self.assertEqual(1, report["summary"]["passed"])
        self.assertEqual(1, report["summary"]["failed"])
        self.assertEqual({"OK": 1, "ERROR": 1}, report["summary"]["by_status"])
        for r in report["results"]:
            self.assertNotIn("stdout", r)
            self.assertNotIn("stderr", r)


class BuildJUnitTests(unittest.TestCase):
    def test_pass_has_no_failure_child_and_fail_has_detail(self):
        results = [
            make_result("OK", file="examples/name.dub"),
            make_result("ERROR", file="examples/bad.dub", rc=1, instrs=None,
                        stdout="out-tail", stderr="err-tail"),
        ]
        xml = rae.build_junit_xml(results)
        suite = ET.fromstring(xml).find("testsuite")
        self.assertEqual("2", suite.get("tests"))
        cases = suite.findall("testcase")
        self.assertEqual(2, len(cases))
        self.assertIsNone(cases[0].find("failure"))
        self.assertIsNone(cases[0].find("error"))
        failure = cases[1].find("failure")
        self.assertIsNotNone(failure)
        self.assertIn("out-tail", failure.text)
        self.assertIn("err-tail", failure.text)
        self.assertIn("status=ERROR", failure.get("message"))

    def test_error_status_emits_error_element(self):
        results = [make_result("EXC", file="examples/x.dub", rc=-1, stdout="", stderr="exc-msg")]
        suite = ET.fromstring(rae.build_junit_xml(results)).find("testsuite")
        tc = suite.find("testcase")
        self.assertIsNotNone(tc.find("error"))
        self.assertEqual("1", suite.get("errors"))


class ApplyGoldenTests(unittest.TestCase):
    def test_match_keeps_ok(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "name.txt").write_text("Halted by STOP after 209174 instructions", encoding="utf-8")
            self.assertEqual("OK", rae.apply_golden(make_result("OK"), d)["status"])

    def test_mismatch_downgrades_to_mismatch(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "name.txt").write_text("different golden", encoding="utf-8")
            self.assertEqual("MISMATCH", rae.apply_golden(make_result("OK"), d)["status"])

    def test_no_golden_dir_unchanged(self):
        self.assertEqual("OK", rae.apply_golden(make_result("OK"), None)["status"])

    def test_non_pass_unchanged_even_with_mismatch_golden(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "name.txt").write_text("different", encoding="utf-8")
            self.assertEqual("ERROR", rae.apply_golden(make_result("ERROR"), d)["status"])


class GoldenFilesTests(unittest.TestCase):
    GOLDEN_DIR = ROOT / "tests" / "golden" / "examples"

    def test_fast_goldens_exist_and_are_nonempty(self):
        for name in rae.FAST_EXAMPLES:
            gp = self.GOLDEN_DIR / (name + ".txt")
            self.assertTrue(gp.exists(), f"нет golden {gp}")
            self.assertGreater(len(gp.read_text(encoding="utf-8")), 50)

    def test_goldens_contain_controlled_stop(self):
        for name in rae.FAST_EXAMPLES:
            text = (self.GOLDEN_DIR / (name + ".txt")).read_text(encoding="utf-8")
            self.assertIn("Halted by STOP", text)

    def test_golden_is_stable_under_normalization(self):
        text = (self.GOLDEN_DIR / "name.txt").read_text(encoding="utf-8")
        self.assertTrue(rae.compare_golden(text, text))
        self.assertTrue(rae.compare_golden(text, text.replace("\n", "\r\n")))


if __name__ == "__main__":
    unittest.main()