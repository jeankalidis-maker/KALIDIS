// Read explicit linked intersections in host internal coordinates; no shared-coordinate writes.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';
const exec=promisify(execFile),cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a',scriptHash='06c6731aca7728e880417bb1945adc3eaf904476a4ff139c9c6f7c52bd427424';
const id={type:'integer',minimum:1},uid={type:'string',minLength:1,maxLength:128},pair={type:'object',properties:{hostId:id,hostUniqueId:uid,linkId:id,linkUniqueId:uid,linkedId:id,linkedUniqueId:uid},required:['hostId','hostUniqueId','linkId','linkUniqueId','linkedId','linkedUniqueId'],additionalProperties:false};
export const linkedSolidTool={name:'linked_solid_clash',description:'Check explicit host versus linked physical solids in host internal coordinates with native total transform and project-location metadata. No coordinate changes. Local fixtures only.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},pairs:{type:'array',minItems:1,maxItems:20,items:pair},toleranceM3:{type:'number',minimum:0,maximum:1}},required:['expectedPath','pairs'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','linked_solid_clash164.py')});
async function pin(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export async function linkedSolidAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pin(f.cli,cliHash,100000000);await pin(f.script,scriptHash,12000);return true}catch{return false}}
const finite=v=>Number.isFinite(v)&&v>=0,vec=v=>Array.isArray(v)&&v.length===3&&v.every(Number.isFinite),dot=(a,b)=>a.reduce((n,v,i)=>n+v*b[i],0);
function rigid(t){const b=[t?.basisX,t?.basisY,t?.basisZ];return vec(t?.originMm)&&b.every(vec)&&Math.abs(t.determinant-1)<1e-7&&b.every((v,i)=>b.every((w,j)=>Math.abs(dot(v,w)-(i===j?1:0))<1e-7))}
function location(l){return typeof l?.name==='string'&&['eastWestMm','northSouthMm','elevationMm','angleRad'].every(k=>Number.isFinite(l[k]))}
export function verifyLinkedSolid(r,a){
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.blocked?.length||r.failures?.length||r.unclosed_documents?.length||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.result?.path!==a.expectedPath||r.result.toleranceM3!==(a.toleranceM3??1e-6)||r.result.rows?.length!==a.pairs.length)throw Error('Linked solid query incomplete');
 const tol=a.toleranceM3??1e-6;
 const rows=r.result.rows.map((x,i)=>{
  if(Object.keys(a.pairs[i]).some(k=>x[k]!==a.pairs[i][k])||!rigid(x.totalTransform)||!rigid(x.instanceTransform)||!location(x.hostProjectLocation)||!location(x.linkedProjectLocation)||!['hostSolidCount','linkedSolidCount','tests','failed'].every(k=>Number.isSafeInteger(x[k])&&x[k]>=0)||x.tests!==x.hostSolidCount*x.linkedSolidCount||x.tests>400||x.failed>x.tests||!finite(x.maxPairOverlapM3)||!finite(x.sumPairOverlapM3)||x.sumPairOverlapM3+1e-12<x.maxPairOverlapM3)throw Error('Invalid identity, transform or solid result');
  const expected=x.maxPairOverlapM3>tol?'overlap':x.failed||!x.tests?'inconclusive':'no_overlap_above_tolerance';
  if(x.status!==expected)throw Error('Invalid status');return x;
 });
 return {ok:true,runId:r.run_id,rows,toleranceM3:tol,overlaps:rows.filter(x=>x.status==='overlap').length,inconclusive:rows.filter(x=>x.status==='inconclusive'||x.failed).length,modelChanges:0,note:'Coordinates are host internal; project-location metadata is informational and does not certify shared-coordinate registration. Individual solid-pair volumes may double-count. No linked or host edits.'};
}
export async function linkedSolid(a,call,root){
 validate(linkedSolidTool.inputSchema,a,'linked_solid_clash');
 if(new Set(a.pairs.map(p=>[p.hostId,p.linkId,p.linkedId].join(':'))).size!==a.pairs.length)throw Error('Duplicate pair');
 if(!await linkedSolidAvailable(root))throw Error('Reviewed query unavailable');
 const c=await call('project_context',{}),sandbox=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox');
 if(!c.ok||c.path!==a.expectedPath||path.dirname(c.path)!==sandbox||c.modified||c.cloud||c.family||c.readOnly||c.workshared)throw Error('Saved local Sandbox required');
 const f=files(root);let r;
 try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=query','--engine=ironpython','--title=Kalidis linked solid review','--revit=2025','--timeout=30','--inputs='+JSON.stringify({pairs:a.pairs,toleranceM3:a.toleranceM3??1e-6})],{timeout:55000,maxBuffer:2097152,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Query outcome unavailable; inspect history before retry')}
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed');
 return verifyLinkedSolid(r,a);
}