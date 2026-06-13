/* ============================================================
   MANTIS site — shared interactions
   preloader · node-field bg · nav · reveal · 3D orbit hero · prompt bar
   Vanilla JS, no build step. Three.js lazy-loaded only for the 3D hero.
   ============================================================ */
(function(){
'use strict';
var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;

/* ---------- 1. PRELOADER (index only) ---------- */
(function preloader(){
  var intro = document.getElementById('intro');
  if (!intro) return;
  if (reduce){ intro.parentNode && intro.parentNode.removeChild(intro); return; }
  var canvas = document.getElementById('intro-canvas'), ctx = canvas.getContext('2d');
  var fill = document.getElementById('intro-bar-fill'), pctEl = document.getElementById('intro-pct');
  var logo = document.getElementById('intro-logo'), bar = intro.querySelector('.intro-bar');
  var dpr = Math.min(window.devicePixelRatio||1,2), W,H, nodes=[], raf, removed=false, ending=false;
  var mouse={x:innerWidth/2,y:innerHeight/2,has:false};
  function resize(){ W=canvas.width=innerWidth*dpr; H=canvas.height=innerHeight*dpr; }
  resize(); addEventListener('resize',resize);
  var N=Math.min(64,Math.round(innerWidth/24));
  for(var i=0;i<N;i++) nodes.push({x:Math.random()*innerWidth,y:Math.random()*innerHeight,
    vx:(Math.random()-.5)*.25,vy:(Math.random()-.5)*.25,br:Math.random()<.16,ph:Math.random()*6.28});
  addEventListener('mousemove',function(e){mouse.x=e.clientX;mouse.y=e.clientY;mouse.has=true;},{passive:true});
  var start=performance.now(), DUR=6500;
  function hyp(x,y){return Math.sqrt(x*x+y*y);}
  function loop(t){
    var prog=Math.min(1,(t-start)/DUR);
    if(fill) fill.style.width=(prog*100)+'%';
    if(pctEl) pctEl.textContent=Math.round(prog*100);
    var R=160,i,j;
    ctx.clearRect(0,0,W,H); ctx.save(); ctx.scale(dpr,dpr);
    for(i=0;i<nodes.length;i++){var n=nodes[i];n.ph+=.01;
      var dx=mouse.x-n.x,dy=mouse.y-n.y,d=hyp(dx,dy);
      if(mouse.has&&d<R*1.9&&d>0){var pull=.022*(1-d/(R*1.9));n.vx+=dx/d*pull;n.vy+=dy/d*pull;}
      n.vx*=.95;n.vy*=.95;n.x+=n.vx;n.y+=n.vy;
      if(n.x<0||n.x>innerWidth)n.vx*=-1; if(n.y<0||n.y>innerHeight)n.vy*=-1;}
    for(i=0;i<nodes.length;i++)for(j=i+1;j<nodes.length;j++){var a=nodes[i],b=nodes[j],dd=hyp(a.x-b.x,a.y-b.y);
      if(dd<R){var near=hyp(a.x-mouse.x,a.y-mouse.y)<R||hyp(b.x-mouse.x,b.y-mouse.y)<R;
        ctx.globalAlpha=(1-dd/R)*(near?.85:.32);ctx.strokeStyle=near?'#7AE99A':'#2f6b40';ctx.lineWidth=near?1.1:.6;
        ctx.beginPath();ctx.moveTo(a.x,a.y);var mx=(a.x+b.x)/2;ctx.bezierCurveTo(mx,a.y,mx,b.y,b.x,b.y);ctx.stroke();}}
    for(i=0;i<nodes.length;i++){var n=nodes[i];var near=hyp(n.x-mouse.x,n.y-mouse.y)<R*1.25;
      ctx.globalAlpha=near?1:.5;ctx.fillStyle=n.br?'#e8b84d':'#5CDB7A';
      var r=(near?2.6:1.5)*(.9+Math.sin(n.ph)*.12);
      ctx.beginPath();ctx.arc(n.x,n.y,r,0,6.2832);ctx.fill();}
    ctx.restore();
    if(prog>=1&&!ending){ending=true;finish();}
    raf=requestAnimationFrame(loop);
  }
  raf=requestAnimationFrame(loop);
  // The logo-build / MANTIS / subtitle reveals are pure-CSS auto-animations that run from
  // page mount (~3.4s total). The bar fills alongside; at 100% we fade it and sweep to the page.
  function finish(){ if(bar) setTimeout(function(){bar.classList.add('fade');},750); setTimeout(dissolve,1150); }
  function dissolve(){ if(removed||intro.classList.contains('sweep'))return; intro.classList.add('sweep'); setTimeout(cleanup,2050); }
  function cleanup(){ if(removed)return; removed=true; cancelAnimationFrame(raf); intro.parentNode&&intro.parentNode.removeChild(intro); }
  setTimeout(function(){ if(!ending){ending=true;dissolve();} },9500);
  setTimeout(cleanup,14000);
})();

/* ---------- 2. NODE-FIELD background (every page) ---------- */
(function nodeField(){
  var canvas=document.getElementById('field');
  if(!canvas||reduce) return;
  var ctx=canvas.getContext('2d'), dpr=Math.min(devicePixelRatio||1,2), W,H, nodes=[];
  var mouse={x:-999,y:-999};
  function resize(){W=canvas.width=innerWidth*dpr;H=canvas.height=innerHeight*dpr;}
  resize(); addEventListener('resize',resize);
  var N=Math.min(46,Math.round(innerWidth/34));
  for(var i=0;i<N;i++) nodes.push({x:Math.random()*innerWidth,y:Math.random()*innerHeight,
    vx:(Math.random()-.5)*.14,vy:(Math.random()-.5)*.14,br:Math.random()<.14});
  addEventListener('mousemove',function(e){mouse.x=e.clientX;mouse.y=e.clientY;},{passive:true});
  function hyp(x,y){return Math.sqrt(x*x+y*y);}
  function loop(){
    var R=150,i,j;
    ctx.clearRect(0,0,W,H);ctx.save();ctx.scale(dpr,dpr);
    for(i=0;i<nodes.length;i++){var n=nodes[i];
      var dx=mouse.x-n.x,dy=mouse.y-n.y,d=hyp(dx,dy);
      if(d<R&&d>0){var pull=.006*(1-d/R);n.vx+=dx/d*pull;n.vy+=dy/d*pull;}
      n.vx*=.985;n.vy*=.985;n.x+=n.vx;n.y+=n.vy;
      if(n.x<0||n.x>innerWidth)n.vx*=-1; if(n.y<0||n.y>innerHeight)n.vy*=-1;}
    for(i=0;i<nodes.length;i++)for(j=i+1;j<nodes.length;j++){var a=nodes[i],b=nodes[j],dd=hyp(a.x-b.x,a.y-b.y);
      if(dd<R){var near=hyp(a.x-mouse.x,a.y-mouse.y)<R*1.1;
        ctx.globalAlpha=(1-dd/R)*(near?.5:.18);ctx.strokeStyle=near?'#3a9b55':'#244d31';ctx.lineWidth=.6;
        ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke();}}
    for(i=0;i<nodes.length;i++){var n=nodes[i];
      ctx.globalAlpha=.6;ctx.fillStyle=n.br?'#e8b84d':'#3a9b55';
      ctx.beginPath();ctx.arc(n.x,n.y,1.4,0,6.2832);ctx.fill();}
    ctx.restore();
    requestAnimationFrame(loop);
  }
  loop();
})();

/* ---------- 3. NAV: scroll state + mobile burger ---------- */
(function nav(){
  var nav=document.querySelector('.nav');
  if(nav){ addEventListener('scroll',function(){ nav.classList.toggle('scrolled',scrollY>10); },{passive:true}); }
  var burger=document.querySelector('.nav-burger'), links=document.querySelector('.nav-links');
  if(burger&&links){ burger.addEventListener('click',function(){ links.classList.toggle('open'); }); }
})();

/* ---------- 4. REVEAL on scroll ---------- */
(function reveal(){
  var els=document.querySelectorAll('.reveal');
  if(!els.length) return;
  if(reduce||!('IntersectionObserver'in window)){ els.forEach(function(e){e.classList.add('in');}); return; }
  var io=new IntersectionObserver(function(ents){
    ents.forEach(function(e){ if(e.isIntersecting){ e.target.classList.add('in'); io.unobserve(e.target); } });
  },{threshold:.12});
  els.forEach(function(e){io.observe(e);});
})();

/* ---------- 5. COPY-TO-CLIPBOARD ---------- */
window.mantisCopy=function(btn,text){
  navigator.clipboard&&navigator.clipboard.writeText(text);
  var o=btn.textContent; btn.textContent='✓'; setTimeout(function(){btn.textContent=o;},1400);
};

/* ---------- 6. INTERACTIVE PROMPT BAR ---------- */
(function promptBar(){
  var input=document.getElementById('hero-prompt'), btn=document.getElementById('hero-prompt-btn'),
      out=document.getElementById('hero-prompt-out');
  if(!input||!btn||!out) return;
  // canned "what MANTIS would do" plans keyed by intent
  var PLANS={
    diagrid:['Plan: lofted tower surface → diagrid skin','Build: 11 native components, 2 groups','Inspect: 0 warnings, mesh closed','Wire: U/V density on sliders'],
    voronoi:['Plan: populate surface → Voronoi 3D cells','Build: 9 native components, 1 group','Inspect: 240 cells, 0 nulls','Wire: cell count on a slider'],
    tower:['Plan: floor stack → taper profile → loft','Build: 14 native components, 3 groups','Inspect: 24 floors, solid valid','Wire: twist + taper on sliders'],
    facade:['Plan: surface → panel grid → attractor','Build: 12 native components, 2 groups','Inspect: 320 panels, 0 warnings','Wire: attractor point draggable'],
    stair:['Plan: helix → tread array → railing sweep','Build: 13 native components, 2 groups','Inspect: 18 treads, rise checked','Wire: rise + radius on sliders'],
    _default:['Plan: decompose intent → stage the graph','Build: native components, grouped & labelled','Inspect: read back values, auto-heal warnings','Wire: key parameters on sliders']
  };
  function planFor(t){
    t=t.toLowerCase();
    for(var k in PLANS){ if(k!=='_default'&&t.indexOf(k)>=0) return PLANS[k]; }
    if(/stair|stipair|spiral/.test(t)) return PLANS.stair;
    if(/panel|skin|louver|brise/.test(t)) return PLANS.facade;
    return PLANS._default;
  }
  function run(){
    var t=(input.value||'').trim(); if(!t) t=input.placeholder.replace('…','');
    var steps=planFor(t);
    out.innerHTML='';
    steps.forEach(function(s,i){
      var div=document.createElement('div');
      div.className='po-step'+(i===steps.length-1?' verdict':'');
      div.style.animationDelay=(i*0.12)+'s';
      div.innerHTML=(i===steps.length-1?'<span class="ic">✓</span>':'<span class="ic">→</span>')+'<span>'+s+'</span>';
      out.appendChild(div);
    });
    out.classList.add('open');
  }
  btn.addEventListener('click',run);
  input.addEventListener('keydown',function(e){ if(e.key==='Enter') run(); });
})();

/* ---------- 7. 3D ORBIT HERO — parametric diagrid tower ---------- */
(function hero3D(){
  var canvasEl=document.getElementById('build-canvas'); if(!canvasEl) return;
  var container=canvasEl;
  var isMobile=innerWidth<700;
  // WebGL test
  var ok=false; try{ ok=!!(canvasEl.getContext('webgl')||canvasEl.getContext('experimental-webgl')); }catch(e){}
  if(!ok||reduce){ fallback(); return; }
  loadScript('https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js',function(err){
    if(err||typeof THREE==='undefined'){ fallback(); return; }
    setTimeout(build,40);
  });

  function fallback(){
    var c=canvasEl.getContext('2d'); if(!c) return;
    function fit(){ canvasEl.width=canvasEl.offsetWidth*2; canvasEl.height=canvasEl.offsetHeight*2; }
    fit(); addEventListener('resize',fit);
    var t=0;
    (function draw(){
      var W=canvasEl.width,H=canvasEl.height; c.clearRect(0,0,W,H); c.save(); c.translate(W/2,H/2);
      t+=.01; var floors=20,segs=8,r=W*0.16,h=H*0.62;
      for(var f=0;f<=floors;f++){ c.beginPath();
        for(var s=0;s<=segs;s++){ var a=s/segs*6.2832+f/floors*1.2+t, y=(f/floors-.5)*h, x=Math.cos(a)*r*Math.cos(t*.3), z=Math.sin(a)*r;
          if(s===0)c.moveTo(x,y-z*.18);else c.lineTo(x,y-z*.18); }
        c.strokeStyle=f===0||f===floors?'rgba(92,219,122,.7)':'rgba(58,155,85,.3)'; c.lineWidth=1; c.stroke(); }
      c.restore(); requestAnimationFrame(draw);
    })();
  }

  function build(){
    var scene=new THREE.Scene();
    var W=canvasEl.offsetWidth||560,H=canvasEl.offsetHeight||380;
    var cam=new THREE.PerspectiveCamera(42,W/H,.1,1000); cam.position.set(0,3,18);
    var rnd=new THREE.WebGLRenderer({canvas:canvasEl,antialias:!isMobile,alpha:true});
    rnd.setPixelRatio(Math.min(devicePixelRatio,isMobile?1:2)); rnd.setSize(W,H); rnd.setClearColor(0x000000,0);
    var p={twist:1.2,density:8,height:12,radius:2.2};
    var mGreen=new THREE.LineBasicMaterial({color:0x5CDB7A,transparent:true,opacity:.8});
    var mDim=new THREE.LineBasicMaterial({color:0x2d6b42,transparent:true,opacity:.35});
    var mAmber=new THREE.LineBasicMaterial({color:0xe8b84d,transparent:true,opacity:.5});
    var grp=new THREE.Group(); scene.add(grp);
    function rebuild(){
      while(grp.children.length){var c=grp.children[0];c.geometry&&c.geometry.dispose();grp.remove(c);}
      var floors=Math.max(4,Math.round(p.height*1.6)),segs=Math.max(4,Math.round(p.density)),r=p.radius,h=p.height,tw=p.twist,f,s;
      for(f=0;f<=floors;f++){var y=f/floors*h-h/2,ang=f/floors*tw,pts=[];
        for(s=0;s<=segs;s++){var a=s/segs*6.2832+ang;pts.push(new THREE.Vector3(Math.cos(a)*r,y,Math.sin(a)*r));}
        grp.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts),(f===0||f===floors)?mAmber:mDim));}
      for(s=0;s<segs;s++){
        for(f=0;f<floors;f++){var y0=f/floors*h-h/2,y1=(f+1)/floors*h-h/2;
          var a0=s/segs*6.2832+f/floors*tw,a1=(s+1)/segs*6.2832+(f+1)/floors*tw;
          grp.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(Math.cos(a0)*r,y0,Math.sin(a0)*r),new THREE.Vector3(Math.cos(a1)*r,y1,Math.sin(a1)*r)]),mGreen));
          var b0=(s+1)/segs*6.2832+f/floors*tw,b1=s/segs*6.2832+(f+1)/floors*tw;
          grp.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(Math.cos(b0)*r,y0,Math.sin(b0)*r),new THREE.Vector3(Math.cos(b1)*r,y1,Math.sin(b1)*r)]),mGreen));}
      }
    }
    rebuild();
    var orbit={active:false,px:0,py:0,rx:.3,ry:0,auto:true};
    canvasEl.addEventListener('mousedown',function(e){orbit.active=true;orbit.px=e.clientX;orbit.py=e.clientY;orbit.auto=false;});
    addEventListener('mouseup',function(){ if(orbit.active){orbit.active=false;clearTimeout(orbit._t);orbit._t=setTimeout(function(){orbit.auto=true;},3000);} });
    addEventListener('mousemove',function(e){ if(!orbit.active)return; orbit.ry+=(e.clientX-orbit.px)*.008; orbit.rx+=(e.clientY-orbit.py)*.008; orbit.px=e.clientX;orbit.py=e.clientY; orbit.rx=Math.max(-1.2,Math.min(1.2,orbit.rx)); });
    var t0=null;
    canvasEl.addEventListener('touchstart',function(e){if(e.touches.length===1){t0={x:e.touches[0].clientX,y:e.touches[0].clientY};orbit.auto=false;}},{passive:true});
    canvasEl.addEventListener('touchmove',function(e){if(!t0||e.touches.length!==1)return;orbit.ry+=(e.touches[0].clientX-t0.x)*.008;orbit.rx+=(e.touches[0].clientY-t0.y)*.008;t0={x:e.touches[0].clientX,y:e.touches[0].clientY};orbit.rx=Math.max(-1.2,Math.min(1.2,orbit.rx));},{passive:true});
    canvasEl.addEventListener('touchend',function(){t0=null;clearTimeout(orbit._t);orbit._t=setTimeout(function(){orbit.auto=true;},3000);},{passive:true});
    // sliders — drive geometry AND the live value readout
    bindSlider('sl-twist','val-twist',function(v){p.twist=v/100*3;rebuild();return p.twist.toFixed(1);});
    bindSlider('sl-density','val-density',function(v){p.density=4+Math.round(v/100*10);rebuild();return p.density;});
    bindSlider('sl-height','val-height',function(v){p.height=6+v/100*14;rebuild();return Math.round(p.height);});
    function bindSlider(id,outId,fn){var el=document.getElementById(id),out=document.getElementById(outId);
      function upd(){var r=fn(+el.value);if(out)out.textContent=r;}
      if(el){el.addEventListener('input',upd);upd();}}
    function onResize(){W=canvasEl.offsetWidth;H=canvasEl.offsetHeight;cam.aspect=W/H;cam.updateProjectionMatrix();rnd.setSize(W,H);}
    addEventListener('resize',onResize);
    var vis=true; new IntersectionObserver(function(es){es.forEach(function(e){vis=e.isIntersecting;});},{threshold:.01}).observe(canvasEl);
    var coordEl=document.getElementById('build-coord'),frame=0;
    (function animate(){ requestAnimationFrame(animate); if(!vis)return;
      if(orbit.auto) orbit.ry+=.0035;
      grp.rotation.y=orbit.ry; grp.rotation.x=orbit.rx;
      if(coordEl&&(frame++%8===0)){
        var rx=Math.round(orbit.rx*57.2958),ry=Math.round(orbit.ry*57.2958)%360; if(ry<0)ry+=360;
        coordEl.innerHTML='rot <b>x</b> '+rx+'° <b>y</b> '+ry+'°';
      }
      rnd.render(scene,cam); })();
  }

  function loadScript(src,cb){var s=document.createElement('script');s.src=src;s.onload=function(){cb(null);};s.onerror=function(){cb(true);};document.head.appendChild(s);}
})();

