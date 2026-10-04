"""只读整理幼神随附剧情与数据，生成离线攻略所需索引。"""
from pathlib import Path
import re, json, collections, base64, argparse, os

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-path',type=Path,default=os.environ.get('INFANTGOD_GAME_PATH'),help='包含 Aistalt.exe 的游戏目录')
parser.add_argument('--output-dir',type=Path,default=Path(__file__).parent)
args=parser.parse_args()
if not args.game_path:parser.error('请指定 --game-path，或设置 INFANTGOD_GAME_PATH。')
ROOT = args.game_path if args.game_path.name=='Aistalt_Data' else args.game_path/'Aistalt_Data'
MODS = ROOT / 'StreamingAssets/Mods'
OUT = args.output_dir
OUT.mkdir(parents=True,exist_ok=True)

CHAR_NAMES = {'Aistalt':'爱式塔','BotChecker':'菲利克斯','Greta':'阿尔娃','Nerd':'纪子','Masturbator':'米娅','MoldLand':'真菌兰德','Cop':'科尔瓦斯基','MaoGirl':'罗莎','Vmz':'伊万诺夫','Muslim':'艾曼','Gothic':'琪琪','Gachi':'阿杰','Company':'塔公司','Judge':'大法官','Journalist':'记者','NoBody':'沙舟商人','Nobody':'沙舟商人','Soldier':'士兵妹','Victim':'哈妮','Terrorist':'恐怖分子','Veteran':'科兹洛夫','Nana':'娜娜','Hachimide':'哈吉米德','Trump':'川宝','Biden':'拜恩','Commissar':'政委','Greenguard':'绿色先锋','Suicider':'轻生者','Boss':'老板','Seer':'预言者','Mesuga':'梅苏加'}

def loadtext(path):
    return path.read_text(encoding='utf-8-sig')

def clean(s):
    s = re.sub(r'<<.*?>>', '', s)
    s = re.sub(r'\s*#[\w:.-]+', '', s)
    s = re.sub(r'\[/?[^\]]+\]', '', s)
    return re.sub(r'^(?:->|=>)\s*','',s.strip())

def group_for(path):
    parts = path.parts
    if 'Characters' in parts:
        i=parts.index('Characters')
        if len(parts)>i+3:
            return parts[i+2] if parts[i+2]!='Storage' else parts[i+3]
        return parts[i+1]
    if 'Days' in parts: return 'Days'
    if 'Ends' in parts: return 'Ends'
    return 'System'

