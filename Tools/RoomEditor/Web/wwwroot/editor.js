// Presentation and browser APIs only. Room data is read, changed, validated and serialized in C#.
window.roomEditor = (() => {
  let bridge, model, canvas, context, start, path=[], generation=0;
  function render(){
    if(!model||!canvas)return;
    const m=model,z=m.zoom,w=m.viewWidth*z,h=m.viewHeight*z;
    if(canvas.width!==w)canvas.width=w;if(canvas.height!==h)canvas.height=h;
    context.setTransform(1,0,0,1,0,0);context.clearRect(0,0,w,h);context.fillStyle='#24303a';context.fillRect(0,0,w,h);if(m.plan){const rendered=remainsRoomRenderer.render(m.plan,{width:w,height:h,zoom:z,cell:m.cell,panX:m.panX,panRow:m.panRow},render,new URLSearchParams(location.search).get('renderer')==='canvas');context.drawImage(rendered,0,0);}
    context.translate(-m.panX*z,-m.panRow*z);
    for(const c of m.cells){const x=c.x*z,y=c.y*z;context.fillStyle=c.color;if(c.layer==='height'){context.globalAlpha=.6;context.fillRect(x,y+z*.6,z,z*.4);context.globalAlpha=1;context.fillStyle='#f7e2cf';context.fillText(c.value,x+3,y+z-3);}else context.fillRect(x,y,z,z);}
    if(m.showGrid){context.lineWidth=1;context.strokeStyle='#ffffff15';context.beginPath();for(let x=m.panX*z;x<=(m.panX+m.viewWidth)*z;x+=z){context.moveTo(x+.5,m.panRow*z);context.lineTo(x+.5,(m.panRow+m.viewHeight)*z);}for(let y=m.panRow*z;y<=(m.panRow+m.viewHeight)*z;y+=z){context.moveTo(m.panX*z,y+.5);context.lineTo((m.panX+m.viewWidth)*z,y+.5);}context.stroke();
    context.font='10px system-ui';context.fillStyle='#dce4e9';for(let x=Math.ceil(m.panX/5)*5;x<m.panX+m.viewWidth;x+=5)context.fillText(`${x} · ${(x*m.cell).toFixed(1)}м`,x*z+3,m.panRow*z+11);}
    context.strokeStyle='#a6e7ae';context.lineWidth=2;context.setLineDash([5,3]);for(const l of m.links){context.beginPath();context.moveTo(l.x0,l.y0);context.lineTo(l.x1,l.y1);context.stroke();}context.setLineDash([]);
    for(const p of m.routePoints||[]){context.fillStyle=p.found?'#75ed99':'#f0ba65';context.fillRect(p.x-3,p.y-4,6,4);}
    if(m.objectMarkers)for(const p of m.points){context.fillStyle='#ffd28c';context.beginPath();context.arc(p.x,p.y,3,0,Math.PI*2);context.fill();if(m.showNames&&z>=24){context.font='9px system-ui';context.fillText(p.label,p.x+4,p.y-4);}}
    if(m.selection){const s=m.selection;context.fillStyle='#b0e4bf22';context.fillRect(s.x*z,s.y*z,s.w*z,s.h*z);context.strokeStyle='#b0e4bf';context.strokeRect(s.x*z,s.y*z,s.w*z,s.h*z);}
  }
  function coordinates(e){const r=canvas.getBoundingClientRect();return [Math.floor((e.clientX-r.left)*canvas.width/r.width),Math.floor((e.clientY-r.top)*canvas.height/r.height)];}
  let database,pendingDrafts=0;
  window.addEventListener('beforeunload',e=>{if(pendingDrafts>0){e.preventDefault();e.returnValue='';}});
  function drafts(){
    if(!database)database=new Promise((resolve,reject)=>{const request=indexedDB.open('remains-room-editor',1);request.onupgradeneeded=()=>request.result.createObjectStore('drafts');request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);});
    return database;
  }
  async function putDraft(key,text){pendingDrafts++;try{const db=await drafts();await new Promise((resolve,reject)=>{const tx=db.transaction('drafts','readwrite');tx.objectStore('drafts').put(text,key);tx.oncomplete=()=>resolve();tx.onerror=()=>reject(tx.error);tx.onabort=()=>reject(tx.error||new Error('Draft transaction aborted'));});}finally{pendingDrafts--;}}
  async function getDraft(key,legacy){const db=await drafts();return new Promise((resolve,reject)=>{const tx=db.transaction('drafts','readonly'),request=tx.objectStore('drafts').get(key);request.onsuccess=()=>{let value=request.result;if(value===undefined){try{value=localStorage.getItem(legacy);}catch{value=null;}}resolve(value??null);};request.onerror=()=>reject(request.error);});}
  return {
    init(dotnet){bridge=dotnet;canvas=document.getElementById('room-canvas');context=canvas.getContext('2d');
      canvas.onpointerdown=e=>{if(e.button!==0)return;canvas.focus();start=coordinates(e);path=[...start];canvas.setPointerCapture(e.pointerId);e.preventDefault();};
      canvas.onpointermove=e=>{if(start&&path.length<8190)path.push(...coordinates(e));};
      canvas.onpointerup=async e=>{if(!start)return;path.push(...coordinates(e));start=null;const stroke=path;path=[];await bridge.invokeMethodAsync('Stroke',stroke);};
      canvas.onpointercancel=()=>{start=null;};
      document.onkeydown=e=>{if(e.target.closest('input,textarea,select')||e.altKey)return;let action=null;const key=e.key.toLowerCase();if(e.ctrlKey||e.metaKey)action=({z:e.shiftKey?'redo':'undo',y:'redo',c:'copy',v:'paste'})[key];else action=({b:'brush',r:'rectangle',f:'fill',e:'erase',i:'pick',s:'select'})[key];if(action){e.preventDefault();bridge.invokeMethodAsync('Shortcut',action);}};
    },
    draw(value){model=value;generation++;render();},
    renderStats(){return remainsRoomRenderer.stats();},
    renderPlan(){return model?.plan;},
    download(name,text){const blob=new Blob([text],{type:'application/json;charset=utf-8'}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);},
    saveDraft(text){return putDraft('room',text);},
    saveMapDraft(text){return putDraft('map',text);},
    loadMapDraft(){return getDraft('map','remains-map-editor-draft-v1');},
    loadDraft(){return getDraft('room','remains-room-editor-v1');}
  };
})();
