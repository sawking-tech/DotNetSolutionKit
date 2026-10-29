#!/usr/bin/env python3
"""The rules of check-style.py on bytes, with no files: python3 scripts/test_check_style.py"""

import importlib.util
import os
import unittest

spec = importlib.util.spec_from_file_location("check_style", os.path.join(os.path.dirname(__file__), "check-style.py"))
style = importlib.util.module_from_spec(spec)
spec.loader.exec_module(style)

BOM = style.BOM


class Findings(unittest.TestCase):
    def test_a_file_in_the_style_has_none(self):
        self.assertEqual(style.findings("a.cs", b"namespace A;\n"), [])
        self.assertEqual(style.findings("a.sln", BOM + b"\tProject\n"), [])
        self.assertEqual(style.findings("a.png", b"\x89PNG\0\r\n  \n"), [])
        self.assertEqual(style.findings("a.md", b""), [])

    def test_the_bytes_are_named(self):
        self.assertEqual(style.findings("a.cs", BOM + b"a  \r\n\tb"), [
            "has a byte order mark", "has CR line ends", "does not end with a line end",
            "has spaces at the end of a line", "has a tab in an indent"])
        self.assertEqual(style.findings("a.sln", b"x\n"), ["needs a byte order mark"])
        self.assertEqual(style.findings("a.cs", "é".encode("cp1252") + b"\n"), [style.NOT_UTF8])

    def test_cyrillic_is_test_data_of_a_test_project_and_russian_docs_only(self):
        russian = "// \u041f\u0440\u0438\u0432\u0435\u0442\n".encode()
        self.assertEqual(style.findings("tool/DotsKit/A.cs", b"a\n" + russian),
                         ["line 2 has Cyrillic: code and comments are in English"])
        self.assertEqual(style.findings("tool/DotsKit/A.cs", 'var s = """\n  \u041f\u0440\u0438\u0432\u0435\u0442\n  """;\n'.encode()),
                         ["line 2 has Cyrillic: code and comments are in English"])
        self.assertEqual(style.findings("tool/DotsKit.Tests/A.cs", russian), [])
        self.assertEqual(style.findings("docs/a.ru.md", "\u0422\u0435\u043a\u0441\u0442\n".encode()), [])

    def test_a_line_ending_in_a_lone_cr_is_a_line(self):
        self.assertEqual(style.findings("a.py", b"a\rb\r# \xd0\x9f\n")[-1], "line 3 has Cyrillic: code and comments are in English")


class Fixed(unittest.TestCase):
    def test_the_bytes_are_brought_to_the_style(self):
        self.assertEqual(style.fixed("a.cs", BOM + b"a  \r\nb\rc"), b"a\nb\nc\n")
        self.assertEqual(style.fixed("a.sln", b"x\r\n"), BOM + b"x\n")
        self.assertEqual(style.fixed("a.md", b"a\n\n\n"), b"a\n\n\n")

    def test_what_a_person_rewrites_is_left_as_it_is(self):
        self.assertEqual(style.fixed("a.cs", b"\tx\n"), b"\tx\n")
        self.assertEqual(style.fixed("a.cs", b"\xff\n"), b"\xff\n")
        self.assertEqual(style.fixed("a.png", b"\0 \r\n"), b"\0 \r\n")


if __name__ == "__main__":
    unittest.main()