/* ---------- 8. THE ORB — ambient presence state machine ---------- */
(function orb(){
  var stage=document.getElementById('orb-stage'); if(!stage) return;
  var cap=document.getElementById('orb-caption'), live=document.getElementById('orb-live'),
      fixBtn=document.getElementById('fix-do');
  function set(cls,caption,amber){
    stage.classList.remove('is-aware','is-active','is-fixed');
    if(cls) stage.classList.add(cls);
    if(caption!=null) cap.textContent=caption;
    if(live) live.classList.toggle('amber',!!amber);
  }
  // reduced-motion: show the meaningful moment (a caught + offered fix), then rest.
  if(reduce){ set('is-active','',true); return; }

  var timers=[];
  function at(ms,fn){ timers.push(setTimeout(fn,ms)); }
  function cycle(){
    timers.forEach(clearTimeout); timers=[];
    set(null,'watching · all clear',false);
    at(2600, function(){ set('is-aware','something just went quiet…',true); });
    at(5200, function(){ set('is-active',null,true); });        // fix card slides up
    // auto-resolve if the visitor doesn't click Fix
    at(9000, function(){ if(stage.classList.contains('is-active')) resolve(); });
    at(15500, cycle);
  }
  function resolve(){
    timers.forEach(clearTimeout); timers=[];
    set('is-fixed','',false);
    at(4200, cycle);
  }
  if(fixBtn) fixBtn.addEventListener('click',resolve);
  // only run the cycle while the hero is on screen
  if('IntersectionObserver'in window){
    var io=new IntersectionObserver(function(es){
      es.forEach(function(e){ if(e.isIntersecting) cycle(); else { timers.forEach(clearTimeout); timers=[]; } });
    },{threshold:.3});
    io.observe(stage);
  } else cycle();
})();

