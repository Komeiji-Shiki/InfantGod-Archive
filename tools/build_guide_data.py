"""把剧情索引、人工导读和授权资产合并为网页与插件共用的资料。"""
from pathlib import Path
import json,re,base64,io,collections,html,argparse
from PIL import Image
from curation import CHARACTER_NOTES,TIMELINE,ROUTES,NODE_LABELS,ACHIEVEMENTS,MECHANICS
from scene_titles import assign_explored_titles

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--input-dir',type=Path,default=Path(__file__).parent)
parser.add_argument('--plugin-dir',type=Path,help='生成原生插件资源的目录')
args=parser.parse_args()
WORK=args.input_dir
DATA=json.loads((WORK/'game-data.json').read_text(encoding='utf8'))
ROOT=Path(DATA['sourceRoot'])/'SODefines'
NODES=DATA['nodes']
BY_ID={n['id']:n for n in NODES}
SOURCE_NAMES=DATA['names']
PERSONAS={'Empathy':'共情','Tech':'技术','Troll':'键政','Kitsch':'媚俗'}
LOCAL=DATA['localization']
OUT=WORK.parent/'outputs'
OUT.mkdir(exist_ok=True)
PREVIEWS=(args.plugin_dir or OUT/'幼神资料终端-Mod'/'BepInEx/plugins/InfantGodArchive')/'previews'
PREVIEWS.mkdir(parents=True,exist_ok=True)

def refs(labels):
    return [n['id'] for n in NODES if n['label'] in labels or n['title'] in labels]

def clean(s):
    s=re.sub(r'\s+//.*$','',s)
    s=re.sub(r'<<.*?>>','',s)
    s=re.sub(r'#[^\s#]+','',s)
    s=re.sub(r'\[/?[^\]]+\]','',s)
    return re.sub(r'^(?:->|=>)\s*','',s.strip())

def speaker_name(raw,speaker):
    persona=re.search(r'#(Empathy|Tech|Troll|Kitsch)(?=\s|#|$)',raw)
    if speaker=='Aistalt' and persona:return '爱式塔·'+PERSONAS[persona[1]]
    return SOURCE_NAMES.get(speaker,speaker)

def plain(n):
    # 源脚本完整保存在 raw；正文略去控制指令与编辑注释。
    out=[]
    for row in n['raw'].splitlines():
        s=row.strip()
        if not s or s.startswith(('//','<<')):continue
        s=clean(s)
        if not s:continue
        s=re.sub(r'^(\w+):',lambda m:speaker_name(row,m[1])+':',s)
        out.append(s)
    return '\n'.join(out)

def readable_script(n):
    result=[]
    for row in n['raw'].splitlines():
        text=row.strip()
        if not text or text.startswith('//'):continue
        control=re.fullmatch(r'<<(.+?)>>\s*(?://.*)?',text)
        if control:
            command=control[1]
            if command.startswith('if '):result.append('【条件：'+command[3:]+'】')
            elif command.startswith('elseif '):result.append('【否则，如果：'+command[7:]+'】')
            elif command=='else':result.append('【否则】')
            elif command=='endif':result.append('【条件结束】')
            elif command.startswith('once'):result.append('【仅首次'+('，条件：'+command[8:] if command.startswith('once if ') else '')+'】')
            elif command=='endonce':result.append('【首次片段结束】')
            elif command.startswith(('jump ','detour ')):result.append('【继续到：'+command.split(None,1)[1]+'】')
            continue
        content=re.sub(r'^(\w+):',lambda m:speaker_name(row,m[1])+':',clean(text))
        if text.startswith('->'):content='选项：'+content
        conditions=re.findall(r'<<\s*(?:if|once if)\s+(.+?)>>',text)
        if conditions:content+=' 【条件：'+'；'.join(conditions)+'】'
        result.append(content)
    return '\n'.join(result)