def analyze_node(node):
    rows=node['raw'].splitlines()
    active=[]; scoped=[]
    opts=[]
    node['conditions']=[]; node['checks']=[]; node['effects']=[]; node['edges']=[]
    for idx,line in enumerate(rows):
        s=line.strip(); n=node['start']+idx; indent=len(line)-len(line.lstrip())
        if not s or s.startswith('//'): continue
        while scoped and indent<=scoped[-1]['indent']: scoped.pop()
        # 缩进限定选项作用域；同缩进的新选项会结束上一个选项。
        while opts and indent<=opts[-1]['indent']:
            opts.pop()
        inline=re.findall(r'<<\s*(?:if|once if)\s+(.+?)>>',s)
        if re.match(r'^<<\s*(?:else|elseif|endif)',s):
            if active:
                old=active.pop()
                if s.startswith('<<else>>'):
                    active.append({'indent':old['indent'],'expr':'否则：'+old['expr'],'alternates':old.get('alternates',[])})
                elif s.startswith('<<elseif'):
                    expr=re.search(r'<<elseif\s+(.+?)>>',s).group(1)
                    active.append({'indent':old['indent'],'expr':'前述条件不满足，且 '+expr,'alternates':old.get('alternates',[])+[old['expr']]})
        ctx=list(dict.fromkeys([x['expr'] for x in active]+[x['expr'] for x in scoped]+inline))
        choice_path=[x['text'] for x in opts]
        cm=re.match(r'^->\s*(.*)',s)
        if cm:
            choice={'line':n,'text':clean(cm.group(1)), 'indent':indent,'conditions':ctx[:], 'path':choice_path[:], 'effects':[], 'targets':[], 'checks':[]}
            node['options'].append(choice); opts.append(choice)
            choice_path=[x['text'] for x in opts]
        for expr in inline:
            node['conditions'].append({'expr':expr,'line':n,'text':clean(s),'path':choice_path[:],'context':[x['expr'] for x in active]})
        for tag in re.findall(r'#Check:([^\s#]+)',s):
            fields=tag.split(':')
            check={'command':'Check '+' '.join(fields),'line':n,'path':choice_path[:],'conditions':ctx[:],'tag':True}
            node['checks'].append(check)
            for opt in opts: opt['checks'].append(check)
        for command in re.findall(r'<<(.+?)>>',s):
            command=command.strip()
            if command.startswith(('jump ','detour ')):
                typ,target=command.split(None,1)
                edge={'target':target.strip(),'type':typ,'line':n,'path':choice_path[:],'conditions':ctx[:]}
                node['edges'].append(edge)
                for opt in opts: opt['targets'].append(edge)
            elif command.startswith(('Check ','CheckSugar ')):
                check={'command':command,'line':n,'path':choice_path[:],'conditions':ctx[:]}
                node['checks'].append(check)
                for opt in opts: opt['checks'].append(check)
            elif re.match(r'^(set |Add|Remove|Unlock|Install|Grant|EndGame|Enable|Disable|Set|StartCombat|EnterCombat|GetAchievement|Achieve|Gottocol|Love |Hate |Damage |Heal |Break |Fragile |Dead |Energy )',command):
                effect={'command':command,'line':n,'path':choice_path[:],'conditions':ctx[:]}
                node['effects'].append(effect)
                for opt in opts: opt['effects'].append(effect)
        # 只把独立 if 视为后续块；行末 if 是该台词的条件。
        if re.match(r'^<<if\s+.+?>>\s*(?://.*)?$',s):
            active.append({'indent':indent,'expr':inline[0]})
        elif inline and s.startswith(('->','=>')):
            scoped.append({'indent':indent,'expr':' 且 '.join(inline)})
    for opt in node['options']:
        # 保留选项自己的完整片段，可在界面查看所有嵌套判断。
        at=opt['line']-node['start']; end=at+1
        while end<len(rows):
            t=rows[end]
            if t.strip() and len(t)-len(t.lstrip())<=opt['indent']: break
            end+=1
        opt['raw']='\n'.join(rows[at:end]).rstrip()

