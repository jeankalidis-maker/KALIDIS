// Global host-category clash inventory and bounded solid intersection review.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';
const exec=promisify(execFile),cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a',scriptHash='d4012525c04eb90865973c0a13daa648d0a69a4d054d80285a9ab82196655f00';
const categories=['OST_Walls','OST_Floors','OST_Ceilings','OST_Roofs','OST_Furniture','OST_Casework','OST_Doors','OST_Windows','OST_Columns','OST_StructuralColumns','OST_StructuralFoundation','OST_StructuralFraming','OST_PipeCurves','OST_PipeFitting','OST_PipeAccessory','OST_DuctCurves','OST_DuctFitting','OST_DuctAccessory','OST_Conduit','OST_ConduitFitting','OST_CableTray','OST_CableTrayFitting','OST_MechanicalEquipment','OST_ElectricalEquipment','OST_PlumbingFixtures','OST_GenericModel','OST_CurtainWallPanels','OST_CurtainWallMullions','OST_Stairs','OST_StairsRailing'];
const obj=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
const id={type:'integer',minimum:1},key=(a,b)=>[a,b].sort((x,y)=>x-y).join(':');
export const hostClashTool={name:'host_clash_review',description:'Discover all host elements in selected physical categories, verify coverage, prioritize solid clashes and distinguish declared intent, Boolean failures and absent bounds. Bounded 1000 instances and 2000 pairs; links and excluded categories reported separately.',inputSchema:obj({expectedPath:{type:'string',minLength:1},categories:{type:'array',minItems:1,maxItems:40,uniqueItems:true,items:{type:'string',enum:categories}},toleranceM3:{type:'number',minimum:0,maximum:1},maxPairs:{type:'integer',minimum:1,maximum:2000},intentionalPairs:{type:'array',maxItems:200,items:obj({firstId:id,secondId:id,reason:{type:'string',minLength:1,maxLength:200}})}},['expectedPath']),annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','host_clash_scope163.py')});
async function pin(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export async function hostClashAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pin(f.cli,cliHash,100000000);await pin(f.script,scriptHash,12000);return true}catch{return false}}
const vec=v=>Array.isArray(v)&&v.length===3&&v.every(Number.isFinite);
export function candidatePairs(rows,maxPairs=2000){
 if(!Array.isArray(rows)||rows.length>1000||new Set(rows.map(x=>x.id)).size!==rows.length)throw Error('Invalid inventory');
 const eligible=[],omitted=[];
 for(const r of rows){
  if(!Number.isSafeInteger(r.id)||r.id<1)throw Error('Invalid element ID');
  if(r.minMm===null&&r.maxMm===null){omitted.push({id:r.id,category:r.category,reason:'bounds_unavailable'});continue}
  if(!vec(r.minMm)||!vec(r.maxMm)||r.minMm.some((v,i)=>v>r.maxMm[i]))throw Error('Invalid bounding box');
  eligible.push(r);
 }
 eligible.sort((a,b)=>a.minMm[0]-b.minMm[0]||a.id-b.id);
 const pairs=[];let count=0;
 for(let i=0;i<eligible.length;i++){const a=eligible[i];for(let j=i+1;j<eligible.length;j++){
  const b=eligible[j];if(b.minMm[0]>a.maxMm[0])break;
  if([1,2].every(k=>Math.min(a.maxMm[k],b.maxMm[k])>=Math.max(a.minMm[k],b.minMm[k]))){
   count++;if(count<=maxPairs)pairs.push([a.id,b.id].sort((x,y)=>x-y));
  }
 }}
 return {pairs:count>maxPairs?[]:pairs,candidateCount:count,omitted,examined:eligible.length,limited:count>maxPairs};
}
export function verifyClashBatch(batch,r,tol){
 if(r.status!=='RolledBack'||!r.result||r.result.count!==batch.length||r.result.pairs?.length!==batch.length||r.result.toleranceM3!==tol)throw Error('Solid audit coverage mismatch');
 return r.result.pairs.map((p,i)=>{
  if(key(p.firstId,p.secondId)!==key(...batch[i])||!['overlap','inconclusive','no_overlap_above_tolerance'].includes(p.status)||!Number.isSafeInteger(p.tests)||p.tests<0||!Number.isSafeInteger(p.failed)||p.failed<0||p.failed>p.tests||!Number.isFinite(p.maxPairOverlapM3)||p.maxPairOverlapM3<0||!Number.isFinite(p.sumPairOverlapM3)||p.sumPairOverlapM3+1e-12<p.maxPairOverlapM3)throw Error('Invalid solid result');
  const expected=p.maxPairOverlapM3>tol?'overlap':p.failed>0||p.tests===0?'inconclusive':'no_overlap_above_tolerance';
  if(p.status!==expected)throw Error('Unexpected clash classification');
  return p;
 });
}
export async function hostClash(a,call,root){
 validate(hostClashTool.inputSchema,a,'host_clash_review');
 if(!await hostClashAvailable(root))throw Error('Reviewed inventory query unavailable');
 const sandbox=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox'),c=await call('project_context',{});
 if(!c.ok||c.path!==a.expectedPath||path.dirname(c.path)!==sandbox||c.modified||c.cloud||c.workshared||c.family||c.readOnly)throw Error('Saved local Sandbox required');
 const scope=a.categories??categories,inv=await call('list_categories',{}),f=files(root);let r;
 try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=query','--engine=ironpython','--title=Kalidis host clash inventory','--revit=2025','--timeout=30','--inputs='+JSON.stringify({categories:scope})],{timeout:55000,maxBuffer:4194304,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Inventory query outcome unavailable; inspect history before retry')}
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.blocked?.length||r.failures?.length||r.unclosed_documents?.length||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.result?.path!==a.expectedPath)throw Error('Native inventory query incomplete');
 const x=r.result;if(!inv.ok||x.categories?.length!==scope.length||!Array.isArray(x.rows)||x.rows.some(e=>!scope.includes(e.category)))throw Error('Inventory scope mismatch');
 for(const cat of scope){
  const counts=x.categories.filter(v=>v.category===cat),native=inv.categories.filter(v=>v.builtInCategory===cat);
  if(counts.length!==1||native.length>1||counts[0].count!==(native[0]?.instanceCount??0)||x.rows.filter(v=>v.category===cat).length!==counts[0].count)throw Error('Category count mismatch: '+cat);
 }
 const broad=candidatePairs(x.rows,a.maxPairs??1000),byId=new Map(x.rows.map(e=>[e.id,e])),declared=new Map();
 for(const d of a.intentionalPairs??[]){const k=key(d.firstId,d.secondId);if(d.firstId===d.secondId||!byId.has(d.firstId)||!byId.has(d.secondId)||declared.has(k)||!d.reason.trim())throw Error('Invalid declared intentional pair');declared.set(k,d.reason)}
 const tol=a.toleranceM3??.000001,rows=[];
 for(let i=0;i<broad.pairs.length;i+=20){
  const batch=broad.pairs.slice(i,i+20);
  const result=await call('solid_clash',{expectedPath:a.expectedPath,pairs:batch,toleranceM3:tol});
  for(const p of verifyClashBatch(batch,result,tol)){
   const first=byId.get(p.firstId),second=byId.get(p.secondId),reason=declared.get(key(p.firstId,p.secondId))??null;
   rows.push({...p,firstCategory:first.category,secondCategory:second.category,hostRelation:first.hostId===second.id||second.hostId===first.id,declaredIntent:reason,disposition:p.status==='overlap'?(reason?'declared_intentional_overlap':'review_overlap'):p.status==='inconclusive'?'inconclusive':'clear_above_tolerance',partialBooleanFailure:p.failed>0});
  }
 }
 rows.sort((a,b)=>b.maxPairOverlapM3-a.maxPairOverlapM3||a.firstId-b.firstId||a.secondId-b.secondId);
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed; discard report');
 const omittedCategories=inv.categories.filter(v=>!scope.includes(v.builtInCategory)).map(v=>({category:v.builtInCategory,count:v.instanceCount}));
 return {ok:true,runId:r.run_id,scope:x.categories,inventoryCount:x.rows.length,examined:broad.examined,omitted:broad.omitted,candidateCount:broad.candidateCount,limited:broad.limited,evaluated:rows.length,reviewCount:rows.filter(p=>p.disposition==='review_overlap').length,intentionalCount:rows.filter(p=>p.disposition==='declared_intentional_overlap').length,inconclusive:rows.filter(p=>p.status==='inconclusive').length,booleanFailurePairs:rows.filter(p=>p.partialBooleanFailure).length,rows,linksExcluded:x.links,omittedCategories,completeWithinScope:!broad.limited&&!broad.omitted.length&&!rows.some(p=>p.status==='inconclusive'||p.partialBooleanFailure),note:'Host category scope only, no linked models or design-option/phase filtering. Ranking by maximum individual solid-pair overlap, not severity certification; declared intent never erases the raw result. Bounds may include symbolic geometry; no automatic repairs.'};
}