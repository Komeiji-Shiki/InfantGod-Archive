"""从各组件的官方源保存许可证，供离线交付。"""
import concurrent.futures
import pathlib
import sys
import urllib.request
from html.parser import HTMLParser

sys.stdout.reconfigure(encoding="utf-8")
root = pathlib.Path(__file__).parent / "LICENSES"
root.mkdir(exist_ok=True)
sources = {
    "CC-BY-NC-SA-4.0.txt": "https://raw.githubusercontent.com/creativecommons/creativecommons.org/main/docroot/legalcode/by-nc-sa_4.0.html",
    "BepInEx-MIT.txt": "https://raw.githubusercontent.com/BepInEx/BepInEx/v5.4.23.5/LICENSE",
    "UnityDoorstop.txt": "https://raw.githubusercontent.com/NeighTools/UnityDoorstop/master/LICENSE",
    "HarmonyX.txt": "https://raw.githubusercontent.com/BepInEx/HarmonyX/master/LICENSE",
    "Harmony-Original.txt": "https://raw.githubusercontent.com/BepInEx/HarmonyX/master/LICENSE.Harmony",
    "Harmony.txt": "https://raw.githubusercontent.com/pardeike/Harmony/master/LICENSE",
    "MonoMod.txt": "https://raw.githubusercontent.com/MonoMod/MonoMod/reorganize/LICENSE",
    "Mono.Cecil.txt": "https://raw.githubusercontent.com/jbevain/cecil/master/LICENSE.txt",
}

class PlainLegalCode(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []
        self.skipping = False

    def handle_starttag(self, tag, attrs):
        if tag in {"script", "style"}: self.skipping = True

    def handle_endtag(self, tag):
        if tag in {"script", "style"}: self.skipping = False
        if tag in {"p", "li", "h1", "h2", "h3", "br"}: self.parts.append("\n\n")

    def handle_data(self, data):
        if not self.skipping: self.parts.append(data)

def fetch(item):
    filename, url = item
    text = urllib.request.urlopen(url, timeout=30).read().decode("utf-8-sig")
    if filename.startswith("CC-"):
        (root / "CC-BY-NC-SA-4.0.html").write_text(text, encoding="utf-8")
        parser = PlainLegalCode()
        parser.feed(text)
        text = "\n".join(line.strip() for line in "".join(parser.parts).splitlines() if line.strip())
    (root / filename).write_text(text, encoding="utf-8")
    return filename + ": " + text.splitlines()[0]

with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
    futures = [pool.submit(fetch, item) for item in sources.items()]
    for future in concurrent.futures.as_completed(futures):
        print(future.result())
(root / "SOURCES.txt").write_text("\n".join(name + "\n" + url + "\n" for name, url in sources.items()), encoding="utf-8")