/* ---------- 9. BUILD DEMO — type → think → tower ---------- */
(function buildDemo(){
  var chat=document.getElementById('build-chat'), typed=document.getElementById('btyped'),
      caret=document.getElementById('bcaret'), stage=document.querySelector('.build-stage'),
      thinkLabel=document.getElementById('bthink-label'), live=document.getElementById('build-live');
  if(!chat||!typed||!stage) return;
  var PROMPT='diagrid skin on a 24-storey twisting tower', ran=false;
  function reveal(){ stage.classList.add('show-tower'); if(live){live.innerHTML='<span class="live-dot"></span>LIVE';} }
  function done(){ if(caret) caret.style.display='none'; }

  if(reduce){ typed.textContent=PROMPT; done(); chat.classList.add('show-think','show-plan'); reveal(); return; }

  function run(){
    if(ran) return; ran=true;
    var i=0;
    (function type(){
      if(i<=PROMPT.length){ typed.textContent=PROMPT.slice(0,i++); setTimeout(type,46); return; }
      done();
      setTimeout(function(){ chat.classList.add('show-think'); }, 420);
      var labels=['reading the canvas…','staging the graph…','wiring 14 components…','reading the result back…'];
      var li=0, lt=setInterval(function(){ li++; if(li<labels.length&&thinkLabel){thinkLabel.textContent=labels[li];} else clearInterval(lt); },820);
      setTimeout(function(){ chat.classList.remove('show-think'); chat.classList.add('show-plan'); reveal(); }, 3600);
    })();
  }
  if('IntersectionObserver'in window){
    new IntersectionObserver(function(es,obs){
      es.forEach(function(e){ if(e.isIntersecting){ run(); obs.disconnect(); } });
    },{threshold:.35}).observe(chat);
  } else run();
})();

})();