def safe_opening(n):
    # 只显示任何分支之前、无条件的共同开场，避免从看过节点推定看过全部分支。
    out=[]
    for row in n['raw'].splitlines():
        s=row.strip()
        if not s or s.startswith('//'):continue
        if s.startswith(('->','=>')) or re.search(r'<<(?:if|once|detour|jump)\b',s):break
        if s.startswith('<<'):continue
        if '<<if' in s or '#Check' in s:break
        s=re.sub(r'^(\w+):',lambda m:speaker_name(row,m[1])+':',clean(s))
        if s:out.append(s)
        if len(out)>=8:break
    return '\n'.join(out) or '此场景已经到达。为保留其他分支的探索体验，此模式不展开完整选项与后果。'

def localized(key,fallback=''):
    value=LOCAL.get(key,'')
    return value if value and not (value==key or value.endswith('的描述') and re.search('[A-Za-z]',value)) else fallback

# 这三个配置的名称仍是占位文本，采用对应神性核心的正式中文名称。
for topic in DATA['definitions']['Topic']:
    if topic['_id'] in {'Topic_Control','Topic_Embody','Topic_Sublime'}:
        topic['_name']={'Topic_Control':'控制','Topic_Embody':'具身','Topic_Sublime':'崇高'}[topic['_id']]

incoming=collections.defaultdict(list)
for n in NODES:
    for e in n['edges']:
        for i in e['ids']:
            incoming[i].append({'id':n['id'],'line':e['line'],'path':e['path'],'conditions':e['conditions'],'type':e['type']})
for n in NODES:
    candidate=next((o['text'] for o in n['options'] if len(o['text'])>3),'')
    n['display']=NODE_LABELS.get(n['label'],NODE_LABELS.get(n['title'],''))
    if not n['display']:
        if n['title'].startswith('Entry_') and n['subtitle'] in ['one','two','three']:
            n['display']=SOURCE_NAMES.get(n['group'],n['group'])+'：'+{'one':'首次连线','two':'第二次连线','three':'第三次连线'}[n['subtitle']]
        elif n['title'].startswith('Live_Entry_'):n['display']=SOURCE_NAMES.get(n['group'],n['group'])+'：直播匹配入口'
        elif n['title'].startswith('Night_Entry_'):n['display']=SOURCE_NAMES.get(n['group'],n['group'])+'：夜间联系'
    if not n['display']:
        text=plain(n).splitlines()
        first=re.sub(r'^\w+[:：]\s*','',text[0]) if text else n['title']
        n['display']=(candidate if candidate and len(candidate)>6 else first)[:48] if first else n['title']
    n['opening']=safe_opening(n)
    n['incoming']=incoming[n['id']]
    n['visitKeys']=([n['title']+'.'+n['subtitle']] if n['when'] and n['subtitle'] else [n['title']] if not n['when'] else [])
    # 节点组本身的访问记录不足以证明访问了其某个匿名变体。
    n['ambiguousProgress']=bool(n['when'] and not n['subtitle'])

assign_explored_titles(NODES,SOURCE_NAMES)

for route in ROUTES:
    route['nodeIds']=refs(route['refs'])
    route['unlockNodes']=route.pop('unlock',[])
    route['unlockVariables']=['$'+x for x in route.pop('variables',[])]
    if route['id']=='felix-arid':route['unlockVariables']=[]

groups=[]
for group,(tagline,note) in CHARACTER_NOTES.items():
    nodes=[n for n in NODES if n['group']==group]
    char=next((c for c in DATA['definitions']['Character'] if c['_id']==group),{})
    entries=[n for n in nodes if n['title'].startswith(('Entry_','Live_Entry','Live_Special','Night_Entry'))]
    groups.append({'id':group,'name':SOURCE_NAMES.get(group,group),'tagline':tagline,'note':note,'description':char.get('_desc',''),'nodes':[n['id'] for n in nodes],'entries':[n['id'] for n in entries],'portrait':DATA['assets'].get(group,''),'future':group=='Seer'})

writes=collections.defaultdict(list)
for n in NODES:
    for effect in n['effects']:
        m=re.match(r'set \$(\w+)\s*(?:=|\+=)\s*(.*)',effect['command'])
        if m:
            writes[m[1]].append({'node':n['id'],'line':effect['line'],'value':m[2],'path':effect['path'],'conditions':effect['conditions']})

