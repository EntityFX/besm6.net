import importlib.util
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("diff_trace", ROOT / "tools" / "diff_trace.py")
diff_trace = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(diff_trace)


HEADER = [
    "seq", "pc", "half", "raw48", "rk24", "opcode", "reg", "addr",
    "acc_b", "rmr_b", "rau_b", "mod_b", "amod_b", "aex_b",
    "acc_a", "rmr_a", "rau_a", "mod_a", "amod_a", "aex_a", "pc_a", "half_a",
]


def row(**changes):
    values = {
        "seq": "0", "pc": "1032", "half": "L", "raw48": "1A7FFB038602",
        "rk24": "1A7FFB", "opcode": "160", "reg": "1", "addr": "32763",
        "acc_b": "000000000000", "rmr_b": "000000000000", "rau_b": "0",
        "mod_b": "0", "amod_b": "0", "aex_b": "0",
        "acc_a": "000000000000", "rmr_a": "000000000000", "rau_a": "0",
        "mod_a": "0", "amod_a": "0", "aex_a": "32763", "pc_a": "1032", "half_a": "R",
    }
    values.update({key: str(value) for key, value in changes.items()})
    return "\t".join(values[column] for column in HEADER)


class TraceDiffTests(unittest.TestCase):
    def write_trace(self, directory, name, rows):
        path = Path(directory) / name
        path.write_text("\t".join(HEADER) + "\n" + "\n".join(rows) + "\n", encoding="utf-8")
        return path

    def test_reports_first_post_state_divergence_with_matching_pre_state(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp = self.write_trace(directory, "cpp.tsv", [row(acc_a="000000000001")])
            cs = self.write_trace(directory, "cs.tsv", [row(acc_a="000000000002")])

            result = diff_trace.compare_traces(cpp, cs)

        self.assertEqual("ACC_RMR", result.classification)
        self.assertEqual(0, result.sequence)
        self.assertTrue(result.pre_match)
        self.assertEqual({"acc_a": ("000000000001", "000000000002")}, result.differences)

    def test_reports_fetch_divergence_at_first_different_raw_word(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp = self.write_trace(directory, "cpp.tsv", [row(raw48="AAAAAAAAAAAA", rk24="AAAAAA")])
            cs = self.write_trace(directory, "cs.tsv", [row(raw48="BBBBBBBBBBBB", rk24="BBBBBB")])

            result = diff_trace.compare_traces(cpp, cs)

        self.assertEqual("FETCH", result.classification)
        self.assertEqual(0, result.sequence)
        self.assertIn("raw48", result.differences)

    def test_sequence_is_part_of_alignment_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp = self.write_trace(directory, "cpp.tsv", [row(seq=10)])
            cs = self.write_trace(directory, "cs.tsv", [row(seq=11)])

            result = diff_trace.compare_traces(cpp, cs)

        self.assertEqual("CONTROL_FLOW", result.classification)
        self.assertEqual({"seq": ("10", "11")}, result.differences)

    def test_control_flow_mismatch_takes_precedence_over_raw_word(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp = self.write_trace(directory, "cpp.tsv", [row(pc=100, raw48="AAAAAAAAAAAA")])
            cs = self.write_trace(directory, "cs.tsv", [row(pc=101, raw48="BBBBBBBBBBBB")])

            result = diff_trace.compare_traces(cpp, cs)

        self.assertEqual("CONTROL_FLOW", result.classification)
        self.assertIn("pc", result.differences)
        self.assertIn("raw48", result.differences)

    def test_accepts_legacy_five_line_cpp_records(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp = Path(directory) / "cpp-legacy.tsv"
            cpp.write_text(
                "\t".join(HEADER[:8]) + "\n"
                + "\t".join(HEADER[8:14]) + "\n"
                + "\t".join(HEADER[14:14]) + "\n"
                + "\t".join(HEADER[14:22]) + "\n"
                + "\n"
                + "\t".join(row().split("\t")[:8]) + "\n"
                + "\t".join(row().split("\t")[8:14]) + "\n"
                + "\n"
                + "\t".join(row().split("\t")[14:22]) + "\n"
                + "\n",
                encoding="utf-8",
            )
            cs = self.write_trace(directory, "cs.tsv", [row()])

            result = diff_trace.compare_traces(cpp, cs)

        self.assertEqual("MATCH", result.classification)

    def test_classifies_incomplete_legacy_terminal_record(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp = Path(directory) / "cpp-legacy.tsv"
            values = row(seq=17, pc=279, opcode=60).split("\t")
            cpp.write_text(
                "\t".join(HEADER[:8]) + "\n"
                + "\t".join(HEADER[8:14]) + "\n"
                + "\n"
                + "\t".join(HEADER[14:22]) + "\n"
                + "\n"
                + "\t".join(values[:8]) + "\n"
                + "\t".join(values[8:14]) + "\n"
                + "\n",
                encoding="utf-8",
            )
            cs = self.write_trace(directory, "cs.tsv", [row(seq=17, pc=279, opcode=60)])

            try:
                result = diff_trace.compare_traces(cpp, cs)
            except ValueError as error:
                self.fail(f"A truncated terminal record must be classified, not rejected: {error}")

        self.assertEqual("TRACE_TRUNCATED", result.classification)
        self.assertEqual(17, result.sequence)
        self.assertTrue(result.pre_match)
        self.assertEqual(("incomplete", "complete"), result.differences["trace"])

    def test_keeps_five_rows_of_context_around_divergence(self):
        with tempfile.TemporaryDirectory() as directory:
            cpp_rows = [row(seq=i, pc=100 + i) for i in range(4)]
            cs_rows = [row(seq=i, pc=100 + i) for i in range(4)]
            cs_rows[2] = row(seq=2, pc=102, acc_a="000000000001")
            cpp = self.write_trace(directory, "cpp.tsv", cpp_rows)
            cs = self.write_trace(directory, "cs.tsv", cs_rows)

            result = diff_trace.compare_traces(cpp, cs)

        self.assertEqual(["0", "1"], [pair[0]["seq"] for pair in result.context_before])
        self.assertEqual(["3"], [pair[0]["seq"] for pair in result.context_after])


if __name__ == "__main__":
    unittest.main()
