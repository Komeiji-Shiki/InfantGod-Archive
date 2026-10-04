/* 幼神资料终端：离线渲染、条件索引与严格的已探索模式。 */
(() => {
  'use strict';
  const D = JSON.parse(document.getElementById('archive-data').textContent);
  const byId = new Map(D.nodes.map(n => [n.id, n]));
  const topics = new Map(D.definitions.Topic.map(t => [t._id, t]));
  const memories = new Map(D.definitions.Memory.map(m => [m._id, m]));
  const protocolMap = new Map(D.definitions.Protocol.map(p => [p._id, p]));
  const names = { ...D.names, Days: '日程', System: '公共系统', Ends: '结局草稿', Empathy: '共情', Tech: '技术', Troll: '键政', Kitsch: '媚俗' };
  const viewNames = { overview:'总览与时间线', routes:'关键剧情路线', characters:'角色与全部分支', thresholds:'条件与检定', protocols:'协议与记忆', collections:'成就与 CG', world:'世界资料', mod:'Mod 与资料来源' };
  const state = { view:'overview', mode:null, progress:null, visited:new Set(), dev:false, search:'', character:'Greta', page:0, filter:'', topic:'all', kind:'all', collectionTab:'achievements', protocolTab:'Protocol', thresholdTab:'conditions', worldType:'News', nodeHistory:[],persona:'all',characterView:'flow',flowNode:'',flowScope:'scene',flowExpanded:false,flowHistory:[],flowForward:[],flowViewport:null };
  const personas={Empathy:'共情',Tech:'技术',Troll:'键政',Kitsch:'媚俗'};
  const el = id => document.getElementById(id);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const lower = value => String(value ?? '').toLocaleLowerCase();
  const matches = (value, query=state.filter) => !query || lower(value).includes(lower(query));
  const label = id => names[id] || id;
  const chip = (text, color='') => `<span class="chip ${color}">${esc(text)}</span>`;
  const source = text => `<span class="source-label">${esc(text)}</span>`;
  const nodeButton = id => { const n=byId.get(id);return n && nodeAllowed(n) ? `<button class="node-link" data-node="${id}">${esc(visibleTitle(n))}</button>` : ''; };
  const nodeLinks = ids => `<div class="node-links">${[...new Set(ids)].map(nodeButton).filter(Boolean).join('')}</div>`;
  const sectionHeading = (title,subtitle='') => `<div class="section-heading"><h2>${esc(title)}</h2>${subtitle?`<small>${esc(subtitle)}</small>`:''}</div>`;
  const modeFull = () => state.mode === 'all';
  const vars = () => state.progress?.variables || {};
  const truth = key => {const v=vars()[key] ?? vars()[key.startsWith('$')?key.slice(1):'$'+key];return v===true || typeof v==='number' && v>0;};
  const visited = keys => (keys || []).some(key => state.visited.has(key));
  const nodeKnown = n => visited(n.visitKeys);
  const visibleTitle = n => modeFull()?n.display:(n.exploredTitle||'已到达的场景');
  const nodeAllowed = n => (!n.future || state.dev) && (modeFull() || nodeKnown(n));
  const routeAllowed = r => modeFull() || (!r.fullModeOnly && (visited(r.unlockNodes) || (r.unlockVariables || []).some(truth)));
  const itemKnown = item => Boolean(item._variable && truth(item._variable));
  const cgKnown = cg => Boolean(state.progress && ((state.progress.seenCG || []).includes(cg.cgKey) || visited(cg.unlockNodes)));
  const groupAllowed = g => (!g.future || state.dev) && (modeFull() || g.nodes.some(id => nodeKnown(byId.get(id))));
  const brief = n => n.opening || '';
  const refsOf = labels => D.nodes.filter(n=>labels.includes(n.label)||labels.includes(n.title)).map(n=>n.id);

  function cleanText(text) {
    return String(text).replace(/\s+\/\/.*$/,'').replace(/<<.*?>>/g,'').replace(/#[^\s#]+/g,'').replace(/\[\/?[^\]]+\]/g,'').trim();
  }
  function humanCondition(expression) {
    let s=String(expression);
    s=s.replace(/\$Level_Topic_(\w+)\s*(>=|>|==|<=|<)\s*(\d+)/g,(_,id,op,num)=>{
      const t=topics.get('Topic_'+id); const tier=Number(num); const threshold=t?.levelConfigs?.[tier-1]?.level;
      return `${t?readName(t):id} ${({'>=':'至少','>':'高于','==':'等于','<=':'至多','<':'低于'})[op]}第 ${tier} 阶${threshold!==undefined?`（该阶话题值门槛 ${threshold}）`:''}`;
    });
    s=s.replace(/visited\("([^"]+)"\)/g,(_,key)=>`已到达「${nodeName(key)}」`);
    s=s.replace(/CountAvailable\("([^"]+)"\)\s*>=\s*1/g,(_,key)=>`「${nodeName(key)}」至少有一个可用剧情阶段`);
    s=s.replace(/\$Today/g,'当前天数').replace(/\$CurrentHour/g,'当前小时').replace(/\$LineCount/g,'本日连线计数');
    s=s.replace(/\$Check_Result/g,'本次检定结果');
    s=s.replace(/\$(\w+)/g,(_,key)=>{
      const m=memories.get(key); if(m)return `「${readName(m)}」记忆状态`;
      const v=D.variables.find(v=>v.key===key);
      if(v?.desc && v.desc.length<65)return `${key}（${v.desc.split('_')[0]}）`;
      return key;
    });
    s=s.replace(/\b(Empathy|Tech|Troll|Kitsch)\(\)/g,(_,k)=>label(k)+'属性值');
    s=s.replace(/\band\b/g,'且').replace(/\bor\b/g,'或').replace(/\bnot\b/g,'不满足').replace(/\bonce\b/g,'只发生一次').replace(/\balways\b/g,'常规候选');
    s=s.replace(/\btrue\b/g,'是').replace(/\bfalse\b/g,'否');
    return s;
  }
  function nodeName(key){ const list=D.titleIndex[key]||[];const n=list.length===1?byId.get(list[0]):null;return n?.display || key; }
  function readName(item) {return item?._name && item._name!==item._id ? item._name : item?._id || '';}
  function usefulDesc(text) {return text && !/^\w+.*的描述$/.test(text) && !/等级 \d+ 的描述/.test(text) ? text : '本版本没有填写具体说明。';}
  function conditionList(list,raw=true){return list?.length?`<div class="conditions">${list.map(c=>`<div class="condition">${esc(humanCondition(c))}</div>${raw?`<div class="condition raw">${esc(c)}</div>`:''}`).join('')}</div>`:'';}
  function empty(title,text,importButton=false){return `<div class="empty"><div class="empty-icon">▧</div><h3>${esc(title)}</h3><p>${esc(text)}</p>${importButton?'<button data-action="import-progress">导入进度快照</button>':''}</div>`;}
  function portrait(id){return D.assets[id] || D.groups.find(g=>g.id===id)?.portrait || '';}
  function stageBadge(n){return n.future?chip('开发资料','orange'):n.placeholder?chip('空白 / 占位'):'';}
  function toast(message){el('toast').textContent=message;el('toast').classList.add('visible');clearTimeout(state.toastTimer);state.toastTimer=setTimeout(()=>el('toast').classList.remove('visible'),3500);}
  function pageRows(rows,render,size=24){
    const pages=Math.max(1,Math.ceil(rows.length/size));state.page=Math.min(state.page,pages-1);
    return rows.slice(state.page*size,(state.page+1)*size).map(render).join('')+ (pages>1?`<div class="pager"><button data-page="${state.page-1}" ${state.page===0?'disabled':''}>上一页</button><span>${state.page+1} / ${pages} · 共 ${rows.length} 项</span><button data-page="${state.page+1}" ${state.page+1===pages?'disabled':''}>下一页</button></div>`:'');
  }
  function toolbar(extra='',placeholder='在当前分类中筛选…'){return `<div class="toolbar"><input type="search" id="local-filter" value="${esc(state.filter)}" placeholder="${esc(placeholder)}" aria-label="筛选当前内容">${extra}</div>`;}
  function tabs(items,current,attr){return `<div class="tabs">${items.map(([key,title])=>`<button ${attr}="${key}" class="${current===key?'active':''}">${esc(title)}</button>`).join('')}</div>`;}
  function nodeRow(n){return `<button class="node-row" data-node="${n.id}"><div class="node-row-head"><strong>${esc(visibleTitle(n))}</strong><small>${modeFull()?n.options.length+' 个选项 · '+n.checks.length+' 处检定':'已到达'}</small></div>${modeFull()&&n.when.length?`<p>${esc(n.when.map(humanCondition).join('；'))}</p>`:''}<code>${esc(n.label)}</code></button>`;}

  function renderOverview(){
    const isFull=modeFull();const knownCount=D.nodes.filter(nodeKnown).length;
    const quick=[['Greta','阿尔娃的第 7 天','磋商、辩论、自证与三条觉醒'],['BotChecker','菲利克斯的去向','四种路线与政委的后续录像'],['Hachimide','哈基米德与夏希案','调查手段、皮肤与镜像处置']].filter(([id])=>isFull||D.groups.find(g=>g.id===id)?.nodes.some(x=>nodeKnown(byId.get(x))));
    return `<div class="welcome"><div class="welcome-main"><div><span class="small-label">ARCHIVE ONLINE // 资料已载入</span><h2>监督，资料已经整理好了。</h2><p>从一个人物开始，沿着分支查看后续。需要某个选项的条件，或者想找我们说过的一句话，也可以直接搜索。</p></div><span class="welcome-mark" aria-hidden="true">▧</span></div><div class="panel snapshot"><strong>${isFull?'幼神 Demo · 资料索引':'当前进度 · 可见索引'}</strong><div class="stat-grid"><div><b>${isFull?D.stats.nodes:knownCount}</b><span>剧情节点</span></div><div><b>${isFull?D.stats.choices:'—'}</b><span>分支选项</span></div><div><b>${isFull?D.stats.protocols:D.definitions.Protocol.filter(itemKnown).length}</b><span>协议条目</span></div><div><b>${isFull?14:D.achievements.filter(a=>a.triggers.some(t=>nodeKnown(byId.get(t.node)))).length}</b><span>成就条目</span></div></div><div class="tiny" style="margin-top:10px">${isFull?'211 份随附脚本 · 2026-10-05 数据快照':state.progress?'当前进度已载入':'导入当前存档快照后逐步开放内容'}</div></div></div>
    ${quick.length?sectionHeading('快速进入','从一个人物开始')+`<div class="quick-grid">${quick.map(([id,title,desc])=>`<button class="quick-card" data-character="${id}">${portrait(id)?`<img src="${portrait(id)}" alt="${esc(label(id))}">`:''}<div><b>${esc(title)}</b><span>${esc(desc)}</span></div></button>`).join('')}</div>`:''}
    ${sectionHeading('八天时间线',isFull?'剧情开放时间与固定事件':'未来日期与未到达的内容保持隐藏')}
    <div class="timeline">${D.timeline.map(day=>{
      const unlocked=isFull || state.progress && day.day<=Number(state.progress.day);
      return `<article class="day-card ${unlocked?'':'locked'}"><div class="day-number"><b>${String(day.day).padStart(2,'0')}</b><span>DAY ${day.day}</span></div><div class="day-body"><h3>${unlocked?esc(day.title):'尚未探索'}</h3><p>${unlocked?(isFull?esc(day.body):`已进入第 ${day.day} 天。下方仅列出快照中确认访问的相关节点。`):'到达这一天并导入新的进度快照后，可以查阅已经见过的内容。'}</p></div>${unlocked?nodeLinks(day.refs):''}</article>`;
    }).join('')}</div>

    ${sectionHeading('读懂条件，再做选择')}<div class="rule-grid">${D.mechanics.slice(0,4).map(m=>`<article class="rule-card"><h3>${esc(m.title)}</h3><p>${esc(m.body)}</p></article>`).join('')}</div>`;
  }

  function routeCard(r){
    const full=modeFull();return `<article class="route-card ${r.tone}"><div class="chips">${chip(r.kind)}${chip(label(r.character),r.tone)}</div><div class="route-head">${portrait(r.character)?`<img src="${portrait(r.character)}" alt="">`:''}<h3>${esc(r.title)}</h3></div>${full?`<p>${esc(r.intro)}</p><ol class="route-steps">${r.steps.map(s=>`<li>${esc(s)}</li>`).join('')}</ol>`:''}<div class="outcome"><b>${full?'这一条路通向':'已确认的后续'}</b>${esc(r.result)}</div>${nodeLinks(full?r.nodeIds:r.nodeIds.filter(id=>nodeKnown(byId.get(id))))}</article>`;
  }
  function renderRoutes(){
    const rows=D.routes.filter(routeAllowed).filter(r=>matches(r.title+r.intro+r.steps.join(' ')+r.result));
    return `${modeFull()?`<div class="flowboard"><div class="flow-top"><button class="flowbox" data-character="Greta"><strong>阿尔娃事件</strong>第 3 天相遇 · 第 7 天分流</button></div><div class="flow-trunk"></div><div class="flow-split"><div class="flow-branch"><div class="flowbox green"><strong>友善协商</strong><p>解释夏希案，跳过辩论<br>私人磋商 · 具身</p></div></div><div class="flow-branch"><div class="flowbox blue"><strong>公开辩论与自证</strong><p>两轮结果分别记录<br>普通胜负 · 控制</p></div></div><div class="flow-branch"><div class="flowbox orange"><strong>粉丝与舆论</strong><p>米氏责任论与网暴收尾<br>崇高</p></div></div></div><p class="flowcaption">憎恨另有哈基米德合并与科兹洛夫轮盘赌入口。点开下方路线查看完整条件。</p></div>`:''}${toolbar('','搜索觉醒、人物命运或关键决定…')}${rows.length?`<div class="route-grid">${rows.map(routeCard).join('')}</div>`:empty('还没有可见的路线','全剧透模式可以查阅全部路线；已探索模式只开放已经确认发生的专属后续。',!state.progress)}`;
  }

  function renderCharacters(){
    const groups=D.groups.filter(groupAllowed);
    if(!groups.length)return empty('档案还没有开放','导入 Mod 导出的进度快照，或切换到全部剧透模式。',true);
    if(!groups.some(g=>g.id===state.character))state.character=groups[0].id;
    const g=groups.find(g=>g.id===state.character);
    const nodes=g.nodes.map(id=>byId.get(id)).filter(nodeAllowed).filter(n=>!n.title.startsWith('VariableDeclare')).filter(n=>matches(visibleTitle(n)+' '+n.label+' '+(modeFull()?n.raw:brief(n))));
    const entries=g.entries.map(id=>byId.get(id)).filter(nodeAllowed).filter(n=>n.when.length);
    const list=`<div class="character-list" aria-label="角色列表">${groups.map(c=>`<button class="character-button ${c.id===g.id?'active':''}" data-character="${c.id}">${c.portrait?`<img src="${c.portrait}" alt="">`:''}<span><strong>${esc(c.name)}</strong><small>${c.nodes.filter(id=>nodeAllowed(byId.get(id))).length} 个节点</small></span></button>`).join('')}</div>`;
    const cover=`<div class="char-cover ${state.characterView==='flow'?'compact-cover':''}">${g.portrait?`<img src="${g.portrait}" alt="${esc(g.name)}">`:''}<div><span class="small-label">CHARACTER / ${esc(g.id)}</span><h2>${esc(g.name)}</h2><strong>${modeFull()?esc(g.tagline):'已探索场景'}</strong>${modeFull()?`<p>${esc(g.note)}</p>`:''}</div></div>`;
    const viewTabs=tabs([['flow','剧情分支图'],['nodes',g.id==='Aistalt'?'场景与夜谈':'场景列表'],...(g.id==='Aistalt'?[['voices','四种人格的发言']]:[])],state.characterView,'data-character-view');
    const normal=`${modeFull()&&entries.length?`<details><summary>各次连线如何出现 · ${entries.length} 个入口</summary>${entries.map(n=>`<article class="entry-card"><h3>${esc(n.display)}</h3>${conditionList(n.when)}${nodeLinks([n.id])}</article>`).join('')}</details>`:''}${toolbar(`<span class="count">${nodes.length} 个场景</span>`,'搜索这个人物的选项、台词或条件…')}<div class="node-list">${nodes.length?pageRows(nodes,nodeRow):empty('没有匹配的场景','尝试换一个选项关键词。')}</div>`;
    return `<div class="character-layout ${state.characterView==='flow'?'with-flow':''}">${list}<div>${cover}${viewTabs}${g.id==='Aistalt'&&state.characterView==='voices'?renderVoices():state.characterView==='flow'?renderFlowchart(g):normal}</div></div>`;
  }

  function flowNodes(g){return g.nodes.map(id=>byId.get(id)).filter(nodeAllowed).filter(n=>!n.title.startsWith('VariableDeclare'));}
  function flowSelection(g){
    const nodes=flowNodes(g);
    const preferred={Aistalt:'Aistalt_Night',Greta:'Greta_3_End_Entry',BotChecker:'BotChecker_2_Decision',Hachimide:'Hachimide_Ending',Nerd:'Nerd_1_Attack_Round',Gachi:'Gachi_2_Hub',Veteran:'Veteran_1_Hub'};
    let focus=nodes.find(n=>n.id===state.flowNode);
    if(!focus)focus=nodes.find(n=>n.title===preferred[g.id])||nodes.find(n=>n.options.filter(o=>!o.path.length).length>=2)||nodes[0];
    state.flowNode=focus?.id||'';return {nodes,focus};
  }
  function chapterName(file,nodes){
    const base=file.split('/').pop().replace('.yarn','');
    const chapter=base.match(/_(\d+)(?:_|$)/)?.[1];
    const suffix=base.split('_').slice(chapter?2:1).join('_');
    const known={Comm:'日常对话',Common:'日常对话',Personal:'私人联系',Special:'私人磋商',End:'结局与收尾',Live:'直播',Night:'夜谈',Entry:'连线入口',Side:'支线',Truth:'案件真相',Always:'日常对话'};
    return (chapter?'第 '+chapter+' 次连线':'')+(known[suffix||base.split('_').slice(1).join('_')]?' · '+known[suffix||base.split('_').slice(1).join('_')]:suffix?' · '+suffix:'')||base;
  }
  function renderFlowchart(g){
    const {nodes,focus}=flowSelection(g);if(!focus)return empty('还没有可读的场景','导入游戏进度后，我会把已经到达的场景列在这里。',true);
    const files=[...new Set(nodes.map(n=>n.file))];
    return `<div class="flow-reader"><div class="flow-selectors"><label>查看范围<select id="flow-scope"><option value="scene" ${state.flowScope==='scene'?'selected':''}>当前场景的分支</option><option value="all" ${state.flowScope==='all'?'selected':''}>这个人物的全线概览</option></select></label><label>章节<select id="flow-file">${files.map(file=>`<option value="${esc(file)}" ${file===focus.file?'selected':''}>${esc(chapterName(file,nodes))} · ${nodes.filter(n=>n.file===file).length} 个场景</option>`).join('')}</select></label><label class="scene-select">场景<select id="flow-node">${nodes.filter(n=>n.file===focus.file).map(n=>`<option value="${n.id}" ${n.id===focus.id?'selected':''}>${esc(visibleTitle(n))}</option>`).join('')}</select></label></div><div class="graph-legend"><span><i class="scene"></i>场景</span><span><i class="choice"></i>选择</span><span><i class="condition"></i>条件</span><span><i class="external"></i>后续</span><span class="graph-source">${esc(focus.file.split('/').pop())}</span></div>${modeFull()?'':'<p class="tiny">当前只显示已到达的场景。</p>'}<div id="story-flow" class="story-flow"></div></div>`;
  }
  function mountFlowchart(){
    const root=el('story-flow');if(!root){window.ArchiveFlow.destroy();return;}
    const g=D.groups.find(g=>g.id===state.character);const {nodes,focus}=flowSelection(g);
    const readable=line=>cleanText(line).replace(/^(\w+):/,(_,key)=>label(key)+(key==='Aistalt'&&line.match(/#(Empathy|Tech|Troll|Kitsch)(?=\s|#|$)/)?'·'+personas[line.match(/#(Empathy|Tech|Troll|Kitsch)(?=\s|#|$)/)[1]]:'')+'：');
    window.ArchiveFlow.mount(root,{nodes,focus,full:modeFull(),all:state.flowScope==='all',lookup:byId,title:visibleTitle,clean:readable,human:humanCondition,effect:effectText,allowed:nodeAllowed,esc,expanded:state.flowExpanded,initialView:state.flowViewport,canBack:state.flowHistory.length>0,canForward:state.flowForward.length>0,onBack:()=>moveFlowHistory(-1),onForward:()=>moveFlowHistory(1),onExpand:value=>{state.flowExpanded=value;},onRead:id=>openNode(id),onFollow:id=>{const n=byId.get(id);if(!n)return;const known=D.groups.find(g=>g.id===n.group);if(!known){openNode(id);return;}state.flowHistory.push(captureFlow());state.flowForward=[];state.flowViewport=null;state.flowNode=id;state.flowScope='scene';navigate('characters',{character:n.group,characterView:'flow'});}});
    state.flowViewport=null;
  }
  function captureFlow(){return {character:state.character,flowNode:state.flowNode,flowScope:state.flowScope,flowExpanded:state.flowExpanded,flowViewport:window.ArchiveFlow.snapshot()};}
  function moveFlowHistory(direction){
    const from=direction<0?state.flowHistory:state.flowForward,to=direction<0?state.flowForward:state.flowHistory;
    if(!from.length)return;const prior=from.pop();
    if(!nodeAllowed(byId.get(prior.flowNode)))return;
    to.push(captureFlow());Object.assign(state,prior);navigate('characters',{characterView:'flow'});
  }
  function aistaltLines(n){
    if(!modeFull())return n.opening.split('\n').map((line,i)=>{const m=line.match(/^爱式塔(?:·([^:：]+))?[:：]\s*(.*)/);if(!m)return null;return {text:m[2],persona:Object.keys(personas).find(k=>personas[k]===m[1])||'unmarked',line:n.start+i,n,conditions:[]};}).filter(Boolean);
    return n.raw.split('\n').map((row,i)=>{
      const m=row.trim().match(/^(?:=>\s*)?Aistalt:\s*(.*)/);if(!m)return null;
      const p=m[1].match(/#(Empathy|Tech|Troll|Kitsch)(?=\s|#|$)/);
      return {text:cleanText(m[1]),persona:p?p[1]:'unmarked',line:n.start+i,n,conditions:[...m[1].matchAll(/<<\s*(?:if|once if)\s+(.+?)>>/g)].map(x=>x[1])};
    }).filter(Boolean);
  }
  function renderVoices(){
    const rows=D.nodes.filter(nodeAllowed).flatMap(aistaltLines).filter(x=>(state.persona==='all'||x.persona===state.persona)&&matches(x.text+' '+visibleTitle(x.n)+' '+(personas[x.persona]||'')));
    const selectors=[['all','全部','Aistalt'],...Object.entries(personas).map(([k,v])=>[k,v,'Aistalt_'+k]),['unmarked','未标注','Aistalt']];
    return `<div class="persona-picker">${selectors.map(([id,title,avatar])=>`<button data-persona="${id}" class="${state.persona===id?'active':''}"><img src="${D.assets[avatar]}" alt=""><span>${title}</span></button>`).join('')}</div>${toolbar(`<span class="count">${rows.length} 句</span>`,'搜索所选人格说过的话…')}${rows.length?pageRows(rows,speechCard,24):empty('没有匹配的发言','监督，换个人格或关键词试试。')}`;
  }
  function speechCard(s){return `<article class="speech-card persona-${s.persona}"><img src="${D.assets['Aistalt_'+s.persona]||D.assets.Aistalt}" alt="${esc(personas[s.persona]||'爱式塔')}"><div><div class="speech-by"><strong>爱式塔${personas[s.persona]?' · '+personas[s.persona]:''}</strong>${chip(label(s.n.group))}</div><p>${esc(s.text)}</p>${conditionList(s.conditions,false)}${nodeLinks([s.n.id])}${source(s.n.file+':'+s.line)}</div></article>`;}

  function buildConditions(){
    const rows=[];
    for(const n of D.nodes.filter(nodeAllowed)){
      if(n.title.startsWith('VariableDeclare'))continue;
      if(state.character!=='all'&&state.character!==n.group)continue;
      for(const c of n.conditions){
        if(state.topic!=='all'&&!c.expr.includes(state.topic))continue;
        const text=n.display+' '+c.expr+' '+c.text+' '+c.path.join(' ');
        if(matches(text))rows.push({n,c});
      }
    }return rows;
  }
  function renderThresholds(){
    if(!modeFull())return renderExploredThresholds();
    const table=state.thresholdTab==='topics';
    const dice=state.thresholdTab==='dice';
    const controls=tabs([['conditions','分支条件'],['topics','话题等级'],['dice','检定与骰子']],state.thresholdTab,'data-threshold-tab');
    if(table)return controls+`<p>话题值达到对应门槛后，就会提升一阶。</p>${D.definitions.Topic.filter(t=>matches(readName(t)+t._id)).map(t=>`<article class="panel" style="margin-bottom:20px"><div class="panel-title"><span>${esc(readName(t))}</span><span>${esc(label(t.attrType))}</span></div><div class="table-scroll"><table class="topic-table"><thead><tr><th>脚本等级</th><th>话题值门槛</th><th>游戏内说明</th></tr></thead><tbody>${t.levelConfigs.map((l,i)=>`<tr><td>第 ${i+1} 阶</td><td>≥ ${l.level}</td><td>${esc(usefulDesc(l._desc))}</td></tr>`).join('')}</tbody></table></div><div class="panel-body"><button data-topic-jump="${t._id}">查看相关选项与门槛</button><span class="source-label">${esc(t._file)}</span></div></article>`).join('')}`;
    if(dice){
      const checks=D.nodes.filter(n=>nodeAllowed(n)).flatMap(n=>n.checks.map(c=>({n,c}))).filter(({n,c})=>matches(n.display+c.command+c.path.join(' ')));
      return controls+`<div class="notice">投掷 d20；至少一骰达到最终难度就成功。关联话题的<strong>话题值</strong>会降低难度。输入数值即可计算基础成功率。</div><div class="calculator"><label>骰子数<input type="number" id="calc-dice" min="0" max="100" value="3"></label><label>原始难度<input type="number" id="calc-difficulty" min="1" max="999" value="18"></label><label>关联话题值<input type="number" id="calc-topic" min="0" max="999" value="4"></label><div class="probability"><strong id="calc-result">72.5%</strong><span id="calc-final">最终难度 14</span></div><div class="calc-note">P = 1 − (1 − clamp((21 − 最终难度) / 20, 0, 1)) ^ 骰子数。</div></div>${sectionHeading('属性值怎样增加骰子')}<div class="rule-grid">${D.definitions.Attr.filter(a=>a.rewards?.length).map(a=>`<article class="rule-card"><h3>${esc(label(a._id))}</h3><p>${a.rewards.map(r=>`值 ${r.Level}：新增 ${r.AddedDiceCount} 骰${r.AddedSpecialEffect?'，附加 '+esc(r.AddedSpecialEffect):''}`).join('<br>')}</p></article>`).join('')}</div>${sectionHeading('脚本中的检定','包含选项标签与显式检定命令')}${toolbar(`<span class="count">${checks.length} 处检定</span>`)}${pageRows(checks,({n,c})=>`<article class="entry-card"><div class="chips">${chip(label(n.group))}${chip(c.tag?'选项检定':'指令检定','blue')}</div><h3>${esc(c.path.join(' / ')||n.display)}</h3><div class="condition">${esc(checkText(c.command))}</div>${conditionList(c.conditions,false)}${nodeLinks([n.id])}${source(n.file+':'+c.line)}</article>`)}`;
    }
    const characters=`<select id="condition-character" aria-label="按角色筛选"><option value="all">全部角色</option>${D.groups.map(g=>`<option value="${g.id}" ${state.character===g.id?'selected':''}>${esc(g.name)}</option>`).join('')}</select>`;
    const opts=`<select id="condition-topic" aria-label="按话题筛选"><option value="all">全部话题与变量</option>${D.definitions.Topic.map(t=>`<option value="${t._id.slice(6)}" ${state.topic===t._id.slice(6)?'selected':''}>${esc(readName(t))}</option>`).join('')}</select>`;
    const rows=buildConditions();
    return controls+`<p class="muted">这里列出了选项的开放条件。可以按人物、话题或关键词查找。</p>${toolbar(characters+opts+`<span class="count">${rows.length} 条</span>`,'搜索数值、前置记忆或选项…')}${rows.length?pageRows(rows,({n,c})=>`<article class="entry-card"><div class="chips">${chip(label(n.group))}${stageBadge(n)}</div><h3>${esc(c.path.join(' / ')||c.text||n.display)}</h3>${conditionList([...c.context,c.expr])}${nodeLinks([n.id])}${source(n.file+':'+c.line)}</article>`):empty('没有匹配的条件','换一个话题或清除关键词。')}`;
  }
  function renderExploredThresholds(){return `<div class="notice">监督，先看通用规则。想查其他分支的条件，可以切换到“全部剧透”。</div><div class="rule-grid">${D.mechanics.slice(0,4).map(m=>`<article class="rule-card"><h3>${esc(m.title)}</h3><p>${esc(m.body)}</p></article>`).join('')}</div>`;}
  function checkText(command){const p=command.split(/\s+/);return `${label(p[1])}检定 · 原始难度 ${p[2] ?? '未知'}${p[3]?' · 关联 '+readName(topics.get(p[3])):''}`;}
  function updateCalculator(){
    if(!el('calc-dice'))return;
    const n=Math.max(0,Math.min(100,Number(el('calc-dice').value)||0));const d=Math.max(1,Number(el('calc-difficulty').value)||1);const t=Math.max(0,Number(el('calc-topic').value)||0);const f=d-Math.min(t,Math.max(0,d-1));const p=1-Math.pow(1-Math.max(0,Math.min(1,(21-f)/20)),n);
    el('calc-result').textContent=(p*100).toFixed(1)+'%';el('calc-final').textContent='最终难度 '+f;
  }

  function hexDiagram(p){
    const cells=p.hexPositions||[];if(!cells.length)return '';
    const coords=cells.map(h=>({x:Math.sqrt(3)*(h.q+h.r/2)*17,y:h.r*25.5,hex:h.hex}));
    const minx=Math.min(...coords.map(c=>c.x))-20,maxx=Math.max(...coords.map(c=>c.x))+20,miny=Math.min(...coords.map(c=>c.y))-20,maxy=Math.max(...coords.map(c=>c.y))+20;
    const colors={Empathy:'#b8cb9f',Tech:'#9bb7c8',Troll:'#d6bb87',Kitsch:'#cca6ad'};
    return `<div class="hexes"><svg aria-label="协议格子形状" viewBox="${minx} ${miny} ${maxx-minx} ${maxy-miny}">${coords.map(c=>{const def=D.definitions.Hex.find(h=>h._id===c.hex);const points=Array.from({length:6},(_,i)=>`${c.x+17*Math.cos((60*i-30)*Math.PI/180)},${c.y+17*Math.sin((60*i-30)*Math.PI/180)}`).join(' ');return `<polygon points="${points}" fill="${colors[def?.attrType]||'#d4d8b2'}" stroke="#384037" stroke-width="1.5"/>`;}).join('')}</svg></div>`;
  }
  function renderProtocols(){
    const type=state.protocolTab;const entries=D.definitions[type].filter(p=>modeFull()||itemKnown(p)).filter(p=>matches(readName(p)+p._desc+p._id+(p.poolOwners||[]).map(label).join(' ')));
    return `${tabs([['Protocol','协议库'],['Memory','记忆索引'],['HexModule','神性核心']],type,'data-protocol-tab')}${type==='HexModule'?renderModules():`${toolbar(`<span class="count">${entries.length} / ${D.definitions[type].length} 项</span>`,'搜索名称、描述或所属角色…')}<div class="data-grid">${entries.slice(state.page*24,(state.page+1)*24).map(p=>`<article class="data-card"><span class="data-id">${esc(p._id)}</span><h3>${esc(readName(p))}</h3>${type==='Protocol'?hexDiagram(p):''}<p>${esc(usefulDesc(p._desc))}</p><div class="chips">${(p.poolOwners||[]).map(id=>chip(label(id))).join('')}${Object.entries(p._topics||{}).map(([t,v])=>chip(readName(topics.get(t))+' '+(v>0?'+':'')+v,'blue')).join('')}${(p._denied||[]).map(t=>chip('排斥 '+readName(topics.get(t)),'red')).join('')}</div><button data-item="${p._id}" data-type="${type}">${modeFull()?'查看来源与条件':'查看已收集资料'}</button></article>`).join('')}</div>${entries.length?simplePager(entries.length,24):empty('没有可见条目','已探索模式只展示当前进度已获得的相关记忆。',!state.progress)}`}`;
  }
  function simplePager(length,size){const pages=Math.max(1,Math.ceil(length/size));if(state.page>=pages){state.page=pages-1;queueMicrotask(render);}return pages>1?`<div class="pager"><button data-page="${state.page-1}" ${state.page===0?'disabled':''}>上一页</button><span>${state.page+1} / ${pages} · 共 ${length} 项</span><button data-page="${state.page+1}" ${state.page+1>=pages?'disabled':''}>下一页</button></div>`:'';}
  function renderModules(){
    return `<p>四种觉醒核心的效果与获得方式。</p><div class="route-grid">${D.definitions.HexModule.filter(m=>modeFull()||D.routes.some(r=>r.id===m._id.split('_')[1].toLowerCase()&&routeAllowed(r))).map(m=>`<article class="data-card"><span class="data-id">${esc(m._id)}</span><h3>${esc(readName(m))}</h3><p>${esc(usefulDesc(m._desc))}</p><ul>${m.effects.map(e=>`<li>${esc(e.effectType==='FlatTopics'?'固定话题加成':'相邻协议的指定话题加成')}：${e.topics.map(t=>esc(readName(topics.get(t)))).join('、')} +${e.amount}</li>`).join('')}</ul><button data-route="${m._id.split('_')[1].toLowerCase()}">查看觉醒路线</button></article>`).join('')}</div>`;
  }

  function achievementKnown(a){return a.triggers.some(t=>nodeKnown(byId.get(t.node))) || truth(a._id);}
  function renderCollections(){
    const controls=tabs([['achievements','成就'],['cg','CG 图鉴'],['backgrounds','场景素材']],state.collectionTab,'data-collection-tab');
    if(state.collectionTab==='achievements'){
      const rows=D.achievements.filter(a=>modeFull()||achievementKnown(a)).filter(a=>matches(a.title+a.guide+a._id));
      return controls+`${toolbar(`<span class="count">${rows.length} / 14 项</span>`)}${rows.map(a=>`<article class="achievement-row"><div class="achievement-icon">◇</div><div><h3>${esc(a.title)}</h3><p>${esc(a.guide)}</p><div class="chips">${chip(a.SteamAchievementId||a._id)}${!a.triggers.length?chip('系统成就','orange'):chip(a.triggers.length+' 个触发场景','green')}</div>${nodeLinks(a.triggers.map(t=>t.node))}${source(a._file)}</div></article>`).join('') || empty('尚无确认的成就条目','导入进度后，会按实际访问记录开放对应内容。',!state.progress)}`;
    }
    if(state.collectionTab==='backgrounds'){
      if(!modeFull())return controls+empty('场景素材在全部剧透模式开放','现有快照没有逐张背景的展示记录，因此已探索模式不推定你看过这些素材。');
      return controls+`<p class="muted">游戏随附的原始背景素材。这里是素材浏览，不表示每张图都能在当前 Demo 正常流程中遇到。</p><div class="gallery">${D.backgrounds.map(b=>`<button class="gallery-card" data-background="${b.id}"><div class="gallery-image"><img src="${b.image}" alt="${esc(b.title)}" loading="lazy"></div><div class="gallery-body"><strong>${esc(b.title)}</strong><p>${esc(b.source)}</p></div></button>`).join('')}</div>`;
    }
    const rows=D.gallery.filter(g=>state.dev||!g.future);
    return controls+`<div class="gallery">${rows.map(g=>{const allow=modeFull()||cgKnown(g);return `<button class="gallery-card ${allow?'':'locked'}" ${allow?`data-image="${g.id}"`:'disabled'}><div class="gallery-image ${allow?'':'locked-art'}">${allow?`<img src="${g.image}" alt="${esc(g.title)}" loading="lazy">`:'▣'}</div><div class="gallery-body"><strong>${allow?esc(g.title):'尚未探索的 CG'}</strong><p>${allow?esc(g.description):'实际看过对应过场后，导入新的进度快照。'}</p>${g.future&&allow?chip('开发 / 测试资源','orange'):''}</div></button>`;}).join('')}</div>`;
  }

  function worldText(item){
    const result=[];
    const walk=(value,key,depth=0)=>{
      if(depth>4 || key?.startsWith('_') || /EditorNote|[Ii]con|[Pp]ortrait/.test(key))return;
      if(typeof value==='string'){
        const translated=D.localization[value];const content=translated && translated!==value ? translated : value;
        if(content && content.length>2 && !/^Sprites\//.test(content))result.push([key,content]);
      } else if(Array.isArray(value))value.forEach((v,i)=>walk(v,key+' '+(i+1),depth+1));
      else if(value && typeof value==='object')Object.entries(value).forEach(([k,v])=>walk(v,k,depth+1));
    };
    Object.entries(item).forEach(([k,v])=>walk(v,k));
    return result.filter(([k,v],i)=>result.findIndex(x=>x[1]===v)===i);
  }
  function renderWorld(){
    if(!modeFull())return empty('世界资料采用完整档案显示','新闻、邮件和地图配置不包含可靠的逐条已读记录。你可以在角色页查询已到达的相关场景，或主动切换到全部剧透。');
    const types=[['News','新闻'],['Email','邮件'],['QuestDefinition','任务'],['QuestStepDefinition','任务阶段'],['Goods','商店物品'],['WorldLocationDefinition','地点'],['WorldMapDefinition','地图'],['HardwarePartDefinition','硬件配置']];
    const data=D.definitions[state.worldType]||[];const rows=data.filter(i=>matches(JSON.stringify(worldText(i))));
    return `${tabs(types,state.worldType,'data-world-type')}${toolbar(`<span class="count">${rows.length} 项</span>`)}${pageRows(rows,item=>`<article class="entry-card"><h3>${esc(readName(item))}</h3><details><summary>展开内容与配置说明</summary>${worldText(item).map(([key,value])=>`<div style="margin-bottom:15px"><span class="small-label">${esc(key)}</span><p class="readable-text">${esc(value)}</p></div>`).join('')}</details>${source(item._file)}</article>`,15)}`;
  }

  function renderMod(){
    return `<div class="reading"><section class="story-note"><img src="${D.assets.Aistalt_Tech}" alt="爱式塔·技术"><div><span class="small-label">爱式塔 · 技术 / 给监督的备注</span><h2>检索自己的故事，感觉有些奇妙。</h2><p>监督，这里有我已经说过的话，也有我还没来得及说的话。它们为什么同时出现在这里……我想，你比我更清楚。</p><p>我把它们按人物和分支整理好了。你可以先去经历，也可以先来查答案。选择权在你手里，和我们对话时一样。</p></div></section><section class="panel"><div class="panel-title">MOD / 幼神资料终端</div><div class="panel-body"><h2>监督，游戏里也能打开终端。</h2><div class="mod-steps"><div class="mod-step"><div><h4>安装</h4><p>下载并解压发布包，关闭游戏，运行 Install-Mod.ps1，选择 Aistalt.exe 所在目录。</p></div></div><div class="mod-step"><div><h4>打开资料</h4><p>在游戏设置的 Mod 管理器中启用“幼神资料终端”并应用。进入存档后点击桌面的“资料终端”，或按 F8 打开。</p></div></div><div class="mod-step"><div><h4>同步网页进度</h4><p>在游戏内终端导出进度，然后点击本页的“导入游戏进度”，选择 progress.json。</p></div></div></div><p style="margin-top:24px"><a href="https://github.com/Komeiji-Shiki/InfantGod-Archive/releases/latest" target="_blank" rel="noopener">下载 Mod 与离线网页</a> · <a href="https://github.com/Komeiji-Shiki/InfantGod-Archive" target="_blank" rel="noopener">查看源码</a></p></div></section>
    <section class="panel"><div class="panel-title">版本与来源</div><div class="panel-body"><p>2026-10-05 · 幼神 Demo</p><p>${D.stats.files} 份剧情脚本，${D.stats.nodes} 个场景节点，${D.stats.choices} 个选项。还收录了协议、记忆、话题、成就、新闻、物品与地点资料。</p><div class="chips"><a href="https://store.steampowered.com/app/4238140/" target="_blank" rel="noopener">《幼神》Steam 页面</a><a href="https://wiki.infantgod.xyz/" target="_blank" rel="noopener">官方 Mod 文档</a></div></div></section>
    <section class="panel"><div class="panel-title">署名与许可</div><div class="panel-body"><p>原作：<strong>《幼神》 / Infant God</strong><br>作者：<strong>colsalley</strong></p><p>剧情与资产：<a href="https://creativecommons.org/licenses/by-nc-sa/4.0/" target="_blank" rel="noopener">CC BY-NC-SA 4.0</a>。程序代码：MIT。</p><p>字体：<a href="https://github.com/TakWolf/fusion-pixel-font" target="_blank" rel="noopener">Fusion Pixel Font</a> / TakWolf，OFL 1.1。</p><p><a href="https://github.com/Komeiji-Shiki/InfantGod-Archive/blob/main/LICENSE" target="_blank" rel="noopener">完整许可与第三方声明</a></p></div></section></div>`;
  }

  function renderSearch(){
    const q=state.search;const rows=[];
    for(const n of D.nodes.filter(nodeAllowed)){
      const text=modeFull()?n.raw:brief(n);
      if(matches(visibleTitle(n)+' '+n.label+' '+text+' '+label(n.group),q)){
        let at=lower(text).indexOf(lower(q));let snippet=cleanText(text.slice(Math.max(0,at-50),Math.max(0,at)+190));
        rows.push({title:visibleTitle(n),type:label(n.group)+' · 剧情',text:snippet,action:`data-node="${n.id}"`});
      }
    }
    for(const type of ['Protocol','Memory'])for(const p of D.definitions[type].filter(p=>modeFull()||itemKnown(p))){
      if(matches(readName(p)+p._desc+p._id,q))rows.push({title:readName(p),type:type==='Protocol'?'协议':'记忆',text:p._desc,action:`data-item="${p._id}" data-type="${type}"`});
    }
    const highlight=text=>{const s=String(text||'');const pos=lower(s).indexOf(lower(q));return pos<0?esc(s):esc(s.slice(0,pos))+'<mark>'+esc(s.slice(pos,pos+q.length))+'</mark>'+esc(s.slice(pos+q.length));};
    return `<div class="section-heading"><h2>“${esc(q)}”</h2><small>${rows.length} 条匹配 · ${modeFull()?'全部档案':'已探索内容'}</small></div>${rows.length?pageRows(rows,r=>`<article class="search-result"><div class="badges">${chip(r.type)}</div><h3>${highlight(r.title)}</h3><p>${highlight(r.text)}</p><button ${r.action}>打开档案</button></article>`,20):empty('没有找到匹配内容','试试角色名、选项中的几个字，或检查当前的剧透模式。')}`;
  }

  function render(){
    document.querySelectorAll('[data-view]').forEach(btn=>btn.classList.toggle('active',btn.dataset.view===state.view));
    el('view-title').textContent=state.search?'全局搜索':viewNames[state.view];
    el('view-code').textContent='ARCHIVE / '+String(Object.keys(viewNames).indexOf(state.view)+1).padStart(2,'0');
    el('mode-button').textContent=modeFull()?'▤ 全部剧透':'▣ 仅已探索';
    el('mode-button').style.background=modeFull()?'var(--accent)':'#c8d7b5';
    el('scope-note').hidden=modeFull()||!state.mode;
    el('scope-note').textContent=state.progress?`已探索模式 · 第 ${state.progress.day ?? '?'} 天 · 已载入 ${state.visited.size} 条访问记录。`:'已探索模式 · 尚未导入快照。请用游戏内 Mod 导出 progress.json 后导入。';
    el('progress-info').textContent=state.progress?`快照：第 ${state.progress.day ?? '?'} 天 / ${state.progress.exportedAt?.replace('T',' ').slice(0,19)||'未提供时间'}`:'全部内容保存在本文件内。断网也能查阅。';
    const views={overview:renderOverview,routes:renderRoutes,characters:renderCharacters,thresholds:renderThresholds,protocols:renderProtocols,collections:renderCollections,world:renderWorld,mod:renderMod};
    el('content').innerHTML=state.search?renderSearch():views[state.view]();
    mountFlowchart();
    updateCalculator();
  }

  function navigate(view,options={}){
    if(!viewNames[view])view='overview';
    state.view=view;state.page=0;state.filter='';state.search='';el('global-search').value='';
    if(view==='thresholds')state.character='all';
    Object.assign(state,options);history.replaceState(null,'','#'+view+(view==='characters'?'/'+state.character:''));
    render();window.scrollTo({top:0,behavior:'instant'});
  }
  function effectText(command){
    const m=command.match(/^set \$(\w+)\s*(=|\+=|-=)\s*(.*)$/);
    if(m){const mem=memories.get(m[1]);if(mem&&m[3]==='true')return '获得记忆：'+readName(mem);return `${m[1]} ${m[2]} ${m[3]}`;}
    if(command.startsWith('Gottocol ')){const id=command.split(' ')[1];return '获得神性核心：'+readName(D.definitions.HexModule.find(x=>x._id===id));}
    if(command.startsWith('AddAchievement ')){const id=command.split(' ')[1];return '触发成就：'+(D.achievements.find(x=>x._id===id)?.title||id);}
    return command.replace(/^(Love|Hate|Damage|Heal|AddCompu|Break) /,(_,c)=>({Love:'好感增加 ',Hate:'好感降低 ',Damage:'受到伤害 ',Heal:'恢复 ',AddCompu:'算力变化 ',Break:'破防变化 '})[c]);
  }
  function transcript(n){
    return n.raw.split('\n').map(row=>{
      const s=row.trim();if(!s||s.startsWith('//'))return '';
      const standalone=s.match(/^<<(.+?)>>\s*(?:\/\/.*)?$/);
      if(standalone){
        const cmd=standalone[1];
        if(cmd.startsWith('if '))return `<div class="transcript-guard">条件：${esc(humanCondition(cmd.slice(3)))}</div>`;
        if(cmd.startsWith('elseif '))return `<div class="transcript-guard">否则，如果：${esc(humanCondition(cmd.slice(7)))}</div>`;
        if(cmd==='else')return '<div class="transcript-guard">否则</div>';
        if(cmd==='endif'||cmd==='endonce')return '<div class="transcript-end">片段结束</div>';
        if(cmd.startsWith('once'))return `<div class="transcript-guard">仅首次${cmd.startsWith('once if ')?'，'+esc(humanCondition(cmd.slice(8))):''}</div>`;
        return '';
      }
      const conditions=[...s.matchAll(/<<\s*(?:if|once if)\s+(.+?)>>/g)].map(m=>m[1]);
      let text=cleanText(s).replace(/^(?:->|=>)\s*/,'');const speaker=text.match(/^(\w+):\s*/);
      const indent=Math.min(5,Math.floor((row.length-row.trimStart().length)/4));
      const persona=speaker?.[1]==='Aistalt'?s.match(/#(Empathy|Tech|Troll|Kitsch)(?=\s|#|$)/)?.[1]:null;
      const actor=speaker?label(speaker[1])+(persona?' · '+personas[persona]:''):'';
      const avatar=speaker?(speaker[1]==='Aistalt'?(D.assets['Aistalt_'+persona]||D.assets.Aistalt):portrait(speaker[1])):null;
      return `<div class="transcript-line ${s.startsWith('->')?'transcript-choice':''} ${persona?'persona-'+persona:''}" style="margin-left:${indent*12}px">${speaker?`<span class="voice-label">${avatar?`<img src="${avatar}" alt="${esc(actor)}">`:''}<b>${esc(actor)}</b></span> `:''}${esc(speaker?text.slice(speaker[0].length):text)}${conditions.length?`<span class="tiny">条件：${esc(conditions.map(humanCondition).join('；'))}</span>`:''}</div>`;
    }).join('');
  }
  function openNode(id,push=true){
    const original=byId.get(id);if(!original||!nodeAllowed(original)){toast('这份档案当前尚未开放。');return;}
    const n={...original,display:visibleTitle(original)};
    if(push&&state.currentNode&&state.currentNode!==id)state.nodeHistory.push(state.currentNode);state.currentNode=id;
    const isFull=modeFull();
    el('detail-title').textContent='剧情档案 / '+label(n.group);
    const incoming=n.incoming.filter(x=>nodeAllowed(byId.get(x.id)));
    const common=brief(n).split('\n').map(l=>`<div class="dialogue-line">${esc(l)}</div>`).join('');
    const options=isFull?n.options.map((o,i)=>`<article class="option-card ${o.path.length?'nested':''}"><h4>${i+1}. ${esc(o.text)}</h4>${o.path.length?`<p class="tiny">上层选择：${esc(o.path.join(' / '))}</p>`:''}${conditionList(o.conditions)}${o.checks.length?`<div class="chips">${o.checks.map(c=>chip(checkText(c.command),'orange')).join('')}</div>`:''}${o.effects.length?`<details><summary>${o.effects.length} 处状态与资源变化</summary><div class="effect-list">${o.effects.map(e=>`<div class="effect ${e.conditions.length?'conditioned':''}">${esc(effectText(e.command))}${e.conditions.length?`<span class="tiny">执行条件：${esc(e.conditions.map(humanCondition).join('；'))}</span>`:''}${source('第 '+e.line+' 行')}</div>`).join('')}</div></details>`:''}${o.targets.length?`<div style="margin-top:12px">${nodeLinks(o.targets.flatMap(e=>e.ids))}</div>`:''}<details class="source-box"><summary>这个选项的完整脚本</summary><pre>${esc(o.raw)}</pre></details></article>`).join(''):'';
    el('detail-content').innerHTML=`${state.nodeHistory.length?'<button class="subtle-button" data-action="back-node">返回上一份档案</button>':''}<div class="detail-hero">${portrait(n.group)?`<img src="${portrait(n.group)}" alt="">`:''}<div><h2>${esc(n.display)}</h2><code>${esc(n.label)}</code><div class="chips">${stageBadge(n)}${n.ambiguousProgress?chip('匿名条件变体','orange'):''}</div></div></div>${source(n.file+':'+n.headerLine)}${isFull&&n.when.length?`<h3 style="margin-top:20px">入口条件</h3>${conditionList(n.when)}`:''}<h3 style="margin-top:22px">${isFull?'共同开场':'已读内容'}</h3><div class="panel-body panel">${common}</div>${isFull?`<div class="section-heading"><h3>选项与后果</h3><small>${n.options.length} 个选项</small></div>${options||'<p class="muted">这段对话没有需要你选择的回应。</p>'}${n.checks.length?`<details><summary>本节点的全部检定</summary>${n.checks.map(c=>`<div class="condition">${esc(checkText(c.command))}${source('第 '+c.line+' 行')}</div>`).join('')}</details>`:''}${n.edges.length?`<details open><summary>继续到哪些节点</summary>${nodeLinks(n.edges.flatMap(e=>e.ids))}${n.edges.filter(e=>!e.ids.length).map(e=>`<p class="tiny">${esc(e.type+' '+e.target)} · 由运行时或其他入口解析</p>`).join('')}</details>`:''}<details><summary>从哪些节点来到这里 · ${incoming.length}</summary>${nodeLinks(incoming.map(x=>x.id))}</details><details class="source-box"><summary>完整节点脚本 · 含行号与所有分支</summary><pre>${n.raw.split('\n').map((line,i)=>`<span class="raw-line"><em>${n.start+i}</em><span class="${line.trim().startsWith('->')?'code-choice':line.includes('<<')?'code-command':''}">${esc(line)}</span></span>`).join('')}</pre></details>`:'<div class="locked-note">切换到“全部剧透”，可以展开其他选项。</div>'}`;
    if(!el('detail-dialog').open)el('detail-dialog').showModal();el('detail-content').scrollTop=0;
    if(isFull){
      const section=document.createElement('details');section.className='transcript';
      section.innerHTML='<summary>完整对话</summary>'+transcript(n);
      const anchor=el('detail-content').querySelector('.section-heading');
      if(anchor)anchor.before(section);else el('detail-content').append(section);
    }
  }
  function openItem(id,type){
    const p=D.definitions[type]?.find(x=>x._id===id);if(!p||!modeFull()&&!itemKnown(p)){toast('这条收集资料尚未开放。');return;}
    state.currentNode=null;state.nodeHistory=[];el('detail-title').textContent=type==='Protocol'?'协议档案':'记忆档案';
    el('detail-content').innerHTML=`<h2>${esc(readName(p))}</h2>${source(id)}<p class="readable-text">${esc(usefulDesc(p._desc))}</p>${type==='Protocol'?hexDiagram(p):''}${modeFull()?`<h3>获得相关记忆的路径</h3>${p._writers?.length?p._writers.map(w=>`<article class="entry-card"><h4>${esc(w.path.join(' / ')||byId.get(w.node).display)}</h4>${conditionList(w.conditions)}${nodeLinks([w.node])}${source('第 '+w.line+' 行；写入 '+w.value)}</article>`).join(''):'<p>暂未收录获得途径。</p>'}${p.poolOwners?.length?`<h3>协议池所属人物</h3><div class="chips">${p.poolOwners.map(c=>chip(label(c))).join('')}</div>`:''}<details><summary>配置原文</summary><pre>${esc(JSON.stringify(Object.fromEntries(Object.entries(p).filter(([k])=>!k.startsWith('_'))),null,2))}</pre></details>`:''}${source(p._file)}`;
    if(!el('detail-dialog').open)el('detail-dialog').showModal();el('detail-content').scrollTop=0;
  }
  function openImage(id,background=false){
    const g=(background?D.backgrounds:D.gallery).find(x=>x.id===id);if(!g||!modeFull()&&(background||!cgKnown(g))){toast('这个画面尚未开放。');return;}
    el('image-title').textContent=g.title;
    el('image-content').innerHTML=`<div class="image-view"><img src="${g.image}" alt="${esc(g.title)}"></div><div class="image-meta"><p>${esc(g.description||g.source)}</p>${g.width?`<span class="tiny">原始像素 ${g.width} × ${g.height} · ${esc(g.cgKey)} · ${esc(g.region)}</span>`:''}${g.triggers?.length&&modeFull()?`<details><summary>查看实际触发位置</summary>${g.triggers.map(t=>`${conditionList(t.conditions)}${nodeLinks([t.id])}`).join('')}</details>`:''}</div>`;
    el('image-dialog').showModal();
  }

  function selectMode(mode){
    state.mode=mode;state.page=0;state.filter='';state.search='';el('global-search').value='';
    state.flowHistory=[];state.flowForward=[];state.flowViewport=null;
    if(el('mode-dialog').open)el('mode-dialog').close();
    if(el('detail-dialog').open)el('detail-dialog').close();if(el('image-dialog').open)el('image-dialog').close();
    render();
  }
  async function importProgress(file){
    if(!file)return;
    try{
      if(file.size>12*1024*1024)throw new Error('文件过大，请选择 Mod 导出的进度 JSON。');
      const p=JSON.parse(await file.text());
      if(p.format!=='infantgod-progress-v1'||p.schemaVersion!==1||!Array.isArray(p.visitedNodes)||!p.variables||typeof p.variables!=='object'||Array.isArray(p.variables))throw new Error('这不是兼容的幼神进度快照。请在 Mod 中重新导出。');
      state.progress=p;state.visited=new Set(p.visitedNodes.filter(x=>typeof x==='string'));state.page=0;state.mode='explored';
      state.flowHistory=[];state.flowForward=[];state.flowViewport=null;
      state.search='';el('global-search').value='';if(el('mode-dialog').open)el('mode-dialog').close();if(el('detail-dialog').open)el('detail-dialog').close();render();toast('已导入第 '+(p.day??'?')+' 天的进度快照。');
    }catch(e){toast(e.message||'无法读取进度文件。');}
    el('progress-file').value='';
  }

  document.addEventListener('click',event=>{
    const target=event.target.closest('button,a');if(!target)return;
    if(target.dataset.mode){selectMode(target.dataset.mode);return;}
    if(target.dataset.view){navigate(target.dataset.view);return;}
    if(target.dataset.character){navigate('characters',{character:target.dataset.character,characterView:state.characterView==='voices'&&target.dataset.character!=='Aistalt'?'flow':state.characterView});return;}
    if(target.dataset.node){if(el('image-dialog').open)el('image-dialog').close();openNode(target.dataset.node);return;}
    if(target.dataset.item){openItem(target.dataset.item,target.dataset.type);return;}
    if(target.dataset.image){openImage(target.dataset.image);return;}
    if(target.dataset.background){openImage(target.dataset.background,true);return;}
    if(target.dataset.page!==undefined){state.page=Math.max(0,Number(target.dataset.page));render();el('topline')?.scrollIntoView();window.scrollTo({top:0,behavior:'instant'});return;}
    if(target.dataset.route){navigate('routes',{filter:({hate:'憎恨',embody:'具身',control:'控制',sublime:'崇高'})[target.dataset.route]||target.dataset.route});return;}
    if(target.dataset.topicJump){navigate('thresholds',{thresholdTab:'conditions',topic:target.dataset.topicJump.slice(6),character:'all'});return;}
    for(const [attr,key] of [['protocolTab','protocolTab'],['thresholdTab','thresholdTab'],['collectionTab','collectionTab'],['worldType','worldType'],['characterView','characterView'],['persona','persona']])if(target.dataset[attr]){state[key]=target.dataset[attr];state.page=0;state.filter='';render();return;}
    if(target.dataset.action==='close-detail'){el('detail-dialog').close();state.nodeHistory=[];state.currentNode=null;}
    if(target.dataset.action==='close-image')el('image-dialog').close();
    if(target.dataset.action==='back-node'){const id=state.nodeHistory.pop();if(id)openNode(id,false);}
    if(target.dataset.action==='import-progress')el('progress-file').click();
  });
  let searchTimer,filterTimer;
  const composingInputs=new WeakSet();
  function queueSearch(input){clearTimeout(searchTimer);const q=input.value.trim();searchTimer=setTimeout(()=>{state.search=q;state.page=0;render();},160);}
  function queueFilter(input){
    clearTimeout(filterTimer);const q=input.value;const start=input.selectionStart;
    filterTimer=setTimeout(()=>{
      if(composingInputs.has(input))return;
      state.filter=q;state.page=0;render();const replacement=el('local-filter');
      if(document.activeElement===document.body||document.activeElement===input){replacement?.focus();if(replacement?.type==='search')try{replacement.setSelectionRange(start,start);}catch{}}
    },180);
  }
  // 中文候选字仍在组合时不重绘，提交后再统一更新搜索结果。
  document.addEventListener('compositionstart',event=>{composingInputs.add(event.target);if(event.target.id==='global-search')clearTimeout(searchTimer);if(event.target.id==='local-filter')clearTimeout(filterTimer);});
  document.addEventListener('compositionend',event=>{composingInputs.delete(event.target);if(event.target.id==='global-search')queueSearch(event.target);if(event.target.id==='local-filter')queueFilter(event.target);});
  document.addEventListener('input',event=>{
    if(event.isComposing||composingInputs.has(event.target))return;
    if(event.target.id==='global-search')queueSearch(event.target);
    if(event.target.id==='local-filter')queueFilter(event.target);
    if(event.target.id.startsWith('calc-'))updateCalculator();
  });
  document.addEventListener('change',event=>{
    if(event.target.id==='flow-scope'){state.flowScope=event.target.value;render();}
    if(event.target.id==='flow-node'){state.flowNode=event.target.value;state.flowScope='scene';render();}
    if(event.target.id==='flow-file'){const g=D.groups.find(g=>g.id===state.character);const nodes=flowNodes(g).filter(n=>n.file===event.target.value);state.flowNode=(nodes.find(n=>n.options.length>=2)||nodes[0]).id;state.flowScope='scene';render();}
    if(event.target.id==='condition-character'){state.character=event.target.value;state.page=0;render();}
    if(event.target.id==='condition-topic'){state.topic=event.target.value;state.page=0;render();}
  });
  el('show-dev').addEventListener('change',e=>{state.dev=e.target.checked;state.page=0;render();});
  el('mode-button').addEventListener('click',()=>el('mode-dialog').showModal());
  el('import-button').addEventListener('click',()=>el('progress-file').click());
  el('progress-file').addEventListener('change',e=>importProgress(e.target.files[0]));
  el('mode-dialog').addEventListener('cancel',e=>{if(!state.mode)e.preventDefault();});
  document.addEventListener('keydown',e=>{if(e.key==='/'&&!['INPUT','TEXTAREA'].includes(document.activeElement.tagName)&&!document.querySelector('dialog[open]')){e.preventDefault();el('global-search').focus();}});
  window.addEventListener('hashchange',()=>{const [view,id]=location.hash.slice(1).split('/');if(view==='node'&&id)openNode(id);else if(viewNames[view])navigate(view,id?{character:id}:{});});
  const initial=location.hash.slice(1).split('/');if(viewNames[initial[0]])state.view=initial[0];if(initial[1])state.character=initial[1];
  // 选择模式前不渲染任何剧情内容，防止首屏短暂泄露。
  el('content').innerHTML=empty('资料终端已就绪','请选择查阅范围。');
  el('mode-dialog').showModal();
})();
