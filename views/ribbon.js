/* Eval Ribbon — Stockfish evaluation plotted against ply.
   Every root-to-leaf line is a polyline; shared prefixes overlap into one heavier
   stroke, so the trunk reads as a trunk and divergences fray apart.
   Handles the fact that most nodes have no eval: gaps are drawn as gaps, coverage
   is stated plainly, and a dedicated Stockfish worker can scan the tree to fill in. */
(function(){
  if(!window.RT||!RT.registerView) return;
  const SVGNS="http://www.w3.org/2000/svg";
  const CLAMP=5;                       // pawns
  const SCAN_DEPTH=12;

  /* ---------------- style ---------------- */
  const css=`
.eribbon{display:flex;flex-direction:column;height:100%;min-height:0;color:var(--ink);font-size:13px}
.eribbon .erb-bar{display:flex;align-items:center;gap:10px;flex-wrap:wrap;
  padding:8px 10px;border-bottom:1px solid var(--line);background:var(--card)}
.eribbon .erb-title{font-weight:600;letter-spacing:.02em}
.eribbon .erb-cov{color:var(--ink-2)}
.eribbon .erb-cov b{color:var(--ink);font-weight:600}
.eribbon .erb-spacer{flex:1 1 auto}
.eribbon button{font:inherit;cursor:pointer;padding:4px 12px;border-radius:5px;
  border:1px solid var(--line);background:var(--parchment-2);color:var(--ink)}
.eribbon button:hover{border-color:var(--brass)}
.eribbon button.erb-go{background:var(--brass);color:var(--on-brass);border-color:var(--brass)}
.eribbon button[disabled]{opacity:.5;cursor:default}
.eribbon .erb-prog{min-width:120px;color:var(--ink-2)}
.eribbon .erb-zoom{display:inline-flex;gap:2px;margin-right:6px}
.eribbon button.erb-z{padding:1px 7px;font-size:12px;line-height:1.4;min-width:24px}
.eribbon .erb-wrap{cursor:grab}
.eribbon .erb-wrap.dragging{cursor:grabbing}
.eribbon .erb-zlabel{font-family:"IBM Plex Mono",monospace;font-size:10px;fill:var(--ink-3)}
.eribbon .erb-wrap{position:relative;flex:1 1 auto;min-height:0;overflow:hidden}
.eribbon svg{display:block;width:100%;height:100%}
.eribbon .erb-hit{stroke:transparent;fill:none;stroke-width:9;cursor:pointer}
.eribbon .erb-empty{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;
  text-align:center;padding:24px;color:var(--ink-2);line-height:1.6;pointer-events:none}
.eribbon .erb-empty span{max-width:34em}
.eribbon .erb-tip{position:absolute;z-index:5;pointer-events:none;max-width:280px;
  background:var(--card);border:1px solid var(--line);border-radius:6px;
  padding:7px 9px;box-shadow:0 6px 22px rgba(0,0,0,.22);display:none;line-height:1.45}
.eribbon .erb-tip .t-san{font-weight:600}
.eribbon .erb-tip .t-line{color:var(--ink-2);margin-top:3px;font-size:12px;
  max-height:5.6em;overflow:hidden}
.eribbon .erb-tip .t-ev{margin-top:4px;color:var(--brass);font-weight:600}
.eribbon .erb-tip .t-note{margin-top:3px;color:var(--ink-3);font-size:11.5px}
.eribbon .erb-key{padding:5px 10px;border-top:1px solid var(--line);color:var(--ink-3);
  font-size:11.5px;display:flex;gap:14px;flex-wrap:wrap}
`;
  if(!document.getElementById("eribbon-style")){
    const st=document.createElement("style"); st.id="eribbon-style"; st.textContent=css;
    document.head.appendChild(st);
  }

  /* ---------------- scan state (module-level, survives update()) ---------------- */
  const scanCache=new Map();          // fen -> {cp}|{mate}  (WHITE perspective)
  let zx=1, panPly=0, yMax=CLAMP;     // zoom: x scale, x offset in plies, y half-range
  let scan=null;                      // {worker,queue,i,total,cancel}
  let ui=null;                        // dom refs

  const el=(t,a,p)=>{const e=document.createElementNS(SVGNS,t);
    for(const k in a) e.setAttribute(k,a[k]); if(p) p.appendChild(e); return e;};

  function evalFor(n){
    const e=RT.evalOf(n); if(e) return e;
    return scanCache.get(n.fen)||null;
  }
  function toY(e){                     // pawns, clamped to the visible range
    if(e.mate!==undefined&&e.mate!==null) return e.mate>=0?yMax:-yMax;
    return Math.max(-yMax,Math.min(yMax,(e.cp||0)/100));
  }
  function evText(e){
    if(e.mate!==undefined&&e.mate!==null) return "#"+(e.mate>0?"":"-")+Math.abs(e.mate);
    const v=(e.cp||0)/100;
    return (v>0?"+":v<0?"−":"")+Math.abs(v).toFixed(2);
  }
  function lineText(node){
    const path=RT.pathTo(node); const out=[];
    for(const n of path){ const mn=RT.moveNumber(n);
      out.push((mn.white?mn.no+". ":(out.length?"":mn.no+"... "))+n.san); }
    return out.join(" ")||"start";
  }

  /* ---------------- view ---------------- */
  RT.registerView({
    id:"ribbon", label:"Eval",
    mount(pane){
      pane.classList.add("eribbon");
      pane.innerHTML=
        '<div class="erb-bar">'+
          '<span class="erb-title">Eval ribbon</span>'+
          '<span class="erb-cov"></span>'+
          '<span class="erb-spacer"></span>'+
          '<span class="erb-prog"></span>'+
          '<span class="erb-zoom">'+
            '<button class="erb-z" data-z="out" title="Zoom out (wheel down)">−</button>'+
            '<button class="erb-z" data-z="in" title="Zoom in (wheel up over the chart)">+</button>'+
            '<button class="erb-z" data-z="fit" title="Fit the whole study">fit</button>'+
          '</span>'+
          '<button class="erb-go"></button>'+
        '</div>'+
        '<div class="erb-wrap"><div class="erb-empty"><span></span></div>'+
          '<div class="erb-tip"></div></div>'+
        '<div class="erb-key">'+
          '<span>solid = measured &middot; dashed = gap between measured points (no data in between)</span>'+
          '<span>thicker = more lines share the move</span>'+'<span>up = better for White &middot; shaded = equal (±0.5)</span>'+
          '<span>◆ = mate score, pinned at ±'+CLAMP+'</span>'+
        '</div>';
      const wrap=pane.querySelector(".erb-wrap");
      const svg=el("svg",{}); wrap.insertBefore(svg,wrap.firstChild);
      ui={pane,svg,wrap,
        cov:pane.querySelector(".erb-cov"),
        prog:pane.querySelector(".erb-prog"),
        btn:pane.querySelector(".erb-go"),
        empty:pane.querySelector(".erb-empty"),
        emptyMsg:pane.querySelector(".erb-empty span"),
        tip:pane.querySelector(".erb-tip")};
      initZoom();
      ui.btn.onclick=()=>{ if(scan) cancelScan(); else startScan(); };
      if(window.ResizeObserver){
        let raf=0;
        new ResizeObserver(()=>{ if(raf) return;
          raf=requestAnimationFrame(()=>{raf=0; try{render()}catch(e){}}); }).observe(wrap);
      }
    },
    update(){ render(); }
  });

  /* ---------------- render ---------------- */
  function render(){
    if(!ui||!ui.pane.isConnected) return;
    const nodes=RT.allNodes();
    const root=RT.root, cur=RT.cur;

    /* ply + leaf-count per node */
    const ply=new Map(), leaves=new Map();
    ply.set(root,0);
    for(const n of nodes) if(n.parent) ply.set(n,(ply.get(n.parent)||0)+1);
    for(let i=nodes.length-1;i>=0;i--){ const n=nodes[i];
      let c=0; for(const k of n.children) c+=leaves.get(k)||0;
      leaves.set(n,n.children.length?c:1); }

    /* coverage */
    let known=0; const ev=new Map();
    for(const n of nodes){ const e=evalFor(n); if(e){ known++; ev.set(n,e); } }
    ui.cov.innerHTML="<b>"+known+"</b> of <b>"+nodes.length+"</b> positions evaluated"+
      (known<nodes.length?" — "+(nodes.length-known)+" unknown":"");
    if(!scan){ ui.btn.textContent=known>=nodes.length?"Rescan":"Scan"; ui.btn.disabled=false;
      ui.btn.className="erb-go"; }

    /* geometry */
    const W=Math.max(320,ui.wrap.clientWidth||640), H=Math.max(200,ui.wrap.clientHeight||360);
    const mL=44,mR=14,mT=12,mB=26;
    const maxPly=Math.max(1,Math.max.apply(null,nodes.map(n=>ply.get(n)||0)));
    const span=Math.max(2,maxPly/zx);                       // plies visible
    panPly=Math.max(0,Math.min(maxPly-span,panPly));        // keep the window on the data
    view.maxPly=maxPly; view.span=span; view.W=W; view.mL=mL; view.mR=mR;
    const X=p=>mL+(W-mL-mR)*((p-panPly)/span);
    /* non-linear y: expands the near-equal region where opening evals live,
       while ±5 still sits at the edges. Honest — every gridline is placed by Y(). */
    const K=0.55, NRM=Math.pow(yMax,K);
    const warp=v=>{const c=Math.max(-yMax,Math.min(yMax,v));
      return (c<0?-1:1)*Math.pow(Math.abs(c),K)/NRM;};      // -> [-1,1]
    const Y=v=>mT+(H-mT-mB)*((1-warp(v))/2);

    const svg=ui.svg; svg.textContent="";
    svg.setAttribute("viewBox","0 0 "+W+" "+H);
    svg.setAttribute("width",W); svg.setAttribute("height",H);

    /* equal band + zero line + y grid */
    el("rect",{x:mL,y:Y(0.5),width:W-mL-mR,height:Math.max(1,Y(-0.5)-Y(0.5)),
      fill:"var(--ink-3)","fill-opacity":.07},svg);
    const ticks=(yMax>3?[-4,-2,-1,1,2,4]:yMax>1.2?[-2,-1,-0.5,0.5,1,2]
                :yMax>0.6?[-1,-0.5,-0.25,0.25,0.5,1]:[-0.5,-0.25,-0.1,0.1,0.25,0.5])
                .filter(v=>Math.abs(v)<=yMax);
    for(const v of ticks){
      el("line",{x1:mL,y1:Y(v),x2:W-mR,y2:Y(v),stroke:"var(--line)","stroke-width":1,
        "stroke-opacity":.7},svg);
      el("text",{x:mL-6,y:Y(v)+3.5,"text-anchor":"end","font-size":10,fill:"var(--ink-3)"},svg)
        .textContent=(v>0?"+":"")+(Math.abs(v)<1?v.toFixed(2):v); }
    el("line",{x1:mL,y1:Y(0),x2:W-mR,y2:Y(0),stroke:"var(--ink-2)","stroke-width":1.2},svg);
    el("text",{x:mL-6,y:Y(0)+3.5,"text-anchor":"end","font-size":10,fill:"var(--ink-2)"},svg)
      .textContent="0.00";

    /* x ticks: one per full move */
    const step=Math.max(2,2*Math.ceil(span/2/12));
    const p0=Math.max(0,Math.floor(panPly/step)*step);
    for(let p=p0;p<=Math.min(maxPly,panPly+span);p+=step){
      el("line",{x1:X(p),y1:H-mB,x2:X(p),y2:H-mB+3,stroke:"var(--line)"},svg);
      el("text",{x:X(p),y:H-mB+14,"text-anchor":"middle","font-size":10,fill:"var(--ink-3)"},svg)
        .textContent=String(1+Math.floor(p/2));
    }
    el("line",{x1:mL,y1:H-mB,x2:W-mR,y2:H-mB,stroke:"var(--line)"},svg);
    if(zx>1.01||yMax<CLAMP-0.01)
      el("text",{x:W-mR,y:mT+10,"text-anchor":"end",class:"erb-zlabel"},svg)
        .textContent="moves "+(1+Math.floor(panPly/2))+"–"+(1+Math.floor((panPly+span)/2))
          +" · ±"+yMax.toFixed(yMax<1?2:1);

    /* nearest known ancestor for each evaluated node -> one segment per node */
    const curPath=new Set(RT.pathTo(cur)); curPath.add(root);
    const gLines=el("g",{},svg), gHot=el("g",{},svg), gDots=el("g",{},svg), gHit=el("g",{},svg);
    const total=leaves.get(root)||1;
    let drawn=0;

    for(const n of nodes){
      const e=ev.get(n); if(!e) continue;
      let a=n.parent, hops=1;
      while(a&&!ev.has(a)){ a=a.parent; hops++; }
      if(!a) continue;                                   // no known ancestor: dot only
      const ea=ev.get(a);
      const x1=X(ply.get(a)), y1=Y(toY(ea)), x2=X(ply.get(n)), y2=Y(toY(e));
      const share=(leaves.get(n)||1)/total;
      const onCur=curPath.has(n)&&curPath.has(a);
      const w=0.9+3.6*Math.sqrt(share);
      const d="M"+x1.toFixed(1)+" "+y1.toFixed(1)+"L"+x2.toFixed(1)+" "+y2.toFixed(1);
      const seg=el("path",{d:d,fill:"none",
        stroke:onCur?"var(--claret)":"var(--ink-2)",
        "stroke-width":onCur?Math.max(2.6,w):w,
        "stroke-opacity":onCur?1:(0.30+0.55*Math.sqrt(share)),
        "stroke-linecap":"round"},onCur?gHot:gLines);
      if(hops>1) seg.setAttribute("stroke-dasharray","3 3");
      drawn++;
      const hit=el("path",{d:d,class:"erb-hit"},gHit);
      bind(hit,n,e,hops>1?hops-1:0);
    }

    /* dots at measured nodes (diamond for mate) */
    for(const n of nodes){
      const e=ev.get(n); if(!e) continue;
      const x=X(ply.get(n)), y=Y(toY(e));
      const isMate=e.mate!==undefined&&e.mate!==null;
      const onCur=curPath.has(n), isCur=(n===cur);
      let dot;
      if(isMate){
        dot=el("path",{d:"M"+x+" "+(y-4)+"L"+(x+4)+" "+y+"L"+x+" "+(y+4)+"L"+(x-4)+" "+y+"Z",
          fill:"var(--claret)",stroke:"var(--card)","stroke-width":1},gDots);
      }else{
        dot=el("circle",{cx:x,cy:y,r:isCur?5:(onCur?3:2),
          fill:isCur?"var(--brass)":(onCur?"var(--claret)":"var(--ink-2)"),
          "fill-opacity":isCur||onCur?1:.55,
          stroke:isCur?"var(--ink)":"none","stroke-width":isCur?1.5:0},gDots);
      }
      dot.style.cursor="pointer";
      bind(dot,n,e,0);
    }
    /* current node even when it has no eval: a ply marker on the axis */
    if(!ev.has(cur)){
      const x=X(ply.get(cur)||0);
      el("line",{x1:x,y1:mT,x2:x,y2:H-mB,stroke:"var(--brass)","stroke-width":1.2,
        "stroke-dasharray":"2 4","stroke-opacity":.9},svg);
      el("text",{x:x+4,y:mT+10,"font-size":10,fill:"var(--brass)"},svg)
        .textContent="here (no eval)";
    }

    /* empty state */
    if(!drawn){
      ui.empty.style.display="flex";
      ui.emptyMsg.innerHTML=known?
        "Only <b>"+known+"</b> position"+(known===1?" is":"s are")+
          " evaluated — not enough connected points to draw a line yet."+
          "<br>Press <b>Scan</b> to evaluate the tree at depth "+SCAN_DEPTH+"."
        :"No positions have been evaluated yet, so there is nothing to plot."+
         "<br>Stockfish only scores the position you stand on. Press <b>Scan</b> to walk"+
         " the whole tree at depth "+SCAN_DEPTH+" and fill this chart in.";
    }else ui.empty.style.display="none";
  }

  /* ---------------- hover / click ---------------- */
  const view={maxPly:1,span:1,W:640,mL:44,mR:14};
  function zoomAt(clientX,factor){
    const r=ui.wrap.getBoundingClientRect();
    const inner=Math.max(1,view.W-view.mL-view.mR);
    const t=Math.max(0,Math.min(1,(clientX-r.left-view.mL)/inner));   // cursor in [0,1]
    const anchor=panPly+t*view.span;                                  // ply under cursor
    zx=Math.max(1,Math.min(24,zx*factor));
    const span=Math.max(2,view.maxPly/zx);
    panPly=anchor-t*span;                                             // hold it in place
    render();
  }
  function initZoom(){
    ui.wrap.addEventListener("wheel",e=>{
      if(e.ctrlKey) return;
      e.preventDefault();
      if(e.shiftKey){                       // vertical zoom: tighten the eval range
        yMax=Math.max(0.4,Math.min(CLAMP,yMax*(e.deltaY>0?1.18:1/1.18)));
        render(); return;
      }
      zoomAt(e.clientX,e.deltaY>0?1/1.18:1.18);
    },{passive:false});
    let dragX=null,dragPan=0;
    ui.wrap.addEventListener("pointerdown",e=>{
      if(e.target.closest(".erb-tip")) return;
      dragX=e.clientX; dragPan=panPly; ui.wrap.classList.add("dragging");
      ui.wrap.setPointerCapture(e.pointerId);
    });
    ui.wrap.addEventListener("pointermove",e=>{
      if(dragX===null) return;
      const inner=Math.max(1,view.W-view.mL-view.mR);
      panPly=dragPan-((e.clientX-dragX)/inner)*view.span;
      render();
    });
    const end=()=>{ dragX=null; ui.wrap.classList.remove("dragging"); };
    ui.wrap.addEventListener("pointerup",end);
    ui.wrap.addEventListener("pointercancel",end);
    ui.pane.querySelectorAll(".erb-z").forEach(b=>b.onclick=()=>{
      const r=ui.wrap.getBoundingClientRect();
      if(b.dataset.z==="in") zoomAt(r.left+r.width/2,1.5);
      else if(b.dataset.z==="out") zoomAt(r.left+r.width/2,1/1.5);
      else { zx=1; panPly=0; yMax=CLAMP; render(); }
    });
  }

  function bind(target,n,e,gap){
    target.addEventListener("mouseenter",ev=>showTip(ev,n,e,gap));
    target.addEventListener("mousemove",ev=>posTip(ev));
    target.addEventListener("mouseleave",hideTip);
    target.addEventListener("click",()=>{ hideTip(); RT.goTo(n); });
  }
  function showTip(evt,n,e,gap){
    const t=ui.tip;
    t.innerHTML="";
    const san=document.createElement("div"); san.className="t-san";
    san.textContent=n.parent?(RT.moveNumber(n).white?RT.moveNumber(n).no+". ":RT.moveNumber(n).no+"... ")+n.san
      +(n.name?"  — "+n.name:""):"Starting position";
    t.appendChild(san);
    const ln=document.createElement("div"); ln.className="t-line"; ln.textContent=lineText(n);
    t.appendChild(ln);
    const ev=document.createElement("div"); ev.className="t-ev";
    ev.textContent=evText(e)+(e.depth?"  (depth "+e.depth+")":"  (scan d"+SCAN_DEPTH+")");
    t.appendChild(ev);
    if(gap){ const g=document.createElement("div"); g.className="t-note";
      g.textContent=gap+" position"+(gap>1?"s":"")+" between here and the previous measured point"+
        " have no eval — dashed, not interpolated data.";
      t.appendChild(g); }
    t.style.display="block";
    posTip(evt);
  }
  function posTip(evt){
    const t=ui.tip, r=ui.wrap.getBoundingClientRect();
    let x=evt.clientX-r.left+14, y=evt.clientY-r.top+14;
    if(x+t.offsetWidth>r.width-4) x=Math.max(4,evt.clientX-r.left-t.offsetWidth-14);
    if(y+t.offsetHeight>r.height-4) y=Math.max(4,evt.clientY-r.top-t.offsetHeight-14);
    t.style.left=x+"px"; t.style.top=y+"px";
  }
  function hideTip(){ if(ui) ui.tip.style.display="none"; }

  /* ---------------- scan ---------------- */
  function cancelScan(){
    if(!scan) return;
    scan.cancelled=true;
    try{ scan.worker.terminate(); }catch(e){}
    scan=null;
    if(ui){ ui.prog.textContent="Scan cancelled."; ui.btn.textContent="Scan"; ui.btn.disabled=false; }
    render();
  }

  function startScan(){
    const nodes=RT.allNodes();
    const seen=new Set(), queue=[];
    for(const n of nodes){
      if(RT.evalOf(n)) continue;                 // app already has a (deeper) score
      if(scanCache.has(n.fen)) continue;         // we already scanned it
      if(seen.has(n.fen)) continue;
      seen.add(n.fen); queue.push(n.fen);
    }
    if(!queue.length){ RT.toast&&RT.toast("Every position already has an eval."); return; }

    let w;
    try{ w=new Worker("engine/stockfish-18-lite-single.js"); }
    catch(err){
      ui.prog.textContent="";
      RT.toast&&RT.toast("Can't start the scan engine: "+(err&&err.message||err)+
        " (Stockfish needs an http:// origin)");
      return;
    }
    scan={worker:w,queue,i:0,total:queue.length,cancelled:false,phase:"uci",t0:Date.now()};
    ui.btn.textContent="Cancel"; ui.btn.className="";
    ui.prog.textContent="starting engine…";

    const post=s=>{ try{ w.postMessage(s); }catch(e){} };
    let curFen=null, best=null;

    w.onerror=err=>{
      if(!scan) return;
      RT.toast&&RT.toast("Scan engine error: "+(err&&err.message||"failed to load"));
      finish(true);
    };
    w.onmessage=ev=>{
      if(!scan||scan.cancelled) return;
      const line=typeof ev.data==="string"?ev.data:(ev.data&&ev.data.data)||"";
      if(!line) return;
      if(scan.phase==="uci"){
        if(line.indexOf("uciok")===0||line==="uciok"){
          post("setoption name MultiPV value 1"); post("isready"); scan.phase="ready";
        }
        return;
      }
      if(scan.phase==="ready"){
        if(line.indexOf("readyok")===0){ scan.phase="go"; next(); }
        return;
      }
      if(line.lastIndexOf("info",0)===0){
        const m=/\bscore (cp|mate) (-?\d+)/.exec(line);
        if(m) best=m[1]==="cp"?{cp:+m[2]}:{mate:+m[2]};
        return;
      }
      if(line.lastIndexOf("bestmove",0)===0){
        if(curFen&&best){
          const btm=(curFen.split(" ")[1]==="b")?-1:1;
          const white=best.cp!==undefined?{cp:best.cp*btm}:{mate:best.mate*btm};
          scanCache.set(curFen, Object.assign({depth:SCAN_DEPTH},white));
          if(RT.setEval) RT.setEval(curFen, white, SCAN_DEPTH);   // share it with the whole app
        }
        best=null; curFen=null;
        next();
      }
    };

    function next(){
      if(!scan||scan.cancelled) return;
      if(scan.i%4===0||scan.i>=scan.total) render();
      if(scan.i>=scan.total){ finish(false); return; }
      const fen=scan.queue[scan.i++];
      curFen=fen; best=null;
      ui.prog.textContent="scanning "+scan.i+" / "+scan.total+"…";
      post("position fen "+fen);
      post("go depth "+SCAN_DEPTH);
    }
    function finish(bad){
      const secs=scan?((Date.now()-scan.t0)/1000).toFixed(1):"0";
      const done=scan?scan.i:0;
      try{ w.terminate(); }catch(e){}
      scan=null;
      if(ui){
        ui.btn.textContent="Rescan"; ui.btn.disabled=false; ui.btn.className="erb-go";
        ui.prog.textContent=bad?"scan stopped":"scanned "+done+" in "+secs+"s";
      }
      render();
      if(!bad) RT.toast&&RT.toast("Scan complete: "+done+" positions at depth "+SCAN_DEPTH);
    }

    post("uci");
  }
})();
