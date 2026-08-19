/* Contact Sheet view — the study as a wall of proof-sheet thumbnails.
   Depth costs rows, branching costs columns: a fork visibly fans the sheet wider. */
(function(){
  if(!window.RT||!RT.registerView) return;

  /* ---------------- style ---------------- */
  const CSS = `
.csheet{font:12px/1.3 ui-sans-serif,system-ui,sans-serif;color:var(--ink)}
.csheet .cs-bar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;
  position:sticky;top:0;z-index:5;background:var(--card);padding:2px 0 6px;margin-bottom:4px;
  border-bottom:1px solid var(--line)}
.csheet .cs-bar b{font-weight:600;color:var(--ink-2);font-size:11px;letter-spacing:.04em;text-transform:uppercase}
.csheet .cs-zoom{display:flex;border:1px solid var(--line);border-radius:6px;overflow:hidden}
.csheet .cs-zoom button{font:inherit;font-size:11px;padding:2px 8px;border:0;cursor:pointer;
  background:var(--parchment-2);color:var(--ink-2)}
.csheet .cs-zoom button+button{border-left:1px solid var(--line)}
.csheet .cs-zoom button.on{background:var(--brass);color:var(--on-brass)}
.csheet .cs-hint{color:var(--ink-3);font-size:11px}
.csheet .cs-scroll{overflow:auto;max-height:min(48vh,470px);padding:4px 2px 10px}
.csheet .cs-canvas{position:relative}
.csheet .cs-links{position:absolute;inset:0;pointer-events:none;overflow:visible}
.csheet .frame{position:absolute;box-sizing:border-box;cursor:pointer;
  background:var(--parchment-2);border:1px solid var(--line);border-radius:3px;padding:2px;
  opacity:.55;transition:opacity .12s}
.csheet .frame.onpath{opacity:1}
.csheet .frame:hover{opacity:1;border-color:var(--brass)}
.csheet .frame.fork{border-width:2px;border-color:var(--ink-2);padding:1px;box-shadow:0 0 0 1px var(--parchment)}
.csheet .frame.cur{border-color:var(--claret);border-width:2px;padding:1px;
  box-shadow:0 0 0 2px var(--claret),0 0 0 4px var(--parchment)}
.csheet .frame .mini{display:block;border-radius:1px;overflow:hidden}
.csheet .cap{font-size:9px;line-height:1.15;color:var(--ink-2);text-align:center;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:1px}
.csheet .cap .mv{color:var(--ink-3)}
.csheet .nm{font-size:8px;line-height:1.15;color:var(--brass);text-align:center;font-weight:600;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.csheet .dot{position:absolute;top:2px;right:2px;width:5px;height:5px;border-radius:50%;
  background:var(--claret);box-shadow:0 0 0 1px var(--parchment)}
.csheet .nmtag{position:absolute;top:2px;left:2px;width:0;height:0;
  border-top:7px solid var(--brass);border-right:7px solid transparent}
.csheet .ev{position:absolute;left:2px;right:2px;bottom:-1px;height:2px;border-radius:1px}
.csheet .strip{position:absolute;box-sizing:border-box;border:1px dashed var(--ink-3);
  border-radius:3px;background:var(--parchment-2);color:var(--ink-2);cursor:pointer;
  display:flex;align-items:center;justify-content:center;font-size:9px;opacity:.7}
.csheet .strip:hover{opacity:1;border-color:var(--brass);color:var(--brass)}
.csheet .cs-empty{color:var(--ink-3);padding:14px 2px}
`;
  if(!document.getElementById("csheet-style")){
    const st=document.createElement("style"); st.id="csheet-style"; st.textContent=CSS;
    document.head.appendChild(st);
  }

  /* ---------------- ids & caches ---------------- */
  let idc=0; const IDS=new WeakMap();
  const idOf=n=>{ let i=IDS.get(n); if(i===undefined){ i=++idc; IDS.set(n,i); } return i; };

  const boardCache=new Map();          // fen|px|flip|hl -> DOM element (detached clones handed out)
  function board(fen,px,hl,flip){
    const key=fen+"|"+px+"|"+(flip?1:0)+"|"+(hl?hl.join(""):"");
    let el=boardCache.get(key);
    if(!el){
      el=RT.miniBoard(fen,px,hl?{highlight:hl,flip:flip}:{flip:flip});
      if(boardCache.size>1400) boardCache.clear();
      boardCache.set(key,el);
    }
    return el;
  }

  const STOPS=[{px:54,cap:true},{px:36,cap:true},{px:22,cap:false}];
  let stop=1;
  const CAP_COLS=6;                    // a subtree wider than this collapses to a strip
  const expanded=new Set();            // node ids the user opened

  let pane,scroll,canvas,links,bar,hint;
  let lastSig="", frames=new Map();     // id -> {el, node}

  /* ---------------- layout ---------------- */
  let openSet=new Set();               // ids that must never collapse (root + current path)
  const isOpen=n=>openSet.has(idOf(n))||expanded.has(idOf(n));

  function widthOf(n,W){
    let w;
    if(!n.children.length) w=1;
    else{
      w=0; for(const c of n.children) w+=widthOf(c,W);
      if(w>CAP_COLS && !isOpen(n)) w=1;
    }
    W.set(n,w); return w;
  }
  function place(n,col,row,W,out,strips){
    out.push({n,col,row});
    if(!n.children.length) return row;
    if(W.get(n)===1 && sumW(n,W)>CAP_COLS && !isOpen(n)){
      strips.push({n,col,row:row+1});
      return row+1;
    }
    let c=col, deepest=row;
    for(const ch of n.children){
      deepest=Math.max(deepest,place(ch,c,row+1,W,out,strips));
      c+=W.get(ch);
    }
    return deepest;
  }
  function sumW(n,W){ let w=0; for(const c of n.children) w+=W.get(c); return w; }

  function signature(){
    let s=stop+"|"+(RT.flipped?1:0)+"|"+[...openSet].join(".")+"|";
    const walk=n=>{ s+=idOf(n)+(n.name?"n":"")+(n.comment?"c":"")+((n.shapes&&n.shapes.length)?"s":"")+",";
      if(expanded.has(idOf(n))) s+="+";
      for(const c of n.children) walk(c); s+=")"; };
    walk(RT.root);
    return s;
  }

  /* ---------------- frame building ---------------- */
  function makeFrame(n,px,withCap){
    const el=document.createElement("div");
    el.className="frame";
    el.style.width=(px+6)+"px";
    const hl=n.move?[n.move.from,n.move.to]:null;
    el.appendChild(board(n.fen,px,hl,RT.flipped).cloneNode(true));
    if(withCap){
      const cap=document.createElement("div"); cap.className="cap";
      if(n.parent){
        if(px>=48){
          const mn=RT.moveNumber(n);
          const s=document.createElement("span"); s.className="mv";
          s.textContent=mn.no+(mn.white?". ":"… ");
          cap.appendChild(s);
        }
        cap.appendChild(document.createTextNode(n.san));
      } else cap.textContent="start";
      el.appendChild(cap);
      if(n.name){ const nm=document.createElement("div"); nm.className="nm"; nm.textContent=n.name; el.appendChild(nm); }
    } else if(n.name){
      const t=document.createElement("div"); t.className="nmtag"; el.appendChild(t);
    }
    if(n.comment||(n.shapes&&n.shapes.length)){
      const d=document.createElement("div"); d.className="dot"; el.appendChild(d);
    }
    const ev=document.createElement("div"); ev.className="ev"; el.appendChild(ev);
    el.title=(n.parent? (RT.moveNumber(n).no+(RT.moveNumber(n).white?". ":"… ")+n.san):"start")
             + (n.name?" — "+n.name:"") + (n.comment?"\n"+n.comment:"");
    el.onclick=()=>RT.goTo(n);
    return el;
  }

  function evColor(n){
    const e=RT.evalOf(n); if(!e) return "";
    let v;
    if(e.mate!==undefined&&e.mate!==null) v=e.mate>0?1:-1;
    else v=Math.max(-1,Math.min(1,(e.cp||0)/400));
    const hue=v>=0?142:6, a=.25+.55*Math.abs(v);
    return "hsl("+hue+" 55% 45% / "+a.toFixed(2)+")";
  }

  /* ---------------- render ---------------- */
  function rebuild(){
    const px=STOPS[stop].px, withCap=STOPS[stop].cap;
    const cellW=px+6+8, cellH=px+6+(withCap?22:0)+8;

    const W=new Map(); widthOf(RT.root,W);
    const out=[], strips=[];
    place(RT.root,0,0,W,out,strips);

    let maxC=0,maxR=0;
    for(const p of out){ maxC=Math.max(maxC,p.col); maxR=Math.max(maxR,p.row); }
    for(const p of strips){ maxC=Math.max(maxC,p.col); maxR=Math.max(maxR,p.row); }

    canvas.textContent="";
    frames=new Map();
    const ns="http://www.w3.org/2000/svg";
    const svg=document.createElementNS(ns,"svg");
    svg.setAttribute("class","cs-links");
    canvas.appendChild(svg);

    const pos=new Map();
    for(const p of out) pos.set(p.n,p);

    const X=p=>p.col*cellW+ (px+6)/2, Y=p=>p.row*cellH;
    for(const p of out){
      if(!p.n.parent) continue;
      const q=pos.get(p.n.parent); if(!q) continue;
      const path=document.createElementNS(ns,"path");
      const y0=Y(q)+(px+6+(withCap?22:0)), y1=Y(p);
      const d = q.col===p.col
        ? "M"+X(q)+" "+y0+"V"+y1
        : "M"+X(q)+" "+y0+"V"+((y0+y1)/2)+"H"+X(p)+"V"+y1;
      path.setAttribute("d",d);
      path.setAttribute("fill","none");
      path.setAttribute("stroke","var(--line)");
      path.setAttribute("stroke-width",1);
      svg.appendChild(path);
    }

    for(const p of out){
      const el=makeFrame(p.n,px,withCap);
      el.style.left=(p.col*cellW)+"px";
      el.style.top=(p.row*cellH)+"px";
      canvas.appendChild(el);
      frames.set(idOf(p.n),{el,node:p.n});
    }
    for(const s of strips){
      const b=document.createElement("div");
      b.className="strip";
      b.style.left=(s.col*cellW)+"px";
      b.style.top=(s.row*cellH)+"px";
      b.style.width=(px+6)+"px";
      b.style.height=Math.max(18,(px+6)/2)+"px";
      const m=RT.metricsOf(s.n);
      b.textContent="+"+m.size+" ▾";
      b.title=m.size+" moves, "+m.forks+" forks below — click to unfold";
      b.onclick=e=>{ e.stopPropagation(); expanded.add(idOf(s.n)); lastSig=""; render(); };
      canvas.appendChild(b);
    }

    canvas.style.width=((maxC+1)*cellW)+"px";
    canvas.style.height=((maxR+1)*cellH+10)+"px";
    svg.setAttribute("width",canvas.style.width);
    svg.setAttribute("height",canvas.style.height);
  }

  function paint(){
    const path=new Set([idOf(RT.root)]);
    for(const n of RT.pathTo(RT.cur)) path.add(idOf(n));
    const curId=idOf(RT.cur);
    let curEl=null;
    for(const [id,f] of frames){
      const n=f.node, el=f.el;
      el.classList.toggle("onpath",path.has(id));
      el.classList.toggle("cur",id===curId);
      el.classList.toggle("fork",n.children.length>1);
      const ev=el.querySelector(".ev");
      if(ev){ const c=evColor(n); ev.style.background=c; ev.style.display=c?"":"none"; }
      if(id===curId) curEl=el;
    }
    if(curEl){
      const r=curEl.getBoundingClientRect(), s=scroll.getBoundingClientRect();
      if(r.top<s.top||r.bottom>s.bottom||r.left<s.left||r.right>s.right)
        curEl.scrollIntoView({block:"nearest",inline:"nearest"});
    }
    const m=RT.metricsOf(RT.root);
    hint.textContent=(m.size+1)+" frames · "+m.forks+" forks · depth "+m.depth;
  }

  function render(){
    openSet=new Set([idOf(RT.root)]);
    for(const n of RT.pathTo(RT.cur)) openSet.add(idOf(n));
    const sig=signature();
    if(sig!==lastSig){ lastSig=sig; rebuild(); }
    paint();
  }

  /* ---------------- mount ---------------- */
  RT.registerView({
    id:"sheet", label:"Sheet",
    mount(p){
      pane=p; p.classList.add("csheet");
      bar=document.createElement("div"); bar.className="cs-bar";
      const lbl=document.createElement("b"); lbl.textContent="Contact sheet"; bar.appendChild(lbl);
      const z=document.createElement("div"); z.className="cs-zoom";
      ["L","M","S"].forEach((t,i)=>{
        const b=document.createElement("button"); b.textContent=t;
        b.onclick=()=>{ stop=i; [...z.children].forEach((c,j)=>c.className=j===i?"on":""); render(); };
        z.appendChild(b);
      });
      [...z.children].forEach((c,j)=>c.className=j===stop?"on":"");
      bar.appendChild(z);
      const all=document.createElement("button");
      all.textContent="unfold all"; all.style.cssText="font:inherit;font-size:11px;padding:2px 8px;border:1px solid var(--line);border-radius:6px;background:var(--parchment-2);color:var(--ink-2);cursor:pointer";
      all.onclick=()=>{ for(const n of RT.allNodes()) expanded.add(idOf(n)); lastSig=""; render(); };
      bar.appendChild(all);
      hint=document.createElement("span"); hint.className="cs-hint"; bar.appendChild(hint);
      p.appendChild(bar);
      scroll=document.createElement("div"); scroll.className="cs-scroll";
      canvas=document.createElement("div"); canvas.className="cs-canvas";
      scroll.appendChild(canvas); p.appendChild(scroll);
    },
    update(){ render(); }
  });
})();