memory_map={x['_id']:x for x in DATA['definitions']['Memory']}
hex_map={x['_id']:x for x in DATA['definitions']['Hex']}
for mem in memory_map.values():
    variable=mem.get('MainVariable',{}).get('VariableName',mem['_id'])
    mem['_writers']=writes.get(variable,[])
    mem['_variable']=variable
for p in DATA['definitions']['Protocol']:
    mem=memory_map.get(p.get('linkedMemory'),{})
    p['_memoryName']=mem.get('_name','')
    p['_name']=mem.get('_name') or p['_name']
    p['_desc']=mem.get('_desc') or p['_desc']
    p['_writers']=mem.get('_writers',[])
    p['_variable']=mem.get('_variable','')
    p['_attrs']=dict(collections.Counter(hex_map.get(h.get('hex'),{}).get('attrType','其他') for h in p.get('hexPositions',[])))
    topics=collections.Counter(); denied=[]
    for h in p.get('hexPositions',[]):
        for t in hex_map.get(h.get('hex'),{}).get('topicInfluences',[]):
            topics[t['topic']]+=t.get('tendencyInfluence',0)
            if t.get('denyTopic'):denied.append(t['topic'])
    p['_topics']=dict(topics); p['_denied']=denied

achievements=[]
for a in DATA['definitions']['AchievementDefinition']:
    title,desc=ACHIEVEMENTS.get(a['_id'],(a['_name'],a['_desc']))
    triggers=[{'node':n['id'],'line':e['line'],'path':e['path'],'conditions':e['conditions']} for n in NODES for e in n['effects'] if e['command']=='AddAchievement '+a['_id']]
    achievements.append({**a,'title':title,'guide':desc,'triggers':triggers})

# CG 是 Spine 图集内的原始区域，提取区域时保留原始像素，不生成或重画内容。
cg_info={
 'CG_Self':('Self','与阿尔娃的合照','第 7 天在无威胁的情况下跳过辩论后出现。',['Entry_Greta_3'],['Greta_3_Special_Hub'],False),
 'CG_Protest':('Protest','街头抗议','绿色先锋相关情节中的抗议画面。',[],[],False),
 'CG_Shoot':('Shoot','洪水行动','道路遇袭过场，图集包含四个镜头帧。',['CG_TheAssault'],['CG_TheAssault'],False),
 'CG_Mesuga':('Mesuga-CG','一之濑瑠夏','随包包含的 CG；已知显式调用位于拍摄／测试脚本。',[],[],True),
}
gallery=[]
for key,(stem,title,desc,source,unlocks,dev) in cg_info.items():
    atlas=(ROOT/'Spine'/f'{stem}.atlas.txt').read_text(encoding='utf8')
    im=Image.open(ROOT/'Spine'/f'{stem}.png').convert('RGBA')
    regions=[]; current=None
    for ln in atlas.splitlines()[1:]:
        t=ln.strip()
        if t and ':' not in t:
            current={'name':t}; regions.append(current)
        elif current and ':' in t:
            k,v=t.split(':',1); current[k]=v
    for index,region in enumerate(regions):
        if 'bounds' not in region:continue
        x,y,w,h=map(int,region['bounds'].split(','))
        rot=region.get('rotate','false')
        degrees=90 if rot=='true' else 0 if rot=='false' else int(rot)
        frame=im.crop((x,y,x+(h if degrees%180 else w),y+(w if degrees%180 else h)))
        if degrees:frame=frame.rotate(-degrees,expand=True)
        buf=io.BytesIO();frame.save(buf,format='PNG');payload=buf.getvalue()
        path=PREVIEWS/f'{key}_{index+1}.png';path.write_bytes(payload)
        trigger=[{'id':n['id'],'line':e['line'],'conditions':e['conditions']} for n in NODES for e in n['effects'] if e['command']=='AddCG '+key]
        gallery.append({'id':key+'_'+str(index+1),'cgKey':key,'title':title+(f' · 镜头 {index+1}' if len(regions)>1 else ''),'description':desc,'image':'data:image/png;base64,'+base64.b64encode(payload).decode(),'width':frame.width,'height':frame.height,'region':region['name'],'refs':refs(source),'triggers':trigger,'unlockNodes':unlocks,'future':dev,'preview':'previews/'+path.name})

