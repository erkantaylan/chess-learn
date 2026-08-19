/* Repertoire Wheel — sunburst of the whole study.
   Ring = ply depth, arc angle proportional to subtree size. */
(function(){
  if(!window.RT) return;
  const SVGNS="http://www.w3.org/2000/svg";
  const VB=1000, CX=500, CY=500;
  const MAXRINGS=8;          // rings drawn from the current wheel root
  const R0=62;                // hub radius
  const RMAX=482;

  /* ---- style ---- */
  const css=`
.rwheel{display:flex;flex-direction:column;gap:8px;font-family:inherit}
.rwheel .rw-bar{display:flex;align-items:center;gap:6px;flex-wrap:wrap;font-size:11.5px;color:var(--ink-3)}
.rwheel .rw-crumb{display:flex;align-items:center;gap:3px;flex-wrap:wrap;min-height:18px}
.rwheel .rw-crumb button{background:none;border:0;padding:1px 4px;border-radius:4px;cursor:pointer;
  font:inherit;font-size:11.5px;color:var(--ink-2)}
.rwheel .rw-crumb button:hover{background:var(--parchment-2);color:var(--ink)}
.rwheel .rw-crumb button.on{color:var(--brass);font-weight:600}
.rwheel .rw-crumb span.sep{color:var(--ink-3);opacity:.6}
.rwheel .rw-btn{background:var(--card);border:1px solid var(--line);color:var(--ink-2);
  border-radius:6px;padding:2px 8px;font:inherit;font-size:11.5px;cursor:pointer}
.rwheel .rw-btn:hover{border-color:var(--brass);color:var(--ink)}
.rwheel .rw-btn[disabled]{opacity:.45;cursor:default}
.rwheel .rw-stage{position:relative;width:100%}
.rwheel svg{display:block;width:100%;height:auto}
.rwheel .arc{cursor:pointer}
.rwheel .arc:hover{opacity:.85}
.rwheel .lbl{pointer-events:none;font-family:"IBM Plex Mono",ui-monospace,monospace;font-weight:600}
.rwheel .hub{cursor:pointer}
.rwheel .rw-tip{position:absolute;z-index:9;pointer-events:none;background:var(--card);
  border:1px solid var(--line);border-radius:8px;padding:7px 9px;max-width:250px;
  box-shadow:0 6px 20px rgba(0,0,0,.22);font-size:11.5px;color:var(--ink);display:none}
.rwheel .rw-tip .ln{font-family:"IBM Plex Mono",ui-monospace,monospace;line-height:1.5;
  word-break:break-word;color:var(--ink-2)}
.rwheel .rw-tip .nm{color:var(--brass);font-weight:600;margin-bottom:2px}
.rwheel .rw-tip .mt{color:var(--ink-3);margin-top:3px}
.rwheel .rw-tip .mini{margin-top:5px;border:1px solid var(--line);border-radius:4px;overflow:hidden}
.rwheel .rw-legend{display:flex;gap:10px;flex-wrap:wrap;font-size:11px;color:var(--ink-3);align-items:center}
.rwheel .rw-legend i{display:inline-block;width:10px;height:10px;border-radius:2px;
  margin-right:4px;vertical-align:-1px;border:1px solid var(--line)}
`;
  const st=document.createElement("style"); st.textContent=css; document.head.appendChild(st);

  let pane,svg,tip,crumbEl,upEl,resetEl,statEl;
  let wheelRoot=null;          // node the wheel is centred on (null => RT.root)
  let hoverNode=null;
  let arcsByNode=new Map();

  const mk=(t,a)=>{const e=document.createElementNS(SVGNS,t);
    if(a) for(const k in a) e.setAttribute(k,a[k]); return e;};

  function ringRadii(){
    // shrinking ring widths so deep rings still fit
    const q=.87, w=[]; let s=0;
    for(let i=0;i<MAXRINGS;i++){ const v=Math.pow(q,i); w.push(v); s+=v; }
    const span=RMAX-R0, r=[R0];
    for(let i=0;i<MAXRINGS;i++) r.push(r[i]+span*w[i]/s);
    return r;
  }
  const RR=ringRadii();

  function polar(cx,cy,r,a){ return [cx+r*Math.cos(a), cy+r*Math.sin(a)]; }
  function arcPath(r0,r1,a0,a1){
    if(a1-a0>=Math.PI*2-1e-6){ // full annulus
      return `M ${CX-r1} ${CY} A ${r1} ${r1} 0 1 1 ${CX+r1} ${CY} A ${r1} ${r1} 0 1 1 ${CX-r1} ${CY} Z `+
             `M ${CX-r0} ${CY} A ${r0} ${r0} 0 1 0 ${CX+r0} ${CY} A ${r0} ${r0} 0 1 0 ${CX-r0} ${CY} Z`;
    }
    const large=(a1-a0)>Math.PI?1:0;
    const [x0,y0]=polar(CX,CY,r1,a0), [x1,y1]=polar(CX,CY,r1,a1);
    const [x2,y2]=polar(CX,CY,r0,a1), [x3,y3]=polar(CX,CY,r0,a0);
    return `M ${x0.toFixed(2)} ${y0.toFixed(2)} A ${r1} ${r1} 0 ${large} 1 ${x1.toFixed(2)} ${y1.toFixed(2)} `+
           `L ${x2.toFixed(2)} ${y2.toFixed(2)} A ${r0} ${r0} 0 ${large} 0 ${x3.toFixed(2)} ${y3.toFixed(2)} Z`;
  }

  /* colour ramp derived from theme tokens: white moves light, black moves dark,
     alternating shades per ring for readability. Eval only tints the stroke. */
  function fillFor(n, ply, onPath){
    const whiteMove = ply%2===1;      // ply 1 = White's 1st move
    const base = whiteMove ? "var(--sq-light)" : "var(--sq-dark)";
    if(onPath) return "var(--brass)";
    return base;
  }
  function evalStroke(n){
    const e=RT.evalOf(n); if(!e) return null;
    let v = e.mate!==undefined ? (e.mate>0?900:-900) : Math.max(-600,Math.min(600,e.cp));
    if(Math.abs(v)<40) return null;
    return v>0 ? "var(--ink)" : "var(--claret)";
  }

  function lineOf(n){
    const p=RT.pathTo(n); let out=[];
    for(const m of p){ const mn=RT.moveNumber(m);
      if(mn.white) out.push(mn.no+"."+m.san); else out.push(out.length?m.san:mn.no+"..."+m.san); }
    return out.join(" ");
  }

  function mount(p){
    pane=p; pane.classList.add("rwheel");

    const bar=document.createElement("div"); bar.className="rw-bar";
    crumbEl=document.createElement("div"); crumbEl.className="rw-crumb";
    upEl=document.createElement("button"); upEl.className="rw-btn"; upEl.textContent="↑ up";
    upEl.onclick=()=>{ const w=wheelRoot; if(w&&w.parent){ wheelRoot=w.parent.parent?w.parent:null; } else wheelRoot=null; draw(); };
    resetEl=document.createElement("button"); resetEl.className="rw-btn"; resetEl.textContent="whole study";
    resetEl.onclick=()=>{ wheelRoot=null; draw(); };
    const hereEl=document.createElement("button"); hereEl.className="rw-btn"; hereEl.textContent="centre on current";
    hereEl.onclick=()=>{ wheelRoot=RT.cur===RT.root?null:RT.cur; draw(); };
    bar.appendChild(upEl); bar.appendChild(resetEl); bar.appendChild(hereEl);
    pane.appendChild(bar);
    pane.appendChild(crumbEl);

    const stage=document.createElement("div"); stage.className="rw-stage";
    svg=mk("svg",{viewBox:"0 0 "+VB+" "+VB}); stage.appendChild(svg);
    tip=document.createElement("div"); tip.className="rw-tip"; stage.appendChild(tip);
    pane.appendChild(stage);

    const lg=document.createElement("div"); lg.className="rw-legend";
    lg.innerHTML='<span><i style="background:var(--sq-light)"></i>White move</span>'+
                 '<span><i style="background:var(--sq-dark)"></i>Black move</span>'+
                 '<span><i style="background:var(--brass)"></i>current path</span>'+
                 '<span><i style="background:var(--card);border-color:var(--brass);border-width:2px"></i>named</span>';
    pane.appendChild(lg);
    statEl=document.createElement("div"); statEl.className="rw-legend"; pane.appendChild(statEl);

    stage.addEventListener("mouseleave",()=>{ hoverNode=null; hideTip(); paint(); });
  }

  function hideTip(){ tip.style.display="none"; tip.textContent=""; }
  function showTip(n,ev){
    tip.textContent="";
    if(n.name){ const d=document.createElement("div"); d.className="nm"; d.textContent=n.name; tip.appendChild(d); }
    const ln=document.createElement("div"); ln.className="ln"; ln.textContent=lineOf(n)||"start position";
    tip.appendChild(ln);
    const m=RT.metricsOf(n), e=RT.evalOf(n);
    const mt=document.createElement("div"); mt.className="mt";
    mt.textContent=m.size+" moves below · depth "+m.depth+" · "+m.forks+" forks"+
      (e? " · "+(e.mate!==undefined?("#"+e.mate):((e.cp>0?"+":"")+(e.cp/100).toFixed(2))) : "");
    tip.appendChild(mt);
    try{ tip.appendChild(RT.miniBoard(n.fen,116,{highlight:n.move?[n.move.from,n.move.to]:null})); }catch(err){}
    tip.style.display="block";
    const r=pane.querySelector(".rw-stage").getBoundingClientRect();
    let x=ev.clientX-r.left+12, y=ev.clientY-r.top+12;
    const tw=tip.offsetWidth, th=tip.offsetHeight;
    if(x+tw>r.width) x=Math.max(2,r.width-tw-2);
    if(y+th>r.height) y=Math.max(2,r.height-th-2);
    tip.style.left=x+"px"; tip.style.top=y+"px";
  }

  /* ---- layout ---- */
  function trunkFrom(n){           // collapse a single-child chain into the hub
    const t=[]; let x=n;
    while(x.children.length===1){ x=x.children[0]; t.push(x); }
    return {base:x, trunk:t};
  }
  function layout(){
    const start = wheelRoot || RT.root;
    const {base,trunk} = trunkFrom(start);
    const items=[];   // {node, ring, a0, a1}
    const weight = n => RT.metricsOf(n).size + 1;
    (function rec(n,ring,a0,a1){
      if(ring>=MAXRINGS){
        const m=RT.metricsOf(n);
        if(m.size>0) items.push({node:n,ring:ring,a0:a0,a1:a1,stub:true});
        return;
      }
      const kids=n.children; if(!kids.length) return;
      let tot=0; for(const c of kids) tot+=weight(c);
      let a=a0;
      for(const c of kids){
        const w=(a1-a0)*weight(c)/tot;
        items.push({node:c,ring:ring,a0:a,a1:a+w});
        rec(c,ring+1,a,a+w);
        a+=w;
      }
    })(base,0,-Math.PI/2,-Math.PI/2+Math.PI*2);
    return {base,items,trunk,start};
  }

  function onPathSet(){
    const s=new Set(); let n=RT.cur;
    while(n){ s.add(n); n=n.parent; } return s;
  }
  function hoverSet(){
    const s=new Set(); let n=hoverNode;
    while(n){ s.add(n); n=n.parent; } return s;
  }

  let lastLayout=null, spokeEl=null, angleOf=new Map();
  function drawSpoke(){
    if(spokeEl&&spokeEl.parentNode) spokeEl.parentNode.removeChild(spokeEl);
    spokeEl=null;
    const chain=[]; let n=RT.cur; while(n){ chain.unshift(n); n=n.parent; }
    const pts=[[CX,CY]];
    for(const nd of chain){ const a=angleOf.get(nd); if(a) pts.push(polar(CX,CY,a.r,a.a)); }
    if(pts.length<2) return;
    const g=mk("g",{"pointer-events":"none"});
    const d="M "+pts.map(q=>q[0].toFixed(1)+" "+q[1].toFixed(1)).join(" L ");
    g.appendChild(mk("path",{d:d,fill:"none",stroke:"var(--card)","stroke-width":7,
      "stroke-linecap":"round","stroke-linejoin":"round",opacity:.9}));
    g.appendChild(mk("path",{d:d,fill:"none",stroke:"var(--brass)","stroke-width":2.8,
      "stroke-linecap":"round","stroke-linejoin":"round"}));
    const last=pts[pts.length-1];
    g.appendChild(mk("circle",{cx:last[0].toFixed(1),cy:last[1].toFixed(1),r:6,
      fill:"var(--brass)",stroke:"var(--card)","stroke-width":2}));
    spokeEl=g; svg.appendChild(g);
    for(const [,el] of arcsByNode) if(el.text) svg.appendChild(el.text);
  }
  function paint(){
    const cur=onPathSet(), hov=hoverSet();
    for(const [node,el] of arcsByNode){
      const onCur=cur.has(node), onHov=hov.has(node);
      el.path.setAttribute("fill", onCur? "var(--brass)" : el.baseFill);
      el.path.setAttribute("stroke", onHov? "var(--claret)" : (onCur? "var(--brass)" : (node.name? "var(--brass)" : "var(--line)")));
      el.path.setAttribute("stroke-width", onHov? 3.6 : (onCur? 3 : (node.name? 2 : .8)));
      el.path.setAttribute("opacity", (hoverNode && !onHov) ? .45 : 1);
      if(el.text) el.text.setAttribute("fill", onCur? "var(--on-brass)" : "var(--ink)");
    }
    drawSpoke();
  }

  function draw(){
    if(!svg) return;
    // keep wheelRoot valid
    if(wheelRoot){ let n=wheelRoot,ok=false; while(n){ if(n===RT.root){ok=true;break;} n=n.parent; }
      if(!ok) wheelRoot=null; }
    svg.textContent=""; arcsByNode=new Map(); angleOf=new Map(); spokeEl=null;
    const {base,items,trunk,start}=layout();
    lastLayout=items;

    // hub
    const hub=mk("circle",{cx:CX,cy:CY,r:R0-4,fill:"var(--card)",stroke:"var(--brass)","stroke-width":2});
    hub.setAttribute("class","hub"); svg.appendChild(hub);
    hub.addEventListener("click",()=>{
      if(start.parent){ wheelRoot = start.parent===RT.root? null : start.parent; draw(); }
      else RT.toast("Already at the start position");
    });
    hub.addEventListener("mousemove",e=>{ hoverNode=base; showTip(base,e); paint(); });
    hub.addEventListener("dblclick",()=>{ RT.goTo(base); });
    hub.addEventListener("mouseleave",()=>{ hoverNode=null; hideTip(); paint(); });
    const hubTxt=mk("text",{x:CX,y:CY-4,"text-anchor":"middle","font-size":22,fill:"var(--ink)"});
    hubTxt.setAttribute("class","lbl");
    hubTxt.textContent = base===RT.root ? "start" : base.san;
    svg.appendChild(hubTxt);
    const hubSub=mk("text",{x:CX,y:CY+18,"text-anchor":"middle","font-size":15,fill:"var(--ink-3)"});
    hubSub.setAttribute("class","lbl");
    hubSub.textContent = RT.metricsOf(base).size+" below";
    if(trunk.length){
      const tt=mk("text",{x:CX,y:CY+36,"text-anchor":"middle","font-size":13,fill:"var(--ink-3)"});
      tt.setAttribute("class","lbl");
      tt.textContent="via "+trunk.slice(0,3).map(n=>n.san).join(" ")+(trunk.length>3?"…":"");
      svg.appendChild(tt);
    }
    svg.appendChild(hubSub);

    for(const it of items){
      const r0=RR[it.ring], r1=it.stub? RR[MAXRINGS]+14 : RR[it.ring+1];
      const rr0=it.stub? RR[MAXRINGS]+3 : r0;
      const g=mk("g");
      const p=mk("path",{d:arcPath(rr0+1.2,r1-1.2,it.a0+0.009,Math.max(it.a0+0.009,it.a1-0.009)),
        "stroke-linejoin":"round"});
      p.setAttribute("class","arc");
      const baseFill = it.stub? "var(--parchment-2)" : fillFor(it.node,plyOf(it.node),false);
      p.setAttribute("fill",baseFill);
      const es = it.stub? null : evalStroke(it.node);
      svg.appendChild(g); g.appendChild(p);

      const nd=it.node;
      if(!it.stub) angleOf.set(nd,{a:(it.a0+it.a1)/2, r:(rr0+r1)/2});
      if(it.stub){
        p.addEventListener("click",e=>{ e.stopPropagation(); wheelRoot=nd; draw(); RT.toast("Re-rooted on "+(nd.san||"start")); });
        p.addEventListener("mousemove",e=>{ hoverNode=nd; showTip(nd,e); paint(); });
      }else{
        p.addEventListener("click",e=>{ e.stopPropagation(); RT.goTo(nd); });
        p.addEventListener("dblclick",e=>{ e.stopPropagation(); wheelRoot=nd; draw(); });
        p.addEventListener("mousemove",e=>{ hoverNode=nd; showTip(nd,e); paint(); });
      }
      p.addEventListener("mouseleave",()=>{ hoverNode=null; hideTip(); paint(); });

      // eval tick: thin radial mark at the outer edge
      if(es){
        const am=(it.a0+it.a1)/2;
        const [ex,ey]=polar(CX,CY,r1-3,am);
        const t=mk("circle",{cx:ex.toFixed(1),cy:ey.toFixed(1),r:2.6,fill:es,"pointer-events":"none"});
        g.appendChild(t);
      }

      // label
      const span=it.a1-it.a0, rm=(rr0+r1)/2, ringW=r1-rr0;
      const txt = it.stub ? ("+"+RT.metricsOf(nd).size) : nd.san;
      const fs = Math.min(26, Math.max(15, ringW*0.42));
      const need = txt.length*fs*0.62;
      const avail = span*rm;
      if(!it.stub || true){
        if(avail > need*1.25+8 && ringW > 24){
          let deg = (it.a0+it.a1)/2*180/Math.PI;
          let flip = deg>90 || deg<-90;
          const t=mk("text",{"text-anchor":"middle","dominant-baseline":"central",
            "font-size":fs.toFixed(1),fill:"var(--ink)"});
          t.setAttribute("class","lbl");
          const [tx,ty]=polar(CX,CY,rm,(it.a0+it.a1)/2);
          t.setAttribute("transform",`translate(${tx.toFixed(1)},${ty.toFixed(1)}) rotate(${(flip?deg+180:deg).toFixed(1)})`);
          t.textContent=txt;
          g.appendChild(t);
          arcsByNode.set(nd,{path:p,text:t,baseFill});
        } else {
          arcsByNode.set(nd,{path:p,text:null,baseFill});
        }
      }
    }

    drawCrumb(base);
    upEl.disabled = !base.parent;
    resetEl.disabled = !wheelRoot;
    const m=RT.metricsOf(base);
    statEl.textContent = m.size+" moves · depth "+m.depth+" · "+m.forks+" forks · "+m.named+" named"+
      (m.depth>MAXRINGS? "  (rings capped at "+MAXRINGS+" — click a rim stub to dive)":"");
    paint();
  }

  function plyOf(n){ let d=0,x=n; while(x&&x.parent){ d++; x=x.parent; } return d; }

  function drawCrumb(base){
    crumbEl.textContent="";
    const chain=[]; let n=base; while(n){ chain.unshift(n); n=n.parent; }
    chain.forEach((n,i)=>{
      if(i) { const s=document.createElement("span"); s.className="sep"; s.textContent="›"; crumbEl.appendChild(s); }
      const b=document.createElement("button");
      b.textContent = n===RT.root? "start" : (n.name || n.san);
      if(n===base) b.className="on";
      b.onclick=()=>{ wheelRoot = n===RT.root? null : n; draw(); };
      crumbEl.appendChild(b);
    });
  }

  RT.registerView({
    id:"wheel", label:"Wheel",
    mount, update(){ try{ draw(); }catch(e){ console.error("wheel:",e); } }
  });
})();
