// Conservative dependency extraction, not a Revit formula parser or evaluator.
// Only known, identifier-shaped labels are tracked; strings and bracketed labels
// are handled explicitly, native Revit remains the final authority.
export function reviewFormulaDependencies(parameters,formulas={}){
 const names=new Map([...parameters.keys()].map(k=>[k.toLowerCase(),k]));
 const graph=new Map();
 for(const [target,raw] of Object.entries(formulas)){
  const key=target.toLowerCase();
  if(!names.has(key)||typeof raw!=='string'||!raw.trim()||raw.length>4096)throw Error('Invalid formula target or length '+target);
  if(graph.has(key))throw Error('Duplicate formula target '+target);
  const refs=new Set();
  const expression=raw.replace(/"(?:[^"]|"")*"/g,' ');
  const plain=expression.replace(/\[([^\]]+)\]/g,(_,n)=>{const k=n.trim().toLowerCase();if(names.has(k))refs.add(k);return ' '});
  for(const m of plain.matchAll(/[A-Za-z_][A-Za-z0-9_]*/g)){const k=m[0].toLowerCase();if(names.has(k))refs.add(k)}
  graph.set(key,refs);
 }
 const active=new Set(),done=new Set(),order=[];
 function visit(k,chain){
  if(active.has(k))throw Error('Circular formula dependency: '+[...chain,k].join(' -> '));
  if(done.has(k)||!graph.has(k))return;
  active.add(k);for(const d of graph.get(k))visit(d,[...chain,k]);active.delete(k);done.add(k);order.push(k);
 }
 for(const k of graph.keys())visit(k,[]);
 return {order,dependencies:Object.fromEntries([...graph].map(([k,v])=>[k,[...v]])),note:'Known-label dependency screening only; Revit checks syntax, unit consistency and constraints.'};
}