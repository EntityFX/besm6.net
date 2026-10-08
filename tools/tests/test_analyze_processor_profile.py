import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from analyze_processor_profile import aggregate


class ProcessorProfileTests(unittest.TestCase):
    def test_nested_samples_preserve_inclusive_and_drop_pseudo_leaf(self):
        frames = [{"name": name} for name in ["caller", "callee", "CPU_TIME"]]
        events = [("O", 0, 0), ("O", 1, 1), ("O", 2, 1), ("C", 2, 4),
                  ("C", 1, 4), ("C", 0, 5)]
        total, own, inclusive = aggregate({"type": "evented", "startValue": 0,
            "events": [{"type": kind, "frame": frame, "at": at} for kind, frame, at in events]}, frames)
        self.assertEqual(5, total)
        self.assertEqual({"caller": 2, "callee": 3}, dict(own))
        self.assertEqual({"caller": 5, "callee": 3}, dict(inclusive))

    def test_inconsistent_stack_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unbalanced"):
            aggregate({"type": "evented", "startValue": 0, "events": [
                {"type": "O", "frame": 0, "at": 0}, {"type": "C", "frame": 1, "at": 1}]},
                [{"name": "a"}])
