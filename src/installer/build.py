"""把已构建的 Mod 文件嵌入独立 Windows 图形安装器。"""
from pathlib import Path
import argparse
import os
import gzip
import subprocess
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--package', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parent
package = args.package.resolve()
required = [
    'Install-Mod.ps1', 'Uninstall-Mod.ps1', 'Installer-Common.ps1', 'winhttp.dll', 'doorstop_config.ini',
    'BepInEx/plugins/InfantGodArchive/Graywill.InfantGodCodex.dll',
    'BepInEx/plugins/InfantGodArchive/webhost/Graywill.InfantGod.WebHost.exe',
    'Aistalt_Data/StreamingAssets/Mods/InfantGodArchive/mod.json',
]
for filename in required:
    if not (package / filename).is_file():
        raise SystemExit('安装包缺少文件：' + filename)
build = root / 'obj'
build.mkdir(exist_ok=True)
payload = build / 'payload.zip'
assets = root.parents[1] / 'assets'
font = build / 'pixel-font.gz'
font.write_bytes(gzip.compress((assets / 'fonts/fusion-pixel.ttf').read_bytes(), mtime=0))
with zipfile.ZipFile(payload, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
    for path in sorted(package.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(package)
        if 'webhost-profile' in relative.parts or path.name == 'progress.json' or path.suffix in ('.log', '.pdb'):
            raise SystemExit('请使用干净的构建目录，安装包中包含运行数据：' + str(relative))
        bundle.write(path, relative.as_posix())
compiler = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if not compiler.exists():
    raise SystemExit('需要 Windows .NET Framework 4.8 编译器。')
args.output.parent.mkdir(parents=True, exist_ok=True)
command = [
    str(compiler), '/nologo', '/optimize+', '/target:winexe', '/platform:x64',
    '/out:' + str(args.output.resolve()), '/win32manifest:' + str(root / 'app.manifest'),
    '/resource:' + str(payload) + ',InfantGodArchive.Payload.zip',
    '/resource:' + str(font) + ',InfantGodArchive.PixelFont.gz',
    '/resource:' + str(assets / 'portraits/Aistalt_Tech.png') + ',InfantGodArchive.AistaltTech.png',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
    '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
] + [str(path) for path in sorted(root.glob('*.cs'))]
subprocess.run(command, check=True)
print('已生成一键安装器：' + str(args.output))
