// FamFab schema 0.1 adapter, original local implementation of MIT upstream concepts.
// Fixed dimensional snapshot; no external provider, eval, or implicit geometry constraints.
import {validate} from './command-registry.mjs';import {reviewFamilySpec} from './family-spec-review.mjs';
export const famfabTool={name:'famfab_definition_review',description:'Convert a bounded FamFab 0.1 definition to a native Revit recipe using explicit dimensional snapshot consent. Reports unsupported constraints, parameter groups and voids. No model write or remote AI provider.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},template:{type:'string',minLength:1},fileName:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}\\.rfa$'},definition:{type:'object'},acceptStaticGeometry:{type:'boolean'},cuts:{type:'array',maxItems:50,items:{type:'object',properties:{solid:{type:'string',minLength:1,maxLength:100},voids:{type:'array',minItems:1,maxItems:10,uniqueItems:true,items:{type:'string',minLength:1,maxLength:100}}},required:['solid','voids'],additionalProperties:false}}},required:['expectedPath','template','fileName','definition','acceptStaticGeometry'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
function exact(o,allowed,required=allowed){if(!o||typeof o!=='object'||Array.isArray(o)||Object.keys(o).some(k=>!allowed.includes(k))||required.some(k=>!Object.hasOwn(o,k)))throw Error('Unrecognized or missing definition field: '+allowed.join(','))}
const list=(a,max,min=0)=>{if(!Array.isArray(a)||a.length<min||a.length>max)throw Error('Invalid bounded definition array');return a};
const name=s=>{if(typeof s!=='string'||!s.trim()||s.length>80)throw Error('Invalid name');return s};
const key=s=>name(s).trim().replace(/\s+/g,'_');
export function arithmetic(s,variables){
 if(typeof s==='number'){if(!Number.isFinite(s))throw Error('Nonfinite expression');return s}
 if(typeof s!=='string'||s.length>200)throw Error('Invalid expression');
 const ts=[];let i=0;while(i<s.length){if(/\s/.test(s[i])){i++;continue}const m=/^(?:\d+(?:\.\d*)?|\.\d+|[A-Za-z_][A-Za-z_0-9]*|[()+*/-])/.exec(s.slice(i));if(!m)throw Error('Invalid expression token');ts.push(m[0]);i+=m[0].length}
 let p=0;function primary(depth){if(depth>30)throw Error('Expression too deep');const t=ts[p++];if(t==='('){const v=sum(depth+1);if(ts[p++]!==')')throw Error('Unclosed expression');return v}if(t==='+'||t==='-')return (t==='-'?-1:1)*primary(depth+1);if(t&&/^[\d.]/.test(t))return Number(t);if(variables.has(t))return variables.get(t);throw Error('Unknown expression parameter '+t)}
 function prod(d){let v=primary(d);while(ts[p]==='*'||ts[p]==='/'){const op=ts[p++],n=primary(d);if(op==='/'&&n===0)throw Error('Division by zero');v=op==='*'?v*n:v/n}return v}
 function sum(d){let v=prod(d);while(ts[p]==='+'||ts[p]==='-'){const op=ts[p++],n=prod(d);v=op==='+'?v+n:v-n}return v}
 const v=sum(0);if(p!==ts.length||!Number.isFinite(v))throw Error('Invalid expression result');return v;
}
export function famfabReview(a){
 validate(famfabTool.inputSchema,a,'famfab_definition_review');
 if(!a.acceptStaticGeometry)throw Error('Explicit static geometry consent required; constraints are not converted');
 const d=a.definition;if(JSON.stringify(d).length>200000)throw Error('Oversized definition');
 exact(d,['metadata','units','reference_planes','parameters','geometry','subcategories','constraints','_TODO'],['metadata','units','reference_planes','parameters','geometry']);
 const meta=d.metadata;exact(meta,['description','schema_version','category','prompt','origin_offset'],['description','schema_version','category']);
 if(meta.schema_version!=='0.1'||!['Furniture','Casework','Generic Models','Doors','Windows','Specialty Equipment'].includes(meta.category))throw Error('Unsupported schema or category');
 if(meta.origin_offset&&Object.values(meta.origin_offset).some(v=>v!==0))throw Error('Nonzero origin offset requires review');
 const factor={inches:25.4,feet:304.8,millimeters:1,centimeters:10,meters:1000}[d.units];if(!factor)throw Error('Unsupported units');
 const vars=new Map(),params=[],groups=[];
 for(const p of list(d.parameters,100,1)){
  exact(p,['name','type','instance_or_type','default_value','group','description'],['name','type','instance_or_type','default_value','group']);
  const k=key(p.name);if(vars.has(k)||params.some(q=>key(q.name)===k))throw Error('Duplicate parameter');
  if(!['length','number','integer','yes_no','angle','text'].includes(p.type)||!['instance','type'].includes(p.instance_or_type))throw Error('Unsupported parameter type or scope');
  let v=p.default_value;
  if(['length','number','integer','angle'].includes(p.type)){if(typeof v!=='number'||!Number.isFinite(v))throw Error('Expected numeric default');vars.set(k,v);if(p.type==='length')v*=factor;}
  else if(p.type==='yes_no'){if(typeof v!=='boolean')throw Error('Expected boolean default')}else if(typeof v!=='string')throw Error('Expected text default');
  params.push({name:p.name,kind:p.type,value:v,instance:p.instance_or_type==='instance'});groups.push({name:p.name,sourceGroup:p.group,targetGroup:'Data'});
 }
 const mm=x=>{const v=arithmetic(x,vars)*factor;if(!Number.isFinite(v)||Math.abs(v)>30000)throw Error('Dimension outside 30m bound');return v};
 const pt=p=>{exact(p,['u','v']);return {x:mm(p.u),y:mm(p.v)}};
 const rect=(x,y,w,h)=>{if(w<=0||h<=0)throw Error('Positive rectangle required');return [{x,y},{x:x+w,y},{x:x+w,y:y+h},{x,y:y+h}]};
 const profile=p=>{
  switch(p.shape){
   case 'rectangle':{exact(p,['shape','origin','width','height']);const o=pt(p.origin);return {points:rect(o.x,o.y,mm(p.width),mm(p.height))};}
   case 'circle':exact(p,['shape','center','radius']);return {radiusMm:mm(p.radius),centerMm:pt(p.center)};
   case 'custom':exact(p,['shape','vertices']);return {points:list(p.vertices,100,3).map(pt)};
   case 'l_shape':{exact(p,['shape','origin','overall_width','overall_height','horizontal_leg_thickness','vertical_leg_thickness']);const o=pt(p.origin),w=mm(p.overall_width),h=mm(p.overall_height),ht=mm(p.horizontal_leg_thickness),vt=mm(p.vertical_leg_thickness);if(!(ht>0&&vt>0&&ht<h&&vt<w))throw Error('Invalid L shape');return {points:[[0,0],[w,0],[w,ht],[vt,ht],[vt,h],[0,h]].map(([x,y])=>({x:o.x+x,y:o.y+y}))};}
   default:throw Error('Unsupported profile');
  }
 };
 const planes=new Map(),referencePlanes=[];
 for(const p of list(d.reference_planes,50,1)){
  exact(p,['name','direction','offset','is_reference'],['name','direction','offset']);
  if(planes.has(p.name)||!['x','y','z'].includes(p.direction))throw Error('Invalid plane');
  const row={name:'FF_'+name(p.name),axis:p.direction,offsetMm:mm(p.offset)};
  if(row.name.length>80)throw Error('Reference name too long');planes.set(p.name,row);referencePlanes.push(row);
 }
 const subcategories=list(d.subcategories??[],50).map(name);if(new Set(subcategories.map(x=>x.toLowerCase())).size!==subcategories.length)throw Error('Duplicate subcategory');
 const entries=[],names=new Set();
 for(const g of list(d.geometry,100,1)){
  const common=['name','type','is_void','sketch_plane','subcategory'],required=common.slice(0,4);
  const fields=g.type==='extrusion'?['profile','extrusion_start','extrusion_end']:g.type==='blend'?['bottom_profile','top_profile','bottom_offset','top_offset']:g.type==='sweep'?['profile','path']:null;
  if(!fields)throw Error('Unsupported geometry type');exact(g,[...common,...fields],[...required,...fields]);
  if(names.has(name(g.name))||typeof g.is_void!=='boolean'||!planes.has(g.sketch_plane))throw Error('Invalid geometry identity');
  names.add(g.name);const p=planes.get(g.sketch_plane),base={axis:p.axis,void:g.is_void,...(g.subcategory===undefined?{}:{subcategory:g.subcategory})};
  if(g.subcategory!==undefined&&!subcategories.includes(g.subcategory))throw Error('Undeclared subcategory');
  let spec,box=null,kind;
  if(g.type==='extrusion'){const z=p.offsetMm+mm(g.extrusion_start),end=p.offsetMm+mm(g.extrusion_end),pr=profile(g.profile);if(end<=z)throw Error('Extrusion end must exceed start');spec={...base,zMm:z,heightMm:end-z,...pr};kind='shapes';
   if(g.profile.shape==='rectangle'){const xs=pr.points.map(v=>v.x),ys=pr.points.map(v=>v.y),u=[Math.min(...xs),Math.max(...xs)],v=[Math.min(...ys),Math.max(...ys)],w=[z,end];const axes=p.axis==='x'?[w,u,v]:p.axis==='y'?[u,w,v]:[u,v,w];box={min:axes.map(q=>q[0]),max:axes.map(q=>q[1]),volume:axes.reduce((s,q)=>s*(q[1]-q[0]),1)/1e9};}
  }else if(g.type==='blend'){const b=profile(g.bottom_profile),t=profile(g.top_profile);if(!b.points||!t.points)throw Error('Circular blends not represented');spec={...base,bottomMm:p.offsetMm+mm(g.bottom_offset),topMm:p.offsetMm+mm(g.top_offset),bottomPoints:b.points,topPoints:t.points};kind='blends';}
  else {const pr=profile(g.profile);let cross;if(g.profile.shape==='circle'&&pr.centerMm.x===0&&pr.centerMm.y===0)cross={radiusMm:pr.radiusMm};else if(g.profile.shape==='rectangle'){const xs=pr.points.map(q=>q.x),ys=pr.points.map(q=>q.y),w=Math.max(...xs)-Math.min(...xs),h=Math.max(...ys)-Math.min(...ys);if(Math.abs(Math.min(...xs)+Math.max(...xs))>.001||Math.abs(Math.min(...ys)+Math.max(...ys))>.001)throw Error('Sweep profile must be centered');cross={widthMm:w,heightMm:h};}else throw Error('Unsupported sweep profile');
   exact(g.path,['segments']);const path=list(g.path.segments,100,1).map(seg=>{if(seg.segment_type==='line'){exact(seg,['segment_type','start','end']);return {kind:'line',start:pt(seg.start),end:pt(seg.end)}}exact(seg,['segment_type','start','end','center']);if(seg.segment_type!=='arc')throw Error('Unsupported segment');const start=pt(seg.start),end=pt(seg.end),c=pt(seg.center),u=[start.x-c.x,start.y-c.y],v=[end.x-c.x,end.y-c.y],r=Math.hypot(...u),r2=Math.hypot(...v),sum=[u[0]+v[0],u[1]+v[1]],l=Math.hypot(...sum);if(r<=.01||Math.abs(r-r2)>.01||l<.01)throw Error('Invalid or ambiguous arc center');return {kind:'arc',start,end,mid:{x:c.x+r*sum[0]/l,y:c.y+r*sum[1]/l}}});
   spec={...base,zMm:p.offsetMm,...cross,path};kind='sweeps';
  }
  entries.push({name:g.name,kind,spec,box});
 }
 const explicit=a.cuts??[],used=new Set(explicit.flatMap(c=>c.voids)),solids=entries.filter(e=>!e.spec.void);
 const overlap=(a,b)=>a.min.every((v,i)=>Math.min(a.max[i],b.max[i])-Math.max(v,b.min[i])>.00001);
 const omitted=[];
 for(const e of entries.filter(e=>e.spec.void&&!used.has(e.name))){if(!e.box||solids.some(s=>!s.box||overlap(e.box,s.box)))throw Error('Explicit cut mapping required for '+e.name);omitted.push(e.name)}
 const accepted=entries.filter(e=>!omitted.includes(e.name)),shapes=accepted.filter(e=>e.kind==='shapes'),blends=accepted.filter(e=>e.kind==='blends'),sweeps=accepted.filter(e=>e.kind==='sweeps'),ordered=[...shapes,...blends,...sweeps];
 const formCuts=explicit.map(c=>{const si=ordered.findIndex(e=>e.name===c.solid),vi=c.voids.map(n=>ordered.findIndex(e=>e.name===n));if(si<0||vi.some(i=>i<0))throw Error('Unknown cut name');return {solidIndex:si,voidIndices:vi}});
 const expectedCategory={'Casework':'OST_Casework','Furniture':'OST_Furniture','Generic Models':'OST_GenericModel','Doors':'OST_Doors','Windows':'OST_Windows','Specialty Equipment':'OST_SpecialityEquipment'}[meta.category];
 const spec={expectedPath:a.expectedPath,template:a.template,fileName:a.fileName,expectedCategory,shapes:shapes.map(e=>e.spec),blends:blends.map(e=>e.spec),sweeps:sweeps.map(e=>e.spec),parameters:params,referencePlanes,subcategories,...(formCuts.length?{formCuts}:{})};
 reviewFamilySpec(spec);
 const constraints=list(d.constraints??[],100);for(const c of constraints){exact(c,['description','type','geometry','reference_plane']);if(!names.has(c.geometry)||!planes.has(c.reference_plane))throw Error('Unknown constraint reference')}
 let expected=null;
 if(!formCuts.length&&solids.every(e=>e.box)&&solids.every((s,i)=>solids.slice(i+1).every(t=>!overlap(s.box,t.box)))){const min=[0,1,2].map(i=>Math.min(...solids.map(e=>e.box.min[i]))),max=[0,1,2].map(i=>Math.max(...solids.map(e=>e.box.max[i])));expected={dimensionsMm:max.map((x,i)=>x-min[i]),volumeM3:solids.reduce((n,e)=>n+e.box.volume,0),solidCount:solids.length};}
 return {ok:true,spec,sourceCategory:meta.category,sourceUnits:d.units,expected,omittedNonintersectingVoids:omitted,notAppliedConstraints:constraints,parameterGroups:groups,referenceDesignationsNotApplied:d.reference_planes.filter(p=>p.is_reference),todo:d._TODO??[],note:'Dimensional snapshot, not a flexable translation of source expressions. Explicitly review template/category, source constraints and any omitted void; native multi-axis engine required.'};
}