import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock

import static_gate


class StaticGateTests(unittest.TestCase):
    def test_source_archive_replay_includes_native_and_installer_inputs(self):
        with tempfile.TemporaryDirectory() as root:
            fixtures = {'native/header.h': 'typedef struct NativeRequest',
                        'installer/language.isl': 'LanguageName=繁體中文',
                        'installer/other.islu': 'LanguageName=한국어',
                        'build/new.sourceformat': 'new text format target',
                        'build/extensionless': 'extensionless target'}
            for relative, content in fixtures.items():
                path = Path(root, relative)
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding='utf-8')
            Path(root, 'binary.bin').write_bytes(b'\0binary-only target')
            Path(root, 'obj').mkdir()
            Path(root, 'obj', 'Generated.cs').write_text('generated-only target', encoding='utf-8')
            test_path = Path(root, 'tests', 'FixtureContractTests.cs')
            test_path.parent.mkdir()
            test_path.write_text('\n'.join(
                f'Assert.Contains("{content}", source, StringComparison.Ordinal);'
                for content in fixtures.values()), encoding='utf-8')
            outcomes = []
            for available in (True, False):
                with self.subTest(ripgrep=available), \
                        mock.patch.object(static_gate, 'ROOT', root), \
                        mock.patch.object(static_gate, 'TESTS', str(test_path.parent)), \
                        mock.patch.object(static_gate, '_RG_AVAILABLE', available), \
                        mock.patch.object(static_gate, '_REPO_FILE_CACHE', None), \
                        mock.patch.object(static_gate, 'load_baseline', return_value={}):
                    contents = '\n'.join(static_gate._literal_repository_contents())
                    self.assertNotIn('binary-only target', contents)
                    self.assertNotIn('generated-only target', contents)
                    outcomes.append(static_gate.replay_contract_assertions())
            self.assertEqual(outcomes[0], outcomes[1])
            self.assertEqual((len(fixtures), [], [], []), outcomes[0])

    @unittest.skipUnless(shutil.which('git'), 'Git file inventory requires Git')
    def test_git_inventory_respects_ignores_and_keeps_tracked_inputs(self):
        with tempfile.TemporaryDirectory() as root:
            subprocess.run(['git', 'init', '-q', root], check=True, capture_output=True)
            tracked = Path(root, 'tracked.input')
            tracked.write_text('tracked target', encoding='utf-8')
            subprocess.run(['git', '-C', root, 'add', 'tracked.input'], check=True, capture_output=True)
            Path(root, '.gitignore').write_text('*.input\nprivate/\n', encoding='utf-8')
            Path(root, 'ignored.input').write_text('ignored target', encoding='utf-8')
            Path(root, 'private').mkdir()
            Path(root, 'private', 'data.h').write_text('private target', encoding='utf-8')
            Path(root, 'new.h').write_text('untracked target', encoding='utf-8')
            for available in (True, False):
                with self.subTest(ripgrep=available), \
                        mock.patch.object(static_gate, 'ROOT', root), \
                        mock.patch.object(static_gate, '_RG_AVAILABLE', available), \
                        mock.patch.object(static_gate, '_REPO_FILE_CACHE', None):
                    contents = '\n'.join(static_gate._literal_repository_contents())
                    self.assertIn('tracked target', contents)
                    self.assertIn('untracked target', contents)
                    self.assertNotIn('ignored target', contents)
                    self.assertNotIn('private target', contents)

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
