// Durable BIMCraft-style plan status; read-only and never resumes or retries a plan.
import {readFile,lstat} from 'node:fs/promises';import {createHash} from 'node:crypto';import path from 'node:path';import {validate} from './command-registry.mjs';
export const planStatusTool={name:'agent_plan_status',description:'Recover a recorded plan preview, approval digest and operation receipts; report stale/unknown states. No execution or retry.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},planId:{type:'string',pattern:'^[A-Za-z0-9_-]{8,80}$'}},required:['expectedPath','planId'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
function canonical(v){return Array.isArray(v)?v.map(canonical):v&&typeof v==='object'?Object.fromEntries(Object.keys(v).sort().map(k=>[k,canonical(v[k])])):v}
const hash=v=>createHash('sha256').update(JSON.stringify(canonical(v))).digest('hex');
async function read(f){try{const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>100000)throw Error('Invalid plan record');return JSON.parse(await readFile(f,'utf8'))}catch(e){if(e.code==='ENOENT')return null;throw e}}
export async function planStatus(a,call,root){
 validate(planStatusTool.inputSchema,a,'agent_plan_status');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==path.resolve(a.expectedPath).toLowerCase()||path.dirname(c.path).toLowerCase()!==path.join(path.dirname(root),'Sandbox').toLowerCase())throw Error('Project mismatch');
 const file=path.join(root,a.planId+'.json'),start=await read(file);
 if(!start)return {ok:true,planId:a.planId,state:'not_found',safeToRetry:false};
 if(start.path!==c.path||start.state!=='started')throw Error('Plan belongs to another document or is invalid');
 const verified=await read(file+'.verified');
 if(verified){const {digest,...receipt}=verified;if(hash(receipt)!==digest||verified.state!=='preview_verified'||verified.path!==start.path||verified.session!==start.session||verified.scriptDigest!==start.scriptDigest)throw Error('Plan receipt mismatch')}
 const preview=await call('operation_status',{requestId:'plan-preview-'+a.planId}),apply=await call('operation_status',{requestId:'plan-apply-'+a.planId});
 const sameContext=!!verified&&verified.session===c.session&&verified.revision===c.revision;
 let state='preview_unverified';
 if(apply.state==='completed'&&apply.receipt?.verified&&apply.receipt.committed&&apply.receipt.status==='Committed')state='apply_reported_committed';
 else if(apply.state!=='not_found')state='apply_requires_inspection';
 else if(verified)state=sameContext?'preview_current':'preview_stale';
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision)throw Error('Context changed during status read');
 return {ok:true,planId:a.planId,state,currentRevision:c.revision,sameContext,preview:verified?{digest:verified.digest,revision:verified.revision,createdCount:verified.createdCount,counts:verified.counts}:null,previewReceipt:preview,applyReceipt:apply,safeToRetry:false,note:'Persistent receipts are evidence, not authorization or proof of current geometry. Never auto-apply or replay.'};
}