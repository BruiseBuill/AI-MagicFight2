"""Read-only repository layout checks. Run from any working directory."""
from __future__ import annotations

import argparse
import ast
import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[2]
LINK = re.compile(r'!?\[[^\]\n]*\]\(<?((?:[^()\n]|\([^()\n]*\))*)>?\)')
REFERENCE = re.compile(r'^\s*\[[^\]]+\]:\s*<?([^\s>]+)>?', re.MULTILINE)


def prose(text: str) -> str:
    """Ignore examples in fenced blocks while retaining line numbers."""
    lines = []
    fence = None
    for line in text.splitlines():
        marker = re.match(r'^\s*(`{3,}|~{3,})', line)
        if marker:
            token = marker[1]
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            lines.append('')
        else:
            lines.append('' if fence else re.sub(r'`[^`]*`', '', line))
    return '\n'.join(lines)


def check(root: Path) -> dict:
    errors: list[str] = []
    warnings: list[str] = []
    incoming: set[Path] = set()
    documents = {root / 'README.md'}
    for name in ['Docs', 'Tools', 'Captures']:
        documents.update((root / name).rglob('*.md'))
    documents.add(root / 'Artifacts/README.md')
    links = 0

    for file in sorted(documents):
        rel = file.relative_to(root).as_posix()
        if not file.is_file():
            errors.append(f'Missing entry: {rel}')
            continue
        try:
            text = file.read_text(encoding='utf-8-sig')
        except UnicodeError:
            errors.append(f'Not UTF-8: {rel}')
            continue
        body = prose(text)
        matches = [*LINK.finditer(body), *REFERENCE.finditer(body)]
        for match in matches:
            target = match[1].strip().strip('<>')
            # An optional Markdown title is not part of the destination.
            target = re.split(r'\s+[\"\']', target, maxsplit=1)[0]
            parts = urlsplit(target)
            if not target or parts.scheme or parts.netloc or target.startswith('#'):
                continue
            path = unquote(parts.path)
            if not path or '<' in path or '>' in path:
                continue
            links += 1
            resolved = (file.parent / path).resolve()
            line = body.count('\n', 0, match.start()) + 1
            if not resolved.is_relative_to(root):
                errors.append(f'{rel}:{line}: link escapes repository: {target}')
            elif not resolved.exists():
                errors.append(f'{rel}:{line}: missing link: {target}')
            else:
                incoming.add(resolved)

    for file in sorted((root / 'Docs').rglob('*')):
        if not file.is_file():
            continue
        rel = file.relative_to(root).as_posix()
        if file.suffix.lower() != '.md':
            errors.append(f'Docs must contain Markdown; move raw output to Artifacts: {rel}')
        if file.suffix == '.md' and file.name != 'README.md' and file.resolve() not in incoming:
            errors.append(f'Document missing from navigation: {rel}')

    sources = list((root / 'Assets/Scripts').rglob('*.cs'))
    sources += [p for p in (root / 'Tools').rglob('*.py') if 'archive' not in p.relative_to(root / 'Tools').parts]
    python_files = 0
    for file in sorted(sources):
        rel = file.relative_to(root).as_posix()
        text = file.read_text(encoding='utf-8-sig')
        if file.suffix == '.py':
            python_files += 1
            try:
                ast.parse(text, filename=rel)
            except SyntaxError as error:
                errors.append(f'{rel}:{error.lineno}: Python syntax: {error.msg}')
        if file.resolve() == Path(__file__).resolve():
            continue  # The checker itself contains the retired-path assertions.
        for line_number, line in enumerate(text.splitlines(), 1):
            if re.search(r'[A-Z]:[\\/]+UnityProject[\\/]+Unity_AI_CardFight2', line):
                errors.append(f'{rel}:{line_number}: hard-coded retired project root')
            if re.search(r'Assets[/\\]+Font(?:[/\\]+|[\"\'])', line):
                errors.append(f'{rel}:{line_number}: retired font path')
            if re.search(r'PROJECT\s*/\s*[\"\']Docs[\"\']\s*/\s*[\"\']art-review[\"\']', line):
                errors.append(f'{rel}:{line_number}: image output under Docs')

    for old in ['Assets/Font', 'Assets/Font.meta', 'Assets/UniversalRenderPipelineGlobalSettings.asset',
                'Docs/art-review', 'Tools/stale-audit']:
        if (root / old).exists():
            errors.append(f'Retired location recreated: {old}')
    for required in ['Assets/Art/Fonts', 'Assets/Settings/UniversalRenderPipelineGlobalSettings.asset',
                     'Docs/design', 'Docs/templates', 'Artifacts/README.md', 'Tools/ArchitectureSmoke/ArchitectureSmoke.csproj']:
        if not (root / required).exists():
            errors.append(f'Missing required location: {required}')

    records = list((root / 'Docs/implementation').glob('*.md'))
    numbers = [p.name.split('-', 1)[0] for p in records if re.match(r'^\d{2}-', p.name)]
    if len(numbers) != len(set(numbers)):
        errors.append('Duplicate legacy implementation record number')

    return {'ok': not errors, 'documents': len(documents), 'local_links': links,
            'python_files': python_files, 'errors': errors, 'warnings': warnings,
            'limits': ['External URLs and heading anchors are not checked.',
                       'Historical raw artifacts and archived scripts are not executed.',
                       'Unity import, compilation and GUID checks run separately.']}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--json', type=Path, help='Optional report path, relative to repository root or absolute')
    args = parser.parse_args()
    result = check(ROOT)
    print(f"Documents: {result['documents']}; local links: {result['local_links']}; Python files: {result['python_files']}")
    for error in result['errors']:
        print('ERROR:', error)
    for warning in result['warnings']:
        print('WARNING:', warning)
    print('PASS' if result['ok'] else f"FAIL: {len(result['errors'])} error(s)")
    if args.json:
        target = args.json if args.json.is_absolute() else ROOT / args.json
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return 0 if result['ok'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
