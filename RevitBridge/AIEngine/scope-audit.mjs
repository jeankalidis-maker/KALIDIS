import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1},ids={type:'array',items:id,minItems:1,maxItems:100,uniqueItems:true};
export const scopeAuditTool={name:'scope_audit',description:'Audit explicit physical model elements and optional sheets: material totals, geometry overlap screening and viewport boxes. No model changes; selected scope only, not design certification.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},ids,sheetIds:{type:'array',items:id,maxItems:10,uniqueItems:true},toleranceM3:{type:'number',minimum:0,maximum:1},intentionalPairs:{type:'array',maxItems:200,items:{type:'object',properties:{firstId:id,secondId:id,reason:{type:'string',minLength:1,maxLength:200}},required:['firstId','secondId','reason'],additionalProperties:false}}},required:['expectedPath','ids'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const key=(a,b)=>[a,b].sort((x,y)=>x-y).join(':');
const verified=(r,n)=>{if(r.status!=='RolledBack'||!r.result)throw Error(n+' did not return a verified rollback');return r.result};
export async function scopeAudit(a,call){
 validate(scopeAuditTool.inputSchema,a,'scope_audit');
 const start=performance.now(),times=[];
 const run=async(n,args)=>{const t=performance.now();try{return await call(n,args)}finally{times.push({name:n,elapsedMs:Math.round(performance.now()-t)})}};
 const before=await run('snapshot',{});if(before.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Active document mismatch');
 const info=await run('element_info',{ids:a.ids});
 const byId=new Map(info.elements.map(e=>[e.id,e]));if(byId.size!==a.ids.length||a.ids.some(i=>!byId.has(i)))throw Error('Element identity mismatch');
 const mat=verified(await run('material_takeoff',{expectedPath:a.expectedPath,ids:a.ids}),'material_takeoff');
 const groups=new Map(),seen=new Set();
 for(const r of mat.rows){
  const e=byId.get(r.elementId);if(!e||!Number.isFinite(r.areaM2)||!Number.isFinite(r.volumeM3)||r.areaM2<0||r.volumeM3<0)throw Error('Unexpected material row');
  const k=[e.category??'Unknown',r.materialId,r.paint].join(':');
  const g=groups.get(k)??{category:e.category,materialId:r.materialId,name:r.name,paint:r.paint,areaM2:0,volumeM3:0,elementIds:[]};
  const rowKey=[r.elementId,r.materialId,r.paint].join(':');if(seen.has(rowKey))throw Error('Duplicate quantity row');seen.add(rowKey);
  g.areaM2+=r.areaM2;g.volumeM3+=r.volumeM3;g.elementIds.push(r.elementId);groups.set(k,g);
 }
 const declaredMissing=new Set(mat.noMaterialQuantities),observedMissing=a.ids.filter(i=>!mat.rows.some(r=>r.elementId===i));
 if(declaredMissing.size!==observedMissing.length||observedMissing.some(i=>!declaredMissing.has(i)))throw Error('Missing-material accounting mismatch');
 const exclusions=new Map();for(const p of a.intentionalPairs??[]){if(p.firstId===p.secondId||!byId.has(p.firstId)||!byId.has(p.secondId)||exclusions.has(key(p.firstId,p.secondId)))throw Error('Invalid intentional-pair reference');exclusions.set(key(p.firstId,p.secondId),p.reason)}
 const broad=a.ids.length>1?await run('clash_candidates',{ids:a.ids,toleranceMm:0}):{examined:1,omitted:0,pairs:[],truncated:false};
 if(broad.truncated||broad.pairs.length>200)throw Error('Too many candidate pairs; split the explicit scope');
 const rows=[];
 for(let i=0;i<broad.pairs.length;i+=20){
  const batch=broad.pairs.slice(i,i+20).map(p=>[p.firstId,p.secondId]);
  const r=verified(await run('solid_clash',{expectedPath:a.expectedPath,pairs:batch,toleranceM3:a.toleranceM3??0.000001}),'solid_clash');
  if(r.pairs.length!==batch.length||r.pairs.some((p,j)=>key(p.firstId,p.secondId)!==key(...batch[j])))throw Error('Clash pair mismatch');
  for(const p of r.pairs){const reason=exclusions.get(key(p.firstId,p.secondId))??null;rows.push({...p,reviewDisposition:reason?'user_declared_intentional':p.status==='overlap'?'review':p.status==='inconclusive'?'inconclusive':'clear',reason})}
 }
 rows.sort((x,y)=>y.maxPairOverlapM3-x.maxPairOverlapM3);
 const sheets=[];
 for(const sheetId of a.sheetIds??[]){
  const s=verified(await run('sheet_review',{expectedPath:a.expectedPath,sheetId,mode:'audit'}),'sheet_review');
  sheets.push({...s,unverified:['labels','schedule extents','title block clearance']});
 }
 const after=await run('snapshot',{});if(after.path!==before.path||after.title!==before.title)throw Error('Document changed during audit');
 return {ok:true,selectedIds:a.ids,quantityGroups:[...groups.values()],noMaterialQuantities:observedMissing,clashes:{examined:broad.examined,omitted:broad.omitted,candidateCount:broad.pairs.length,reviewCount:rows.filter(p=>p.reviewDisposition==='review').length,inconclusive:rows.filter(p=>p.status==='inconclusive').length,rows},sheets,timing:{elapsedMs:Math.round(performance.now()-start),calls:times},note:'Non-atomic audit of explicit scope. Quantity sums are not procurement totals; intentional conflicts are declared, not automatically suppressed. Bounds may omit elements. Sheet labels and schedules not checked.'};
}
