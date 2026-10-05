"""将已构建的安装器、手动安装包和当前提交整理为发行文件。"""
from pathlib import Path
import argparse
import json
import shutil
import subprocess
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
version = json.loads((root / 'src/plugin/native-mod/InfantGodArchive/mod.json').read_text(encoding='utf-8'))['version']
destination = args.output.resolve()
destination.mkdir(parents=True, exist_ok=True)
package = root / 'dist/InfantGod-Archive-Mod'
with zipfile.ZipFile(destination / f'InfantGod-Archive-Mod-v{version}.zip', 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
    for path in sorted(package.rglob('*')):
        if path.is_file():
            bundle.write(path, Path(f'InfantGod-Archive-Mod-v{version}') / path.relative_to(package))
for source, name in [
    (root / f'dist/InfantGod-Archive-Setup-v{version}.exe', f'InfantGod-Archive-Setup-v{version}.exe'),
    (root / 'dist/archive.html', 'InfantGod-Archive.html'),
    (root / f'docs/RELEASE-v{version}.md', 'Release-Notes.md'),
]:
    shutil.copy2(source, destination / name)
subprocess.run([
    'git', '-C', str(root), 'archive', '--format=zip',
    f'--prefix=InfantGod-Archive-v{version}/',
    '--output=' + str(destination / f'InfantGod-Archive-Source-v{version}.zip'), 'HEAD',
], check=True)
for path in sorted(destination.iterdir()):
    print(path.name, path.stat().st_size)
