"""从本机游戏资源读取简体中文名称，不修改游戏文件。"""
from pathlib import Path
import argparse,csv,io,json
import UnityPy

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-path',type=Path,required=True,help='Aistalt.exe 所在目录')
parser.add_argument('--output-dir',type=Path,required=True)
args=parser.parse_args()
bundle=args.game_path/'Aistalt_Data/data.unity3d'
environment=UnityPy.load(str(bundle))
for obj in environment.objects:
    if obj.type.name!='TextAsset':continue
    asset=obj.read()
    if asset.m_Name!='language':continue
    text=asset.m_Script
    if not isinstance(text,str):text=text.decode('utf-8-sig')
    rows=csv.DictReader(io.StringIO(text))
    if 'Chinese [zh-CN]' not in (rows.fieldnames or []):continue
    translations={row['Key']:row['Chinese [zh-CN]'] for row in rows if row.get('Key')}
    args.output_dir.mkdir(parents=True,exist_ok=True)
    target=args.output_dir/'localization.json'
    target.write_text(json.dumps(translations,ensure_ascii=False,indent=2),encoding='utf8')
    print(f'已提取 {len(translations)} 条中文文本：{target}')
    break
else:
    raise SystemExit('没有找到名为 language 的简体中文表。当前游戏版本的数据格式可能已改变。')
