"""用已整理的数据构建单文件 HTML，不联网，不需要安装 Python 依赖。"""
from pathlib import Path
import argparse,base64,json

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
target=args.output or root/'dist/archive.html'
data=json.loads((root/'data/archive.json').read_text(encoding='utf8'))
css=(root/'src/web/styles.css').read_text(encoding='utf8').replace('__FONT__',base64.b64encode((root/'assets/fonts/fusion-pixel.woff2').read_bytes()).decode())
js=(root/'src/web/app.js').read_text(encoding='utf8')
payload=json.dumps(data,ensure_ascii=False,separators=(',',':')).replace('<','\\u003c').replace('>','\\u003e').replace('&','\\u0026')
page=(root/'src/web/index.html').read_text(encoding='utf8').replace('/*__CSS__*/',css).replace('/*__JS__*/',js).replace('/*__DATA__*/',payload)
page=page.replace('/*__DAGRE__*/',(root/'src/web/vendor/dagre.min.js').read_text(encoding='utf8')).replace('/*__FLOW__*/',(root/'src/web/flowchart.js').read_text(encoding='utf8'))
target.parent.mkdir(parents=True,exist_ok=True)
target.write_text(page,encoding='utf8')
print('离线资料页已生成：'+str(target))
