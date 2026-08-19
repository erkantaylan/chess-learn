/* Decision Cards — one card per branch point, with hole detection and a quiz mode. */
(function(){
  if(!window.RT) return;

  var CSS = `
.dcards{font-size:13px}
.dcards .bar{display:flex;align-items:center;gap:10px;flex-wrap:wrap;
  padding-bottom:8px;margin-bottom:10px;border-bottom:1px solid var(--line)}
.dcards .bar .sp{flex:1;min-width:0}
.dcards .stat{flex:1 1 auto;min-width:0}
.dcards .tgl{display:inline-flex;align-items:center;gap:6px;cursor:pointer;
  font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:var(--ink-3);font-weight:600;
  border:1px solid var(--line);background:var(--card);padding:5px 9px;border-radius:2px}
.dcards .tgl:hover{border-color:var(--brass)}
.dcards .tgl.on{background:var(--brass);color:var(--on-brass);border-color:var(--brass)}
.dcards .tgl.on.warn{background:var(--claret);border-color:var(--claret);color:var(--on-brass)}
.dcards .stat{font-family:"IBM Plex Mono",monospace;font-size:11.5px;color:var(--ink-3)}
.dcards .stat b{color:var(--ink)}
.dcards .conn{display:flex;align-items:center;gap:8px;margin:0 0 8px 26px;cursor:pointer;
  font-size:11.5px;color:var(--ink-3);font-family:"IBM Plex Mono",monospace}
.dcards .conn:hover{color:var(--brass)}
.dcards .conn .ln{width:1px;height:16px;background:var(--line)}
.dcards .conn .mvs{display:none;color:var(--ink-2);flex:1}
.dcards .conn.open .mvs{display:block}
.dcards .card{display:flex;gap:10px;background:var(--card);border:1px solid var(--line);
  padding:10px;margin-bottom:10px;border-left:3px solid var(--line)}
.dcards .card.cur{border-left-color:var(--brass)}
.dcards .card.hashole{border-left-color:var(--claret)}
.dcards .card .left{flex:0 0 auto;cursor:pointer}
.dcards .card .mini{border:1px solid var(--line)}
.dcards .card .right{flex:1;min-width:0}
.dcards .hd{display:flex;align-items:baseline;gap:8px;flex-wrap:wrap;margin-bottom:2px}
.dcards .hd .no{font-family:"IBM Plex Mono",monospace;font-size:11px;color:var(--ink-3)}
.dcards .hd .nm{color:var(--brass);font-weight:600;font-size:12.5px}
.dcards .hd .mk{font-size:11px;color:var(--ink-3)}
.dcards .path{font-family:"IBM Plex Mono",monospace;font-size:11.5px;color:var(--ink-2);
  margin-bottom:8px;cursor:pointer;line-height:1.5;
  display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}
.dcards .path:hover{color:var(--brass)}
.dcards .chips{display:flex;gap:6px;flex-wrap:wrap}
.dcards .chip{flex:1 1 104px;min-width:98px;max-width:240px;border:1px solid var(--line);
  background:var(--parchment);padding:6px 8px;cursor:pointer;position:relative}
.dcards .chip:hover{border-color:var(--brass)}
.dcards .chip.main{background:var(--parchment-2)}
.dcards .chip.hole{border-color:var(--claret);border-style:dashed;background:var(--parchment-2)}
.dcards .chip .san{font-family:"IBM Plex Mono",monospace;font-weight:600;color:var(--ink);font-size:13.5px}
.dcards .chip .ev{float:right;font-family:"IBM Plex Mono",monospace;font-size:11px;color:var(--ink-3)}
.dcards .chip .cnm{font-size:11px;color:var(--brass);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.dcards .chip .sub{font-family:"IBM Plex Mono",monospace;font-size:10.5px;color:var(--ink-3);margin-top:3px}
.dcards .chip .track{height:3px;background:var(--line);margin-top:3px}
.dcards .chip .fill{height:3px;background:var(--brass)}
.dcards .chip.hole .fill{background:var(--claret)}
.dcards .chip .flag{display:inline-block;font-size:9.5px;letter-spacing:.08em;text-transform:uppercase;
  color:var(--on-brass);background:var(--claret);padding:1px 4px;margin-top:3px;font-weight:700}
.dcards .chip.blank .san,.dcards .chip.blank .cnm,.dcards .chip.blank .sub,
.dcards .chip.blank .ev,.dcards .chip.blank .flag,.dcards .chip.blank .track{visibility:hidden}
.dcards .chip.blank.hole{border-color:var(--line);border-style:solid}
.dcards .chip.blank{background:repeating-linear-gradient(45deg,var(--parchment),var(--parchment) 5px,var(--parchment-2) 5px,var(--parchment-2) 10px);cursor:default}
.dcards .chip.right{border-color:var(--brass);box-shadow:inset 0 0 0 1px var(--brass)}
.dcards .chip.wrong{border-color:var(--claret)}
.dcards .quiz{margin-top:8px;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
.dcards .quiz input{font-family:"IBM Plex Mono",monospace;font-size:13px;padding:5px 8px;
  border:1px solid var(--line);background:var(--card);color:var(--ink);width:120px}
.dcards .quiz input:focus{outline:none;border-color:var(--brass)}
.dcards .quiz .fb{font-size:12px;color:var(--ink-2)}
.dcards .quiz .fb.ok{color:var(--brass);font-weight:600}
.dcards .quiz .fb.no{color:var(--claret);font-weight:600}
.dcards .card.asking{border-left-color:var(--brass);box-shadow:0 0 0 1px var(--brass) inset}
.dcards .empty{color:var(--ink-3);font-size:13px;padding:18px 4px;line-height:1.6}
.dcards .empty b{color:var(--ink)}
.dcards button.mini-b{background:var(--card);border:1px solid var(--line);color:var(--ink);
  padding:4px 9px;cursor:pointer;font-size:11.5px}
.dcards button.mini-b:hover{border-color:var(--brass)}
`;

  var RT=window.RT;
  var pane=null, root=null, listEl=null, statEl=null;
  var showHolesOnly=false, quizOn=false;
  var openConns={};            // key -> true (expanded forced runs)
  var quiz={idx:0, score:0, asked:0, done:{}, typed:"", fb:null, fbCls:""};
  var lastSig="";

  function el(tag,cls,txt){ var e=document.createElement(tag); if(cls)e.className=cls;
    if(txt!==undefined&&txt!==null)e.textContent=txt; return e; }

  /* ---- model ---- */
  function decisions(){
    var out=[];
    (function walk(n){
      if(n.children.length>1) out.push(n);
      for(var i=0;i<n.children.length;i++) walk(n.children[i]);
    })(RT.root);
    out.sort(function(a,b){ return plyOf(a)-plyOf(b); });
    return out;
  }
  function plyOf(n){ var d=0; while(n.parent){ d++; n=n.parent; } return d; }

  function forcedRun(n){            // forced single-child moves immediately before n
    var run=[], p=n.parent;
    while(p && p.parent && p.children.length===1){ run.unshift(p); p=p.parent; }
    return run;
  }

  function pathText(n){
    var p=RT.pathTo(n), s="";
    for(var i=0;i<p.length;i++){
      var mn=RT.moveNumber(p[i]);
      if(mn.white) s+=mn.no+". ";
      else if(i===0) s+=mn.no+"... ";
      s+=p[i].san+" ";
    }
    return s.trim()||"start";
  }

  function isHole(c){
    var m=RT.metricsOf(c);
    return m.size<=2 && !(c.comment&&c.comment.trim());
  }

  function evalText(n){
    var e=RT.evalOf(n); if(!e) return "";
    if(e.mate!==undefined&&e.mate!==null) return "#"+e.mate;
    var v=e.cp/100;
    return (v>0?"+":"")+v.toFixed(2);
  }

  /* ---- SAN matching, case-insensitive + long algebraic ---- */
  function matchSAN(node, text){
    text=String(text||"").trim();
    if(!text) return null;
    var st=RT.stateOf(node);
    var m=RT.findSAN(st,text);
    if(m) return RT.moveSAN(st,m);
    var want=text.replace(/[!?]+/g,"").replace(/[+#]+$/,"").replace(/0/g,"o").toLowerCase();
    var mv=RT.legalMoves(st);
    for(var i=0;i<mv.length;i++){
      var san=RT.moveSAN(st,mv[i]);
      var norm=san.replace(/[+#]+$/,"").replace(/0/g,"o").toLowerCase();
      if(norm===want) return san;
      var la=(mv[i].from?RT.sqName(mv[i].from.r,mv[i].from.c)+RT.sqName(mv[i].to.r,mv[i].to.c):"")
             +(mv[i].promo?String(mv[i].promo).toLowerCase():"");
      if(la&&la===want.replace(/[-x]/g,"")) return san;
    }
    return null;
  }

  /* ---- render ---- */
  function signature(ds){
    var s=quizOn+"|"+showHolesOnly+"|"+quiz.idx+"|"+quiz.score+"|"+quiz.asked+"|"+(RT.cur&&RT.cur.id)+"|"+quiz.fb;
    for(var i=0;i<ds.length;i++){
      var n=ds[i]; s+="#"+n.id+":"+forcedRun(n).length+":"+(openConns[n.id]?1:0)+":"+(quiz.done[n.id]||"");
      for(var j=0;j<n.children.length;j++){
        var c=n.children[j];
        s+="/"+c.id+","+c.san+","+RT.metricsOf(c).size+","+RT.metricsOf(c).depth+","+
           (c.name||"")+","+(c.comment?1:0)+","+((c.shapes||[]).length)+","+evalText(c);
      }
    }
    return s;
  }

  function build(){
    var ds=decisions();
    var sig=signature(ds);
    if(sig===lastSig) return;
    lastSig=sig;

    /* header stats */
    var holeCards=0, holeCount=0;
    for(var i=0;i<ds.length;i++){
      var h=0;
      for(var j=0;j<ds[i].children.length;j++) if(isHole(ds[i].children[j])) h++;
      if(h){ holeCards++; holeCount+=h; }
    }
    statEl.textContent="";
    statEl.appendChild(el("span",null,ds.length+" decisions · "));
    var hb=el("b",null,holeCount+" holes");
    statEl.appendChild(hb);
    statEl.appendChild(el("span",null," in "+holeCards+" cards"));
    if(quizOn) statEl.appendChild(el("span",null," · score "+quiz.score+"/"+quiz.asked));

    listEl.textContent="";
    if(!ds.length){
      var e=el("div","empty");
      e.appendChild(el("b",null,"No branch points."));
      e.appendChild(el("div",null,"This study is a single line with nothing to decide — "
        +"add an alternative move anywhere and it will appear here as a card."));
      listEl.appendChild(e);
      return;
    }

    var shown=0;
    for(var k=0;k<ds.length;k++){
      var n=ds[k];
      var holes=[];
      for(var q=0;q<n.children.length;q++) if(isHole(n.children[q])) holes.push(q);
      if(showHolesOnly && !holes.length) continue;
      shown++;
      var run=forcedRun(n);
      if(run.length>=2) listEl.appendChild(connector(n,run));
      listEl.appendChild(card(n,holes,shown-1));
    }
    if(!shown){
      var e2=el("div","empty");
      e2.appendChild(el("b",null,"No holes. "));
      e2.appendChild(el("span",null,"Every prepared reply has depth behind it or a note attached."));
      listEl.appendChild(e2);
    }
  }

  function connector(n,run){
    var c=el("div","conn"+(openConns[n.id]?" open":""));
    c.appendChild(el("span","ln"));
    c.appendChild(el("span",null,"↓ "+run.length+" forced"));
    var mv=el("span","mvs");
    var t="";
    for(var i=0;i<run.length;i++){ var mn=RT.moveNumber(run[i]);
      t+=(mn.white?mn.no+". ":(i===0?mn.no+"... ":""))+run[i].san+" "; }
    mv.textContent=t.trim();
    c.appendChild(mv);
    c.onclick=function(){ openConns[n.id]=!openConns[n.id]; lastSig=""; build(); };
    return c;
  }

  function card(n,holes,ord){
    var wrap=el("div","card"+(RT.cur===n?" cur":"")+(holes.length?" hashole":""));
    var hidden = quizOn && !quiz.done[n.id];
    var asking = hidden && ord===quiz.idx;
    if(asking) wrap.classList.add("asking");

    var left=el("div","left");
    var hl=n.move?[n.move.from,n.move.to]:null;
    left.appendChild(RT.miniBoard(n.fen,98,{highlight:hl,flip:RT.flipped}));
    left.onclick=function(){ RT.goTo(n); };
    wrap.appendChild(left);

    var right=el("div","right");
    var hd=el("div","hd");
    var mn=n.parent?RT.moveNumber(n):null;
    hd.appendChild(el("span","no",(mn?(mn.white?mn.no+".":mn.no+"..."):"start")+"  "
      +n.children.length+" candidates"));
    if(n.name) hd.appendChild(el("span","nm",n.name));
    var marks="";
    if(n.comment&&n.comment.trim()) marks+="✎ note ";
    if((n.shapes||[]).length) marks+="➔ "+n.shapes.length+" shapes";
    if(marks) hd.appendChild(el("span","mk",marks));
    right.appendChild(hd);

    var pt=el("div","path",pathText(n));
    pt.title=pathText(n);
    pt.onclick=function(){ RT.goTo(n); };
    right.appendChild(pt);

    var chips=el("div","chips");
    var max=1;
    for(var i=0;i<n.children.length;i++) max=Math.max(max,RT.metricsOf(n.children[i]).size);
    for(var i=0;i<n.children.length;i++) chips.appendChild(chip(n,n.children[i],i,max,hidden));
    right.appendChild(chips);

    if(asking) right.appendChild(quizRow(n,ord));
    else if(quizOn && quiz.done[n.id]) right.appendChild(doneRow(n));

    wrap.appendChild(right);
    return wrap;
  }

  function chip(parent,c,idx,max,blank){
    var hole=isHole(c);
    var e=el("div","chip"+(idx===0?" main":"")+(hole?" hole":"")+(blank?" blank":""));
    var top=el("div");
    var ev=evalText(c); if(ev) top.appendChild(el("span","ev",ev));
    top.appendChild(el("span","san",c.san));
    e.appendChild(top);
    if(c.name) e.appendChild(el("div","cnm",c.name));
    var m=RT.metricsOf(c);
    var sub=el("div","sub",m.size+" plies · d"+m.depth+(c.comment&&c.comment.trim()?" ✎":""));
    e.appendChild(sub);
    var tr=el("div","track"), fi=el("div","fill");
    fi.style.width=Math.max(2,Math.round(100*m.size/max))+"%";
    tr.appendChild(fi); e.appendChild(tr);
    if(hole) e.appendChild(el("span","flag","hole"));
    if(!blank) e.onclick=function(ev2){ ev2.stopPropagation(); RT.goTo(c); };
    return e;
  }

  function doneRow(n){
    var r=el("div","quiz");
    var d=quiz.done[n.id];
    var f=el("span","fb "+(d==="ok"?"ok":"no"), d==="ok"?"✓ correct":"✗ missed");
    r.appendChild(f);
    return r;
  }

  function quizRow(n,ord){
    var r=el("div","quiz");
    var inp=document.createElement("input");
    inp.type="text"; inp.placeholder="your move…"; inp.value=quiz.typed;
    inp.setAttribute("data-quiz","1");
    inp.oninput=function(){ quiz.typed=inp.value; };
    inp.onkeydown=function(ev){
      if(ev.key==="Enter"){ ev.preventDefault(); submit(n,ord,inp.value); }
    };
    r.appendChild(inp);
    var go=el("button","mini-b","Check"); go.onclick=function(){ submit(n,ord,inp.value); };
    r.appendChild(go);
    var sk=el("button","mini-b","Skip"); sk.onclick=function(){ mark(n,ord,"no",
      "Skipped — "+sanList(n)); };
    r.appendChild(sk);
    if(quiz.fb) r.appendChild(el("span","fb "+quiz.fbCls,quiz.fb));
    setTimeout(function(){ try{ inp.focus(); inp.setSelectionRange(inp.value.length,inp.value.length);}catch(e){} },0);
    return r;
  }

  function sanList(n){
    var a=[]; for(var i=0;i<n.children.length;i++) a.push(n.children[i].san);
    return a.join(", ");
  }

  function submit(n,ord,text){
    var san=matchSAN(n,text);
    if(!san){ quiz.fb="\""+String(text).trim()+"\" is not a legal move here."; quiz.fbCls="no";
      lastSig=""; build(); return; }
    var ok=false;
    for(var i=0;i<n.children.length;i++)
      if(n.children[i].san.replace(/[+#]+$/,"")===san.replace(/[+#]+$/,"")) ok=true;
    mark(n,ord,ok?"ok":"no", ok?("✓ "+san):("✗ "+san+" — prepared: "+sanList(n)));
  }

  function mark(n,ord,res,msg){
    quiz.done[n.id]=res;
    quiz.asked++; if(res==="ok") quiz.score++;
    quiz.typed=""; quiz.fb=null; quiz.fbCls="";
    /* advance to next unanswered card */
    var ds=visibleDecisions();
    var next=ord+1;
    while(next<ds.length && quiz.done[ds[next].id]) next++;
    quiz.idx=next;
    RT.toast(msg);
    if(next>=ds.length) RT.toast("Quiz complete — "+quiz.score+"/"+quiz.asked);
    lastSig=""; build();
  }

  function visibleDecisions(){
    var ds=decisions();
    if(!showHolesOnly) return ds;
    return ds.filter(function(n){
      for(var i=0;i<n.children.length;i++) if(isHole(n.children[i])) return true;
      return false;
    });
  }

  RT.registerView({
    id:"cards", label:"Cards",
    mount:function(p){
      pane=p;
      var st=document.createElement("style"); st.textContent=CSS; document.head.appendChild(st);
      root=el("div","dcards"); p.appendChild(root);

      var bar=el("div","bar");
      statEl=el("span","stat"); bar.appendChild(statEl);
      bar.appendChild(el("span","sp"));

      var hb=el("button","tgl warn","Holes only");
      hb.onclick=function(){ showHolesOnly=!showHolesOnly;
        hb.classList.toggle("on",showHolesOnly); quiz.idx=0; lastSig=""; build(); };
      bar.appendChild(hb);

      var qb=el("button","tgl","Quiz");
      qb.onclick=function(){
        quizOn=!quizOn; qb.classList.toggle("on",quizOn);
        if(quizOn){ quiz={idx:0,score:0,asked:0,done:{},typed:"",fb:null,fbCls:""}; }
        lastSig=""; build();
      };
      bar.appendChild(qb);

      root.appendChild(bar);
      listEl=el("div","list"); root.appendChild(listEl);
    },
    update:function(){
      try{ build(); }catch(e){ console.error("cards:",e); }
    }
  });
})();
