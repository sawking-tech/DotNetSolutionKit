#!/usr/bin/env python3
"""The rules of check-editorconfig.py, with no files: python3 scripts/test_check_editorconfig.py"""

import importlib.util
import os
import unittest

spec = importlib.util.spec_from_file_location(
    "check_editorconfig", os.path.join(os.path.dirname(__file__), "check-editorconfig.py"))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class Rules(unittest.TestCase):
    def test_a_section_is_read_to_the_next(self):
        text = "root = true\n\n[*.cs]\nindent_size = 4\n\n[*.sln]\ncharset = utf-8-bom\n"
        self.assertEqual(check.rules(text), {("", "root"): "true", ("*.cs", "indent_size"): "4", ("*.sln", "charset"): "utf-8-bom"})

    def test_a_comment_and_a_blank_line_are_no_rule(self):
        self.assertEqual(check.rules("# [*.cs]\n[*.cs]\n# indent_size = 4\nindent_size = 2\n"), {("*.cs", "indent_size"): "2"})

    def test_a_value_is_read_without_the_spaces_around_it(self):
        self.assertEqual(check.rules("[*.cs]\n  indent_size   =   4   \n"), {("*.cs", "indent_size"): "4"})


class Differences(unittest.TestCase):
    def test_the_same_rules_differ_in_nothing(self):
        rules = {("*.cs", "indent_size"): "4"}
        self.assertEqual(check.differences(rules, rules), [])

    def test_a_warning_is_the_suggestion_of_a_solution(self):
        root = {("*.cs", "dotnet_diagnostic.IDE0011.severity"): "warning"}
        solution = {("*.cs", "dotnet_diagnostic.IDE0011.severity"): "suggestion"}
        self.assertEqual(check.differences(root, solution), [])

    def test_none_is_none_in_both(self):
        root = {("*.cs", "dotnet_diagnostic.IDE0160.severity"): "none"}
        self.assertEqual(check.differences(root, dict(root)), [])

    def test_a_rule_with_another_value_is_named(self):
        root = {("*.cs", "indent_size"): "4"}
        self.assertEqual(check.differences(root, {("*.cs", "indent_size"): "2"}),
                         ["[*.cs] indent_size = 2, the root says 4"])

    def test_a_rule_the_solution_lacks_is_named(self):
        self.assertEqual(check.differences({("*.cs", "indent_size"): "4"}, {}), ["[*.cs] indent_size = 4 is missing"])

    def test_a_rule_the_root_lacks_is_named(self):
        self.assertEqual(check.differences({}, {("*.cs", "indent_size"): "4"}), ["[*.cs] indent_size = 4 is not in the root"])

    def test_the_template_section_stays_in_the_root(self):
        root = {("template/**.cs", "generated_code"): "true"}
        self.assertEqual(check.differences(root, {}), [])


class TheRepository(unittest.TestCase):
    def test_the_two_files_agree(self):
        self.assertEqual(check.differences(check.rules(check.SOURCE.read_text(encoding="utf-8")),
                                           check.rules(check.TARGET.read_text(encoding="utf-8"))), [])


if __name__ == "__main__":
    unittest.main()