def extract():
    nodes=[]; files=[]; variables={}; declarations=[]
    yarnroot=MODS/'MyMod/Yarn'
    for path in sorted(yarnroot.rglob('*.yarn')):
        if any(x.startswith('.') for x in path.relative_to(yarnroot).parts): continue
        rel=path.relative_to(yarnroot).as_posix(); text=loadtext(path); rows=text.splitlines()
        files.append({'path':rel,'lines':len(rows),'bytes':path.stat().st_size,'group':group_for(Path(rel))})
        for i,l in enumerate(rows,1):
            m=re.search(r'<<declare\s+\$(\w+)\s*=\s*(.+?)>>\s*(?://(.*))?',l)
            if m and not l.strip().startswith('//'):
                v={'key':m[1],'default':m[2].strip(),'desc':(m[3] or '').strip(),'file':rel,'line':i,'group':group_for(Path(rel))}
                declarations.append(v); variables.setdefault(m[1],v)
        i=0
        while i<len(rows):
            if not re.match(r'^\s*title\s*:',rows[i]): i+=1; continue
            start=i; headers={}; multi=collections.defaultdict(list)
            while i<len(rows) and rows[i].strip()!='---':
                m=re.match(r'^\s*([\w]+)\s*:\s*(.*)',rows[i])
                if m: headers[m[1]]=m[2]; multi[m[1]].append(m[2])
                i+=1
            if i==len(rows): break
            body_start=i+1; i=body_start
            while i<len(rows) and rows[i].strip()!='===': i+=1
            raw='\n'.join(rows[body_start:i])
            node={'id':f'n{len(nodes)}','file':rel,'start':body_start+1,'headerLine':start+1,'title':headers.get('title',''),'subtitle':headers.get('subtitle',''),'when':multi.get('when',[]),'tags':headers.get('tags',''),'group':group_for(Path(rel)),'raw':raw,'options':[]}
            node['label']=node['title']+(('.'+node['subtitle']) if node['subtitle'] and not node['subtitle'].startswith('Auto_') else '')
            d=re.search(r'Days/Day(\d+)',rel)
            node['future']=bool(d and int(d[1])>8) or rel.startswith('Ends/') or '/Storage/' in rel or rel.endswith('ForShot.yarn')
            node['placeholder']=not any(l.strip() and not l.strip().startswith('//') for l in raw.splitlines()) or 'PlaceHolder' in raw
            analyze_node(node); nodes.append(node)
    defs={}
    for category in ('Character','AchievementDefinition','Topic','Attr','Hex','HexModule','Protocol','Memory','QuestDefinition','QuestStepDefinition','News','Email','Goods','WorldLocationDefinition','WorldMapDefinition','SpecialEffect','CombatDefinition','Result','HardwareDefinition','HardwarePartDefinition','SituationDefinition','Attachment'):
        defs[category]=[]
        for p in sorted((MODS/'SODefines/Defines'/category).glob('*.json')):
            obj=json.loads(loadtext(p)); obj['_id']=p.stem; obj['_file']=p.relative_to(MODS).as_posix(); defs[category].append(obj)
    titles=collections.defaultdict(list)
    for n in nodes:
        titles[n['title']].append(n['id'])
        if n['subtitle']: titles[n['title']+'.'+n['subtitle']].append(n['id'])
    for n in nodes:
        for edge in n['edges']: edge['ids']=titles.get(edge['target'],[])
    assets={}
    for c in defs['Character']:
        p=MODS/'SODefines'/(c.get('portrait') or '__none__')
        if p.is_file() and p.stat().st_size<200000 and 'Art/Character/' in (c.get('portrait') or '').replace('\\','/'):
            assets[c['_id']]='data:image/png;base64,'+base64.b64encode(p.read_bytes()).decode()
    # 主角的 portrait 字段是桌面入口图标；头像使用游戏已有的皮肤预览。
    skinroot=MODS/'SODefines/Sprites/Resources/Art/UI/Icon/Decoration/Skin'
    portraits={'Aistalt':'A_Origin','Hachimide':'A_Hachimide','Aistalt_Empathy':'A_Empathy','Aistalt_Tech':'A_Tech','Aistalt_Troll':'A_Troll','Aistalt_Kitsch':'A_Kitsch'}
    for key,stem in portraits.items():
        p=skinroot/(stem+'.png')
        assets[key]='data:image/png;base64,'+base64.b64encode(p.read_bytes()).decode()
    locpath=OUT/'localization.json'; loc=json.loads(locpath.read_text(encoding='utf8')) if locpath.exists() else {}
    for char in defs['Character']:
        name=loc.get(char.get('nameKey',''),'')
        if name and name!=char['_id'] and not name.endswith(('的名字','的描述')): CHAR_NAMES[char['_id']]=name
    for cat,items in defs.items():
        for item in items:
            item['_name']=loc.get(item.get('nameKey') or item.get('NameKey') or item.get('Title') or '',item['_id'])
            item['_desc']=loc.get(item.get('descKey') or item.get('DescKey') or item.get('descriptionKey') or item.get('Desc') or '', '')
            for level in item.get('levelConfigs',[]): level['_desc']=loc.get(level.get('descKey',''),'')
    data={'version':'本机 Demo 数据快照 · 2026-10-05','sourceRoot':str(MODS),'nodes':nodes,'files':files,'variables':list(variables.values()),'declarations':declarations,'definitions':defs,'names':CHAR_NAMES,'localization':loc,'assets':assets,'titleIndex':dict(titles)}
    (OUT/'game-data.json').write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    brief={'files':len(files),'nodes':len(nodes),'choices':sum(len(n['options']) for n in nodes),'checks':sum(len(n['checks']) for n in nodes),'conditions':sum(len(n['conditions']) for n in nodes),'variables':len(variables),'characters':dict(collections.Counter(n['group'] for n in nodes)),'portraits':len(assets),'definitions':{k:len(v) for k,v in defs.items()}}
    (OUT/'extraction-summary.json').write_text(json.dumps(brief,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(brief,ensure_ascii=False,indent=2))

if __name__=='__main__': extract()
