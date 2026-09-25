"""Contract checks for the repository validator, using isolated fixtures."""
import tempfile
import unittest
from pathlib import Path

from verify_repo import check


class RepositoryChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        for directory in ['Docs/design', 'Docs/templates', 'Tools', 'Captures', 'Assets/Art/Fonts']:
            (self.root / directory).mkdir(parents=True, exist_ok=True)
        self.write('README.md', '# Repo\n\n[Spec](Docs/规范.md)\n')
        self.write('Docs/规范.md', '# Spec\n')
        self.write('Artifacts/README.md', '# Artifacts\n')
        self.write('Assets/Settings/UniversalRenderPipelineGlobalSettings.asset', 'fixture\n')
        self.write('Tools/ArchitectureSmoke/ArchitectureSmoke.csproj', '<Project />\n')

    def write(self, name, content):
        target = self.root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding='utf-8')

    def errors(self):
        return '\n'.join(check(self.root)['errors'])

    def test_valid_links_include_unicode_spaces_and_reference_style(self):
        self.write('Captures/some image.png', 'fixture')
        self.write('README.md', '# Repo\n\n[Spec][s]\n[s]: Docs/规范.md\n\n'
                   '![image](<Captures/some image.png>)\n[encoded](Captures/some%20image.png)\n')
        self.assertTrue(check(self.root)['ok'], self.errors())

    def test_examples_and_external_urls_are_not_local_links(self):
        self.write('Docs/规范.md', '# Spec\n\n```md\n[x](not-a-file.md)\n```\n'
                   '[site](https://example.invalid)\n[section](#example)\n')
        self.assertTrue(check(self.root)['ok'], self.errors())

    def test_missing_target_and_escaping_path_fail(self):
        self.write('Docs/规范.md', '# Spec\n\n[x](missing.md)\n[y](../../outside.md)\n')
        self.assertIn('missing link', self.errors())
        self.assertIn('escapes repository', self.errors())

    def test_unindexed_document_and_binary_in_docs_fail(self):
        self.write('Docs/orphan.md', '# Orphan\n')
        self.write('Docs/capture.png', 'fixture')
        self.assertIn('missing from navigation', self.errors())
        self.assertIn('Docs must contain Markdown', self.errors())

    def test_retired_asset_paths_and_invalid_python_fail(self):
        self.write('Tools/example.py', 'path = "Assets/' + 'Font/old.asset"\n')
        self.write('Tools/broken.py', 'def broken(\n')
        self.assertIn('retired font path', self.errors())
        self.assertIn('Python syntax', self.errors())

    def test_missing_required_directory_fails(self):
        (self.root / 'Assets/Art/Fonts').rmdir()
        self.assertIn('Missing required location', self.errors())


if __name__ == '__main__':
    unittest.main()