backgrounds=[]
for p in sorted((ROOT/'Sprites/Resources/Art/BG').glob('*.png')):
    backgrounds.append({'id':p.stem,'title':p.stem.removeprefix('BG_'),'image':'data:image/png;base64,'+base64.b64encode(p.read_bytes()).decode(),'source':p.relative_to(ROOT).as_posix()})

timeline=[]
for day,title,body,related in TIMELINE:
    ids=refs(related)
    ids=[i for i in ids if BY_ID[i]['group']!='Days' or BY_ID[i]['file']==f'Main/Days/Day{day}.yarn']
    ids=[i for i in ids if BY_ID[i]['title'] not in ['Live_Hub','Welcome_Node','Initial_Node'] or BY_ID[i]['file']==f'Main/Days/Day{day}.yarn']
    timeline.append({'day':day,'title':title,'body':body,'refs':ids})

DATA.update({'groups':groups,'routes':ROUTES,'timeline':timeline,'mechanics':MECHANICS,'achievements':achievements,'gallery':gallery,'backgrounds':backgrounds})
DATA.pop('sourceRoot',None);DATA.pop('declarations',None)
DATA['stats']={'files':len(DATA['files']),'nodes':len(NODES),'choices':sum(len(n['options']) for n in NODES),'conditions':sum(len(n['conditions']) for n in NODES),'checks':sum(len(n['checks']) for n in NODES),'protocols':len(DATA['definitions']['Protocol']),'memories':len(memory_map),'future':sum(n['future'] for n in NODES)}
DATA['licenses']={'work':'幼神 / Infant God','author':'colsalley','license':'CC BY-NC-SA 4.0','url':'https://creativecommons.org/licenses/by-nc-sa/4.0/','game':'https://store.steampowered.com/app/4238140/','wiki':'https://wiki.infantgod.xyz/','font':'Fusion Pixel Font / TakWolf · OFL 1.1'}
(WORK/'guide-data.json').write_text(json.dumps(DATA,ensure_ascii=False,separators=(',',':')),encoding='utf8')

# 原生插件的内容更紧凑，但每个条目仍具有精确的进度门控。
catalog={'schemaVersion':1,'title':'幼神资料终端','version':'1.0.0','sections':[{'id':k,'title':v} for k,v in [('routes','剧情导读'),('characters','角色场景'),('voices','人格发言'),('protocols','协议收集'),('memories','记忆索引'),('achievements','成就条件'),('mechanics','机制说明')]],'entries':[],'cg':[]}
portrait_dir=PREVIEWS.parent/'portraits';portrait_dir.mkdir(exist_ok=True)
catalog['portraits']={}
for key,datauri in DATA['assets'].items():
    path=portrait_dir/(key+'.png');path.write_bytes(base64.b64decode(datauri.split(',',1)[1]));catalog['portraits'][key]='portraits/'+path.name
for route in ROUTES:
    catalog['entries'].append({'id':'route-'+route['id'],'section':'routes','title':route['title'],'characters':[route['character']],'summary':route['intro'],'body':'\n\n'.join([route['intro']]+[f'{i+1}. {s}' for i,s in enumerate(route['steps'])]+[route['result']]),'exploredBody':route['result'],'source':'；'.join(route['refs']),'nodes':route['unlockNodes'],'unlockVariables':route['unlockVariables'],'fullModeOnly':bool(route.get('fullModeOnly'))})
