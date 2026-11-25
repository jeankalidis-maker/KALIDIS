// Kalidis fixed pyRevit bridge. Only the reviewed local audit, no arbitrary Python or CLI arguments.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';
const exec=promisify(execFile);
const cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a';
const scriptHash='e529abd65cfddfcdfc0f774cfb1faf78c0eae325f4f750911e50659733e0ca33';
const fixture='Smoke-9c3c99fd336746a0b555995ee2728a38.rvt';
export const pyrevitAuditTool={name:'pyrevit_reviewed_audit',description:'Run the hash-pinned Kalidis audit through the installed pyRevit agent on the Smoke test project and compare native Kalidis readings. No arbitrary scripts, no model writes.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},engine:{type:'string',enum:['ironpython','cpython']}},required:['expectedPath'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
async function pinned(f,hash,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max)throw Error('Reviewed file unavailable');if(createHash('sha256').update(await readFile(f)).digest('hex')!==hash)throw Error('Reviewed file hash mismatch; re-review before execution')}
export async function pyrevitAvailable(root){
 if(!process.env.APPDATA)return false;
 try{await pinned(path.join(process.env.APPDATA,'pyRevit-Master','bin','pyrevit.exe'),cliHash,100000000);await pinned(path.join(root,'Integrations','pyRevit','ReviewedScripts','audit_smoke.py'),scriptHash,10000);return true}catch{return false}
}
export function verifyAuditResponse(r,c,s,m,v,engine){
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.result_truncated||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.error||r.unclosed_documents?.length||r.blocked?.length||r.failures?.length||r.engine?.implementation!==engine)throw Error('Unexpected pyRevit run result; inspect recorded run before proceeding');
 const a=r.result;
 if(!a||a.path!==c.path||a.viewId!==c.view.id||a.modified!==c.modified||a.selectionCount!==c.selectionCount||a.levels!==s.levels.length||a.families!==s.families.length||a.materials!==m.total||a.sheets!==v.total)throw Error('Cross-engine audit mismatch');
 return {ok:true,runId:r.run_id,engine:r.engine.implementation,python:r.engine.python,elapsedMs:r.elapsed_ms,counts:{walls:a.walls,levels:a.levels,families:a.families,materials:a.materials,sheets:a.sheets},crossChecked:['path','activeView','modified','selectionCount','levels','families','materials','sheets'],modelChanges:0,note:'Fixed reviewed query only. Wall count is a pyRevit reading; other counts cross-checked. No arbitrary Python or native approval bypass.'};
}
export async function pyrevitAudit(a,call,root){
 validate(pyrevitAuditTool.inputSchema,a,'pyrevit_reviewed_audit');if(!await pyrevitAvailable(root))throw Error('Reviewed pyRevit installation or script unavailable');
 const before=await call('project_context',{}),expected=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox',fixture);
 if(!before.ok||before.path!==expected||path.resolve(a.expectedPath).toLowerCase()!==expected.toLowerCase()||before.family||before.workshared||before.cloud||before.readOnly||before.modified)throw Error('Expected saved Smoke project');
 const engine=a.engine??'ironpython',cli=path.join(process.env.APPDATA,'pyRevit-Master','bin','pyrevit.exe'),script=path.join(root,'Integrations','pyRevit','ReviewedScripts','audit_smoke.py');
 // Fixed argv and no shell. CLI timeout is not a guarantee that a Revit request did not execute.
 let r;try{const {stdout}=await exec(cli,['agent','run',script,'--mode=query','--engine='+engine,'--title=Kalidis MCP audit','--revit=2025','--timeout=30'],{timeout:55000,maxBuffer:1048576,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('pyRevit query outcome unavailable; inspect agent runs before retrying')}
 const c=await call('project_context',{});if(c.session!==before.session||c.path!==before.path||c.view.id!==before.view.id||c.modified!==before.modified||c.selectionCount!==before.selectionCount)throw Error('Project context changed');
 const s=await call('snapshot',{}),m=await call('list_materials',{}),v=await call('list_sheets',{});
 const end=await call('project_context',{});if(end.session!==c.session||end.path!==c.path||end.revision!==c.revision||end.view.id!==c.view.id)throw Error('Model changed during cross-check');
 return verifyAuditResponse(r,c,s,m,v,engine);
}
