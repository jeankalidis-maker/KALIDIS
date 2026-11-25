// Explicit discipline quantity reconciliation. No inference of missing material or purchasing allowance.
import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1};
export const quantityTool={name:'quantity_reconciliation',description:'Reconcile native material quantities against independent solid volumes by explicit discipline and category. Distinguish missing material, paint and volume discrepancies. Scope only, no model writes.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},groups:{type:'array',minItems:1,maxItems:8,items:{type:'object',properties:{discipline:{type:'string',minLength:1,maxLength:80},ids:{type:'array',minItems:1,maxItems:100,uniqueItems:true,items:id}},required:['discipline','ids'],additionalProperties:false}},reinforcementIds:{type:"array",minItems:1,maxItems:100,uniqueItems:true,items:id},relativeTolerance:{type:'number',minimum:.000001,maximum:.05}},required:['expectedPath','groups'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
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
   const spatial=v.room!==null&&v.room!==undefined;const status=spatial?'spatial_not_material':!bulk.length?'material_missing':!v.solidCount?'geometry_missing':near(materialVolumeM3,v.volumeM3,t)?'matched':'volume_discrepancy';
   for(const r of q){const k=key(e.category,r.materialId,r.paint),m=mat.get(k)??{category:e.category,materialId:r.materialId,name:r.name,paint:r.paint,areaM2:0,volumeM3:0};m.areaM2+=r.areaM2;m.volumeM3+=r.volumeM3;mat.set(k,m)}
   const c=cats.get(e.category)??{category:e.category,count:0,solidVolumeM3:0,materialVolumeM3:0,paintAreaM2:0,reviewCount:0};c.count++;c.solidVolumeM3+=spatial?0:v.volumeM3;c.materialVolumeM3+=materialVolumeM3;c.paintAreaM2+=paintAreaM2;c.reviewCount+=Number(!['matched','spatial_not_material'].includes(status));cats.set(e.category,c);
   return {id,category:e.category,solidCount:v.solidCount,solidVolumeM3:spatial?null:v.volumeM3,spatialGeometryVolumeM3:spatial?v.volumeM3:null,materialVolumeM3,paintAreaM2,deltaM3:spatial?null:materialVolumeM3-v.volumeM3,status};
  });
  return {discipline:g.discipline,count:rows.length,categories:[...cats.values()],materials:[...mat.values()],rows};
 });
 const rows=groups.flatMap(g=>g.rows);
 return {ok:true,groups,scopeCount:ids.length,materialTotals:[...materials.values()],noMaterialQuantities:missing.filter(id=>!solid.get(id).room),spatialIds:rows.filter(r=>r.status==="spatial_not_material").map(r=>r.id),matched:rows.filter(r=>r.status==='matched').length,requiresReview:rows.filter(r=>!['matched','spatial_not_material'].includes(r.status)).map(r=>({id:r.id,status:r.status})),complete:rows.every(r=>['matched','spatial_not_material'].includes(r.status)),note:'Discipline classification supplied by caller. Material volumes and physical solid sums are different measurements; discrepancy is advisory. Paint separate; do not add solid and material quantities, infer missing materials or use as procurement totals.'};
}
export async function quantityReconciliation(a,call){
 validate(quantityTool.inputSchema,a,'quantity_reconciliation');
 const ids=a.groups.flatMap(g=>g.ids);if(ids.length>100||new Set(ids).size!==ids.length||(a.reinforcementIds??[]).some(id=>!ids.includes(id)))throw Error('Invalid scope or reinforcement IDs outside scope');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Project mismatch');
 // Native geometry reads limited to 30 elements each.
 const info=[],rows=[],totals=new Map(),missing=[],solids=[],geometry=[];
 for(let i=0;i<ids.length;i+=25){
  const chunk=ids.slice(i,i+25),r=await call('element_info',{ids:chunk}),m=await call('material_takeoff',{expectedPath:a.expectedPath,ids:chunk}),s=await call('solid_audit',{expectedPath:a.expectedPath,ids:chunk}),g=await call('geometry_review',{expectedPath:a.expectedPath,ids:chunk});
  if(m.status!=='RolledBack'||s.status!=='RolledBack'||g.status!=='RolledBack')throw Error('Audit unavailable');
  // Verify each chunk before aggregating; never hide missing records.
  reconcile({groups:[{discipline:'chunk',ids:chunk}]},r.elements,m.result,s.result);
  info.push(...r.elements);rows.push(...m.result.rows);missing.push(...m.result.noMaterialQuantities);solids.push(...s.result.elements);geometry.push(...g.result.elements);
  for(const x of m.result.totals){const k=key(x.materialId,x.paint),v=totals.get(k)??{...x,areaM2:0,volumeM3:0};v.areaM2+=x.areaM2;v.volumeM3+=x.volumeM3;totals.set(k,v)}
 }
 let reinforcement=null;
 if(a.reinforcementIds?.length){
  const r=await call('rebar_inventory',{expectedPath:a.expectedPath,ids:a.reinforcementIds});
  if(r.status!=='RolledBack')throw Error('Reinforcement inventory unavailable');
  reinforcement=verifyReinforcement(r.result,a.reinforcementIds);
 }
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed');
 const measurements=measurementRows(a,info,solids,geometry);
 const result=reconcile(a,info,{count:ids.length,rows,totals:[...totals.values()],noMaterialQuantities:missing},{count:ids.length,elements:solids});
 // Room rows must not contribute their spatial geometry volume to physical totals.
 for(const group of result.groups){
  group.measurements=measurements.filter(r=>group.rows.some(x=>x.id===r.id));
  for(const c of group.categories){
   const list=group.measurements.filter(x=>x.category===c.category);
   c.solidVolumeM3=list.filter(x=>!x.spatial).reduce((n,x)=>n+x.physicalSolidVolumeM3,0);
   c.spatialGeometryVolumeM3=list.filter(x=>x.spatial).reduce((n,x)=>n+x.spatialGeometryVolumeM3,0);
   c.axisLengthM=list.filter(x=>x.axisLengthM!==null).reduce((n,x)=>n+x.axisLengthM,0);
   c.axisLengthCoverage=list.filter(x=>x.axisLengthM!==null).length;
   c.roomAreaM2=list.filter(x=>x.roomAreaM2!==null).reduce((n,x)=>n+x.roomAreaM2,0);
   c.roomAreaCoverage=list.filter(x=>x.roomAreaM2!==null).length;
  }
 }
 result.measurementDiscrepancies=measurements.flatMap(r=>['lengthCheck','roomAreaCheck','roomPerimeterCheck','physicalVolumeCheck'].filter(k=>r[k]==='discrepancy').map(k=>({id:r.id,metric:k})));
 result.reinforcement=reinforcement;
 if(reinforcement){
  for(const g of result.groups){
   const bars=reinforcement.elements.filter(e=>g.rows.some(x=>x.id===e.id));
   g.reinforcement={elementCount:bars.length,totalBars:bars.reduce((n,e)=>n+e.quantity,0),totalLengthM:bars.reduce((n,e)=>n+e.totalLengthM,0),estimatedMassKg:bars.reduce((n,e)=>n+e.estimatedMassKg,0)};
  }
 }
 result.measurementNote='Lengths, native host areas, room areas/perimeters and physical surfaces separately reported; null means unavailable, not zero. Spatial volumes excluded from physical aggregate.';
 result.complete=result.complete&&!result.measurementDiscrepancies.length&&measurements.filter(r=>r.spatial).every(r=>r.roomAreaCheck==='matched'&&r.roomPerimeterCheck==='matched');
 return result;
}
// Preserve different kinds of measurements; native internal feet, square feet, cubic feet.
function nativeMeasure(e,ids,factor,unit){
 const rows=(e.parameters??[]).filter(p=>ids.includes(p.id)&&p.storage==='Double'&&p.unit?.startsWith('autodesk.unit.unit:'+unit+'-')&&finite(p.value));
 if(rows.length!==1)return null;
 return {parameterId:rows[0].id,value:rows[0].value*factor};
}
export function measurementRows(a,info,solids,geometry){
 const ids=a.groups.flatMap(g=>g.ids),by=new Map(info.map(e=>[e.id,e])),sol=new Map(solids.map(e=>[e.id,e])),geo=new Map(geometry.map(e=>[e.id,e]));
 if(geometry.length!==ids.length||geo.size!==ids.length||ids.some(i=>!geo.has(i)||geo.get(i).uniqueId!==by.get(i)?.uniqueId))throw Error('Measurement identity mismatch');
 const compare=(native,independent)=>native===null||independent===null?'unavailable':near(native.value,independent,a.relativeTolerance??.001)?'matched':'discrepancy';
 return ids.map(id=>{
  const e=by.get(id),s=sol.get(id),g=geo.get(id);
  if(!finite(s.surfaceM2)||g.curveLengthMm!==null&&!finite(g.curveLengthMm))throw Error('Invalid measurement');
  const spatial=s.room!==null&&s.room!==undefined;
  const lengthM=g.curveLengthMm===null?null:g.curveLengthMm/1000;
  const nativeLength=nativeMeasure(e,[-1001375,-1004005],.3048,'millimeters');
  const nativeArea=nativeMeasure(e,spatial?[-1006902]:[-1012805],.09290304,'squareMeters');
  const nativeVolume=nativeMeasure(e,spatial?[-1006921]:[-1012806],.028316846592,'cubicMeters');
  const nativePerimeter=nativeMeasure(e,spatial?[-1006917]:[-1001953],.3048,'millimeters');
  let roomAreaM2=null,roomPerimeterM=null;
  if(spatial){
   if(!finite(s.room.areaM2))throw Error('Invalid room area');
   roomAreaM2=s.room.areaM2;
   if(Array.isArray(s.room.loops)&&s.room.loops.length&&s.room.loops.every(l=>Array.isArray(l)&&l.length&&l.every(x=>finite(x.lengthMm))))roomPerimeterM=s.room.loops.flat().reduce((n,x)=>n+x.lengthMm,0)/1000;
  }
  return {id,category:e.category,spatial,axisLengthM:lengthM,nativeLengthM:nativeLength?.value??null,lengthCheck:compare(nativeLength,lengthM),hostOrRoomAreaM2:nativeArea?.value??null,roomAreaM2,roomAreaCheck:compare(nativeArea,roomAreaM2),nativePerimeterM:nativePerimeter?.value??null,roomPerimeterM,roomPerimeterCheck:compare(nativePerimeter,roomPerimeterM),solidSurfaceM2:spatial?null:s.surfaceM2,physicalSolidVolumeM3:spatial?null:s.volumeM3,spatialGeometryVolumeM3:spatial?s.volumeM3:null,nativeVolumeM3:nativeVolume?.value??null,physicalVolumeCheck:spatial?'not_applicable':compare(nativeVolume,s.solidCount?s.volumeM3:null),note:spatial?'Room geometry is spatial, not a material or physical solid quantity.':'Axis length is nominal, not fabrication cut length. Host area is a separate native quantity.'};
 });
}

