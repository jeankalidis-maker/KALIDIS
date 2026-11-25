// Reviewed multi-category Python preview, inspired by AndreyStartsev/revit-mcp sample 03_batch_parameter_update.py (MIT).
// Fixed script and fixed pyRevit dry_run mode; never accepts Python code or real modify.
import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {readFile,lstat} from 'node:fs/promises';import {createHash} from 'node:crypto';import path from 'node:path';import {validate} from './command-registry.mjs';
const exec=promisify(execFile),scriptHash='0a2a272e59439b304bd057d845cd14cecb24088f1d48c69a59d7b28dfcb4f5e0',cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a';
const id={type:'integer',minimum:1},text={type:'string',maxLength:200};
export const pythonBatchTool={name:'pyrevit_parameter_preview',description:'Preview explicit instance Comments/Mark changes across categories through reviewed pyRevit Python; verify rollback with native readback. No model commit or arbitrary scripts.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},changes:{type:'array',minItems:1,maxItems:20,items:{type:'object',properties:{id,uniqueId:{type:'string',minLength:1,maxLength:128},parameter:{type:'string',enum:['comments','mark']},before:{anyOf:[text,{type:'null'}]},after:text},required:['id','uniqueId','parameter','before','after'],additionalProperties:false}}},required:['expectedPath','changes'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','parameter_batch_preview.py')});
async function pin(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export async function pythonBatchAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pin(f.cli,cliHash,100000000);await pin(f.script,scriptHash,10000);return true}catch{return false}}
const paramIds={comments:-1010106,mark:-1001203};
export function inspectChanges(changes,rows){
 const seen=new Set();
 return changes.map(c=>{const key=c.id+':'+c.parameter;if(seen.has(key)||c.before===c.after)throw Error('Duplicate or unchanged edit');seen.add(key);
 const e=rows.find(e=>e.id===c.id),p=e?.parameters?.filter(p=>p.id===paramIds[c.parameter]);
 if(e?.uniqueId!==c.uniqueId||e.typeId<=0||p?.length!==1||p[0].storage!=='String'||p[0].readOnly||p[0].value!==c.before)throw Error('Identity, type or prior value mismatch');
 return {id:c.id,uniqueId:e.uniqueId,parameter:c.parameter,value:p[0].value};});
}
export function verifyPythonPreview(r,a){
 const ids=[...new Set(a.changes.map(c=>c.id))],modified=r.changes?.modified??[];
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.failures?.length||r.blocked?.length||r.unclosed_documents?.length||r.changes?.added_count!==0||r.changes?.deleted_count!==0||r.changes?.modified_count!==ids.length||modified.length!==ids.length||ids.some(id=>!modified.some(m=>m.id===id))||r.result?.path!==a.expectedPath||r.result?.rows?.length!==a.changes.length)throw Error('Unexpected preview effects; inspect run before any further edit');
 for(const c of a.changes){const row=r.result.rows.find(x=>x.id===c.id&&x.parameter===c.parameter);if(!row||row.before!==c.before||row.inside!==c.after)throw Error('Preview value mismatch')}
 return {ok:true,runId:r.run_id,committed:false,modifiedDuringPreview:ids.length,rows:r.result.rows,rollbackVerified:true,note:'Only this explicit preview was tested. No pyRevit modify command or approval bypass.'};
}
export async function pythonBatchPreview(a,call,root){
 validate(pythonBatchTool.inputSchema,a,'pyrevit_parameter_preview');if(!await pythonBatchAvailable(root))throw Error('Reviewed pyRevit files unavailable');
 const c=await call('project_context',{}),expected=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox','Smoke-9c3c99fd336746a0b555995ee2728a38.rvt');
 if(!c.ok||c.path!==expected||path.resolve(a.expectedPath)!==expected||c.family||c.cloud||c.workshared||c.readOnly||c.modified)throw Error('Expected saved Smoke project');
 const ids=[...new Set(a.changes.map(c=>c.id))],before=inspectChanges(a.changes,(await call('element_info',{ids})).elements);
 const q=await call('project_context',{});if(q.path!==c.path||q.session!==c.session||q.revision!==c.revision)throw Error('Context changed before preview');
 const f=files(root);let r;try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=dry_run','--engine=ironpython','--title=Kalidis parameter preview','--revit=2025','--timeout=30','--inputs='+JSON.stringify({changes:a.changes})],{timeout:55000,maxBuffer:1048576,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Preview outcome unavailable; inspect pyRevit run history, do not replay')}
 const after=inspectChanges(a.changes,(await call('element_info',{ids})).elements);
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.modified!==c.modified||JSON.stringify(before)!==JSON.stringify(after))throw Error('Rollback mismatch; inspect model');
 return verifyPythonPreview(r,a);
}