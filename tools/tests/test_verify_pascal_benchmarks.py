import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import verify_pascal_benchmarks as pascal


class PascalBenchmarkTests(unittest.TestCase):
    def dhrystone_output(self, passes=5000):
        values = {'INTGLOB': 5, 'ARR1GLOB 8': 7, 'ARR2GLOB 8/7': passes+10,
                  'PTRGLOB.DISCR': 0, 'PTRGLOB.ENUM': 2, 'PTRGLOB.INT': 17,
                  'NEXTPTR.DISCR': 0, 'NEXTPTR.ENUM': 1, 'NEXTPTR.INT': 18,
                  'INT1LOC': 5, 'INT2LOC': 13, 'INT3LOC': 7, 'ENUMLOC': 1}
        text = '\n'.join(f'{key}: {value}' for key, value in values.items())
        text += '''
BOOLGLOB: TRUE
CH1GLOB: A
CH2GLOB: B
PTRGLOB.STR: DHRYSTONE PROGRAM, SOME STRING
NEXTPTR.STR: DHRYSTONE PROGRAM, SOME STRING
LOCAL STRING1: DHRYSTONE PROGRAM, 1′ST STRING
LOCAL STRING2: DHRYSTONE PROGRAM, 2′ND STRING
BAD CONTROL VALUES= 0
ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC
ALL 4 STRING VALUES MATCH: YES
GUEST CPU SECONDS: 1.0
'''
        return text + f'GUEST DMIPS: {passes/1757:.9f}'

    def whetstone_output(self):
        values = ('-1.1332394367686494', '-1.1332181986811034', '0', '12',
                  '.49999052352563617', '.99999988878912238', '3', '.75001003274361189')
        return '\n'.join(f'N{i} VALUE {value}' for i, value in enumerate(values, 1)) + '''
CHECK SUM 14.9835428096086
BAD CONTROL VALUES= 0
ALL 8 CONTROL VALUES MATCH: YES
GUEST CPU SECONDS: 1.0
GUEST MWIPS: 15.869
'''

    def test_dhrystone_checks_load_and_strings_independently_of_guest_claim(self):
        for passes in (5000, 10000):
            pascal.validate(self.dhrystone_output(passes), 'dh', 'good', passes)
        for wrong in ('INTGLOB: 6', 'CH2GLOB: C', 'PTRGLOB.STR: WRONG STRING'):
            label = wrong.split(':')[0]
            text = '\n'.join(wrong if line.startswith(label+':') else line
                             for line in self.dhrystone_output().splitlines())
            with self.assertRaises(ValueError):
                pascal.validate(text, 'dh', 'good', 5000)

    def test_whetstone_independent_section_and_checksum(self):
        text = self.whetstone_output()
        pascal.validate(text, 'whet', 'good', 100)
        for wrong in (text.replace('N7 VALUE 3', 'N7 VALUE 4'),
                      text.replace('CHECK SUM 14.9835428096086', 'CHECK SUM 15')):
            with self.assertRaises(ValueError):
                pascal.validate(wrong, 'whet', 'good', 100)

    def test_wrong_rating_and_insufficient_interval_are_rejected(self):
        for text in (self.whetstone_output().replace('15.869', '100'),
                     self.whetstone_output().replace('SECONDS: 1.0', 'SECONDS: 0.02')):
            with self.assertRaises(ValueError):
                pascal.validate(text, 'whet', 'good', 100)

    def test_guard_must_be_executed_and_have_no_numeric_rating(self):
        message = 'DMIPS UNAVAILABLE: ZERO PASSES'
        pascal.validate(message, 'dh', 'zero', 0)
        for text in (" 123 WRITELN('"+message+"');", message+'\nGUEST DMIPS: 10'):
            with self.assertRaises(ValueError):
                pascal.validate(text, 'dh', 'zero', 0)

    def test_all_variants_preserve_pascal_card_order_and_cpu_timer(self):
        for case in pascal.CASES:
            family, scenario, passes, source = pascal.prepare(case)
            self.assertEqual(source.splitlines()[1], '*pascal')
            self.assertIn('*library:22\n*execute', source)
            self.assertIn('rt1-rt0', source.lower())
            self.assertTrue(all(len(line) <= 72 for line in source.splitlines()))


if __name__ == '__main__':
    unittest.main()
