import importlib.util
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("check_warnings", ROOT / "tools" / "check_warnings.py")
cw = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(cw)


LOG = """
Some other output.
  E:\\p\\proj\\src\\A.cs(12,5): warning CS8618: Non-nullable field '_x' must contain a non-null value. [E:\\p\\proj\\proj.csproj]
  E:\\p\\proj\\src\\A.cs(30,9): warning CS8618: Non-nullable field '_y' must contain a non-null value. [E:\\p\\proj\\proj.csproj]
  E:\\p\\proj\\src\\B.cs( 5,1): warning CS8600: Converting null literal or possible null value to non-nullable type. [E:\\p\\proj\\proj.csproj]
  E:\\p\\proj\\src\\A.cs(99,9): error CS1002: ; expected. [E:\\p\\proj\\proj.csproj]
  2 Warning(s)
"""


def snap(total, by_code, fc):
    return {"total": total, "by_code": by_code, "fc": [list(x) for x in fc], "rows": []}


class ParseWarningsTests(unittest.TestCase):
    def test_extracts_cs_warnings_only(self):
        p = cw.parse_warnings(LOG)
        self.assertEqual(3, p["total"])                       # error CS1002 не считается
        self.assertEqual({"CS8618": 2, "CS8600": 1}, p["by_code"])
        self.assertIn(("E:\\p\\proj\\src\\A.cs", "CS8618"), set(map(tuple, p["fc"])))
        self.assertIn(("E:\\p\\proj\\src\\B.cs", "CS8600"), set(map(tuple, p["fc"])))
        self.assertNotIn(("E:\\p\\proj\\src\\A.cs", "CS1002"), set(map(tuple, p["fc"])))

    def test_empty_text(self):
        p = cw.parse_warnings("")
        self.assertEqual(0, p["total"])
        self.assertEqual({}, p["by_code"])
        self.assertEqual([], p["fc"])


class CompareWarningsTests(unittest.TestCase):
    allowed = snap(3, {"CS8618": 2, "CS8600": 1},
                   [["A.cs", "CS8618"], ["B.cs", "CS8600"]])

    def test_equal_passes(self):
        current = snap(3, {"CS8618": 2, "CS8600": 1}, [["A.cs", "CS8618"], ["B.cs", "CS8600"]])
        self.assertTrue(cw.compare(current, self.allowed)["passed"])

    def test_subset_passes(self):
        # CS8618 исправлен в A.cs: стало 1 (A.cs) + B.cs — меньше, но (A.cs,CS8618) всё ещё в allowed
        current = snap(2, {"CS8618": 1, "CS8600": 1}, [["A.cs", "CS8618"], ["B.cs", "CS8600"]])
        self.assertTrue(cw.compare(current, self.allowed)["passed"])

    def test_fully_fixed_code_passes(self):
        # CS8600 полностью устранён
        current = snap(2, {"CS8618": 2}, [["A.cs", "CS8618"]])
        r = cw.compare(current, self.allowed)
        self.assertTrue(r["passed"])
        self.assertEqual([], r["new_codes"])

    def test_new_code_fails(self):
        current = snap(4, {"CS8618": 2, "CS8600": 1, "CS4189": 1},
                       [["A.cs", "CS8618"], ["B.cs", "CS8600"], ["C.cs", "CS4189"]])
        r = cw.compare(current, self.allowed)
        self.assertFalse(r["passed"])
        self.assertEqual(["CS4189"], r["new_codes"])
        self.assertIn(["C.cs", "CS4189"], r["new_fc"])

    def test_increased_count_fails(self):
        current = snap(5, {"CS8618": 3, "CS8600": 2},
                       [["A.cs", "CS8618"], ["B.cs", "CS8600"], ["C.cs", "CS8618"], ["D.cs", "CS8600"]])
        r = cw.compare(current, self.allowed)
        self.assertFalse(r["passed"])
        self.assertEqual({"CS8618": (2, 3), "CS8600": (1, 2)}, r["increased"])

    def test_new_file_same_code_fails(self):
        # тот же код CS8600, но в новом файле D.cs (count тот же 1) — всё равно новое место
        current = snap(3, {"CS8618": 2, "CS8600": 1}, [["A.cs", "CS8618"], ["D.cs", "CS8600"]])
        r = cw.compare(current, self.allowed)
        self.assertFalse(r["passed"])
        self.assertIn(["D.cs", "CS8600"], r["new_fc"])


if __name__ == "__main__":
    unittest.main()