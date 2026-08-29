window.rulewrightFlowBuilder = (function(){
  "use strict";

  /* ============================================================
     Constants & node type definitions
     ============================================================ */
  const NODE_W = 220;
  const HEADER_H = 34;
  const PORT_ROW_H = 26;
  const BODY_EXTRA_H = 46;

  // Auto-layout geometry (used by layoutAll / importDocument / Tidy).
  const COL_W = 280;      // horizontal gap between tree columns
  const ROW_H = 140;      // vertical slot per leaf / action
  const BAND_GAP = 90;    // vertical gap between two rules' bands
  const FACT_X = 40;      // the shared Fact Input column
  const GRID = 12;        // snap-to-grid step (world units)

  // portLabels: fixed, named input ports (order matters — build* reads specific indices by
  // meaning). dynamicInput + dynamicLabel: AND/OR/NOT groups and Expression nodes grow an extra
  // empty slot as their existing slots fill up (see addConnection/removeConnection).
  const TYPE_DEFS = {
    trigger:   { label:"Fact Input",      badge:"▶",   color:"var(--accent-teal)",   tagPrefix:"TRG", hasInput:false, isAction:false, noOutput:true },
    rule:      { label:"Rule",            badge:"§",   color:"var(--accent-gold)",   tagPrefix:"RULE",hasInput:true,  isAction:false, isRule:true, portLabels:["Condition"] },
    leaf:      { label:"Compare",         badge:"=",   color:"var(--accent-blue)",   tagPrefix:"CMP", hasInput:true,  isAction:false, portLabels:["Field (expr)"] },
    function:  { label:"Custom Function", badge:"ƒ",   color:"var(--accent-blue)",   tagPrefix:"FN",  hasInput:false, isAction:false },
    and:       { label:"AND Group",       badge:"∧",   color:"var(--accent-copper)", tagPrefix:"GRP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Input", operator:"AND" },
    or:        { label:"OR Group",        badge:"∨",   color:"var(--accent-copper)", tagPrefix:"GRP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Input", operator:"OR" },
    not:       { label:"NOT Group",       badge:"¬",   color:"var(--accent-copper)", tagPrefix:"GRP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Input", operator:"NOT", maxInputs:1 },
    action:    { label:"Action",          badge:"▣",   color:"var(--accent-green)",  tagPrefix:"ACT", hasInput:true,  isAction:true,  portLabels:["Rule","Value (expr)"] },
    valLiteral:{ label:"Literal",         badge:"\"…\"",color:"var(--accent-purple)", tagPrefix:"LIT", hasInput:false, isAction:false, isValue:true },
    valField:  { label:"Field Ref",       badge:"{f}", color:"var(--accent-purple)", tagPrefix:"FLD", hasInput:false, isAction:false, isValue:true },
    valOp:     { label:"Expression",      badge:"ƒx",  color:"var(--accent-purple)", tagPrefix:"EXP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Operand", isValue:true }
  };

  const OPERATORS = [
    "Equals","NotEquals","GreaterThan","GreaterThanOrEqual","LessThan","LessThanOrEqual",
    "Contains","StartsWith","EndsWith","MatchesRegex","In","NotIn","IsNull","IsNotNull"
  ];
  const ACTION_TYPES = ["setOutput","addToOutput","appendToOutput","removeOutput"];
  const EXPR_OPERATORS = ["add","subtract","multiply","divide","modulo","negate","concat","coalesce"];
  const EXPR_ARITY = { negate:{min:1,max:1}, subtract:{min:2,max:2}, divide:{min:2,max:2}, modulo:{min:2,max:2} };

  /* ============================================================
     State
     ============================================================ */
  const state = {
    nodes: new Map(),
    connections: new Map(),
    nextNodeId: 1,
    nextConnId: 1,
    typeCounters: {},
    scale: 1,
    panX: 60,
    panY: 60,
    selectedNode: null,
    selectedConn: null,
    dragNode: null,
    panDrag: null,
    pendingWire: null,
    sampleFact: {
      Customer: { Age: 34, IsVip: true },
      Order: { Total: 120 }
    }
  };

  let dotNetRef = null;
  let canvasWrap, world, wiresSvg, toastEl, resultBanner;

  /* ============================================================
     Utility
     ============================================================ */
  function clamp(v,min,max){ return Math.max(min, Math.min(max, v)); }
  function snap(v){ return Math.round(v/GRID)*GRID; }

  function showToast(msg, tone){
    toastEl.textContent = msg;
    toastEl.classList.remove('success', 'error');
    if(tone === 'success') toastEl.classList.add('success');
    if(tone === 'error') toastEl.classList.add('error');
    toastEl.classList.add('show');
    clearTimeout(showToast._t);
    showToast._t = setTimeout(()=>toastEl.classList.remove('show', 'success', 'error'), 3200);
  }

  function screenToWorld(clientX, clientY){
    const rect = canvasWrap.getBoundingClientRect();
    const sx = clientX - rect.left;
    const sy = clientY - rect.top;
    return { x: (sx - state.panX)/state.scale, y: (sy - state.panY)/state.scale };
  }

  function applyWorldTransform(){
    world.style.transform = `translate(${state.panX}px, ${state.panY}px) scale(${state.scale})`;
    document.getElementById('zoomLabel').textContent = Math.round(state.scale*100) + "%";
  }

  // Frame every node in the viewport. Essential after import/tidy: condition trees lay out to the
  // LEFT of their rule (x − COL_W per level), so deep trees run into negative X.
  function fitToView(){
    if(state.nodes.size === 0){ state.scale = 1; state.panX = 60; state.panY = 60; applyWorldTransform(); return; }
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    state.nodes.forEach(n=>{
      minX = Math.min(minX, n.x); minY = Math.min(minY, n.y);
      maxX = Math.max(maxX, n.x + NODE_W); maxY = Math.max(maxY, n.y + nodeHeight(n));
    });
    const pad = 70;
    const w = canvasWrap.clientWidth, h = canvasWrap.clientHeight;
    const gw = (maxX - minX) + pad*2, gh = (maxY - minY) + pad*2;
    state.scale = clamp(Math.min(w/gw, h/gh), 0.3, 1.25);
    const cx = (minX + maxX)/2, cy = (minY + maxY)/2;
    state.panX = w/2 - cx*state.scale;
    state.panY = h/2 - cy*state.scale;
    applyWorldTransform();
  }

  function focusNode(id){
    const n = state.nodes.get(id);
    if(!n) return;
    const w = canvasWrap.clientWidth, h = canvasWrap.clientHeight;
    const cx = n.x + NODE_W/2, cy = n.y + nodeHeight(n)/2;
    state.panX = w/2 - cx*state.scale;
    state.panY = h/2 - cy*state.scale;
    applyWorldTransform();
  }

  function escapeHtml(s){
    return String(s).replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  /* ============================================================
     Node model
     ============================================================ */
  function nextTag(type){
    const def = TYPE_DEFS[type];
    const key = def.tagPrefix;
    state.typeCounters[key] = (state.typeCounters[key]||0) + 1;
    return key + "-" + String(state.typeCounters[key]).padStart(2,"0");
  }

  function uniqueRuleId(){
    const existing = new Set([...state.nodes.values()].filter(n=>n.type==='rule' && n.config.id).map(n=>n.config.id));
    let i = 1, id;
    do { id = 'rule-' + i++; } while(existing.has(id));
    return id;
  }

  function createNode(type, x, y){
    const def = TYPE_DEFS[type];
    const id = "n" + (state.nextNodeId++);
    let inputCount;
    if(def.portLabels) inputCount = def.portLabels.length;
    else if(def.dynamicInput) inputCount = 1;
    else inputCount = def.hasInput ? 1 : 0;

    const node = {
      id, type, x, y,
      tag: nextTag(type),
      inputs: new Array(inputCount).fill(null),
      config: defaultConfig(type),
      el: null,
      _test: undefined,
      _fired: null,
      _skipped: false
    };
    if(type === 'rule' && !node.config.id) node.config.id = uniqueRuleId();
    state.nodes.set(id, node);
    renderNode(node);
    return node;
  }

  function defaultConfig(type){
    if(type === 'rule') return { id:"", description:"", priority:0, enabled:true };
    if(type === 'leaf') return { field:"", operator:"GreaterThan", value:"" };
    if(type === 'function') return { name:"", field:"", value:"" };
    if(type === 'action') return { type:"setOutput", branch:"then", target:"", value:"" };
    if(type === 'valLiteral') return { value:"null" };
    if(type === 'valField') return { field:"" };
    if(type === 'valOp') return { operator:"add" };
    return {};
  }

  function nodeHeight(node){
    const def = TYPE_DEFS[node.type];
    if(!def.hasInput) return HEADER_H + BODY_EXTRA_H;
    const isSimpleBody = def.dynamicInput; // groups & expression ops: minimal body text
    const bodyH = isSimpleBody ? 10 : BODY_EXTRA_H - 14;
    return HEADER_H + node.inputs.length * PORT_ROW_H + bodyH;
  }

  function inputPortY(node, idx){
    return HEADER_H + idx*PORT_ROW_H + PORT_ROW_H/2;
  }
  function outputPortY(node){
    return nodeHeight(node)/2;
  }

  /* ============================================================
     Rendering: nodes
     ============================================================ */
  function renderNode(node){
    const def = TYPE_DEFS[node.type];
    let el = node.el;
    if(!el){
      el = document.createElement('div');
      el.className = 'node';
      el.dataset.id = node.id;
      el.style.setProperty('--node-color', def.color);
      world.appendChild(el);
      node.el = el;
      el.addEventListener('mousedown', onNodeMouseDown);
      el.addEventListener('click', (e)=>{ e.stopPropagation(); selectNode(node.id); });
    }
    el.className = 'node' + (def.isRule ? ' node-rule' : '');
    el.style.transform = `translate(${node.x}px, ${node.y}px)`;
    el.style.height = nodeHeight(node) + 'px';
    el.classList.toggle('selected', state.selectedNode === node.id);
    el.classList.toggle('pass', node._test === true);
    el.classList.toggle('fail', node._test === false);

    let html = `<div class="node-header">
      <div class="node-badge">${def.badge}</div>
      <div class="node-title">${def.label}</div>
      <div class="node-tag">${node.tag}</div>
      <button class="node-del" title="Delete node">×</button>
    </div>`;

    html += `<div class="node-body">${renderNodeBody(node)}</div>`;

    if(def.hasInput){
      html += `<div class="ports-in">`;
      node.inputs.forEach((connId, idx)=>{
        const label = def.portLabels ? (def.portLabels[idx]||`Port ${idx+1}`) : `${def.dynamicLabel||'Input'} ${idx+1}`;
        html += `<div class="port-row" style="height:${PORT_ROW_H}px;">
          <span class="plabel">${escapeHtml(label)}</span>
          <div class="port port-in" data-node="${node.id}" data-idx="${idx}" style="top:${inputPortY(node,idx)}px;"></div>
        </div>`;
      });
      html += `</div>`;
    }
    if(!def.isAction && !def.noOutput){
      html += `<div class="port port-out" data-node="${node.id}" style="top:${outputPortY(node)}px;"></div>`;
    }

    el.innerHTML = html;
    el.style.setProperty('--node-color', def.color);

    el.querySelector('.node-del').addEventListener('click', (e)=>{
      e.stopPropagation();
      removeNode(node.id);
    });
    if(!def.isAction && !def.noOutput){
      const outPort = el.querySelector('.port-out');
      outPort.addEventListener('mousedown', (e)=>{ e.stopPropagation(); startWireDrag(node.id, e); });
    }
    el.querySelectorAll('.port-in').forEach(p=>{
      p.addEventListener('mouseup', (e)=>{ e.stopPropagation(); completeWireDrag(node.id, parseInt(p.dataset.idx,10)); });
    });
  }

  function renderNodeBody(node){
    const t = node.type;
    if(t === 'trigger'){
      const paths = collectFactPaths(state.sampleFact);
      return paths.slice(0,4).map(p=>`<span class="pill">${escapeHtml(p)}</span>`).join(' ') || `<span class="placeholder">Sample fact</span>`;
    }
    if(t === 'rule'){
      const id = node.config.id || '(unset id)';
      const bits = [`P${Number(node.config.priority)||0}`];
      if(node.config.enabled === false) bits.push('disabled');
      const badge = node._fired ? `<span class="rule-fired ${node._fired}">${node._fired}</span>` : '';
      return `<div class="rule-summary"><span class="rule-id">${escapeHtml(id)}</span><span class="rule-meta">${bits.join(' · ')}</span>${badge}</div>`;
    }
    if(t === 'leaf'){
      const opSym = { GreaterThan:'>',GreaterThanOrEqual:'≥',LessThan:'<',LessThanOrEqual:'≤',Equals:'=',NotEquals:'≠' }[node.config.operator] || node.config.operator;
      const lhs = node.inputs[0] ? '(computed)' : (node.config.field || '(unset)');
      if(['IsNull','IsNotNull'].includes(node.config.operator)){
        return `<span class="summary">${escapeHtml(lhs)} ${escapeHtml(opSym)}</span>`;
      }
      return `<span class="summary">${escapeHtml(lhs)} ${escapeHtml(opSym)} ${escapeHtml(String(node.config.value))}</span>`;
    }
    if(t === 'function'){
      if(!node.config.name) return `<span class="placeholder">Click to configure…</span>`;
      const field = node.config.field ? escapeHtml(node.config.field) : 'fact';
      return `<span class="summary">ƒ ${escapeHtml(node.config.name)}(${field})</span>`;
    }
    if(t === 'action'){
      if(!node.config.target) return `<span class="placeholder">Click to configure…</span>`;
      const branchTag = node.config.branch === 'else' ? 'ELSE' : 'THEN';
      if(node.config.type === 'removeOutput'){
        return `<span class="summary">[${branchTag}] remove ${escapeHtml(node.config.target)}</span>`;
      }
      const valueText = node.inputs[1] ? '(computed)' : String(node.config.value);
      return `<span class="summary">[${branchTag}] ${escapeHtml(node.config.type)} ${escapeHtml(node.config.target)} = ${escapeHtml(valueText)}</span>`;
    }
    if(t === 'valLiteral'){
      return `<span class="summary">${escapeHtml(node.config.value)}</span>`;
    }
    if(t === 'valField'){
      return node.config.field ? `<span class="summary">{${escapeHtml(node.config.field)}}</span>` : `<span class="placeholder">Click to configure…</span>`;
    }
    if(t === 'valOp'){
      return `<span class="summary">${escapeHtml(node.config.operator)}(…)</span>`;
    }
    return `<span class="placeholder" style="font-size:9.5px;">Combines connected inputs</span>`;
  }

  function collectFactPaths(obj, prefix){
    prefix = prefix || "";
    let out = [];
    for(const k in obj){
      const p = prefix ? prefix+"."+k : k;
      if(obj[k] && typeof obj[k] === 'object' && !Array.isArray(obj[k])){
        out = out.concat(collectFactPaths(obj[k], p));
      } else {
        out.push(p);
      }
    }
    return out;
  }

  function removeNode(id){
    const node = state.nodes.get(id);
    if(!node) return;
    [...state.connections.values()].forEach(c=>{
      if(c.from === id || c.to === id) removeConnection(c.id, true);
    });
    if(node.el) node.el.remove();
    state.nodes.delete(id);
    if(state.selectedNode === id){ state.selectedNode = null; renderInspector(); }
    renderWires();
    regenerateJson();
  }

  function clearGraph(){
    state.nodes.forEach(n=>{ if(n.el) n.el.remove(); });
    state.nodes.clear();
    state.connections.clear();
    state.nextNodeId = 1;
    state.nextConnId = 1;
    state.typeCounters = {};
    state.selectedNode = null;
    state.selectedConn = null;
    renderInspector();
  }

  /* ============================================================
     Connections
     ============================================================ */
  // What a node emits from its output port, and what each input port expects — so a wire that
  // couldn't ever produce valid JSON is rejected up front with a clear message instead of being
  // silently dropped at build time.
  function nodeOutputKind(node){
    if(!node) return null;
    const t = node.type;
    if(t === 'rule') return 'rule';
    if(t === 'valLiteral' || t === 'valField' || t === 'valOp') return 'value';
    if(t === 'leaf' || t === 'function' || t === 'and' || t === 'or' || t === 'not') return 'condition';
    return null; // trigger — reference-only, has no output port
  }
  function inputPortKind(node, idx){
    const t = node.type;
    if(t === 'rule') return 'condition';                // the Condition pin
    if(t === 'leaf') return 'value';                    // the Field (expr) pin
    if(t === 'action') return idx === 0 ? 'rule' : 'value';
    if(t === 'and' || t === 'or' || t === 'not') return 'condition';
    if(t === 'valOp') return 'value';                   // operand pins
    return null;
  }
  const KIND_LABEL = {
    condition: "a condition (Compare, Custom Function, or a logic group)",
    value: "a computed value (Literal, Field Ref, or Expression)",
    rule: "a Rule node"
  };

  function isDescendant(candidateAncestorId, nodeId, seen){
    seen = seen || new Set();
    if(seen.has(nodeId)) return false;
    seen.add(nodeId);
    const node = state.nodes.get(nodeId);
    if(!node) return false;
    for(const connId of node.inputs){
      if(!connId) continue;
      const conn = state.connections.get(connId);
      if(!conn) continue;
      if(conn.from === candidateAncestorId) return true;
      if(isDescendant(candidateAncestorId, conn.from, seen)) return true;
    }
    return false;
  }

  function addConnection(fromId, toId, toIdx){
    if(fromId === toId){ showToast("A node can't connect to itself.", 'error'); return null; }
    const toNode = state.nodes.get(toId);
    if(!toNode) return null;
    if(toNode.inputs[toIdx]){ showToast("That input is already connected.", 'error'); return null; }
    if(isDescendant(toId, fromId)){ showToast("That connection would create a loop.", 'error'); return null; }

    const fromKind = nodeOutputKind(state.nodes.get(fromId));
    const expected = inputPortKind(toNode, toIdx);
    if(fromKind && expected && fromKind !== expected){
      showToast(`That pin expects ${KIND_LABEL[expected]} — not ${KIND_LABEL[fromKind]}.`, 'error');
      return null;
    }

    const id = "c" + (state.nextConnId++);
    const conn = { id, from: fromId, to: toId, toIdx };
    state.connections.set(id, conn);
    toNode.inputs[toIdx] = id;

    const def = TYPE_DEFS[toNode.type];
    if(def.dynamicInput && (!def.maxInputs || toNode.inputs.length < def.maxInputs)){
      toNode.inputs.push(null);
    }
    renderNode(toNode);
    renderWires();
    regenerateJson();
    return conn;
  }

  function removeConnection(id, skipRerender){
    const conn = state.connections.get(id);
    if(!conn) return;
    const toNode = state.nodes.get(conn.to);
    if(toNode){
      const idx = toNode.inputs.indexOf(id);
      if(idx > -1) toNode.inputs[idx] = null;
      const def = TYPE_DEFS[toNode.type];
      if(def.dynamicInput){
        while(toNode.inputs.length > 1 &&
              toNode.inputs[toNode.inputs.length-1] === null &&
              toNode.inputs[toNode.inputs.length-2] === null){
          toNode.inputs.pop();
        }
      }
      if(!skipRerender) renderNode(toNode);
    }
    state.connections.delete(id);
    if(state.selectedConn === id) state.selectedConn = null;
    if(!skipRerender){ renderWires(); regenerateJson(); }
  }

  /* ============================================================
     Wire geometry & rendering
     ============================================================ */
  function portWorldPos(nodeId, isOutput, idx){
    const node = state.nodes.get(nodeId);
    if(!node) return {x:0,y:0};
    if(isOutput) return { x: node.x + NODE_W, y: node.y + outputPortY(node) };
    return { x: node.x, y: node.y + inputPortY(node, idx) };
  }

  // An orthogonal (right-angle) route with softly rounded corners — the Azure-Logic-Apps look.
  function orthPoints(x1,y1,x2,y2){
    if(x2 - x1 > 60){
      const mid = (x1 + x2)/2;
      return [[x1,y1],[mid,y1],[mid,y2],[x2,y2]];
    }
    // target is left of / near the source: loop out and back
    const outX = x1 + 40, inX = x2 - 40, midY = (y1 + y2)/2;
    return [[x1,y1],[outX,y1],[outX,midY],[inX,midY],[inX,y2],[x2,y2]];
  }
  function roundedPath(pts, r){
    if(pts.length < 3) return `M ${pts.map(p=>p.join(' ')).join(' L ')}`;
    let d = `M ${pts[0][0]} ${pts[0][1]}`;
    for(let i=1;i<pts.length-1;i++){
      const p0=pts[i-1], p1=pts[i], p2=pts[i+1];
      const l1=Math.hypot(p1[0]-p0[0],p1[1]-p0[1]) || 1;
      const l2=Math.hypot(p2[0]-p1[0],p2[1]-p1[1]) || 1;
      const d1=Math.min(r, l1/2), d2=Math.min(r, l2/2);
      const a=[p1[0]+(p0[0]-p1[0])/l1*d1, p1[1]+(p0[1]-p1[1])/l1*d1];
      const b=[p1[0]+(p2[0]-p1[0])/l2*d2, p1[1]+(p2[1]-p1[1])/l2*d2];
      d += ` L ${a[0]} ${a[1]} Q ${p1[0]} ${p1[1]} ${b[0]} ${b[1]}`;
    }
    const last = pts[pts.length-1];
    d += ` L ${last[0]} ${last[1]}`;
    return d;
  }

  function renderWires(){
    let svgContent = '';
    state.connections.forEach(conn=>{
      const from = portWorldPos(conn.from, true, 0);
      const to = portWorldPos(conn.to, false, conn.toIdx);
      const pts = orthPoints(from.x, from.y, to.x, to.y);
      const d = roundedPath(pts, 12);
      const toNode = state.nodes.get(conn.to);
      const fromNode = state.nodes.get(conn.from);
      let cls = 'wire-path';
      if(state.selectedConn === conn.id) cls += ' selected';
      if(fromNode && fromNode._test === true && toNode && toNode._test !== false) cls += ' pass';
      else if(fromNode && fromNode._test === false) cls += ' fail';

      svgContent += `<path class="${cls}" d="${d}"></path>`;
      svgContent += `<path d="${d}" fill="none" stroke="transparent" stroke-width="14" style="pointer-events:all;cursor:pointer;" data-conn-hit="${conn.id}"></path>`;
      const midX = (from.x + to.x)/2, midY = (from.y + to.y)/2;
      svgContent += `<g class="wire-del-group" data-conn-del="${conn.id}" style="pointer-events:all;cursor:pointer;">
        <circle class="wire-del" cx="${midX}" cy="${midY}" r="7"></circle>
        <path class="wire-del-x" d="M ${midX-3} ${midY-3} L ${midX+3} ${midY+3} M ${midX+3} ${midY-3} L ${midX-3} ${midY+3}"></path>
      </g>`;
    });

    if(state.pendingWire){
      const from = portWorldPos(state.pendingWire.fromNode, true, 0);
      const pts = orthPoints(from.x, from.y, state.pendingWire.x, state.pendingWire.y);
      svgContent += `<path class="wire-path hot" stroke-dasharray="5 3" d="${roundedPath(pts,12)}"></path>`;
    }

    wiresSvg.innerHTML = svgContent;

    let maxX = 400, maxY = 400;
    state.nodes.forEach(n=>{ maxX = Math.max(maxX, n.x + NODE_W + 200); maxY = Math.max(maxY, n.y + nodeHeight(n) + 200); });
    let minX = 0, minY = 0;
    state.nodes.forEach(n=>{ minX = Math.min(minX, n.x - 200); minY = Math.min(minY, n.y - 200); });
    wiresSvg.setAttribute('width', maxX);
    wiresSvg.setAttribute('height', maxY);
    world.style.width = maxX + 'px';
    world.style.height = maxY + 'px';

    wiresSvg.querySelectorAll('[data-conn-hit]').forEach(p=>{
      p.addEventListener('click', (e)=>{ e.stopPropagation(); selectConnection(p.dataset.connHit); });
    });
    wiresSvg.querySelectorAll('[data-conn-del]').forEach(g=>{
      g.addEventListener('click', (e)=>{ e.stopPropagation(); removeConnection(g.dataset.connDel); });
    });
  }

  /* ============================================================
     Selection & Inspector
     ============================================================ */
  function selectNode(id){
    state.selectedNode = id;
    state.selectedConn = null;
    state.nodes.forEach(n=>renderNode(n));
    renderWires();
    renderInspector();
  }
  function selectConnection(id){
    state.selectedConn = id;
    state.selectedNode = null;
    state.nodes.forEach(n=>renderNode(n));
    renderWires();
    renderInspector();
  }
  function clearSelection(){
    state.selectedNode = null;
    state.selectedConn = null;
    state.nodes.forEach(n=>renderNode(n));
    renderWires();
    renderInspector();
  }

  function renderInspector(){
    const body = document.getElementById('nodeInspectorBody');
    if(!state.selectedNode){
      if(state.selectedConn){
        body.innerHTML = `<div class="insp-empty"><span class="insp-empty-icon">⟶</span>Wire selected. Press <strong>Delete</strong> or click the × on the wire to remove it.</div>`;
      } else {
        body.innerHTML = `<div class="insp-empty"><span class="insp-empty-icon">◇</span>Select a node to edit its properties.</div>`;
      }
      return;
    }
    const node = state.nodes.get(state.selectedNode);
    if(!node){ body.innerHTML = ''; return; }
    const def = TYPE_DEFS[node.type];
    let html = `<div style="margin-bottom:12px;"><span class="insp-node-type" style="--node-color:${def.color};color:${def.color}">${node.tag} · ${def.label}</span></div>`;

    if(node.type === 'trigger'){
      html += `<div class="field"><label>Sample fact (used for Test rule)</label>
        <textarea id="factEditor" style="min-height:130px;">${escapeHtml(JSON.stringify(state.sampleFact,null,2))}</textarea></div>
        <div class="insp-note">Paths available: ${collectFactPaths(state.sampleFact).map(p=>'<code>'+p+'</code>').join(', ')}</div>`;
    } else if(node.type === 'rule'){
      html += `<div class="field"><label>Rule ID</label><input type="text" id="ruleCfgId" placeholder="vip-discount" value="${escapeHtml(node.config.id)}"></div>
        <div class="field"><label>Description</label><input type="text" id="ruleCfgDesc" placeholder="VIP customers get 10% off" value="${escapeHtml(node.config.description)}"></div>
        <div class="field-row">
          <div class="field"><label>Priority</label><input type="number" id="ruleCfgPriority" value="${Number(node.config.priority)||0}"></div>
          <div class="field" style="flex:0 0 auto;padding-top:22px;"><div class="checkbox-row"><input type="checkbox" id="ruleCfgEnabled" ${node.config.enabled!==false?'checked':''}> Enabled</div></div>
        </div>
        <div class="insp-note">Wire a condition into the <strong>Condition</strong> pin, and one or more Action nodes to this rule's output. Higher priority evaluates first and wins output collisions.</div>`;
    } else if(node.type === 'leaf'){
      const fieldExprWired = !!node.inputs[0];
      html += `<div class="field"><label>Field path</label>
        <input type="text" id="cfgField" placeholder="Customer.Age" value="${escapeHtml(node.config.field)}" ${fieldExprWired?'disabled':''}></div>`;
      if(fieldExprWired){
        html += `<div class="insp-note">Using the wired <strong>Field (expr)</strong> computed value instead — disconnect that pin to type a plain field path.</div>`;
      }
      html += `<div class="field"><label>Operator</label><select id="cfgOperator">${OPERATORS.map(o=>`<option value="${o}" ${o===node.config.operator?'selected':''}>${o}</option>`).join('')}</select></div>
        <div class="field" id="valueFieldWrap" style="${['IsNull','IsNotNull'].includes(node.config.operator)?'display:none;':''}"><label>Value (constant)</label><input type="text" id="cfgValue" placeholder='18 or true or ["a","b"]' value="${escapeHtml(node.config.value)}"></div>
        <div class="insp-note">A condition's comparison value must be a constant — only its left-hand side can be a computed expression (wire into the Field (expr) pin).</div>`;
    } else if(node.type === 'function'){
      html += `<div class="field"><label>Function name</label><input type="text" id="cfgName" placeholder="IsBusinessDay" value="${escapeHtml(node.config.name)}"></div>
        <div class="field"><label>Field path (optional)</label><input type="text" id="cfgField" placeholder="Customer.Email" value="${escapeHtml(node.config.field)}"></div>
        <div class="field"><label>Value (optional, constant)</label><input type="text" id="cfgValue" placeholder="[10, 20]" value="${escapeHtml(node.config.value)}"></div>
        <div class="insp-note">Registered via <code>IRuleFunction</code> on the host application. The built-in Rulewright.Extensions.Functions catalog is registered for Test rule.</div>`;
    } else if(node.type === 'action'){
      const valueWired = !!node.inputs[1];
      html += `<div class="field"><label>Action type</label><select id="cfgType">${ACTION_TYPES.map(t=>`<option value="${t}" ${t===node.config.type?'selected':''}>${t}</option>`).join('')}</select></div>
        <div class="field"><label>Branch</label><select id="cfgBranch">
          <option value="then" ${node.config.branch!=='else'?'selected':''}>Then (condition is true)</option>
          <option value="else" ${node.config.branch==='else'?'selected':''}>Else (condition is false)</option>
        </select></div>
        <div class="field"><label>Output target</label><input type="text" id="cfgTarget" placeholder="Discount" value="${escapeHtml(node.config.target)}"></div>`;
      if(node.config.type !== 'removeOutput'){
        html += `<div class="field" id="valueFieldWrap"><label>Value (constant)</label><input type="text" id="cfgValue" placeholder="10" value="${escapeHtml(node.config.value)}" ${valueWired?'disabled':''}></div>`;
        if(valueWired){
          html += `<div class="insp-note">Using the wired <strong>Value (expr)</strong> computed value instead — disconnect that pin to type a constant.</div>`;
        }
      } else {
        html += `<div class="insp-note">removeOutput takes no value — it just deletes the target key.</div>`;
      }
      html += `<div class="insp-note">Wire this action's <strong>Rule</strong> pin to a Rule node to attach it.</div>`;
    } else if(node.type === 'valLiteral'){
      html += `<div class="field"><label>Value (JSON)</label><input type="text" id="cfgValue" placeholder='10 or "gold" or true' value="${escapeHtml(node.config.value)}"></div>`;
    } else if(node.type === 'valField'){
      html += `<div class="field"><label>Field path</label><input type="text" id="cfgField" placeholder="Order.Total" value="${escapeHtml(node.config.field)}"></div>`;
    } else if(node.type === 'valOp'){
      const arity = EXPR_ARITY[node.config.operator];
      html += `<div class="field"><label>Operator</label><select id="cfgOperator">${EXPR_OPERATORS.map(o=>`<option value="${o}" ${o===node.config.operator?'selected':''}>${o}</option>`).join('')}</select></div>
        <div class="insp-note">${arity ? `Takes exactly ${arity.min} operand${arity.min===1?'':'s'}.` : 'Takes two or more operands.'} Wire Literal/Field Ref/Expression nodes into the operand pins below.</div>`;
    }

    body.innerHTML = html;

    if(node.type === 'trigger'){
      document.getElementById('factEditor').addEventListener('change', (e)=>{
        try{
          state.sampleFact = JSON.parse(e.target.value);
          renderNode(node);
          showToast("Sample fact updated.", 'success');
        }catch(err){ showToast("Invalid JSON — fact not updated.", 'error'); }
      });
    }
    if(node.type === 'rule'){
      document.getElementById('ruleCfgId').addEventListener('input', (e)=>{ node.config.id=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('ruleCfgDesc').addEventListener('input', (e)=>{ node.config.description=e.target.value; regenerateJson(); });
      document.getElementById('ruleCfgPriority').addEventListener('input', (e)=>{ node.config.priority=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('ruleCfgEnabled').addEventListener('change', (e)=>{ node.config.enabled=e.target.checked; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'leaf'){
      const fEl = document.getElementById('cfgField');
      if(fEl) fEl.addEventListener('input', (e)=>{ node.config.field=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgOperator').addEventListener('change', (e)=>{
        node.config.operator = e.target.value;
        renderInspector();
        renderNode(node); regenerateJson();
      });
      const vf = document.getElementById('cfgValue');
      if(vf) vf.addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'function'){
      document.getElementById('cfgName').addEventListener('input', (e)=>{ node.config.name=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgField').addEventListener('input', (e)=>{ node.config.field=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgValue').addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'action'){
      document.getElementById('cfgType').addEventListener('change', (e)=>{ node.config.type=e.target.value; renderInspector(); renderNode(node); regenerateJson(); });
      document.getElementById('cfgBranch').addEventListener('change', (e)=>{ node.config.branch=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgTarget').addEventListener('input', (e)=>{ node.config.target=e.target.value; renderNode(node); regenerateJson(); });
      const vf = document.getElementById('cfgValue');
      if(vf) vf.addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'valLiteral'){
      document.getElementById('cfgValue').addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'valField'){
      document.getElementById('cfgField').addEventListener('input', (e)=>{ node.config.field=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'valOp'){
      document.getElementById('cfgOperator').addEventListener('change', (e)=>{
        node.config.operator = e.target.value;
        const arity = EXPR_ARITY[e.target.value];
        if(arity){
          while(node.inputs.filter(Boolean).length < arity.min && node.inputs.length < arity.min) node.inputs.push(null);
          while(node.inputs.length > arity.max + 1) node.inputs.pop();
        }
        renderNode(node); regenerateJson();
      });
    }
  }

  /* ============================================================
     Mouse interaction: pan / drag / connect
     ============================================================ */
  function onNodeMouseDown(e){
    if(e.target.classList.contains('port') || e.target.classList.contains('node-del')) return;
    const nodeEl = e.currentTarget;
    const id = nodeEl.dataset.id;
    const node = state.nodes.get(id);
    const startWorld = screenToWorld(e.clientX, e.clientY);
    state.dragNode = { id, offsetX: node.x - startWorld.x, offsetY: node.y - startWorld.y, moved:false };
    selectNode(id);
    e.stopPropagation();
  }

  function initMouseHandlers(){
    canvasWrap.addEventListener('mousedown', (e)=>{
      if(e.target !== canvasWrap && e.target.id !== 'world' && !e.target.classList.contains('canvas-bg')) return;
      clearSelection();
      state.panDrag = { startX:e.clientX, startY:e.clientY, startPanX:state.panX, startPanY:state.panY };
    });

    window.addEventListener('mousemove', (e)=>{
      if(state.dragNode){
        const w = screenToWorld(e.clientX, e.clientY);
        const node = state.nodes.get(state.dragNode.id);
        node.x = w.x + state.dragNode.offsetX;
        node.y = w.y + state.dragNode.offsetY;
        state.dragNode.moved = true;
        renderNode(node);
        renderWires();
      } else if(state.panDrag){
        state.panX = state.panDrag.startPanX + (e.clientX - state.panDrag.startX);
        state.panY = state.panDrag.startPanY + (e.clientY - state.panDrag.startY);
        applyWorldTransform();
      } else if(state.pendingWire){
        const w = screenToWorld(e.clientX, e.clientY);
        state.pendingWire.x = w.x;
        state.pendingWire.y = w.y;
        renderWires();
      }
    });

    window.addEventListener('mouseup', ()=>{
      if(state.dragNode && state.dragNode.moved){
        const node = state.nodes.get(state.dragNode.id);
        if(node){ node.x = snap(node.x); node.y = snap(node.y); renderNode(node); renderWires(); }
      }
      state.dragNode = null;
      state.panDrag = null;
      if(state.pendingWire){
        state.pendingWire = null;
        renderWires();
      }
    });

    canvasWrap.addEventListener('wheel', (e)=>{
      e.preventDefault();
      const rect = canvasWrap.getBoundingClientRect();
      const cx = e.clientX - rect.left, cy = e.clientY - rect.top;
      const wx = (cx - state.panX)/state.scale, wy = (cy - state.panY)/state.scale;
      state.scale = clamp(state.scale * (e.deltaY < 0 ? 1.08 : 0.92), 0.3, 2);
      state.panX = cx - wx*state.scale;
      state.panY = cy - wy*state.scale;
      applyWorldTransform();
    }, { passive:false });

    document.getElementById('zoomIn').addEventListener('click', ()=>{
      state.scale = clamp(state.scale*1.15, 0.3, 2); applyWorldTransform();
    });
    document.getElementById('zoomOut').addEventListener('click', ()=>{
      state.scale = clamp(state.scale*0.87, 0.3, 2); applyWorldTransform();
    });
    document.getElementById('zoomReset').addEventListener('click', fitToView);

    window.addEventListener('keydown', (e)=>{
      if(['INPUT','TEXTAREA','SELECT'].includes(document.activeElement.tagName)) return;
      if(e.key === 'Delete' || e.key === 'Backspace'){
        if(state.selectedNode){ removeNode(state.selectedNode); }
        else if(state.selectedConn){ removeConnection(state.selectedConn); }
      }
    });
  }

  function startWireDrag(fromNodeId, e){
    const w = screenToWorld(e.clientX, e.clientY);
    state.pendingWire = { fromNode: fromNodeId, x:w.x, y:w.y };
    renderWires();
  }
  function completeWireDrag(toNodeId, toIdx){
    if(!state.pendingWire) return;
    addConnection(state.pendingWire.fromNode, toNodeId, toIdx);
    state.pendingWire = null;
    renderWires();
  }

  /* ============================================================
     Drag-drop from palette
     ============================================================ */
  function initPalette(){
    document.querySelectorAll('.palette-item').forEach(item=>{
      item.addEventListener('dragstart', (e)=>{
        e.dataTransfer.setData('text/plain', item.dataset.type);
      });
      item.addEventListener('dblclick', ()=>{
        const w = screenToWorld(canvasWrap.clientWidth/2 + Math.random()*40, canvasWrap.clientHeight/2 + Math.random()*40);
        const n = createNode(item.dataset.type, snap(w.x), snap(w.y));
        selectNode(n.id);
        renderWires();
        regenerateJson();
      });
    });
    canvasWrap.addEventListener('dragover', (e)=>e.preventDefault());
    canvasWrap.addEventListener('drop', (e)=>{
      e.preventDefault();
      const type = e.dataTransfer.getData('text/plain');
      if(!type || !TYPE_DEFS[type]) return;
      const w = screenToWorld(e.clientX, e.clientY);
      const n = createNode(type, snap(w.x - NODE_W/2), snap(w.y - 20));
      selectNode(n.id);
      renderWires();
      regenerateJson();
    });
  }

  /* ============================================================
     JSON generation
     ============================================================ */
  function parseValue(raw){
    if(raw === undefined || raw === "") return "";
    const trimmed = String(raw).trim();
    if(trimmed === 'true') return true;
    if(trimmed === 'false') return false;
    if(trimmed === 'null') return null;
    if(!isNaN(Number(trimmed)) && trimmed !== '') return Number(trimmed);
    try{
      if(trimmed.startsWith('[') || trimmed.startsWith('{')) return JSON.parse(trimmed);
    }catch(err){ /* fall through to string */ }
    return String(raw); // preserve meaningful leading/trailing whitespace (e.g. "Thanks " in a concat)
  }

  // A computed-value node tree (Literal/Field Ref/Expression) -> the schema's value-expression
  // shape ({literal}/{field}/{op,operands}). Used for an Action's Value (expr) pin and a
  // Compare's Field (expr) pin — the only two places the schema allows a computed value.
  function buildValueExpressionTree(nodeId, warnings, seen){
    seen = seen || new Set();
    const node = state.nodes.get(nodeId);
    if(!node) return null;
    if(seen.has(nodeId)) return null;
    seen.add(nodeId);

    if(node.type === 'valLiteral'){
      return { literal: parseValue(node.config.value) };
    }
    if(node.type === 'valField'){
      if(!node.config.field) warnings.push(`${node.tag}: missing field path`);
      return { field: node.config.field || "" };
    }
    if(node.type === 'valOp'){
      const childConns = node.inputs.filter(Boolean);
      if(childConns.length === 0){
        warnings.push(`${node.tag}: ${node.config.operator} has no connected operands`);
        return null;
      }
      const operands = childConns.map(connId=>{
        const conn = state.connections.get(connId);
        return buildValueExpressionTree(conn.from, warnings, seen);
      }).filter(Boolean);
      return { op: node.config.operator, operands };
    }
    warnings.push(`A ${node.tag} node can't be used as a computed value here.`);
    return null;
  }

  // Builds both the JSON-schema condition object AND a parallel "id tree" of the same shape
  // (node id in place of the JSON body) so a later trace result — which mirrors this exact shape
  // (see Rulewright.Execution.ConditionTraceBuilder) — can be zipped against it positionally.
  function buildConditionTree(nodeId, warnings, seen){
    seen = seen || new Set();
    const node = state.nodes.get(nodeId);
    if(!node) return null;
    if(seen.has(nodeId)) return null;
    seen.add(nodeId);

    if(node.type === 'leaf'){
      const leaf = { operator: node.config.operator };
      const fieldExprConn = node.inputs[0];
      if(fieldExprConn){
        const conn = state.connections.get(fieldExprConn);
        const expr = buildValueExpressionTree(conn.from, warnings, seen);
        if(expr) leaf.expression = expr;
        else warnings.push(`${node.tag}: field expression is incomplete`);
      } else {
        if(!node.config.field) warnings.push(`${node.tag}: missing field path`);
        leaf.field = node.config.field || "";
      }
      if(!['IsNull','IsNotNull'].includes(node.config.operator)){
        leaf.value = parseValue(node.config.value);
      }
      return { json:leaf, idTree:{ id:node.id, children:[] } };
    }
    if(node.type === 'function'){
      if(!node.config.name) warnings.push(`${node.tag}: missing function name`);
      const fn = { operator:'custom', name: node.config.name||"" };
      if(node.config.field) fn.field = node.config.field;
      if(node.config.value !== undefined && node.config.value !== "") fn.value = parseValue(node.config.value);
      return { json: fn, idTree:{ id:node.id, children:[] } };
    }
    if(node.type === 'and' || node.type === 'or' || node.type === 'not'){
      const def = TYPE_DEFS[node.type];
      const childConns = node.inputs.filter(Boolean);
      if(childConns.length === 0){
        warnings.push(`${node.tag}: ${def.label} has no connected inputs`);
        return null;
      }
      const built = childConns.map(connId=>{
        const conn = state.connections.get(connId);
        return buildConditionTree(conn.from, warnings, seen);
      }).filter(Boolean);
      return {
        json: { type:'group', operator: def.operator, rules: built.map(b=>b.json) },
        idTree: { id:node.id, children: built.map(b=>b.idTree) }
      };
    }
    return null;
  }

  function actionsOf(ruleNode){
    return [...state.nodes.values()].filter(n=>{
      if(n.type !== 'action') return false;
      const c = n.inputs[0] && state.connections.get(n.inputs[0]);
      return c && c.from === ruleNode.id;
    });
  }

  // Assemble every Rule node on the canvas into a rule (or a { name, rules[] } set).
  function buildRuleSet(){
    const warnings = [];
    const ruleNodes = [...state.nodes.values()].filter(n=>n.type==='rule');
    const actionNodes = [...state.nodes.values()].filter(n=>n.type==='action');

    if(ruleNodes.length === 0){
      warnings.push("No Rule node on the canvas — add a Rule node, then wire a condition and actions into it.");
    }

    const attachedActionIds = new Set();
    const units = ruleNodes.map((rn, ri)=>{
      const cfg = rn.config;
      const label = cfg.id || rn.tag;

      const condConn = rn.inputs[0] && state.connections.get(rn.inputs[0]);
      let condBuilt = null;
      if(condConn){
        condBuilt = buildConditionTree(condConn.from, warnings);
        if(!condBuilt) warnings.push(`${label}: the wired condition is incomplete.`);
      } else {
        warnings.push(`${label}: no condition wired into the Rule node.`);
      }

      const myActions = actionsOf(rn);
      const actions = [], elseActions = [], thenIds = [], elseIds = [];
      myActions.forEach(a=>{
        attachedActionIds.add(a.id);
        if(!a.config.target) warnings.push(`${a.tag}: missing output target`);
        const entry = { type: a.config.type || 'setOutput', target: a.config.target || "" };
        if(entry.type !== 'removeOutput'){
          const valueExprConn = a.inputs[1];
          if(valueExprConn){
            const conn = state.connections.get(valueExprConn);
            const expr = buildValueExpressionTree(conn.from, warnings, new Set());
            entry.value = expr !== null ? expr : parseValue(a.config.value);
          } else {
            entry.value = parseValue(a.config.value);
          }
        }
        if(a.config.branch === 'else'){ elseActions.push(entry); elseIds.push(a.id); }
        else { actions.push(entry); thenIds.push(a.id); }
      });
      if(myActions.length === 0) warnings.push(`${label}: no actions attached to this rule.`);

      const rule = {
        id: cfg.id || `rule-${ri+1}`,
        description: cfg.description || undefined,
        priority: Number(cfg.priority) || 0,
        enabled: cfg.enabled !== false,
        condition: condBuilt ? condBuilt.json : { field:"", operator:"IsNotNull" },
        actions
      };
      if(elseActions.length > 0){ rule.else = elseActions; }

      return { ruleNodeId: rn.id, ruleId: rule.id, rule, idTree: condBuilt ? condBuilt.idTree : null, thenIds, elseIds };
    });

    // actions not attached to any rule
    actionNodes.forEach(a=>{ if(!attachedActionIds.has(a.id)) warnings.push(`${a.tag}: not attached to a Rule node.`); });

    // duplicate rule ids
    const idCounts = {};
    units.forEach(u=>{ idCounts[u.ruleId] = (idCounts[u.ruleId]||0)+1; });
    Object.keys(idCounts).forEach(id=>{ if(idCounts[id] > 1) warnings.push(`Duplicate rule id "${id}" (${idCounts[id]}×) — rule ids must be unique within a set.`); });

    const setName = (document.getElementById('ruleSetName')||{}).value || "";
    let doc;
    if(units.length === 0){
      doc = { id:"untitled-rule", condition:{ field:"", operator:"IsNotNull" }, actions:[] };
    } else if(units.length === 1){
      doc = units[0].rule;
    } else {
      const ordered = [...units].sort((a,b)=> (b.rule.priority - a.rule.priority));
      doc = { name: setName || "rule-set", rules: ordered.map(u=>u.rule) };
    }

    return { doc, warnings, units, multi: units.length > 1 };
  }

  /* ============================================================
     JSON syntax highlighting (for the drawer's JSON tab)
     ============================================================ */
  function renderJsonHighlighted(value, indent){
    indent = indent || 0;
    const pad = '  '.repeat(indent);
    const pad2 = '  '.repeat(indent+1);
    if(value === null || value === undefined) return `<span class="jv-null">null</span>`;
    if(typeof value === 'string') return `<span class="jv-str">"${escapeHtml(value)}"</span>`;
    if(typeof value === 'number') return `<span class="jv-num">${value}</span>`;
    if(typeof value === 'boolean') return `<span class="jv-bool">${value}</span>`;
    if(Array.isArray(value)){
      if(value.length === 0) return '[]';
      const items = value.map(v=>pad2 + renderJsonHighlighted(v, indent+1)).join(',\n');
      return `[\n${items}\n${pad}]`;
    }
    if(typeof value === 'object'){
      const keys = Object.keys(value).filter(k=>value[k] !== undefined);
      if(keys.length === 0) return '{}';
      const items = keys.map(k=>`${pad2}<span class="jk">"${escapeHtml(k)}"</span>: ${renderJsonHighlighted(value[k], indent+1)}`).join(',\n');
      return `{\n${items}\n${pad}}`;
    }
    return String(value);
  }

  function regenerateJson(){
    const built = buildRuleSet();
    document.getElementById('jsonOut').innerHTML = renderJsonHighlighted(built.doc, 0);
    renderWarnings(built.warnings);
    renderRulesList(built.units);
    return built;
  }

  function renderWarnings(warnings){
    const list = document.getElementById('warnList');
    if(warnings.length === 0){
      list.className = 'warn-list ok';
      list.innerHTML = `<li><span class="dot">●</span> Rule set looks complete.</li>`;
      return;
    }
    list.className = 'warn-list';
    list.innerHTML = warnings.map(w=>`<li><span class="dot">●</span>${escapeHtml(w)}</li>`).join('');
  }

  // Right-panel overview: one chip per rule on the canvas.
  function renderRulesList(units){
    const host = document.getElementById('rulesList');
    if(!host) return;
    if(!units || units.length === 0){
      host.innerHTML = `<div class="rules-empty">No rules yet. Click <strong>+ Add rule</strong> or drag a Rule node onto the canvas.</div>`;
      return;
    }
    host.innerHTML = units.map(u=>{
      const rn = state.nodes.get(u.ruleNodeId);
      const enabled = rn && rn.config.enabled !== false;
      const fired = rn && rn._fired;
      const sel = state.selectedNode === u.ruleNodeId ? ' selected' : '';
      const firedBadge = fired ? `<span class="rule-fired ${fired}">${fired}</span>` : (rn && rn._skipped ? `<span class="rule-skip">skipped</span>` : '');
      return `<div class="rule-chip${sel}" data-rule="${u.ruleNodeId}">
        <span class="rule-chip-dot${enabled?'':' off'}"></span>
        <span class="rule-chip-id">${escapeHtml(u.ruleId)}</span>
        <span class="rule-chip-prio">P${Number(rn?rn.config.priority:0)||0}</span>
        ${firedBadge}
        <button class="rule-chip-del" data-rule-del="${u.ruleNodeId}" title="Delete this rule and its nodes">×</button>
      </div>`;
    }).join('');
    host.querySelectorAll('.rule-chip').forEach(c=>{
      c.addEventListener('click', (e)=>{
        if(e.target.classList.contains('rule-chip-del')) return;
        const id = c.dataset.rule;
        selectNode(id); focusNode(id);
      });
    });
    host.querySelectorAll('[data-rule-del]').forEach(b=>{
      b.addEventListener('click', (e)=>{ e.stopPropagation(); removeRuleCluster(b.dataset.ruleDel); });
    });
  }

  /* ============================================================
     Add / remove rules
     ============================================================ */
  function addRuleNode(){
    const w = screenToWorld(canvasWrap.clientWidth/2, canvasWrap.clientHeight/2);
    const n = createNode('rule', snap(w.x - NODE_W/2), snap(w.y - 40));
    selectNode(n.id);
    regenerateJson();
    showToast(`Added ${n.config.id}. Wire a condition + actions into it.`, 'success');
  }

  // The set of node ids that belong exclusively to this rule: the Rule node, its attached actions,
  // and any upstream condition/value node whose every output feeds into that set.
  function ruleClusterIds(rnId){
    const D = new Set([rnId]);
    state.nodes.forEach(n=>{
      if(n.type === 'action' && n.inputs[0]){
        const c = state.connections.get(n.inputs[0]);
        if(c && c.from === rnId) D.add(n.id);
      }
    });
    let changed = true;
    while(changed){
      changed = false;
      state.nodes.forEach(n=>{
        if(D.has(n.id)) return;
        if(n.type === 'trigger' || n.type === 'rule') return; // never absorb the fact input or another rule
        const outs = [...state.connections.values()].filter(c=>c.from === n.id);
        if(outs.length === 0) return; // leave dangling nodes alone
        if(outs.every(c=>D.has(c.to))){ D.add(n.id); changed = true; }
      });
    }
    return D;
  }
  function removeRuleCluster(rnId){
    const ids = ruleClusterIds(rnId);
    ids.forEach(id=>removeNode(id));
    regenerateJson();
  }

  /* ============================================================
     Auto-layout (Tidy) — reposition the live graph into tidy bands
     ============================================================ */
  function placeUpstream(nodeId, x, y){
    const node = state.nodes.get(nodeId);
    if(!node) return y + ROW_H;
    node.x = x; node.y = y;
    const childConns = node.inputs
      .map(id=>id && state.connections.get(id))
      .filter(c=>c && state.nodes.get(c.from) && state.nodes.get(c.from).type !== 'rule');
    if(childConns.length === 0) return y + ROW_H;
    let cy = y;
    childConns.forEach(c=>{ cy = placeUpstream(c.from, x - COL_W, cy); });
    return cy;
  }

  function layoutAll(){
    const facts = [...state.nodes.values()].filter(n=>n.type==='trigger');
    facts.forEach((f,i)=>{ f.x = FACT_X; f.y = 40 + i*ROW_H; });

    const ruleNodes = [...state.nodes.values()].filter(n=>n.type==='rule')
      .sort((a,b)=> (Number(b.config.priority)||0) - (Number(a.config.priority)||0));

    const RULE_X = FACT_X + COL_W*4;
    let bandY = 40;
    ruleNodes.forEach(rn=>{
      let condBottom = bandY;
      const condConn = rn.inputs[0] && state.connections.get(rn.inputs[0]);
      if(condConn && state.nodes.get(condConn.from)){
        condBottom = placeUpstream(condConn.from, RULE_X - COL_W, bandY);
      }
      const acts = actionsOf(rn);
      let ay = bandY;
      acts.forEach(a=>{
        a.x = RULE_X + COL_W*2; a.y = ay;
        const vconn = a.inputs[1] && state.connections.get(a.inputs[1]);
        if(vconn && state.nodes.get(vconn.from)) placeUpstream(vconn.from, RULE_X + COL_W, ay);
        ay += ROW_H;
      });
      const bandHeight = Math.max(condBottom - bandY, (acts.length||1) * ROW_H, nodeHeight(rn));
      rn.x = RULE_X;
      rn.y = bandY + Math.max(0, (bandHeight - nodeHeight(rn))/2);
      bandY += bandHeight + BAND_GAP;
    });

    state.nodes.forEach(n=>renderNode(n));
    renderWires();
  }

  function tidy(){
    if(![...state.nodes.values()].some(n=>n.type==='rule')){ showToast("Add a Rule node first.", 'error'); return; }
    layoutAll();
    fitToView();
    showToast("Canvas arranged.", 'success');
  }

  /* ============================================================
     Drawer & modal chrome
     ============================================================ */
  function openDrawer(tab){
    document.getElementById('drawer').classList.add('open');
    if(tab) switchDrawerTab(tab);
  }
  function closeDrawer(){
    document.getElementById('drawer').classList.remove('open');
  }
  function switchDrawerTab(tab){
    document.querySelectorAll('.drawer-tab').forEach(b=>b.classList.toggle('active', b.dataset.tab===tab));
    document.querySelectorAll('.drawer-panel').forEach(p=>p.classList.remove('active'));
    document.getElementById('panel'+tab.charAt(0).toUpperCase()+tab.slice(1)).classList.add('active');
  }

  function initDrawer(){
    document.querySelectorAll('.drawer-tab').forEach(btn=>{
      btn.addEventListener('click', ()=>switchDrawerTab(btn.dataset.tab));
    });
    document.getElementById('drawerClose').addEventListener('click', closeDrawer);
    const btnDl = document.getElementById('btnDownloadJson');
    if(btnDl) btnDl.addEventListener('click', downloadJson);
    document.getElementById('btnCopyJson').addEventListener('click', async ()=>{
      const built = buildRuleSet();
      try{
        await navigator.clipboard.writeText(JSON.stringify(built.doc, null, 2));
        showToast("JSON copied to clipboard.", 'success');
      }catch(err){ showToast("Couldn't access the clipboard.", 'error'); }
    });
  }

  function initModal(){
    document.getElementById('btnTest').addEventListener('click', ()=>{
      document.getElementById('factInput').value = JSON.stringify(state.sampleFact, null, 2);
      document.getElementById('testModal').classList.add('open');
    });
    document.getElementById('testModalClose').addEventListener('click', closeTestModal);
    document.getElementById('testCancel').addEventListener('click', closeTestModal);
    document.getElementById('testRun').addEventListener('click', runTest);
  }
  function closeTestModal(){
    document.getElementById('testModal').classList.remove('open');
  }

  function showResultBanner(cls, text){
    resultBanner.className = 'result-banner show ' + cls;
    resultBanner.innerHTML = `<span>${escapeHtml(text)}</span><button class="rb-close">×</button>`;
    resultBanner.querySelector('.rb-close').addEventListener('click', ()=>{
      resultBanner.classList.remove('show');
    });
  }

  function clearTestHighlight(){
    state.nodes.forEach(n=>{ n._test = undefined; n._fired = null; n._skipped = false; renderNode(n); });
    renderWires();
  }

  function applyTraceHighlight(idTree, traceNode){
    if(!idTree) return;
    const node = state.nodes.get(idTree.id);
    if(node && traceNode){
      node._test = traceNode.passed === null || traceNode.passed === undefined ? undefined : traceNode.passed;
      renderNode(node);
    }
    (idTree.children||[]).forEach((childIdTree, i)=>{
      const childTrace = traceNode && traceNode.children ? traceNode.children[i] : null;
      applyTraceHighlight(childIdTree, childTrace);
    });
  }

  async function runTest(){
    const built = regenerateJson();
    let fact;
    try{
      fact = JSON.parse(document.getElementById('factInput').value);
    }catch(err){
      showToast("Fact JSON is invalid.", 'error');
      return;
    }
    state.sampleFact = fact;

    if(!dotNetRef){ showToast("Engine bridge not ready.", 'error'); return; }

    const docJson = JSON.stringify(built.doc);
    const factJson = JSON.stringify(fact);
    const responseText = await dotNetRef.invokeMethodAsync('EvaluateRule', docJson, factJson);
    const response = JSON.parse(responseText);

    closeTestModal();
    clearTestHighlight();

    if(!response.ok){
      showResultBanner('fail', 'Error: ' + response.error);
      const list = document.getElementById('warnList');
      list.className = 'warn-list';
      list.innerHTML = `<li><span class="dot">●</span>${escapeHtml(response.error || 'The engine could not evaluate this rule set.')}</li>`;
      openDrawer('warnings');
      return;
    }

    const byId = {};
    (response.rules||[]).forEach(r=>{ byId[r.ruleId] = r; });

    built.units.forEach(u=>{
      const rr = byId[u.ruleId];
      if(!rr) return;
      applyTraceHighlight(u.idTree, rr.trace);
      const rn = state.nodes.get(u.ruleNodeId);
      if(rn){
        rn._fired = rr.firedBranch || null;
        rn._skipped = !!rr.skipped;
        rn._test = rr.firedBranch ? true : (rr.skipped ? undefined : false);
        renderNode(rn);
      }
      u.thenIds.forEach(id=>{ const n = state.nodes.get(id); if(n){ n._test = rr.firedBranch === 'then'; renderNode(n); } });
      u.elseIds.forEach(id=>{ const n = state.nodes.get(id); if(n){ n._test = rr.firedBranch === 'else'; renderNode(n); } });
    });
    renderWires();
    renderRulesList(built.units);

    const firedCount = (response.rules||[]).filter(r=>r.firedBranch).length;
    const outputsText = response.outputs && Object.keys(response.outputs).length
      ? Object.entries(response.outputs).map(([k,v])=>`${k}=${JSON.stringify(v)}`).join(', ')
      : '(no outputs)';
    const total = (response.rules||[]).length;
    const summary = built.multi
      ? `${firedCount} of ${total} rules fired — ${outputsText}`
      : (firedCount > 0 ? `✓ Rule fired — ${outputsText}` : `✗ Rule did not fire — ${outputsText}`);
    showResultBanner(firedCount > 0 ? 'pass' : 'fail', summary);

    renderTracePanel(response.rules || []);
    openDrawer('trace');
  }

  function renderTracePanel(rules){
    const container = document.getElementById('panelTrace');
    if(!rules || rules.length === 0){
      container.innerHTML = `<div class="insp-empty">No rules to trace.</div>`;
      return;
    }
    let html = '';
    rules.forEach(r=>{
      const status = r.skipped ? 'skipped' : (r.firedBranch ? `fired · ${r.firedBranch}` : 'did not fire');
      const cls = r.firedBranch ? 'pass' : (r.skipped ? '' : 'fail');
      html += `<div class="trace-rule">
        <span class="trace-rule-id">${escapeHtml(r.ruleId)}</span>
        <span class="trace-rule-status ${cls}">${status}</span>
      </div>`;
      const lines = [];
      (function walk(node, d){
        if(!node) return;
        const passed = node.passed;
        const c = passed === true ? 'pass' : passed === false ? 'fail' : '';
        const detail = passed === null || passed === undefined ? 'short-circuited' : (passed ? 'passed' : 'failed');
        lines.push(`<div class="trace-line" style="padding-left:${(d*14)+10}px;">
          <span class="trace-dot ${c}"></span>
          <span class="trace-node">${escapeHtml(node.description)}</span>
          <span class="trace-detail">${detail}</span>
        </div>`);
        (node.children||[]).forEach(ch=>walk(ch, d+1));
      })(r.trace, 0);
      html += lines.join('') || `<div class="trace-line" style="padding-left:10px;"><span class="trace-detail">(condition not traced)</span></div>`;
    });
    container.innerHTML = html;
  }

  async function runValidate(){
    const built = regenerateJson();
    if(!dotNetRef){ showToast("Engine bridge not ready.", 'error'); return; }
    const responseText = await dotNetRef.invokeMethodAsync('ValidateRule', JSON.stringify(built.doc));
    const response = JSON.parse(responseText);

    const list = document.getElementById('warnList');
    const combined = [...built.warnings];
    response.errors.forEach(e=>combined.push((e.path ? e.path + ': ' : '') + e.message));

    if(combined.length === 0){
      list.className = 'warn-list ok';
      list.innerHTML = `<li><span class="dot">●</span> ${built.multi ? 'Rule set is structurally valid.' : 'Rule is structurally valid.'}</li>`;
    } else {
      list.className = 'warn-list';
      list.innerHTML = combined.map(w=>`<li><span class="dot">●</span>${escapeHtml(w)}</li>`).join('');
    }
    openDrawer('warnings');
    showToast(response.valid && built.warnings.length===0 ? "Structurally valid." : "Validation found issues — see the Validation tab.",
      response.valid && built.warnings.length===0 ? 'success' : 'error');
  }

  function downloadJson(){
    const built = buildRuleSet();
    const base = built.doc.name || built.doc.id || 'rules';
    const name = String(base).replace(/[^a-z0-9._-]+/gi, '-');
    const blob = new Blob([JSON.stringify(built.doc, null, 2)], { type:'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = name + '.json';
    document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
    showToast("Downloaded " + name + ".json", 'success');
  }

  function initToolbar(){
    document.getElementById('btnValidate').addEventListener('click', runValidate);
    document.getElementById('btnExport').addEventListener('click', ()=>{ regenerateJson(); openDrawer('json'); });
    const btnNew = document.getElementById('btnNew');
    if(btnNew) btnNew.addEventListener('click', newCanvas);
    const btnTidy = document.getElementById('btnTidy');
    if(btnTidy) btnTidy.addEventListener('click', tidy);
    const btnAddRule = document.getElementById('btnAddRule');
    if(btnAddRule) btnAddRule.addEventListener('click', addRuleNode);
  }

  /* ============================================================
     Examples + import / new
     ============================================================ */
  // Text fields (config.value) are parsed by parseValue(), which treats bare unquoted text as a
  // string — matching what a user types by hand ("gold", not "\"gold\""). Only non-string values
  // need their JSON form (numbers/booleans serialize the same either way; arrays/objects need
  // brackets for parseValue's JSON.parse fallback).
  function valueToFieldText(value){
    return typeof value === 'string' ? value : JSON.stringify(value);
  }

  function importValueExpr(expr){
    if(expr === null || typeof expr !== 'object'){
      const n = createNode('valLiteral', 0, 0);
      n.config.value = valueToFieldText(expr);
      return n;
    }
    if('literal' in expr){
      const n = createNode('valLiteral', 0, 0);
      n.config.value = valueToFieldText(expr.literal);
      return n;
    }
    if('field' in expr){
      const n = createNode('valField', 0, 0);
      n.config.field = expr.field;
      return n;
    }
    if('op' in expr){
      const n = createNode('valOp', 0, 0);
      n.config.operator = expr.op;
      (expr.operands || []).forEach((operand, i)=>{
        const child = importValueExpr(operand);
        addConnection(child.id, n.id, i);
      });
      return n;
    }
    const n = createNode('valLiteral', 0, 0);
    n.config.value = 'null';
    return n;
  }

  function importCondition(cond){
    if(cond && cond.type === 'group'){
      const opType = { AND:'and', OR:'or', NOT:'not' }[cond.operator] || 'and';
      const n = createNode(opType, 0, 0);
      (cond.rules || []).forEach((childCond, i)=>{
        const child = importCondition(childCond);
        addConnection(child.id, n.id, i);
      });
      return n;
    }
    if(cond && cond.operator === 'custom'){
      const n = createNode('function', 0, 0);
      n.config.name = cond.name || '';
      if(cond.field) n.config.field = cond.field;
      if(cond.value !== undefined) n.config.value = valueToFieldText(cond.value);
      return n;
    }
    const n = createNode('leaf', 0, 0);
    n.config.operator = (cond && cond.operator) || 'Equals';
    if(cond && cond.expression){
      const exprNode = importValueExpr(cond.expression);
      addConnection(exprNode.id, n.id, 0);
    } else {
      n.config.field = (cond && cond.field) || '';
    }
    if(cond && cond.value !== undefined) n.config.value = valueToFieldText(cond.value);
    return n;
  }

  function importRule(rule){
    const rn = createNode('rule', 0, 0);
    rn.config.id = rule.id || uniqueRuleId();
    rn.config.description = rule.description || '';
    rn.config.priority = rule.priority || 0;
    rn.config.enabled = rule.enabled !== false;

    if(rule.condition){
      const root = importCondition(rule.condition);
      addConnection(root.id, rn.id, 0);
    }

    const all = [
      ...(rule.actions || []).map(a=>({ ...a, branch:'then' })),
      ...(rule.else || []).map(a=>({ ...a, branch:'else' })),
    ];
    all.forEach(a=>{
      const an = createNode('action', 0, 0);
      an.config.type = a.type || 'setOutput';
      an.config.target = a.target || '';
      an.config.branch = a.branch;
      if(a.type !== 'removeOutput' && a.value !== undefined){
        if(a.value !== null && typeof a.value === 'object'){
          const exprNode = importValueExpr(a.value);
          addConnection(exprNode.id, an.id, 1);
        } else {
          an.config.value = valueToFieldText(a.value);
        }
      }
      addConnection(rn.id, an.id, 0);
    });
    return rn;
  }

  function importDocument(doc){
    clearGraph();
    const setName = document.getElementById('ruleSetName');
    if(setName) setName.value = (doc && doc.name) || '';
    createNode('trigger', FACT_X, 40);
    const rules = Array.isArray(doc.rules) ? doc.rules : [doc];
    rules.forEach(rule=>importRule(rule));
    layoutAll();
    regenerateJson();
    fitToView();
  }

  // Decision tables have no native canvas representation — but the engine expands them into
  // ordinary rules at load time, so we ask C# (RuleSetParser.Parse, the same expansion the engine
  // evaluates) to hand back the equivalent { name, rules[] } and render THAT. What you see on the
  // canvas is exactly what the engine would run.
  async function expandDocument(doc){
    if(!dotNetRef){ showToast("Engine bridge not ready.", 'error'); return null; }
    const respText = await dotNetRef.invokeMethodAsync('ExpandDocument', JSON.stringify(doc));
    const resp = JSON.parse(respText);
    if(!resp.ok){ showToast("Couldn't expand that document: " + resp.error, 'error'); return null; }
    return { name: resp.name, rules: resp.rules };
  }

  async function loadDocIntoCanvas(doc, sourceLabel){
    const label = sourceLabel ? ` from ${sourceLabel}` : '';
    if(!doc || typeof doc !== 'object' || Array.isArray(doc)){
      showToast("This JSON file isn't supported by the canvas.", 'error');
      return false;
    }

    const isDecisionTableDoc = !!doc.decisionTable;
    const isRuleSetDoc = Array.isArray(doc.rules);
    const isSingleRuleDoc = !!doc.condition;

    if(!isDecisionTableDoc && !isRuleSetDoc && !isSingleRuleDoc){
      showToast("This JSON file isn't supported by the canvas.", 'error');
      return false;
    }

    if(doc && doc.decisionTable){
      const expanded = await expandDocument(doc);
      if(!expanded || !expanded.rules || expanded.rules.length === 0){
        if(expanded) showToast("That decision table expanded to no rules.", 'error');
        return false;
      }
      importDocument({ name: expanded.name || 'decision-table', rules: expanded.rules });
      showToast(`Expanded a decision table into ${expanded.rules.length} rule${expanded.rules.length===1?'':'s'}${label}.`, 'success');
      return true;
    }
    importDocument(doc);
    if(Array.isArray(doc.rules)){
      showToast(`Loaded ${doc.rules.length} rule${doc.rules.length===1?'':'s'}${label}.`, 'success');
    } else {
      showToast(`Loaded rule${label}.`, 'success');
    }
    return true;
  }

  function newCanvas(){
    clearGraph();
    const setName = document.getElementById('ruleSetName');
    if(setName) setName.value = '';
    createNode('trigger', FACT_X, 40);
    const rn = createNode('rule', FACT_X + COL_W*2, 60);
    selectNode(rn.id);
    regenerateJson();
    fitToView();
  }

  function initExamples(){
    const select = document.getElementById('exampleSelect');
    if(!select) return;
    fetch('examples/manifest.json').then(r=>r.json()).then(list=>{
      list.forEach(e=>{
        const opt = document.createElement('option');
        opt.value = e.file;
        opt.title = e.description || '';
        opt.textContent = e.file;
        select.appendChild(opt);
      });
    }).catch(()=>{ /* examples are optional */ });

    select.addEventListener('change', async (e)=>{
      const file = e.target.value;
      if(!file) return;
      try{
        const res = await fetch('examples/' + file);
        if(!res.ok) throw new Error('HTTP ' + res.status);
        const doc = await res.json();
        await loadDocIntoCanvas(doc, `"${file}"`);
      }catch(err){
        showToast("Couldn't load that example.", 'error');
      }
      select.value = "";
    });
  }

  function initImportModal(){
    const modal = document.getElementById('importModal');
    if(!modal) return;
    const open = ()=>{ document.getElementById('importInput').value = ''; modal.classList.add('open'); };
    const close = ()=>modal.classList.remove('open');
    document.getElementById('btnImport').addEventListener('click', open);
    document.getElementById('importModalClose').addEventListener('click', close);
    document.getElementById('importCancel').addEventListener('click', close);
    document.getElementById('importLoad').addEventListener('click', async ()=>{
      const text = document.getElementById('importInput').value.trim();
      if(!text){ showToast("Paste some rule JSON first.", 'error'); return; }
      let doc;
      try{ doc = JSON.parse(text); }
      catch(err){ showToast("That isn't valid JSON.", 'error'); return; }
      if(await loadDocIntoCanvas(doc)) close();
    });
  }

  /* ============================================================
     Seed graph (nicer first run than a blank canvas)
     ============================================================ */
  function seedGraph(){
    importDocument({
      name: "discount-rules",
      rules: [
        {
          id: "vip-or-high-value",
          description: "VIP or high-value customers over 18 get 10% off.",
          priority: 10,
          enabled: true,
          condition: {
            type: "group", operator: "AND", rules: [
              { field: "Customer.Age", operator: "GreaterThan", value: 18 },
              { type: "group", operator: "OR", rules: [
                { field: "Order.Total", operator: "GreaterThanOrEqual", value: 100 },
                { field: "Customer.IsVip", operator: "Equals", value: true }
              ] }
            ]
          },
          actions: [ { type: "setOutput", target: "Discount", value: 10 } ]
        },
        {
          id: "free-shipping",
          description: "Orders of $50+ ship free.",
          priority: 5,
          enabled: true,
          condition: { field: "Order.Total", operator: "GreaterThanOrEqual", value: 50 },
          actions: [ { type: "setOutput", target: "FreeShipping", value: true } ]
        }
      ]
    });
  }

  /* ============================================================
     Public init
     ============================================================ */
  function init(reference){
    dotNetRef = reference;
    canvasWrap = document.getElementById('canvasWrap');
    world = document.getElementById('world');
    wiresSvg = document.getElementById('wiresSvg');
    toastEl = document.getElementById('toast');
    resultBanner = document.getElementById('resultBanner');

    applyWorldTransform();
    initMouseHandlers();
    initPalette();
    initDrawer();
    initModal();
    initImportModal();
    initToolbar();
    initExamples();

    const setName = document.getElementById('ruleSetName');
    if(setName) setName.addEventListener('input', ()=>regenerateJson());

    seedGraph();
  }

  return { init };
})();
