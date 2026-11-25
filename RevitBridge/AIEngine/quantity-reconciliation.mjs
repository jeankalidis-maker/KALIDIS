// Explicit discipline quantity reconciliation. No inference of missing material or purchasing allowance.
import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1};
export const quantityTool={name:'quantity_reconciliation',description:'Reconcile native material quantities against independent solid volumes by explicit discipline and category. Distinguish missing material, paint and volume discrepancies. Scope only, no model writes.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},groups:{type:'array',minItems:1,maxItems:8,items:{type:'object',properties:{discipline:{type:'string',minLength:1,maxLength:80},ids:{type:'array',minItems:1,maxItems:100,uniqueItems:true,items:id}},required:['discipline','ids'],additionalProperties:false}},relativeTolerance:{type:'number',minimum:.000001,maximum:.05}},required:['expectedPath','groups'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const finite=v=>Number.isFinite(v)&&v>=0;
const key=(...a)=>JSON.stringify(a);
function near(a,b,t){return Math.abs(a-b)<=Math.max(1e-9,Math.abs(b)*t)}
export function reconcile(a,info,m,s){
 const ids=a.groups.flatMap(g=>g.ids),t=a.relativeTolerance??.001;
 if(ids.length>100||new Set(ids).size!==ids.length||new Set(a.groups.map(g=>g.discipline.trim().toLowerCase())).size!==a.groups.length)throw Error('Unique scope of at most 100 IDs required');
 const byId=new Map(info.map(e=>[e.id,e])),solid=new Map(s.elements.map(e=>[e.id,e]));
 if(info.length!==ids.length||s.count!==ids.length||s.elements.length!==ids.length||m.count!==ids.length||ids.some(i=>!byId.has(i)||!solid.has(i)))throw Error('Incomplete scope');
 const materials=new Map(),seen=new Set(),per=new Map(ids.map(i=>[i,[]]));
 for(const r of m.rows){
  const k=key(r.elementId,r.materialId,r.paint);
  if(!per.has(r.elementId)||seen.has(k)||!Number.isSafeInteger(r.materialId)||typeof r.paint!=='boolean'||!finite(r.areaM2)||!finite(r.volumeM3)||(r.paint&&r.volumeM3!==0))throw Error('Invalid or duplicate material row');
  seen.add(k);per.get(r.elementId).push(r);
  const mk=key(r.materialId,r.paint),g=materials.get(mk)??{materialId:r.materialId,paint:r.paint,name:r.name,areaM2:0,volumeM3:0};
  g.areaM2+=r.areaM2;g.volumeM3+=r.volumeM3;materials.set(mk,g);
 }
 const missing=ids.filter(i=>!per.get(i).length);
 if(new Set(m.noMaterialQuantities).size!==m.noMaterialQuantities.length||m.noMaterialQuantities.length!==missing.length||missing.some(i=>!m.noMaterialQuantities.includes(i)))throw Error('Missing accounting mismatch');
 const totals=new Map(m.totals.map(r=>[key(r.materialId,r.paint),r]));
 if(totals.size!==materials.size||totals.size!==m.totals.length||[...materials].some(([k,r])=>{const v=totals.get(k);return !v||!near(v.areaM2,r.areaM2,1e-9)||!near(v.volumeM3,r.volumeM3,1e-9)}))throw Error('Native material total mismatch');
 const groups=a.groups.map(g=>{
  const cats=new Map(),mat=new Map();
  const rows=g.ids.map(id=>{
   const e=byId.get(id),v=solid.get(id),q=per.get(id),bulk=q.filter(x=>!x.paint);
   if(!finite(v.volumeM3)||!Number.isSafeInteger(v.solidCount)||v.solidCount<0)throw Error('Invalid solid data');
   const materialVolumeM3=bulk.reduce((n,r)=>n+r.volumeM3,0),paintAreaM2=q.filter(r=>r.paint).reduce((n,r)=>n+r.areaM2,0);
   const status=!bulk.length?'material_missing':!v.solidCount?'geometry_missing':near(materialVolumeM3,v.volumeM3,t)?'matched':'volume_discrepancy';
   for(const r of q){const k=key(e.category,r.materialId,r.paint),m=mat.get(k)??{category:e.category,materialId:r.materialId,name:r.name,paint:r.paint,areaM2:0,volumeM3:0};m.areaM2+=r.areaM2;m.volumeM3+=r.volumeM3;mat.set(k,m)}
   const c=cats.get(e.category)??{category:e.category,count:0,solidVolumeM3:0,materialVolumeM3:0,paintAreaM2:0,reviewCount:0};c.count++;c.solidVolumeM3+=v.volumeM3;c.materialVolumeM3+=materialVolumeM3;c.paintAreaM2+=paintAreaM2;c.reviewCount+=Number(status!=='matched');cats.set(e.category,c);
   return {id,category:e.category,solidCount:v.solidCount,solidVolumeM3:v.volumeM3,materialVolumeM3,paintAreaM2,deltaM3:materialVolumeM3-v.volumeM3,status};
  });
  return {discipline:g.discipline,count:rows.length,categories:[...cats.values()],materials:[...mat.values()],rows};
 });
 const rows=groups.flatMap(g=>g.rows);
 return {ok:true,groups,scopeCount:ids.length,materialTotals:[...materials.values()],noMaterialQuantities:missing,matched:rows.filter(r=>r.status==='matched').length,requiresReview:rows.filter(r=>r.status!=='matched').map(r=>({id:r.id,status:r.status})),complete:rows.every(r=>r.status==='matched'),note:'Discipline classification supplied by caller. Material volumes and physical solid sums are different measurements; discrepancy is advisory. Paint separate; do not add solid and material quantities, infer missing materials or use as procurement totals.'};
}
export async function quantityReconciliation(a,call){
 validate(quantityTool.inputSchema,a,'quantity_reconciliation');
 const ids=a.groups.flatMap(g=>g.ids);if(ids.length>100||new Set(ids).size!==ids.length)throw Error('Invalid scope');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Project mismatch');
 // Native geometry reads limited to 30 elements each.
 const info=[],rows=[],totals=new Map(),missing=[],solids=[];
 for(let i=0;i<ids.length;i+=25){
  const chunk=ids.slice(i,i+25),r=await call('element_info',{ids:chunk}),m=await call('material_takeoff',{expectedPath:a.expectedPath,ids:chunk}),s=await call('solid_audit',{expectedPath:a.expectedPath,ids:chunk});
  if(m.status!=='RolledBack'||s.status!=='RolledBack')throw Error('Audit unavailable');
  // Verify each chunk before aggregating; never hide missing records.
  reconcile({groups:[{discipline:'chunk',ids:chunk}]},r.elements,m.result,s.result);
  info.push(...r.elements);rows.push(...m.result.rows);missing.push(...m.result.noMaterialQuantities);solids.push(...s.result.elements);
  for(const x of m.result.totals){const k=key(x.materialId,x.paint),v=totals.get(k)??{...x,areaM2:0,volumeM3:0};v.areaM2+=x.areaM2;v.volumeM3+=x.volumeM3;totals.set(k,v)}
 }
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed');
 return reconcile(a,info,{count:ids.length,rows,totals:[...totals.values()],noMaterialQuantities:missing},{count:ids.length,elements:solids});
}