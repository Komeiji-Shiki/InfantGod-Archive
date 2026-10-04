/* 剧情流程图：按 Yarn 的条件、选项和跳转建立连接，布局与阅读状态独立。 */
window.ArchiveFlow = (() => {
  'use strict';
  const NS='http://www.w3.org/2000/svg';
  const labels={entry:'场景',dialogue:'对话',effect:'状态变化',choice:'你的选择',condition:'条件判断',group:'选择回应',external:'继续阅读',dynamic:'动态入口',end:'片段结束',call:'插入对话'};
  const colors={entry:'#fb8b35',dialogue:'#cbd0b0',effect:'#89a898',choice:'#fb8b35',condition:'#8cb8c7',group:'#c5ba85',external:'#9fafcb',dynamic:'#a1acb3',end:'#aeb39a',call:'#baa2c7'};
  let active=null;
  const stripped=s=>s.replace(/\s+\/\/.*$/,'').trim();
  const command=s=>stripped(s).match(/^<<\s*(.*?)\s*>>$/)?.[1]||'';
  const isOpen=s=>/^(if |once(?: |$))/.test(command(s));
  const isClose=s=>/^(endif|endonce)$/.test(command(s));

  function parse(raw,startLine){
    const rows=raw.split('\n').map((text,i)=>({text,trim:stripped(text),indent:text.length-text.trimStart().length,line:startLine+i})).filter(r=>r.trim&&!r.trim.startsWith('//'));
    function block(begin,end){
      const list=[];let i=begin;
      while(i<end){
        const r=rows[i],cmd=command(r.trim);
        if(isOpen(r.trim)){
          const once=cmd.startsWith('once');let depth=1,j=i+1,b=i+1;
          let test=once?(cmd.startsWith('once if ')?'仅首次，且 '+cmd.slice(8):'仅首次'):cmd.slice(3);
          const branches=[];let hasElse=false;
          for(;j<end;j++){
            const c=command(rows[j].trim);
            if(isOpen(rows[j].trim))depth++;
            else if(isClose(rows[j].trim)){if(--depth===0)break;}
            else if(depth===1&&(c==='else'||c.startsWith('elseif '))){
              branches.push({test,body:block(b,j)});b=j+1;
              test=c==='else'?'否则':c.slice(7);hasElse=hasElse||c==='else';
            }
          }
          branches.push({test,body:block(b,j)});
          list.push({kind:'condition',branches,hasElse,line:r.line});i=Math.min(j+1,end);continue;
        }
        if(/^(->|=>)/.test(r.trim)){
          const arrow=r.trim.slice(0,2),indent=r.indent,alternatives=[];
          while(i<end&&rows[i].indent===indent&&rows[i].trim.startsWith(arrow)){
            const option=rows[i];let j=i+1;
            while(j<end&&rows[j].indent>indent)j++;
            alternatives.push({text:option.trim.slice(2).trim(),body:block(i+1,j),line:option.line});i=j;
          }
          list.push({kind:'group',alternatives,arrow,line:r.line});continue;
        }
        if(/^(jump |detour |stop$|return$)/.test(cmd)){
          list.push({kind:cmd.split(' ')[0],target:cmd.slice(cmd.indexOf(' ')+1),line:r.line});i++;continue;
        }
        const textRows=[];let line=r.line;
        while(i<end){
          const t=rows[i],c=command(t.trim);
          if(isOpen(t.trim)||isClose(t.trim)||/^(else|elseif )/.test(c)||/^(->|=>)/.test(t.trim)||/^(jump |detour |stop$|return$)/.test(c))break;
          textRows.push(t.trim);i++;
        }
        if(textRows.length)list.push({kind:'text',rows:textRows,line});else i++;
      }
      return list;
    }
    return block(0,rows.length);
  }

  function makeGraph(options){
    const {nodes,focus,full,all,lookup,title,clean,human,effect}=options;
    const vertices=new Map(),edges=[];let serial=0;
    const add=(id,kind,heading,body,source,line,extra={})=>{vertices.set(id,{id,kind,heading,body,source,line,...extra});return id;};
    const link=(from,to,label='',kind='normal')=>{if(from&&to)edges.push({from,to,label,kind});};
    const newId=source=>source.id+'-flow-'+(++serial);
    function destination(source,target){
      const candidates=source.edges.filter(e=>e.target===target).flatMap(e=>e.ids).filter((id,i,a)=>a.indexOf(id)===i).map(id=>lookup.get(id)).filter(n=>n&&options.allowed(n));
      if(!candidates.length){const id='unresolved-'+target;if(!vertices.has(id))add(id,'dynamic',target,'按当前剧情状态选择后续。',source,null,{unresolved:true});return [id];}
      return candidates.map(n=>{
        if(all&&nodes.some(x=>x.id===n.id))return n.id;
        const id='goto-'+n.id;
        if(!vertices.has(id))add(id,'external',title(n),n.when.length?human(n.when.join('；')):n.label,n,n.headerLine,{navigate:n.id});
        return id;
      });
    }
    function compile(source,statements,next){
      let head=next;
      for(let i=statements.length-1;i>=0;i--){
        const s=statements[i],id=newId(source);
        if(s.kind==='jump'){
          const dest=destination(source,s.target);
          if(dest.length===1)head=dest[0];
          else {add(id,'condition','选择可用的后续','目标节点的入口条件决定去向。',source,s.line);dest.forEach(d=>link(id,d));head=id;}
        }else if(s.kind==='detour'){
          add(id,'call','插入一段对话',s.target,source,s.line);
          destination(source,s.target).forEach(d=>link(id,d,'进入','call'));link(id,head,'返回后继续','return');head=id;
        }else if(s.kind==='stop'||s.kind==='return'){
          head=add(id,'end',s.kind==='return'?'返回调用处':'结束当前对话','',source,s.line);
        }else if(s.kind==='condition'){
          add(id,'condition','检查条件',s.branches.map((b,k)=>(k&&b.test!=='否则'?'否则，':'')+human(b.test)).join('\n'),source,s.line);
          s.branches.forEach((b,k)=>link(id,compile(source,b.body,head),b.test==='否则'?'否则':s.branches.length===1?'满足':`条件 ${k+1}`));
          if(!s.hasElse)link(id,head,'不满足');head=id;
        }else if(s.kind==='group'){
          add(id,'group',s.arrow==='->'?'选择回应':'选择可用发言',s.arrow==='->'?'选择一个选项继续。':'游戏从满足条件的发言中选择。',source,s.line);
          for(const alt of s.alternatives){
            const branch=newId(source),guards=[...alt.text.matchAll(/<<\s*(?:if|once if)\s+(.+?)>>/g)].map(m=>human(m[1]));
            add(branch,s.arrow==='->'?'choice':'dialogue',clean(alt.text),guards.join('；'),source,alt.line);
            link(id,branch);link(branch,compile(source,alt.body,head));
          }
          head=id;
        }else{
          const spoken=s.rows.filter(row=>!command(row)).map(row=>{
            const guards=[...row.matchAll(/<<\s*(?:if|once if)\s+(.+?)>>/g)].map(m=>human(m[1]));
            return clean(row)+(guards.length?'〔'+guards.join('；')+'〕':'');
          }).filter(Boolean);
          const commands=s.rows.filter(row=>command(row)).map(row=>effect(command(row))).filter(Boolean);
          const body=[...spoken,...commands.map(x=>'状态：'+x)].join('\n');
          if(!body)continue;
          add(id,spoken.length?'dialogue':'effect',spoken.length?'对话':'状态变化',body,source,s.line);
          link(id,head);head=id;
        }
      }
      return head;
    }
    if(all){
      for(const n of nodes)add(n.id,'entry',title(n),full?(n.when.length?human(n.when.join('；')):n.opening):n.opening,n,n.headerLine,{navigate:n.id});
      for(const n of nodes)for(const e of n.edges){
        const targets=e.ids.filter(id=>vertices.has(id));
        for(const target of targets)link(n.id,target,full?(e.path.slice(-1)[0]||e.conditions.map(human).join('；')||(e.type==='detour'?'插入对话':'')):'',e.type==='detour'?'call':'normal');
      }
    }else if(focus){
      const n=focus;add(n.id,'entry',title(n),full?n.when.map(human).join('；'):n.opening,n,n.headerLine);
      if(full){
        const terminal=add(n.id+'-end','end','本片段结束','被其他场景调用时，会返回调用处继续。',n,null);
        const head=compile(n,parse(n.raw,n.start),terminal);link(n.id,head);
        if(!edges.some(e=>e.to===terminal))vertices.delete(terminal);
      }
    }
    return {vertices,edges};
  }

  function lines(text,width,max=Infinity){
    const output=[];
    for(const paragraph of String(text||'').split('\n')){
      let line='',units=0;
      for(const ch of paragraph){const size=ch.charCodeAt(0)>255?1:0.56;if(units+size>width){output.push(line);line='';units=0;}line+=ch;units+=size;}
      if(line)output.push(line);
    }
    return output.length>max?[...output.slice(0,max-1),output[max-1].slice(0,-1)+'…']:output;
  }
  const svgEl=(tag,attrs={})=>{const e=document.createElementNS(NS,tag);Object.entries(attrs).forEach(([k,v])=>e.setAttribute(k,String(v)));return e;};
  const addText=(parent,text,x,y,klass)=>{const t=svgEl('text',{x,y,class:klass});t.textContent=text;parent.append(t);return t;};

  function mount(container,options){
    if(active)active.destroy();
    if(!container)return;
    const graph=makeGraph(options),{vertices,edges}=graph;
    const layout=new dagre.graphlib.Graph({multigraph:true});
    layout.setGraph({rankdir:'TB',ranksep:82,nodesep:38,edgesep:16,marginx:50,marginy:50,ranker:'tight-tree'});layout.setDefaultEdgeLabel(()=>({}));
    for(const n of vertices.values()){
      n.titleLines=lines(n.heading,16,3);n.bodyLines=lines(n.body,20,n.kind==='dialogue'?5:3);
      n.width=286;n.height=46+n.titleLines.length*24+(n.bodyLines.length?n.bodyLines.length*20+14:0)+22;
      layout.setNode(n.id,{width:n.width,height:n.height});
    }
    edges.forEach((e,i)=>layout.setEdge(e.from,e.to,{width:e.label?Math.min(180,lines(e.label,15,1)[0]?.length*12+16):0,height:e.label?23:0},String(i)));
    dagre.layout(layout);
    const width=layout.graph().width||800,height=layout.graph().height||600;
    container.innerHTML=`<div class="graph-controls"><button data-graph-action="back" aria-label="返回上一场景" title="返回上一场景，恢复阅读位置" ${options.canBack?'':'disabled'}>← 返回</button><button data-graph-action="forward" aria-label="前进到下一场景" title="前进到下一场景" ${options.canForward?'':'disabled'}>→</button><button data-graph-action="zoom-in" aria-label="放大分支图">＋</button><button data-graph-action="zoom-out" aria-label="缩小分支图">−</button><span class="graph-zoom">100%</span><button data-graph-action="start">起点</button><button data-graph-action="fit">全图</button><button data-graph-action="fullscreen">展开画布</button></div><div class="graph-stage" tabindex="0" aria-label="剧情分支画布，拖动平移，滚轮缩放"><svg class="graph-svg" role="img" aria-label="剧情节点、选项与条件的连接图"></svg><svg class="graph-minimap" aria-label="流程图缩略导航"></svg><div class="graph-hint">拖动平移 · 滚轮缩放 · 点击节点阅读</div></div><div class="graph-detail" hidden></div>`;
    const stage=container.querySelector('.graph-stage'),svg=container.querySelector('.graph-svg'),map=container.querySelector('.graph-minimap'),detail=container.querySelector('.graph-detail');
    const defs=svgEl('defs'),arrow=svgEl('marker',{id:'flow-arrow',markerWidth:8,markerHeight:8,refX:7,refY:4,orient:'auto',markerUnits:'strokeWidth'});
    arrow.append(svgEl('path',{d:'M0,0 L8,4 L0,8 Z',fill:'#d2d5b6'}));defs.append(arrow);svg.append(defs);
    const world=svgEl('g',{class:'graph-world'}),edgeGroup=svgEl('g'),nodeGroup=svgEl('g');world.append(edgeGroup,nodeGroup);svg.append(world);
    const nodeElements=new Map();
    edges.forEach((e,i)=>{
      const info=layout.edge({v:e.from,w:e.to,name:String(i)});if(!info)return;
      const path=svgEl('path',{d:info.points.map((p,j)=>(j?'L':'M')+p.x+','+p.y).join(' '),class:'graph-edge '+e.kind,'marker-end':'url(#flow-arrow)'});edgeGroup.append(path);e.element=path;
      if(e.label){const p=info.x!==undefined?{x:info.x,y:info.y}:info.points[Math.floor(info.points.length/2)];const label=lines(e.label,15,1)[0]||'';const w=Math.min(196,label.length*12+18);edgeGroup.append(svgEl('rect',{x:p.x-w/2,y:p.y-12,width:w,height:24,fill:'#343938'}));const text=addText(edgeGroup,label,p.x,p.y+5,'graph-edge-label');text.setAttribute('text-anchor','middle');}
    });
    for(const n of vertices.values()){
      Object.assign(n,layout.node(n.id));const x=n.x-n.width/2,y=n.y-n.height/2;
      const node=svgEl('g',{transform:`translate(${x},${y})`,class:'graph-node '+n.kind,tabindex:0,role:'button','aria-label':labels[n.kind]+'：'+n.heading,'data-graph-node':n.id});
      node.append(svgEl('rect',{width:n.width,height:n.height,fill:'#ece9c8',stroke:colors[n.kind],'stroke-width':2}));
      node.append(svgEl('rect',{width:n.width,height:29,fill:colors[n.kind]}));
      addText(node,labels[n.kind]||n.kind,12,20,'graph-node-kind');
      if(n.line)addText(node,'L'+n.line,n.width-65,20,'graph-node-line');
      n.titleLines.forEach((line,i)=>addText(node,line,14,55+i*24,'graph-node-title'));
      const offset=64+n.titleLines.length*24;
      n.bodyLines.forEach((line,i)=>addText(node,line,14,offset+i*20,'graph-node-body'));
      const titleEl=svgEl('title');titleEl.textContent=n.heading+'\n'+n.body;node.append(titleEl);nodeGroup.append(node);nodeElements.set(n.id,node);
    }
    map.setAttribute('viewBox',`0 0 ${width} ${height}`);
    for(const n of vertices.values())map.append(svgEl('rect',{x:n.x-n.width/2,y:n.y-n.height/2,width:n.width,height:n.height,fill:colors[n.kind]}));
    const mapView=svgEl('rect',{fill:'#fb8b3528',stroke:'#fb8b35','stroke-width':Math.max(width,height)/150});map.append(mapView);
    let scale=.82,tx=0,ty=30,drag=null,moved=false;
    const apply=()=>{world.setAttribute('transform',`translate(${tx},${ty}) scale(${scale})`);container.querySelector('.graph-zoom').textContent=Math.round(scale*100)+'%';mapView.setAttribute('x',-tx/scale);mapView.setAttribute('y',-ty/scale);mapView.setAttribute('width',stage.clientWidth/scale);mapView.setAttribute('height',stage.clientHeight/scale);};
    const center=(id,zoom=scale)=>{const n=vertices.get(id);if(!n)return;scale=zoom;tx=stage.clientWidth/2-n.x*scale;ty=Math.max(36,stage.clientHeight*.2)- (n.y-n.height/2)*scale;apply();};
    const first=options.focus?.id&&vertices.has(options.focus.id)?options.focus.id:vertices.keys().next().value;
    const fit=()=>{scale=Math.min(1,(stage.clientWidth-64)/width,(stage.clientHeight-64)/height);tx=(stage.clientWidth-width*scale)/2;ty=(stage.clientHeight-height*scale)/2;apply();};
    const zoom=(factor,x=stage.clientWidth/2,y=stage.clientHeight/2)=>{const next=Math.max(.06,Math.min(2.2,scale*factor));tx=x-(x-tx)*next/scale;ty=y-(y-ty)*next/scale;scale=next;apply();};
    const select=id=>{
      const n=vertices.get(id);if(!n)return;
      for(const [key,element] of nodeElements)element.classList.toggle('selected',key===id);
      for(const e of edges)e.element?.classList.toggle('highlight',e.from===id||e.to===id);
      detail.hidden=false;
      detail.innerHTML=`<div class="graph-detail-head"><span>${options.esc(labels[n.kind]||'资料')}</span><button aria-label="收起节点详情" data-graph-action="close-detail">×</button></div><h3>${options.esc(n.heading)}</h3><p>${options.esc(n.body)}</p><div class="graph-detail-actions">${n.navigate?'<button data-graph-follow="'+n.navigate+'">'+(options.all?'展开这个场景':'沿这条分支继续')+'</button>':''}<button data-graph-read="${n.source.id}">查看完整对话</button></div><small>${options.esc(n.source.file)}${n.line?':'+n.line:''}</small>`;
    };
    const activate=id=>{const n=vertices.get(id);if(n?.navigate)options.onFollow(n.navigate);else select(id);};
    stage.addEventListener('wheel',event=>{event.preventDefault();const r=stage.getBoundingClientRect();zoom(Math.exp(-event.deltaY*.0013),event.clientX-r.left,event.clientY-r.top);},{passive:false});
    stage.addEventListener('pointerdown',event=>{if(event.button!==0||event.target.closest('.graph-minimap'))return;drag={x:event.clientX,y:event.clientY,tx,ty,node:event.target.closest('[data-graph-node]')?.dataset.graphNode};moved=false;stage.setPointerCapture(event.pointerId);stage.classList.add('dragging');});
    stage.addEventListener('pointermove',event=>{if(!drag)return;const dx=event.clientX-drag.x,dy=event.clientY-drag.y;if(Math.abs(dx)+Math.abs(dy)>4)moved=true;if(moved){tx=drag.tx+dx;ty=drag.ty+dy;apply();}});
    stage.addEventListener('pointerup',event=>{if(!drag)return;const id=drag.node;drag=null;stage.classList.remove('dragging');stage.releasePointerCapture(event.pointerId);if(!moved&&id)activate(id);});
    stage.addEventListener('pointercancel',()=>{drag=null;stage.classList.remove('dragging');});
    stage.addEventListener('keydown',event=>{const id=event.target.closest('[data-graph-node]')?.dataset.graphNode;if((event.key==='Enter'||event.key===' ')&&id){event.preventDefault();activate(id);}else if(event.key==='+'||event.key==='='){event.preventDefault();zoom(1.2);}else if(event.key==='-'){event.preventDefault();zoom(1/1.2);}else if(event.key==='Home'){event.preventDefault();center(first,.82);}});
    map.addEventListener('click',event=>{const point=map.createSVGPoint();point.x=event.clientX;point.y=event.clientY;const p=point.matrixTransform(map.getScreenCTM().inverse());tx=stage.clientWidth/2-p.x*scale;ty=stage.clientHeight/2-p.y*scale;apply();});
    container.addEventListener('click',event=>{
      const button=event.target.closest('button');if(!button)return;
      if(button.dataset.graphFollow){options.onFollow(button.dataset.graphFollow);return;}
      if(button.dataset.graphRead){options.onRead(button.dataset.graphRead);return;}
      const action=button.dataset.graphAction;
      if(action==='back')options.onBack();else if(action==='forward')options.onForward();else if(action==='zoom-in')zoom(1.25);else if(action==='zoom-out')zoom(.8);else if(action==='start')center(first,.82);else if(action==='fit')fit();else if(action==='close-detail')detail.hidden=true;else if(action==='fullscreen'){container.classList.toggle('graph-expanded');const expanded=container.classList.contains('graph-expanded');button.textContent=expanded?'收起画布':'展开画布';options.onExpand(expanded);apply();}
    });
    const onEscape=event=>{if(event.key==='Escape'&&container.classList.contains('graph-expanded')){container.classList.remove('graph-expanded');container.querySelector('[data-graph-action="fullscreen"]').textContent='展开画布';options.onExpand(false);apply();}};
    document.addEventListener('keydown',onEscape);
    const resize=new ResizeObserver(()=>apply());resize.observe(stage);
    if(options.expanded){container.classList.add('graph-expanded');container.querySelector('[data-graph-action="fullscreen"]').textContent='收起画布';}
    if(options.initialView){scale=options.initialView.scale;tx=options.initialView.tx;ty=options.initialView.ty;apply();}else center(first,.82);
    active={destroy:()=>{resize.disconnect();document.removeEventListener('keydown',onEscape);},center,select,graph,snapshot:()=>({scale,tx,ty})};
    return active;
  }
  return {mount,parse,snapshot:()=>active?.snapshot()||null,destroy(){active?.destroy();active=null;}};
})();