// Explicit rebar inventory cross-check. Nominal mass, not actual weight or procurement allowance.
export function verifyReinforcement(r,ids){
 if(!Array.isArray(r.elements)||r.elements.length!==ids.length||new Set(r.elements.map(x=>x.id)).size!==ids.length||ids.some(id=>!r.elements.some(x=>x.id===id)))throw Error('Incomplete reinforcement scope');
 let count=0,length=0,mass=0;
 for(const e of r.elements){
  if(!Number.isSafeInteger(e.quantity)||e.quantity<1||!finite(e.diameterMm)||e.diameterMm===0||!Array.isArray(e.lengthsMm)||e.lengthsMm.length!==e.quantity||e.lengthsMm.some(x=>!finite(x)||x===0))throw Error('Invalid reinforcement inventory');
  const l=e.lengthsMm.reduce((n,x)=>n+x,0)/1000,m=l*Math.PI*(e.diameterMm/1000)**2/4*7850;
  if(!near(l,e.totalLengthM,1e-8)||!near(m,e.estimatedMassKg,1e-8))throw Error('Reinforcement row totals mismatch');
  count+=e.quantity;length+=l;mass+=m;
 }
 if(count!==r.totalBars||!near(length,r.totalLengthM,1e-8)||!near(mass,r.estimatedMassKg,1e-8))throw Error('Reinforcement aggregate mismatch');
 return {elements:r.elements,totalBars:count,totalLengthM:length,estimatedMassKg:mass,densityKgM3:7850,note:'Nominal centerline length and theoretical mass only. Not actual weight, lap allowance or waste.'};
}
