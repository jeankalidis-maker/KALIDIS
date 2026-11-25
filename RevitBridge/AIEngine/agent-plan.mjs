// Architecture inspired by BIMCraft script_atomic / script_dry_run, pinned 73aceeae81efdece268de404644a2b72822ce408.
// Original implementation for native Revit; no Rust runtime, Python execution or external file commands.
import {createHash} from 'node:crypto';import {mkdir,lstat,open,readFile} from 'node:fs/promises';import path from 'node:path';import {validate} from './command-registry.mjs';
const key={type:'string',pattern:'^[A-Za-z0-9_-]{8,80}$'};
export const agentPlanTool={name:'agent_plan',description:'Inspect dependencies without execution, plan a bounded creation-only Revit script, native rollback preview, then explicit apply with the same plan digest/session/revision. Never replay unknown outcomes.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},planId:key,mode:{type:'string',enum:['inspect','preview','apply']},script:{type:'string',minLength:1,maxLength:80000},digest:{type:'string',pattern:'^[a-f0-9]{64}$'}},required:['expectedPath','planId','mode','script'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:true,idempotentHint:false,openWorldHint:false}};
export const allowedPlanCommands=new Set(['material','wall','floor','ceiling','room','grid','column','beam','foundation','component','hosted_component','pipe_sized','duct_sized','conduit_sized','cable_tray_sized']);
function canonical(v){return Array.isArray(v)?v.map(canonical):v&&typeof v==='object'?Object.fromEntries(Object.keys(v).sort().map(k=>[k,canonical(v[k])])):v}
const hash=v=>createHash('sha256').update(JSON.stringify(canonical(v))).digest('hex');
export function parsePlan(script){
 const lines=script.split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
 if(!lines.length||lines.length>100)throw Error('1-100 command lines required');
 return lines.map((line,i)=>{const m=/^([a-z_]+)\s+(\{.*\})$/.exec(line);if(!m||!allowedPlanCommands.has(m[1]))throw Error('Unsupported plan command on line '+(i+1));let args;try{args=JSON.parse(m[2])}catch{throw Error('Invalid JSON on line '+(i+1))}if(Object.hasOwn(args,'action'))throw Error('Action must be specified only once');if(m[1]==='hosted_component'&&(typeof args.hostId!=='string'||!args.hostId.startsWith('$')))throw Error('Plan hosted components require an earlier newly created host alias');return {action:m[1],...args}});
}
export function planDependencies(steps){
 const aliases=new Map(),nodes=[],external=new Map();
 const idFields=new Set(['hostId','levelId','typeId','systemTypeId','viewId','sheetId','firstId','secondId','thirdId','templateId','topLevelId']);
 for(const [i,s] of steps.entries()){
  const deps=[];
  for(const [field,value] of Object.entries(s)){
   if(!idFields.has(field))continue;
   if(typeof value==='string'){
    const source=aliases.get(value.slice(1));
    if(!source)throw Error('Unknown prior alias '+value);
    if(field!=='hostId'||s.action!=='hosted_component'||!['wall','floor','ceiling'].includes(source.action))throw Error('Alias does not produce a compatible '+field+' on step '+(i+1));
    deps.push({step:source.step,field,alias:value.slice(1)});
   }else{
    if(!Number.isSafeInteger(value)||value<=0)throw Error('Positive external ID required on step '+(i+1));
    const k=String(value);if(!external.has(k))external.set(k,{id:value,uses:[]});
    external.get(k).uses.push({step:i+1,field});
   }
  }
  const node={step:i+1,action:s.action,alias:s.alias??null,dependencies:deps};
  nodes.push(node);
  if(s.alias!==undefined){if(aliases.has(s.alias))throw Error('Duplicate alias');aliases.set(s.alias,node)}
 }
 return {nodes,externalIds:[...external.values()],reverseDependencyOrder:nodes.map(n=>n.step).reverse(),note:'Creation order and explicit alias graph only. Reverse order is advisory, not permission to delete; implicit Revit dependencies require native inspection.'};
}
export async function agentPlan(a,call,root){
 validate(agentPlanTool.inputSchema,a,'agent_plan');const steps=parsePlan(a.script);
 const c=await call('project_context',{});
 if(!c.ok||c.path.toLowerCase()!==path.resolve(a.expectedPath).toLowerCase()||c.family||c.workshared||c.cloud||c.readOnly||path.dirname(c.path).toLowerCase()!==path.join(path.dirname(root),'Sandbox').toLowerCase())throw Error('Matching local Sandbox project required');
 const args={expectedPath:c.path,expectedSession:c.session,expectedRevision:c.revision,steps,commit:false};
 const pre=await call('batch_preflight',args);if(!pre.ok)throw Error('Plan preflight failed');
 const dependencies=planDependencies(steps);
 if(a.mode==='inspect'){
  if(a.digest!==undefined)throw Error('Digest is for apply only');
  const end=await call('project_context',{});
  if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision)throw Error('Context changed during inspection');
  return {ok:true,mode:'inspect',planId:a.planId,revision:c.revision,counts:pre.counts,dependencies,committed:false,executed:false,note:'Static inspection only, no file reservation or native transaction. External IDs and implicit dependencies still require native preview.'};
 }
 await mkdir(root,{recursive:true});const st=await lstat(root);if(!st.isDirectory()||st.isSymbolicLink())throw Error('Invalid plan directory');
 const f=path.join(root,a.planId+'.json'),scriptDigest=hash(steps);
 if(a.mode==='preview'){
  if(a.digest!==undefined)throw Error('Digest is for apply only');
  // Reserve before submitting native preview, never overwrite or replay uncertain previews.
  const h=await open(f,'wx');try{await h.writeFile(JSON.stringify({state:'started',scriptDigest,path:c.path,session:c.session,revision:c.revision}));await h.sync()}finally{await h.close()}
  const r=await call('batch',{...args,requestId:'plan-preview-'+a.planId});
  if(!r.verified||r.committed||r.status!=='RolledBack'||r.modifiedIds?.length)throw Error('Creation-only preview not verified; inspect model');
  const after=await call('project_context',{});if(after.path!==c.path||after.session!==c.session||after.revision!==c.revision+1)throw Error('Unexpected revision transition during preview; inspect and create a new plan');
  const receipt={state:'preview_verified',path:c.path,session:c.session,revision:after.revision,scriptDigest,counts:pre.counts,createdCount:r.createdIds.length};
  const digest=hash(receipt),fd=await open(f+'.verified','wx');try{await fd.writeFile(JSON.stringify({...receipt,digest}));await fd.sync()}finally{await fd.close()}
  return {ok:true,mode:'preview',digest,planId:a.planId,revision:after.revision,counts:pre.counts,createdCount:receipt.createdCount,dependencies,committed:false,verification:'Native rollback and existence check; no global geometry certificate. Preview IDs are temporary.'};
 }
 if(!a.digest)throw Error('Explicit preview digest required');
 const fs=await lstat(f+'.verified');if(!fs.isFile()||fs.isSymbolicLink()||fs.size>100000)throw Error('Invalid preview receipt');
 const old=JSON.parse(await readFile(f+'.verified','utf8'));const {digest,...receipt}=old;
 if(digest!==a.digest||hash(receipt)!==digest||old.scriptDigest!==scriptDigest||old.path!==c.path||old.session!==c.session||old.revision!==c.revision||old.state!=='preview_verified')throw Error('Stale or changed plan; new preview required');
 const r=await call('batch',{...args,commit:true,requestId:'plan-apply-'+a.planId});
 if(!r.verified||!r.committed||r.status!=='Committed'||r.createdIds.length!==old.createdCount)throw Error('Apply outcome differs from preview; inspect, do not retry');
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session)throw Error('Context changed after apply; inspect model');
 return {ok:true,mode:'apply',planId:a.planId,digest,committed:true,verified:r.verified,createdIds:r.createdIds,aliases:r.aliases,dependencies,revision:end.revision,requestId:'plan-apply-'+a.planId,note:'Single native transaction, existence verified. Subsequent geometry inspection required; not global undo.'};
}