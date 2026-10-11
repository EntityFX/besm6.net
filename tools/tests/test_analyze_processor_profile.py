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

    def test_measurement_window_can_start_and_end_inside_an_open_stack(self):
        frames = [{"name": name} for name in ["caller", "callee", "CPU_TIME"]]
        events = [("O", 0, 0), ("O", 1, 2), ("O", 2, 2),
                  ("C", 2, 8), ("C", 1, 8), ("C", 0, 10)]
        profile = {"type": "evented", "startValue": 0,
                   "events": [{"type": kind, "frame": frame, "at": at} for kind, frame, at in events]}
        total, own, inclusive = aggregate(profile, frames, 3, 7)
        self.assertEqual(4, total)
        self.assertEqual({"callee": 4}, dict(own))
        self.assertEqual({"caller": 4, "callee": 4}, dict(inclusive))
        total, own, inclusive = aggregate(profile, frames, 11, 12)
        self.assertEqual(0, total)
        self.assertEqual({}, dict(own))
        self.assertEqual({}, dict(inclusive))

    def test_window_does_not_hide_an_unbalanced_stack_outside_measurement(self):
        with self.assertRaisesRegex(ValueError, "Unbalanced"):
            aggregate({"type": "evented", "startValue": 0, "events": [
                {"type": "O", "frame": 0, "at": 0}, {"type": "C", "frame": 1, "at": 1}]},
                [{"name": "a"}], 2, 3)
