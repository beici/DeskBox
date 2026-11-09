import os
import tempfile
import unittest
from unittest import mock

import static_gate


class StaticGateTests(unittest.TestCase):
    def test_csharp_escapes_preserve_unicode(self):
        self.assertEqual('中文\r\n"path"\\file',
                         static_gate.decode_csharp_literal(r'中文\r\n\"path\"\\file'))
        self.assertEqual('A😀', static_gate.decode_csharp_literal(r'\u0041\U0001F600'))

    def test_multiline_empty_catch_counts_as_one_block(self):
        source = os.path.join(static_gate.SRC, 'Fixture.cs')
        files = {source: 'try { Work(); } catch\n{\n}\ncatch (IOException) { Log(); }'}
        with mock.patch.object(static_gate, 'rg', return_value=[]), \
                mock.patch.object(static_gate, '_fallback_repo_files', return_value=files):
            _, catches, _ = static_gate.check_forbidden_patterns()
        self.assertEqual(1, len(catches))

    def test_fallback_excludes_assertion_literals_but_keeps_test_targets(self):
        with tempfile.TemporaryDirectory() as root:
            files = {os.path.join(root, 'src', 'App.cs'): 'production',
                     os.path.join(root, 'tests', 'Fixture.cs'):
                     'void ProbeMethod() { Assert.Contains("assertion itself", source, StringComparison.Ordinal); }',
                     os.path.join(root, 'tests', 'Tests.csproj'): '<Project />'}
            with mock.patch.object(static_gate, 'ROOT', root), \
                    mock.patch.object(static_gate, '_RG_AVAILABLE', False), \
                    mock.patch.object(static_gate, '_fallback_repo_files', return_value=files):
                content = '\n'.join(static_gate._literal_repository_contents())
                self.assertIn('production', content)
                self.assertIn('ProbeMethod', content)
                self.assertIn('<Project />', content)
                self.assertNotIn('assertion itself', content)

    def test_report_identity_keeps_legacy_escaped_newline(self):
        with tempfile.TemporaryDirectory() as root:
            with open(os.path.join(root, 'FixtureContractTests.cs'), 'w', encoding='utf-8') as source:
                source.write(r'Assert.Contains("\n  \"transactionId\"", json, StringComparison.Ordinal);')
            baseline = {'contract_misses': [r'FixtureContractTests.cs :: \n  "transactionId"']}
            with mock.patch.object(static_gate, 'ROOT', root), \
                    mock.patch.object(static_gate, 'TESTS', root), \
                    mock.patch.object(static_gate, 'load_baseline', return_value=baseline), \
                    mock.patch.object(static_gate, '_literal_repository_contents', return_value=[]):
                _, new_misses, all_misses, _ = static_gate.replay_contract_assertions()
            self.assertEqual([], new_misses)
            self.assertEqual(baseline['contract_misses'], all_misses)

    def test_replay_uses_string_overloads_and_actual_newlines(self):
        with tempfile.TemporaryDirectory() as root:
            test_path = os.path.join(root, 'FixtureContractTests.cs')
            with open(test_path, 'w', encoding='utf-8') as source:
                source.write(r'''
Assert.Contains("first\r\nsecond", source, StringComparison.Ordinal);
Assert.Contains("UPPERCASE", source, StringComparison.OrdinalIgnoreCase);
Assert.Contains("start:begin", events);
items.Contains("unrelated collection value");
''')
            with mock.patch.object(static_gate, 'ROOT', root), \
                    mock.patch.object(static_gate, 'TESTS', root), \
                    mock.patch.object(static_gate, 'load_baseline', return_value={}), \
                    mock.patch.object(static_gate, '_literal_repository_contents',
                                      return_value=['first\nsecond uppercase']):
                hits, new_misses, _, _ = static_gate.replay_contract_assertions()
        self.assertEqual(2, hits)
        self.assertEqual([], new_misses)

    def test_missing_source_literal_still_fails(self):
        with tempfile.TemporaryDirectory() as root:
            with open(os.path.join(root, 'FixtureContractTests.cs'), 'w', encoding='utf-8') as source:
                source.write('Assert.Contains("required guard", source, StringComparison.Ordinal);')
            with mock.patch.object(static_gate, 'ROOT', root), \
                    mock.patch.object(static_gate, 'TESTS', root), \
                    mock.patch.object(static_gate, 'load_baseline', return_value={}), \
                    mock.patch.object(static_gate, '_literal_repository_contents', return_value=['other code']):
                _, new_misses, _, _ = static_gate.replay_contract_assertions()
        self.assertEqual(1, len(new_misses))


if __name__ == '__main__':
    unittest.main()