for n in NODES:
    if n['future'] or n['placeholder'] or n['title'].startswith('VariableDeclare'):continue
    readable=readable_script(n)
    if len(readable)<10:continue
    conditions='\n'.join(n['when'])
    options='\n'.join('· '+o['text']+(' 【'+ '; '.join(o['conditions'])+'】' if o['conditions'] else '') for o in n['options'])
    effects='\n'.join(e['command']+(' 【'+ '; '.join(e['conditions'])+'】' if e['conditions'] else '') for e in n['effects'])
    body=(('入口条件\n'+conditions+'\n\n') if conditions else '')+readable
    if effects:body+='\n\n剧情状态变化（依各分支条件执行）\n'+effects
    catalog['entries'].append({'id':n['id'],'section':'characters','title':SOURCE_NAMES.get(n['group'],n['group'])+' / '+n['display'],'exploredTitle':n['exploredTitle'],'characters':[n['group']],'portrait':catalog['portraits'].get(n['group'],''),'summary':n['label'],'body':body,'exploredBody':n['opening'],'source':n['file']+':'+str(n['headerLine']),'nodes':n['visitKeys'],'fullModeOnly':n['ambiguousProgress']})
    for persona,persona_name in PERSONAS.items():
        voice_lines=[]
        for row in n['raw'].splitlines():
            if re.match(r'^\s*(?:=>\s*)?Aistalt:',row) and re.search('#'+persona+r'(?=\s|#|$)',row):
                guards=re.findall(r'<<\s*(?:if|once if)\s+(.+?)>>',row)
                line=clean(row).removeprefix('Aistalt:').strip()
                voice_lines.append(line+(' 【本句条件：'+'；'.join(guards)+'】' if guards else ''))
        if voice_lines:
            explored_lines=[line for line in n['opening'].splitlines() if line.startswith('爱式塔·'+persona_name+':')]
            catalog['entries'].append({'id':'voice-'+n['id']+'-'+persona,'section':'voices','title':persona_name+'人格 / '+n['display'],'exploredTitle':persona_name+'人格 / '+n['exploredTitle'],'characters':['Aistalt',n['group']],'portrait':catalog['portraits']['Aistalt_'+persona],'summary':n['label'],'body':'爱式塔·'+persona_name+'在此场景中的发言。部分台词分属不同条件，完整上下文请参照对应剧情场景。\n\n'+'\n\n'.join(voice_lines),'exploredBody':'\n\n'.join(explored_lines),'source':n['file']+':'+str(n['headerLine']),'nodes':n['visitKeys'],'fullModeOnly':n['ambiguousProgress'] or not bool(explored_lines)})
for typ,section in [('Protocol','protocols'),('Memory','memories')]:
    for item in DATA['definitions'][typ]:
        variable=item.get('_variable',''); sources=item.get('_writers',[])
        body=item.get('_desc','')+'\n\n'
        if sources:body+='获得相关记忆的脚本位置\n'+'\n'.join(BY_ID[x['node']]['display']+' / '+' → '.join(x['path'])+ ('\n条件：'+'；'.join(x['conditions']) if x['conditions'] else '') for x in sources)
        else:body+='当前导出剧情中未找到直接写入位置；可能由物品、系统流程或尚未开放的内容提供。'
        catalog['entries'].append({'id':item['_id'],'section':section,'title':item['_name'],'characters':item.get('poolOwners',[]),'summary':item['_id'],'body':body,'exploredBody':item.get('_desc','') or '已收集到此条目。','source':item['_file'],'nodes':[],'unlockVariables':['$'+variable] if variable else [],'fullModeOnly':not bool(variable)})
for a in achievements:
    nodes=[BY_ID[t['node']]['label'] for t in a['triggers']]
    catalog['entries'].append({'id':a['_id'],'section':'achievements','title':a['title'],'characters':[],'summary':a['_id'],'body':a['guide']+'\n\n触发节点：'+'、'.join(nodes),'exploredBody':'已到达成就触发场景。\n'+a['guide'],'source':a['_file'],'nodes':nodes,'fullModeOnly':not bool(nodes)})
for i,m in enumerate(MECHANICS):
    catalog['entries'].append({'id':'mechanic-'+str(i),'section':'mechanics','title':m['title'],'characters':[],'summary':'规则说明','body':m['body'],'exploredBody':m['body'],'source':'本机脚本、配置与运行时方法','nodes':[],'alwaysVisible':True})
for key,(stem,title,desc,source,unlocks,dev) in cg_info.items():
    first=next((g for g in gallery if g['cgKey']==key),{})
    catalog['cg'].append({'id':key,'title':title,'description':desc,'unlockNodes':unlocks,'unlockVariables':[],'preview':first.get('preview',''),'developmentOnly':dev})
(WORK/'mod-catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'catalogEntries':len(catalog['entries']),'cgFrames':len(gallery),'backgrounds':len(backgrounds),'guideBytes':(WORK/'guide-data.json').stat().st_size},ensure_ascii=False))
