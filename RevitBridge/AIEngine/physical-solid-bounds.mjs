// Reviewed pyRevit query for physical solid bounds, not the element's symbolic envelope.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';
const exec=promisify(execFile),fixture='Smoke-9c3c99fd336746a0b555995ee2728a38.rvt';
const cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a',scriptHash='ed2f63d79a88a871afd79b2d93964410e8c6864777e20ef7c1c35fe9fcd722c6';
export const physicalBoundsTool={name:'physical_solid_bounds',description:'Inspect explicit host element IDs and UniqueIds using reviewed visible positive-volume solid geometry, bounding boxes transformed to model coordinates and native volume cross-check. Sandbox only.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},elements:{type:'array',minItems:1,maxItems:12,items:{type:'object',properties:{id:{type:'integer',minimum:1},uniqueId:{type:'string',minLength:1,maxLength:128}},required:['id','uniqueId'],additionalProperties:false}}},required:['expectedPath','elements'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','physical_solid_bounds153.py')});
async function pin(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export async function physicalBoundsAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pin(f.cli,cliHash,100000000);await pin(f.script,scriptHash,12000);return true}catch{return false}}
const vec=v=>Array.isArray(v)&&v.length===3&&v.every(Number.isFinite);
const near=(a,b)=>Math.abs(a-b)<=Math.max(1e-9,Math.abs(b)*1e-6);
export function verifyPhysicalBounds(r,a,native){
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.failures?.length||r.blocked?.length||r.unclosed_documents?.length||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.result?.path!==a.expectedPath||r.result.rows?.length!==a.elements.length)throw Error('Unexpected pyRevit query result');
 if(native.status!=='RolledBack'||native.result?.elements?.length!==a.elements.length)throw Error('Native volume audit incomplete');
 const rows=a.elements.map(e=>{
  const matches=r.result.rows.filter(x=>x.id===e.id&&x.uniqueId===e.uniqueId),n=native.result.elements.find(x=>x.id===e.id);
  if(matches.length!==1||!n)throw Error('Identity mismatch');
  const x=matches[0];if(!Array.isArray(x.solids)||x.solids.length>100||!Number.isFinite(x.volumeM3)||x.volumeM3<0)throw Error('Invalid solids');
  for(const s of x.solids)if(!vec(s.minMm)||!vec(s.maxMm)||s.minMm.some((v,i)=>v>s.maxMm[i])||!Number.isFinite(s.volumeM3)||s.volumeM3<=0)throw Error('Invalid solid bounds');
  if(x.solids.length){
   if(!vec(x.minMm)||!vec(x.maxMm)||!vec(x.dimensionsMm)||x.minMm.some((v,i)=>Math.abs(v-Math.min(...x.solids.map(s=>s.minMm[i])))>1e-5)||x.maxMm.some((v,i)=>Math.abs(v-Math.max(...x.solids.map(s=>s.maxMm[i])))>1e-5)||x.dimensionsMm.some((v,i)=>Math.abs(v-(x.maxMm[i]-x.minMm[i]))>1e-5))throw Error('Aggregate bounds mismatch');
  }else if(x.minMm!==null||x.maxMm!==null||x.dimensionsMm!==null)throw Error('Empty solid bounds mismatch');
  if(!near(x.volumeM3,x.solids.reduce((s,v)=>s+v.volumeM3,0)))throw Error('Aggregate volume mismatch');
  return {...x,solidCount:x.solids.length,nativeVolumeM3:n.volumeM3,nativeSolidCount:n.solidCount,crossCheck:x.solids.length===n.solidCount&&Number.isFinite(n.volumeM3)&&near(x.volumeM3,n.volumeM3)};
 });
 return {ok:true,runId:r.run_id,rows,allCrossChecksPassed:rows.every(x=>x.crossCheck),modelChanges:0,note:'Visible fine-detail positive-volume solids, transformed solid AABBs; sums not Boolean unions. Rotated or curved solids can have conservative bounds; symbolic geometry excluded. Not a contact or structural capacity test.'};
}
export async function physicalBounds(a,call,root){
 validate(physicalBoundsTool.inputSchema,a,'physical_solid_bounds');
 if(new Set(a.elements.map(e=>e.id)).size!==a.elements.length)throw Error('Duplicate ID');
 if(!await physicalBoundsAvailable(root))throw Error('Reviewed files unavailable');
 const c=await call('project_context',{}),expected=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox',fixture);
 if(!c.ok||c.path!==expected||path.resolve(a.expectedPath)!==expected||c.modified||c.cloud||c.workshared||c.family||c.readOnly)throw Error('Expected saved Smoke project');
 const ids=a.elements.map(e=>e.id),rows=(await call('element_info',{ids})).elements;
 if(a.elements.some(e=>rows.filter(x=>x.id===e.id&&x.uniqueId===e.uniqueId).length!==1))throw Error('Identity mismatch');
 const f=files(root);let r;try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=query','--engine=ironpython','--title=Kalidis physical bounds','--revit=2025','--timeout=30','--inputs='+JSON.stringify({elements:a.elements})],{timeout:55000,maxBuffer:2097152,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Query outcome unavailable; inspect run history before retry')}
 const native=await call('solid_audit',{expectedPath:a.expectedPath,ids});
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified||end.view.id!==c.view.id)throw Error('Context changed');
 return verifyPhysicalBounds(r,a,native);
}