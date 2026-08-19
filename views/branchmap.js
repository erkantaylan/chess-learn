/* Branch Map — horizontal "git graph" of the move tree.
   Ply runs left->right on a shared x-axis; the main line runs straight along the
   top, alternatives peel down into lanes. Edge thickness = subtree size. */
(function(){
  if(!window.RT) return;
  const SVGNS="http://www.w3.org/2000/svg";

  /* ---------------------------------------------------------------- style */
  const CSS = `
.bmap{position:relative;font-family:"IBM Plex Mono",ui-monospace,monospace}
.bmap .bm-scroll{overflow:auto;max-height:min(52vh,520px);
  background:var(--parchment);border:1px solid var(--line);border-radius:3px}
.bmap .bm-legend{display:flex;gap:12px;align-items:center;flex-wrap:wrap;
  font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;
  color:var(--ink-3);margin:0 0 6px;font-family:inherit}
.bmap .bm-legend b{font-weight:600;color:var(--ink-2)}
.bmap svg{display:block}
.bmap .nd{cursor:pointer}
.bmap .nd rect.box{fill:var(--card);stroke:var(--line);stroke-width:1}
.bmap .nd:hover rect.box{stroke:var(--brass)}
.bmap .nd text.san{fill:var(--ink);font-size:11px;font-family:inherit;
  text-anchor:middle;dominant-baseline:central;pointer-events:none}
.bmap .nd.fork rect.box{stroke:var(--brass);stroke-width:2}
.bmap .nd .halo{fill:none;stroke:var(--brass);stroke-width:1.5;
  stroke-dasharray:3 2;opacity:.9}
.bmap .nd.cur rect.box{fill:var(--claret);stroke:var(--brass);stroke-width:2}
.bmap .nd.cur text.san{fill:var(--card);font-weight:600}
.bmap .nd.named text.san{font-weight:600}
.bmap text.nm{fill:var(--ink-2);font-size:9.5px;font-family:inherit;pointer-events:none}
.bmap text.cap{fill:var(--ink-3);font-size:9.5px;font-family:inherit;
  text-anchor:middle;pointer-events:none}
.bmap .ruler text{fill:var(--ink-3);font-size:9px;font-family:inherit;text-anchor:middle}
.bmap .ruler line{stroke:var(--line)}
.bmap .edge{fill:none;stroke:var(--ink-3);opacity:.55;stroke-linecap:round}
.bmap .edge.main{stroke:var(--brass);opacity:.85}
.bmap .mark{fill:var(--brass)}
.bmap .ev{stroke-linecap:butt}
.bmap .bm-empty{color:var(--ink-3);font-size:12px;padding:14px}
`;

  /* --------------------------------------------------------------- consts */
  const COLW=50, CAPW=66, LANEH=36, TOPY=38, NODEW=40, NODEH=19, PADL=14;

  function esc(s){ return String(s||""); }

  const view = {
    id:"map", label:"Map",

    mount(pane){
      if(!document.getElementById("bmap-style")){
        const st=document.createElement("style");
        st.id="bmap-style"; st.textContent=CSS;
        document.head.appendChild(st);
      }
      pane.classList.add("bmap");
      const leg=document.createElement("div");
      leg.className="bm-legend";
      leg.innerHTML='<span><b>&#9679;</b> fork</span><span><b>&#8213;</b> thickness = branch size</span>'+
                    '<span><b>&#8594;</b> right = deeper</span><span><b>&#9642;</b> note/arrow</span>';
      const sc=document.createElement("div");
      sc.className="bm-scroll";
      pane.appendChild(leg); pane.appendChild(sc);
      this.scroll=sc; this.sig=null;
    },

    update(){
      const sc=this.scroll; if(!sc) return;
      const sig=signature();
      if(sig===this.sig && sc.firstChild){ this.center(); return; }
      this.sig=sig;
      sc.textContent="";
      const built=build();
      if(!built){ const d=document.createElement("div");
        d.className="bm-empty"; d.textContent="No moves yet — play one on the board.";
        sc.appendChild(d); return; }
      sc.appendChild(built.svg);
      this.cur=built.curPos;
      this.center();
    },

    center(){
      const sc=this.scroll, p=this.cur; if(!sc||!p) return;
      const tx=p.x - sc.clientWidth/2, ty=p.y - sc.clientHeight/2;
      sc.scrollLeft=Math.max(0,tx); sc.scrollTop=Math.max(0,ty);
    }
  };

  /* ------------------------------------------------------------ signature */
  function signature(){
    const parts=[];
    for(const n of RT.allNodes()){
      const e=RT.evalOf(n);
      parts.push(n.san+"|"+n.children.length+"|"+(n.name?1:0)+
        "|"+((n.comment?1:0)+((n.shapes&&n.shapes.length)?2:0))+
        "|"+(e?(e.mate!==undefined?"m"+e.mate:e.cp):""));
    }
    parts.push("cur:"+RT.pathTo(RT.cur).map(n=>n.san).join(","));
    return parts.join(";");
  }

  /* ---------------------------------------------------------------- build */
  function build(){
    const root=RT.root;
    if(!root.children.length) return null;

    /* --- ply + lane assignment ------------------------------------- */
    const info=new Map();                    // node -> {ply,lane,main}
    const laneOcc=[];                        // lane -> Set(ply)
    let maxPly=0, maxLane=0;
    const occupy=(lane,ply)=>{ (laneOcc[lane]||(laneOcc[lane]=new Set())).add(ply); };
    const free=(lane,a,b)=>{ const s=laneOcc[lane]; if(!s) return true;
      for(let p=a;p<=b;p++) if(s.has(p)) return false; return true; };

    const stack=[[root,0,0,true]];
    while(stack.length){
      const [n,ply,lane,main]=stack.pop();
      info.set(n,{ply,lane,main});
      occupy(lane,ply);
      if(ply>maxPly) maxPly=ply;
      if(lane>maxLane) maxLane=lane;
      // push in reverse so children[0] is processed first (DFS, LIFO)
      const kids=n.children;
      const pending=[];
      for(let i=0;i<kids.length;i++){
        const c=kids[i];
        let cl;
        if(i===0) cl=lane;
        else{
          const span=RT.metricsOf(c).depth;
          cl=lane+1;
          while(!free(cl,ply+1,ply+1+span)) cl++;
          // pre-reserve so siblings don't collide before DFS reaches them
          for(let p=ply+1;p<=ply+1+span;p++) occupy(cl,p);
        }
        pending.push([c,ply+1,cl,main&&i===0]);
      }
      for(let i=pending.length-1;i>=0;i--) stack.push(pending[i]);
    }

    /* --- which ply columns can be compressed ----------------------- */
    const byPly=[]; for(let p=0;p<=maxPly;p++) byPly.push([]);
    for(const [n,d] of info) byPly[d.ply].push(n);
    const cur=RT.cur;
    const collapsible=[];
    for(let p=0;p<=maxPly;p++){
      if(p===0){ collapsible.push(false); continue; }
      let ok=byPly[p].length>0;
      for(const n of byPly[p]){
        if(n===cur||n.name||n.comment||(n.shapes&&n.shapes.length)||
           n.children.length>1||(n.parent&&n.parent.children.length>1)){ ok=false; break; }
      }
      collapsible.push(ok);
    }
    // only collapse runs of >= 2 consecutive columns
    const hidden=new Array(maxPly+1).fill(false);
    for(let p=0;p<=maxPly;){
      if(!collapsible[p]){ p++; continue; }
      let q=p; while(q<=maxPly&&collapsible[q]) q++;
      if(q-p>=3) for(let i=p;i<q;i++) hidden[i]=true;
      p=q;
    }

    /* --- x positions ----------------------------------------------- */
    const X=new Array(maxPly+1);
    let x=PADL+NODEW/2, i=0;
    while(i<=maxPly){
      if(hidden[i]){
        let q=i; while(q<=maxPly&&hidden[q]) q++;
        for(let k=i;k<q;k++) X[k]=x+CAPW/2;      // all inside the capsule
        x+=CAPW; i=q;
      } else { X[i]=x; x+=COLW; i++; }
    }
    const W=x+PADL, H=TOPY+maxLane*LANEH+LANEH;

    const svg=document.createElementNS(SVGNS,"svg");
    svg.setAttribute("width",W); svg.setAttribute("height",H);
    svg.setAttribute("viewBox","0 0 "+W+" "+H);

    const el=(t,a)=>{ const e=document.createElementNS(SVGNS,t);
      for(const k in a) e.setAttribute(k,a[k]); return e; };
    const yOf=lane=>TOPY+lane*LANEH;

    /* --- ruler: move numbers along the top -------------------------- */
    const ruler=el("g",{class:"ruler"});
    let lastLabel=-99;
    for(let p=1;p<=maxPly;p++){
      if(hidden[p]) continue;
      const n=byPly[p][0]; if(!n) continue;
      const mn=RT.moveNumber(n);
      if(!mn||!mn.white) continue;
      if(X[p]-lastLabel<28) continue;
      lastLabel=X[p];
      const rt=el("text",{x:X[p],y:12}); rt.textContent=mn.no+"."; ruler.appendChild(rt);
      ruler.appendChild(el("line",{x1:X[p],y1:16,x2:X[p],y2:H-6,opacity:.35}));
    }
    svg.appendChild(ruler);

    /* --- edges ------------------------------------------------------ */
    const maxSize=Math.max(1,RT.metricsOf(root).size);
    const thick=n=>{
      const s=RT.metricsOf(n).size+1;
      return Math.max(1.2, Math.min(7, 1.2+5.8*Math.sqrt(s)/Math.sqrt(maxSize+1)));
    };
    const visAnc=n=>{ let a=n.parent;
      while(a&&a.parent&&hidden[info.get(a).ply]) a=a.parent; return a; };

    const edges=el("g",{}), caps=el("g",{}), nodes=el("g",{});
    for(const [n,d] of info){
      if(!n.parent) continue;
      if(hidden[d.ply]) continue;
      const a=visAnc(n); if(!a) continue;
      const ad=info.get(a);
      const x1=X[ad.ply], y1=yOf(ad.lane), x2=X[d.ply], y2=yOf(d.lane);
      const sw=thick(n);
      const sx=x1+NODEW/2, ex=x2-NODEW/2;
      let dstr;
      if(y1===y2) dstr="M"+sx+" "+y1+" L"+ex+" "+y2;
      else{
        const k=Math.min(16,(ex-sx)/2);
        dstr="M"+sx+" "+y1+" L"+(sx+k)+" "+y1+" Q"+(sx+k+8)+" "+y1+" "+(sx+k+8)+" "+(y1+Math.sign(y2-y1)*10)+
             " L"+(sx+k+8)+" "+(y2-Math.sign(y2-y1)*10)+" Q"+(sx+k+8)+" "+y2+" "+(sx+k+18)+" "+y2+
             " L"+ex+" "+y2;
      }
      const p=el("path",{d:dstr,class:"edge"+(d.main?" main":""),"stroke-width":sw.toFixed(2)});
      edges.appendChild(p);
      // capsule label for compressed runs
      const skipped=d.ply-ad.ply-1;
      if(skipped>0){
        const t=el("text",{x:(sx+ex)/2,y:y2-9,class:"cap"});
        t.textContent=skipped+" moves →";
        caps.appendChild(t);
      }
    }
    svg.appendChild(edges); svg.appendChild(caps);

    /* --- nodes ------------------------------------------------------ */
    let curPos=null;
    for(const [n,d] of info){
      if(!n.parent) continue;
      if(hidden[d.ply]) continue;
      const cx=X[d.ply], cy=yOf(d.lane);
      const isFork=n.children.length>1;
      const g=el("g",{class:"nd"+(isFork?" fork":"")+(n===cur?" cur":"")+(n.name?" named":"")});
      g.setAttribute("data-node","1");
      if(n===cur) g.appendChild(el("rect",{class:"halo",x:cx-NODEW/2-4,y:cy-NODEH/2-4,
        width:NODEW+8,height:NODEH+8,rx:5}));
      g.appendChild(el("rect",{class:"box",x:cx-NODEW/2,y:cy-NODEH/2,
        width:NODEW,height:NODEH,rx:3}));
      const mn=RT.moveNumber(n);
      const t=el("text",{class:"san",x:cx,y:cy});
      t.textContent=n.san;
      g.appendChild(t);

      if(isFork){
        // brass diamond above the box — the landmark
        g.appendChild(el("path",{class:"mark",
          d:"M"+cx+" "+(cy-NODEH/2-9)+" l5 5 l-5 5 l-5 -5 z"}));
      }
      if(n.comment||(n.shapes&&n.shapes.length))
        g.appendChild(el("circle",{class:"mark",cx:cx+NODEW/2-3,cy:cy-NODEH/2+3,r:2}));

      // eval bar under the node, when available
      const ev=RT.evalOf(n);
      if(ev){
        let frac;
        if(ev.mate!==undefined&&ev.mate!==null) frac=ev.mate>0?1:0;
        else frac=1/(1+Math.exp(-(ev.cp||0)/320));
        const bw=NODEW-6, bx=cx-bw/2, by=cy+NODEH/2+3;
        g.appendChild(el("line",{class:"ev",x1:bx,y1:by,x2:bx+bw,y2:by,
          stroke:"var(--sq-dark)","stroke-width":3}));
        g.appendChild(el("line",{class:"ev",x1:bx,y1:by,x2:bx+bw*frac,y2:by,
          stroke:"var(--sq-light)","stroke-width":3}));
      }

      // name label: fits into the gap before the next node in this lane
      if(n.name){
        let avail=COLW*3;
        const nx=nextInLane(n,d,info,hidden,X);
        if(nx!==null) avail=Math.max(COLW, nx-(cx-NODEW/2)-11);
        const chars=Math.max(3,Math.floor(avail/5.4));
        let nm=n.name; if(nm.length>chars) nm=nm.slice(0,chars-1)+"…";
        const lt=el("text",{class:"nm",x:cx-NODEW/2,y:cy+NODEH/2+(ev?14:11)});
        lt.textContent=nm;
        g.appendChild(lt);
      }

      const ttl=el("title",{});
      const m=RT.metricsOf(n);
      ttl.textContent=(mn?(mn.no+(mn.white?". ":"... ")):"")+n.san+
        (n.name?"  — "+n.name:"")+
        "\n"+m.size+" moves below, depth "+m.depth+", "+m.forks+" forks"+
        (n.comment?"\n"+n.comment:"");
      g.appendChild(ttl);

      g.addEventListener("click",()=>RT.goTo(n));
      nodes.appendChild(g);
      if(n===cur) curPos={x:cx,y:cy};
    }
    svg.appendChild(nodes);
    if(!curPos) curPos={x:PADL,y:TOPY};
    return {svg,curPos};
  }

  /* x of the next drawn node in the same lane to the right, or null */
  function nextInLane(node,d,info,hidden,X){
    let best=null;
    for(const [m,md] of info){
      if(md.lane!==d.lane||md.ply<=d.ply||hidden[md.ply]||!m.parent) continue;
      const x=X[md.ply];
      if(best===null||x<best) best=x;
    }
    return best===null?null:best-20;
  }

  RT.registerView(view);
})();
