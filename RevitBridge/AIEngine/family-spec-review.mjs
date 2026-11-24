import {validateClosedProfile} from './profile-topology.mjs';
const has=(o,k)=>Object.hasOwn(o,k);
const num=(v,min,max,k)=>{if(!Number.isFinite(v)||v<min||v>max)throw Error('Invalid '+k);return v};
const label=(v)=>{if(typeof v!=='string'||!v.trim()||v.length>80)throw Error('Invalid parameter name');return v.toLowerCase()};
const point=p=>{num(p.x,-30000,30000,'x');num(p.y,-30000,30000,'y');return p};
const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y);
const profile=p=>{if(!Array.isArray(p)||p.length<3||p.length>100)throw Error('3-100 profile vertices required');return validateClosedProfile(p.map(point))};
export function reviewFamilySpec(a){
 const shapes=a.shapes,blends=a.blends??[],sweeps=a.sweeps??[];
 if(shapes.length>100||blends.length>50||sweeps.length>50||shapes.length+blends.length+sweeps.length===0)throw Error('Invalid form count');
 const params=new Map();
 function add(name,kind,initial,shared=false){
  const key=label(name),old=params.get(key);
  if(old){if(shared&&old.kind==='height'&&Math.abs(old.initial-initial)<=.000001)return;throw Error('Duplicate parameter '+name)}
  params.set(key,{kind,initial});
 }
 for(const s of shapes){
  num(s.zMm,-30000,30000,'zMm');num(s.heightMm,1,30000,'heightMm');
  const circular=has(s,'radiusMm'),relative=has(s,'baseParameter'),rect=has(s,'widthParameter')||has(s,'depthParameter');
  if(circular){num(s.radiusMm,10,10000,'radiusMm');if(has(s,'points')||relative||rect||has(s,'xParameter')||has(s,'yParameter'))throw Error('Conflicting circular profile fields');}
  else{profile(s.points);if(has(s,'radiusParameter'))throw Error('Radius parameter requires circular profile')}
  if(relative!==has(s,'endParameter'))throw Error('Base and end parameters required together');
  if(relative){if(s.void||s.zMm<0||has(s,'heightParameter'))throw Error('Invalid relative extrusion');add(s.baseParameter,'length',s.zMm);add(s.endParameter,'length',s.zMm+s.heightMm)}
  if(has(s,'radiusParameter')){if(s.void)throw Error('Radius parameter requires solid');add(s.radiusParameter,'length',s.radiusMm)}
  if(rect){
   if(s.void||!has(s,'widthParameter')||!has(s,'depthParameter')||s.points.length!==4)throw Error('Rectangular constraints require solid and both dimensions');
   const xs=[...new Set(s.points.map(p=>p.x))],ys=[...new Set(s.points.map(p=>p.y))];
   if(xs.length!==2||ys.length!==2)throw Error('Axis-aligned rectangle required');
   add(s.widthParameter,'length',Math.abs(xs[1]-xs[0]));add(s.depthParameter,'length',Math.abs(ys[1]-ys[0]));
   for(const [key,coordinate] of [['xParameter',Math.min(...xs)],['yParameter',Math.min(...ys)]])if(has(s,key)){if(coordinate<=-999.99)throw Error('Position must exceed -1000 mm datum');add(s[key],'length',coordinate+1000)}
  }else if(has(s,'xParameter')||has(s,'yParameter'))throw Error('Position requires rectangular constraints');
  if(has(s,'heightParameter'))add(s.heightParameter,'height',s.heightMm,true);
 }
 const used=new Set();
 for(const c of a.voidCuts??[]){
  const i=c.solidIndex,j=c.voidIndex;
  if(!Number.isInteger(i)||!Number.isInteger(j)||i===j||!shapes[i]||!shapes[j]||shapes[i].void||!shapes[j].void||used.has(i)||used.has(j))throw Error('Invalid or reused solid/void indices');
  used.add(i);used.add(j);
 }
 for(const b of blends){
  num(b.bottomMm,-30000,30000,'bottomMm');num(b.topMm,-30000,30000,'topMm');
  if(b.topMm-b.bottomMm<1||b.bottomPoints.length!==b.topPoints.length)throw Error('Invalid blend elevations or vertex counts');
  profile(b.bottomPoints);profile(b.topPoints);
 }
 for(const s of sweeps){
  num(s.zMm,-30000,30000,'sweep zMm');
  if(has(s,'radiusMm')){num(s.radiusMm,1,5000,'sweep radius');if(has(s,'widthMm')||has(s,'heightMm'))throw Error('Choose one sweep profile')}
  else{num(s.widthMm,2,10000,'sweep width');num(s.heightMm,2,10000,'sweep height')}
  let last;
  for(const seg of s.path){
   const p=point(seg.start),q=point(seg.end);if(distance(p,q)<=.01||last&&distance(last,p)>.01)throw Error('Disconnected or zero-length sweep path');
   if(seg.kind==='arc'){const m=point(seg.mid??{});if(Math.abs((q.x-p.x)*(m.y-p.y)-(q.y-p.y)*(m.x-p.x))<=.01*distance(p,q))throw Error('Collinear sweep arc')}
   last=q;
  }
 }
 for(const p of a.parameters??[]){if(p.kind==='text'){if(typeof p.value!=='string')throw Error('Text parameter value required')}else num(p.value,p.kind==='length'?-30000:-Number.MAX_VALUE,p.kind==='length'?30000:Number.MAX_VALUE,p.name);add(p.name,p.kind,p.value)}
 for(const [name,formula] of Object.entries(a.formulas??{})){if(!params.has(label(name))||typeof formula!=='string'||!formula.trim())throw Error('Unknown or empty formula target '+name)}
 const planes=new Set();for(const p of a.referencePlanes??[]){const k=label(p.name);if(planes.has(k))throw Error('Duplicate reference plane');planes.add(k);num(p.offsetMm,-30000,30000,'plane offset')}
 return {forms:shapes.length+blends.length+sweeps.length,parameters:params.size,note:'Static geometry and label checks only; Revit resolves formulas, sketch constraints and solid cuts.'};
}
