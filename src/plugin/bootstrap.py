"""将编译依赖保存在项目目录，游戏运行无需联网。"""
import pathlib
import sys
import urllib.request
import zipfile

sys.stdout.reconfigure(encoding="utf-8")
root = pathlib.Path(__file__).parent / "deps"
root.mkdir(exist_ok=True)
feed = root / "feed"
feed.mkdir(exist_ok=True)
archive = root / "BepInEx_win_x64_5.4.23.5.zip"
if not archive.exists():
    urllib.request.urlretrieve("https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip", archive)
if not (root / "bepinex/BepInEx/core/BepInEx.dll").exists():
    with zipfile.ZipFile(archive) as package:
        package.extractall(root / "bepinex")
print("项目内的编译依赖已准备完成。")
