'use strict';
const $ = s => document.querySelector(s);
const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const modes = [['Legacy','传统模式','原版相机与联动',0],['ThreePoint','3 点追踪','头显 + 双手柄',3],['SixPoint','6 点追踪','预留 · 未接入',6],['EightPoint','8 点追踪','预留 · 未接入',8],['TenPoint','10 点追踪','预留 · 未接入',10],['ElevenPoint','11 点追踪','预留 · 未接入',11]];
const names = {'01':'核心启动','02':'画面与渲染','03':'手柄与按键','04':'传统模式 · 身体与视角','05':'VR 界面面板','06':'NPC 标记','07':'追踪模式','08':'3 点追踪','09':'6 点追踪 · 预留','10':'8 点追踪 · 预留','11':'10 点追踪 · 预留','12':'11 点追踪 · 预留'};
const enumNames = {Legacy:'传统模式',ThreePoint:'3 点追踪',SixPoint:'6 点追踪 · 预留',EightPoint:'8 点追踪 · 预留',TenPoint:'10 点追踪 · 预留',ElevenPoint:'11 点追踪 · 预留',HeadLocked:'跟随头显',SourceCameraLocked:'跟随原相机',WorldFixed:'固定在世界中',None:'不设置'};
let data, fields=[], values={}, baseline={}, category='overview', visible=[], live={}, toastTimer, busy=false, storageKey='';
let draftTimer, lastDraft='';
const label = f => data.labels[f.key]?.[0] || f.key;
const sectionName = s => names[s.slice(0,2)] || s;
const modeField = () => fields.find(f => f.key === 'TrackingMode');
const modeValue = () => values[modeField()?.id] || 'Legacy';
const changed = () => Object.fromEntries(fields.filter(f => values[f.id] !== baseline[f.id]).map(f => [f.id,values[f.id]]));
const toast = (text,error=false) => { const t=$('#toast');t.textContent=text;t.classList.toggle('error',error);t.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>t.hidden=true,error?7500:4000); };
async function api(path,body) {
  const r=await fetch('api/'+path,{method:body===undefined?'GET':'POST',headers:body===undefined?{}:{'Content-Type':'application/json','X-Config-Token':location.pathname.split('/')[2]},body:body===undefined?undefined:JSON.stringify(body)});
  const json=await r.json().catch(()=>({error:'服务暂时不可用，请重新打开配置工具。'}));
  if(!r.ok) throw new Error(json.error || `请求失败 (${r.status})`);
  return json;
}
function persist() {
  const draft={revision:data.snapshot.revision,changes:changed()}, serialized=JSON.stringify(draft);
  try{if(Object.keys(draft.changes).length)localStorage.setItem(storageKey,serialized);else localStorage.removeItem(storageKey);}catch{}
  if(serialized===lastDraft)return;
  lastDraft=serialized;clearTimeout(draftTimer);
  draftTimer=setTimeout(()=>api('draft',draft).catch(()=>{lastDraft='';}),300);
}
async function flushDraft() { clearTimeout(draftTimer);if(data)await api('draft',{revision:data.snapshot.revision,changes:changed()}); }
function updateBar() {
  const count=Object.keys(changed()).length;
  $('#dirty-label').textContent=count?`${count} 项未保存`:'尚无修改';
  $('#dirty-dot').classList.toggle('dirty',count>0);
  $('#save').disabled=busy || (!count && data.snapshot.exists);
  $('#discard').disabled=busy || !count;
  $('#save-hint').textContent=live.gameRunning?'游戏运行中 · 关闭后可保存':'保存后，下次启动游戏生效';
  persist();
}
function nav() {
  const sections=[...new Set(fields.map(f=>f.section))];
  const item=(id,title,symbol,count='')=>`<button class="nav-item ${category===id?'active':''}" data-category="${esc(id)}"><span class="nav-symbol">${symbol}</span>${esc(title)}<span class="nav-count">${count}</span></button>`;
  $('#nav').innerHTML='<div class="nav-heading">工作台</div>'+item('overview','追踪模式','◉')+item('all','全部配置','≡',fields.length)+'<div class="nav-heading">通用设置</div>'+sections.filter(s=>['01','02','03','05','06'].includes(s.slice(0,2))).map(s=>item(s,sectionName(s),'◇',fields.filter(f=>f.section===s).length)).join('')+'<div class="nav-heading">模式独立配置</div>'+sections.filter(s=>s.startsWith('04') || Number(s.slice(0,2))>=8).map(s=>item(s,sectionName(s),'⌁',fields.filter(f=>f.section===s).length)).join('')+'<div class="nav-heading">高级</div>'+item('fixed','程序固定参数','⊙',data.fixedSettings.length)+sections.filter(s=>!names[s.slice(0,2)]).map(s=>item(s,sectionName(s),'◇',fields.filter(f=>f.section===s).length)).join('');
  $('#nav').querySelectorAll('[data-category]').forEach(b=>b.onclick=()=>{category=b.dataset.category;$('#search').value='';$('#modified-only').checked=false;render();window.scrollTo({top:0,behavior:'smooth'});});
}
function bodyIcon(n) {
  const nodes=[[25,8],[7,32],[43,32],[25,33],[15,59],[35,59],[18,46],[32,46],[13,24],[37,24],[25,21]];
  return `<svg viewBox="0 0 50 65" aria-hidden="true"><g fill="none" stroke="${n?'#b8c8b0':'#bec7b9'}" stroke-width="1.4" stroke-linecap="round"><circle cx="25" cy="8" r="5"/><path d="M25 14v20m-10-15 10-3 10 3M15 19 7 32m28-13 8 13M25 34 18 46l-3 13m10-25 7 12 3 13"/></g>${nodes.slice(0,n).map(([x,y])=>`<circle cx="${x}" cy="${y}" r="2.7" fill="#6d9260" stroke="#f0f6ea" stroke-width="1"/>`).join('')}</svg>`;
}
function overview() {
  const mode=modeValue(), supported=['Legacy','ThreePoint'].includes(mode);
  let info=mode==='Legacy'?'<strong>传统模式</strong>保留原版相机、头部联动和移动转向。下方为传统模式独立参数。':mode==='ThreePoint'?'<strong>三点追踪</strong>使用真实头显和双手柄，腰胯与双脚由姿态推算。旧相机限位和骨骼联动在此模式下不生效。':'<strong>预留模式</strong>可独立保存参数，但真实追踪器绑定尚未实现；游戏中不会自动降级为三点。';
  return `<section><div class="section-title"><h2>选择追踪方式</h2><small>保存后的模式在下次启动时生效</small></div><div class="mode-grid">${modes.map(([id,title,sub,n])=>`<button class="mode-card ${mode===id?'selected':''} ${n>3?'reserved':''}" data-mode="${id}" aria-pressed="${mode===id}" aria-label="${title}">${mode===id?'<span class="check">✓</span>':''}${bodyIcon(n)}<b>${title}</b><small>${sub}</small></button>`).join('')}</div><div class="mode-info"><span class="info-icon">i</span><span>${info}</span></div><div class="status-strip" id="devices"><span>设备状态</span><span class="device" data-device="HmdValid"><i class="dot"></i>头显</span><span class="device" data-device="LeftValid"><i class="dot"></i>左手柄</span><span class="device" data-device="RightValid"><i class="dot"></i>右手柄</span><span class="live-label">游戏未运行时无需连接设备</span></div></section>`;
}
function fieldHtml(f) {
  const info=data.labels[f.key] || [], id=esc(f.id), value=values[f.id], unit=info[2]||'', migration=['EnableThreePointBody','ThreePointTrackingScale','ThreePointAutoScale'].includes(f.key);
  let control='';
  if(f.type==='Boolean') control=`<label class="switch"><input data-id="${id}" type="checkbox" ${value.toLowerCase()==='true'?'checked':''} aria-label="${esc(label(f))}"><span class="switch-track"></span><span class="switch-state">${value.toLowerCase()==='true'?'开启':'关闭'}</span></label>`;
  else if(f.options.length) control=`<select data-id="${id}" aria-label="${esc(label(f))}">${!f.options.includes(value)?`<option value="${esc(value)}">${esc(value)}（当前值）</option>`:''}${f.options.map(v=>`<option value="${esc(v)}" ${v===value?'selected':''}>${esc(enumNames[v]||v)}</option>`).join('')}</select>`;
  else if(['Single','Double','Int32'].includes(f.type)) {
    const step=f.type==='Int32'?'1':'any', numericRange=f.min!==null && f.max!==null;
    control=(numericRange && f.type!=='Int32' && f.max>f.min && f.max-f.min<=10?`<input type="range" data-slider="${id}" min="${f.min}" max="${f.max}" step="${10**Math.floor(Math.log10((f.max-f.min)/1000))}" value="${esc(value)}" aria-label="${esc(label(f))}滑块">`:'')+`<input data-id="${id}" type="number" step="${step}" ${f.min!==null?`min="${f.min}"`:''} ${f.max!==null?`max="${f.max}"`:''} value="${esc(value)}" required aria-label="${esc(label(f))}">${unit?`<span class="unit">${esc(unit)}</span>`:''}`;
  } else control=`<textarea data-id="${id}" rows="${value.length>55?2:1}" spellcheck="false" aria-label="${esc(label(f))}">${esc(value)}</textarea>`;
  const detail=f.min!==null&&f.max!==null?`${f.min} — ${f.max}${unit?' '+unit:''} · `:'';
  return `<div class="field ${value!==baseline[f.id]?'changed':''}"><div><div class="field-title">${esc(label(f))}${migration?'<span class="field-badge">仅迁移</span>':''}</div><div class="field-help">${esc(info[1]||f.description||'配置文件中的附加选项。')}</div><details><summary>${esc(f.key)}</summary><p>${esc(f.description||'未提供额外说明。')}</p></details></div><div class="controls">${control}${f.default!==null?`<button class="reset-field" data-reset="${id}" title="恢复默认：${esc(f.default)}" aria-label="恢复${esc(label(f))}默认值">↺</button>`:''}<div class="range-help">${esc(detail)}${f.default!==null?'默认 '+esc(f.default||'留空'):'无预设默认值'}</div></div></div>`;
}
function render() {
  if(!data)return;
  const query=$('#search').value.trim().toLowerCase(), only=$('#modified-only').checked;
  const searching=query.length>0;
  nav();
  const title=searching?'搜索配置':category==='overview'?'配置你的 VR 体验':category==='all'?'全部配置':category==='fixed'?'程序固定参数':sectionName(category);
  $('#page-title').textContent=title;$('#breadcrumb').textContent=category==='overview'?'追踪模式':title;
  $('#page-subtitle').textContent=category==='fixed'?'这些参数固定在当前 Mod 程序中，可查看但不能通过配置文件修改。':category==='overview'?'选择追踪方式，再调整适合你的视角与操作。':'配置直接连接游戏目录。编辑草稿，预览后保存。';
  let scope=fields;
  if(!searching && category==='overview') {
    const mode=modeValue(), map={Legacy:'04',ThreePoint:'08',SixPoint:'09',EightPoint:'10',TenPoint:'11',ElevenPoint:'12'};
    scope=fields.filter(f=>f.section.startsWith(map[mode]||'08'));
  } else if(!searching && !['all','fixed'].includes(category)) scope=fields.filter(f=>f.section===category);
  if(!searching && category==='fixed') scope=[];
  visible=scope.filter(f=>(!only || values[f.id]!==baseline[f.id]) && (!query || `${label(f)} ${f.key} ${f.section} ${f.description} ${data.labels[f.key]?.[1]||''}`.toLowerCase().includes(query)));
  let html=category==='overview'&&!searching&&!only?overview():'';
  if(searching || only) html+=`<div class="result-count">${visible.length} 个可编辑配置项${searching?' · 搜索全部分类':''}</div>`;
  for(const section of [...new Set(visible.map(f=>f.section))]) {
    const group=visible.filter(f=>f.section===section);
    html+=`<section class="panel"><div class="panel-head"><h2>${esc(sectionName(section))}</h2><small>${group.length} 项设置${Number(section.slice(0,2))>=8?' · 独立保存':''}</small></div>${group.map(fieldHtml).join('')}</section>`;
  }
  const constants=data.fixedSettings.filter(f=>!only && (category==='fixed'||searching) && (!query || `${data.labels[f.key]?.[0]||''} ${f.key} ${f.value}`.toLowerCase().includes(query)));
  if(constants.length) html+=`<section class="panel"><div class="panel-head"><h2>程序固定参数</h2><small>${constants.length} 项 · 只读</small></div>${constants.map(f=>`<div class="field"><div><div class="field-title">${esc(data.labels[f.key]?.[0]||f.key)} <span class="field-badge">只读</span></div><div class="field-help">${esc(f.key)}</div></div><code class="fixed-value">${esc(f.value===''?'空（使用程序默认路径）':f.value)}</code></div>`).join('')}</section>`;
  $('#content').innerHTML=html || '<div class="empty">没有匹配的配置项。试试其他关键词，或取消「仅看已修改」。</div>';
  $('#reset-section').disabled=!visible.some(f=>f.default!==null);
  $('#content').querySelectorAll('[data-mode]').forEach(b=>b.onclick=()=>{const f=modeField();if(f){values[f.id]=b.dataset.mode;render();}});
  $('#content').querySelectorAll('[data-id]').forEach(el=>el.addEventListener('input',()=>edit(el)));
  $('#content').querySelectorAll('[data-slider]').forEach(el=>el.addEventListener('input',()=>{
    const input=[...el.parentElement.querySelectorAll('[data-id]')].find(x=>x.dataset.id===el.dataset.slider);input.value=el.value;edit(input);
  }));
  $('#content').querySelectorAll('[data-reset]').forEach(b=>b.onclick=()=>{const f=fields.find(f=>f.id===b.dataset.reset);values[f.id]=f.default;render();});
  updateBar();updateDevices();
}
function edit(el) {
  values[el.dataset.id]=el.type==='checkbox'?String(el.checked):el.value;
  const row=el.closest('.field');row.classList.toggle('changed',values[el.dataset.id]!==baseline[el.dataset.id]);row.classList.toggle('invalid',!el.checkValidity());
  if(el.type==='checkbox')el.parentElement.querySelector('.switch-state').textContent=el.checked?'开启':'关闭';
  const slider=row.querySelector('[data-slider]');if(slider)slider.value=el.value;
  updateBar();
}
function modal(title,description,content,buttons) {
  $('#dialog-body').innerHTML=`<div class="dialog-head"><h2>${esc(title)}</h2><p>${esc(description)}</p></div><div class="dialog-content">${content}</div><div class="dialog-actions">${buttons.map((b,i)=>`<button class="${b.primary?'primary':'secondary'}" data-dialog-action="${i}" ${b.disabled?'disabled':''}>${esc(b.label)}</button>`).join('')}</div>`;
  $('#dialog-body').querySelectorAll('[data-dialog-action]').forEach(b=>b.onclick=buttons[Number(b.dataset.dialogAction)].run);
  if(!$('#dialog').open)$('#dialog').showModal();
}
const closeModal=()=>$('#dialog').close();
function resetFields(list,title) {
  const resettable=list.filter(f=>f.default!==null && values[f.id]!==f.default);
  if(!resettable.length){toast('这些配置已经是默认值。');return;}
  modal(title,`将 ${resettable.length} 项配置恢复为默认草稿。点击保存前不会修改游戏文件。`,'',[
    {label:'取消',run:closeModal},{label:'恢复为草稿',primary:true,run:()=>{resettable.forEach(f=>values[f.id]=f.default);closeModal();render();}}
  ]);
}
async function reload(keep=true) {
  const edits=data&&keep?changed():{};
  const next=await api('config');data=next;fields=data.snapshot.fields;baseline=Object.fromEntries(fields.map(f=>[f.id,f.value]));values={...baseline};
  storageKey='manaka-vr-draft:'+data.gameRoot;
  for(const [id,value] of Object.entries(edits))if(id in values)values[id]=value;
  live={...live,gameRunning:data.gameRunning};
  $('#config-path').textContent=data.configPath;$('#config-path').title=data.configPath;
  if(!keep) {
    try {
      const draft=await api('draft') || JSON.parse(localStorage.getItem(storageKey)||'null');
      if(draft?.changes && Object.keys(draft.changes).length) {
        for(const [id,value] of Object.entries(draft.changes))if(id in values)values[id]=value;
        toast(draft.revision===data.snapshot.revision?'已恢复上次未保存的草稿。':'配置文件已变化；已保留上次草稿，请在保存预览中核对。');
      }
    }catch{}
  }
  render();await poll();
}
async function savePreview() {
  const invalid=[...$('#content').querySelectorAll('[data-id]')].find(x=>!x.checkValidity());
  if(invalid){invalid.reportValidity();invalid.focus();return;}
  const edits=changed();
  modal('预览即将保存的修改',live.gameRunning?'游戏正在运行。可以核对修改，关闭游戏后重新打开保存预览。':'保存会自动备份原配置，修改在下次启动游戏时生效。',
    Object.keys(edits).length?`<table class="diff"><thead><tr><th>配置项</th><th>当前文件</th><th>保存后</th></tr></thead><tbody>${fields.filter(f=>f.id in edits).map(f=>`<tr><td>${esc(label(f))}<small>${esc(sectionName(f.section))}</small></td><td>${esc(baseline[f.id]||'留空')}</td><td>${esc(edits[f.id]||'留空')}</td></tr>`).join('')}</tbody></table>`:'将创建完整的默认配置文件。',
    [{label:'继续编辑',run:closeModal},{label:'保存到游戏配置',primary:true,disabled:live.gameRunning,run:async()=>{
      if(busy)return;busy=true;$('#dialog-body').querySelectorAll('button').forEach(b=>b.disabled=true);
      try {
        const r=await api('config',{revision:data.snapshot.revision,changes:edits});
        data.snapshot=r.snapshot;fields=r.snapshot.fields;baseline=Object.fromEntries(fields.map(f=>[f.id,f.value]));values={...baseline};
        closeModal();toast(r.backup?'配置已保存，并已备份原文件。':'配置已保存。');
      }catch(e){closeModal();toast(e.message,true);$('#notice').textContent=e.message;$('#notice').hidden=false;}
      finally{busy=false;render();}
    }}]);
}
async function history() {
  try {
    const backups=await api('backups');
    modal('配置备份','每次保存前自动备份原文件。恢复先进入草稿，可预览后再保存。',backups.length?backups.map(b=>`<div class="backup-row"><div><b>${esc(new Date(b.time).toLocaleString('zh-CN'))}</b><small>${esc(b.name)} · ${(b.bytes/1024).toFixed(1)} KB</small></div><button class="secondary" data-backup="${esc(b.name)}">恢复到草稿</button></div>`).join(''):'还没有备份。首次修改并保存后会自动生成。',[{label:'关闭',run:closeModal}]);
    $('#dialog-body').querySelectorAll('[data-backup]').forEach(b=>b.onclick=async()=>{
      try{const backup=await api('backups/'+encodeURIComponent(b.dataset.backup));for(const f of backup)if(f.id in values)values[f.id]=f.value;closeModal();render();toast('备份已恢复到草稿，请预览后保存。');}catch(e){toast(e.message,true);}
    });
  }catch(e){toast(e.message,true);}
}
function updateDevices() {
  const badge=$('#game-badge');badge.textContent=live.gameRunning?'游戏运行中':'游戏已关闭';badge.classList.toggle('running',!!live.gameRunning);
  document.querySelectorAll('[data-device]').forEach(el=>el.classList.toggle('live',!!live.fresh&&!!live.body?.[el.dataset.device]));
  const caption=$('.live-label');if(caption)caption.textContent=live.fresh?(live.body.Active?'追踪已激活 · '+(enumNames[live.body.SelectedMode]||live.body.SelectedMode):'设备状态来自游戏 · 尚未校准'):live.gameRunning?'暂无有效设备状态':'游戏未运行时无需连接设备';
}
async function poll() {
  try{live=await api('status');updateDevices();if(data)updateBar();}
  catch{$('#game-badge').textContent='连接已断开';}
}
$('#search').oninput=render;$('#modified-only').onchange=render;
$('#reload').onclick=async()=>{try{await reload();$('#notice').hidden=true;toast('已重新读取，保留尚未保存的草稿。');}catch(e){toast(e.message,true);}};
$('#reset-section').onclick=()=>resetFields(visible,'本页恢复默认');
$('#reset-all').onclick=()=>resetFields(fields,'全部恢复默认');
$('#discard').onclick=()=>modal('放弃未保存的修改','只清除当前草稿，不会修改游戏配置文件。','',[{label:'继续编辑',run:closeModal},{label:'放弃草稿',run:()=>{values={...baseline};closeModal();render();}}]);
$('#save').onclick=savePreview;$('#history').onclick=history;
$('#quit').onclick=()=>modal('退出配置工具',Object.keys(changed()).length?'未保存的草稿已留在本地，下次打开会恢复。':'将关闭本地配置服务。游戏不会受影响。','',[{label:'取消',run:closeModal},{label:'退出工具',primary:true,run:async()=>{try{await flushDraft();await api('quit',{});closeModal();busy=true;$('#content').innerHTML='<div class="empty">配置工具已退出，可以关闭此页面。</div>';$('#save').disabled=true;clearInterval(pollTimer);}catch(e){toast(e.message,true);}}}]);
document.addEventListener('keydown',e=>{if(e.key==='/'&&!['INPUT','TEXTAREA','SELECT'].includes(document.activeElement.tagName)){e.preventDefault();$('#search').focus();}if((e.ctrlKey||e.metaKey)&&e.key==='s'){e.preventDefault();if(!$('#save').disabled)savePreview();}});
window.addEventListener('beforeunload',e=>{if(data && !busy && Object.keys(changed()).length){e.preventDefault();e.returnValue='';}});
document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='hidden'&&data)fetch('api/draft',{method:'POST',keepalive:true,headers:{'Content-Type':'application/json','X-Config-Token':location.pathname.split('/')[2]},body:JSON.stringify({revision:data.snapshot.revision,changes:changed()})}).catch(()=>{});});
const pollTimer=setInterval(poll,5000);
reload(false).catch(e=>{$('#content').innerHTML=`<div class="empty">${esc(e.message)}</div>`;toast(e.message,true);});
