// Native Node.js MCP stdio adapter. No external npm dependencies.
// Connects only to the authenticated loopback Revit gateway.
import readline from 'node:readline';
import {validate,loadRegistry} from './command-registry.mjs';
import {batchPreflight} from './batch-preflight.mjs';
import {reviewWallPairs,candidateTypeIds} from './wall-pair-review.mjs';
import {reviewNativeShape} from './native-shape-review.mjs';
import {scopeAudit,scopeAuditTool} from './scope-audit.mjs';
import {reviewEdits} from './edit-preflight.mjs';
import {verifyDeletePreview} from './delete-guard.mjs';
import {reviewFamilySpec} from './family-spec-review.mjs';
import {reviewOrthogonalPlan} from './orthogonal-plan-review.mjs';
import {checkBatchCapabilities,batchSchemaForEngine,requireCatalog} from './engine-capabilities.mjs';
import {legacyRead,legacyTool} from './legacy-read.mjs';
import {legacyInspect,legacyInspectTool} from './legacy-inspect.mjs';
import {rebarBoxReview,rebarBoxTool} from './rebar-box-review.mjs';
import {reviewDynamoFile,dynamoReviewTool} from './dynamo-file-review.mjs';
import {architectureAudit,architectureAuditTool} from './architecture-audit.mjs';
import {mepNetworkReview,mepNetworkTool} from './mep-network-review.mjs';
import {pythonCommentWorkflow,pythonWorkflowTool} from './python-comment-workflow.mjs';
import {referenceReview,referenceTool} from './reference-plan-review.mjs';
import {inventory,inventoryTool} from './bim-catalog-inventory.mjs';
import {reversibleMove,reversibleMoveTool} from './reversible-move.mjs';
import {reversiblePin,pinTool} from './reversible-pin.mjs';
import {once,operationStatus} from './operation-journal.mjs';
import {readFile,lstat} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.dirname(fileURLToPath(import.meta.url));
const registry=await loadRegistry(root);
const {base:manifest,reviewed}=registry;
const extDir=path.join(process.env.LOCALAPPDATA||'','KalidisAIEngine','ReviewedExtensions');
async function resolvedExtensions(){
 if(readonly)return new Map();
 const {response,result}=await gateway('extensions');
 if(response.status===409&&result?.error==='busy')throw Error('Revit busy; reviewed extension inventory unavailable');
 if(!response.ok||!result.ok||!Array.isArray(result.approved))throw Error('Reviewed extension inventory unavailable');
 let approved;try{approved=JSON.parse((await readFile(path.join(extDir,'approved.json'),'utf8')).replace(/^\uFEFF/,''));}catch{return new Map();}
 const out=new Map();
 for(const entry of reviewed){
  const matches=[];
  for(const name of result.approved){
   if(typeof name!=='string'||path.basename(name)!==name||!name.startsWith('Kalidis.Reviewed.'+entry.sourcePrefix+'.')||!name.endsWith('.dll'))continue;
   try{const file=path.join(extDir,name);const stat=await lstat(file);if(!stat.isFile()||stat.isSymbolicLink()||stat.size>20000000)continue;
   const hash=createHash('sha256').update(await readFile(file)).digest('hex');
   if(typeof approved[name]==='string'&&hash===approved[name].toLowerCase())matches.push(name);}catch{}
  }
  if(matches.length===1)out.set(entry.tool.name,{...entry,assembly:matches[0]});
 }
 return out;
}
const tools=new Map(manifest.map(t=>[t.name,t]));
const readonly=process.argv.includes('--read-only');
const tokenPath=path.join(process.env.LOCALAPPDATA||'', 'KalidisAIEngine','token.txt');
const send=o=>process.stdout.write(JSON.stringify(o)+'\n');
const error=(id,code,message)=>send({jsonrpc:'2.0',id,error:{code,message}});
async function gateway(name,args={}){
 const token=(await readFile(tokenPath,'utf8')).trim();
 const response=await fetch('http://127.0.0.1:7895/execute',{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify({...args,action:name}),signal:AbortSignal.timeout(35000)});
 const result=await response.json();
 return {response,result};
}
const statusTool={name:'operation_status',description:'Read the local adapter journal for a requestId. Never retries a command or guarantees model state.',inputSchema:{type:'object',properties:{requestId:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}$'}},required:['requestId'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const preflightTool={name:'batch_preflight',description:'Validate all 58 batch actions, argument schemas, alias dependencies and basic dimensions without executing Revit. Does not replace native transaction rollback.',inputSchema:registry.all.get('batch').tool.inputSchema,annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const cancelTool={name:'cancel_batch',description:'Request cooperative cancellation of a specific active batch by jobId. Cannot interrupt native API calls or commit; verify rollback in the original response.',inputSchema:{type:'object',properties:{jobId:{type:'string',pattern:'^[0-9a-fA-F]{32}$'}},required:['jobId'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
async function liveTools(){
 const {response,result}=await gateway('catalog');
 requireCatalog({response,result});
 const live=new Set(result.implemented);
 const base=manifest.filter(t=>live.has(t.name)&&(!readonly||t.annotations.readOnlyHint));
 if(live.has('run_extension')&&live.has('extensions')&&!readonly)for(const x of (await resolvedExtensions()).values())base.push(x.tool);
 return [statusTool,preflightTool,inventoryTool,referenceTool,dynamoReviewTool,...(!readonly&&["project_context","geometry_review","solid_audit","rebar_inventory"].every(n=>base.some(t=>t.name===n))?[rebarBoxTool]:[]),...(["snapshot","element_info","selection","native_status"].every(n=>base.some(t=>t.name===n))?[legacyInspectTool]:[]),...(!readonly&&["snapshot","geometry_review","edit_elements"].every(n=>base.some(t=>t.name===n))?[pinTool]:[]),...(!readonly&&["snapshot","element_info","material_takeoff","clash_candidates","solid_clash","sheet_review"].every(n=>base.some(t=>t.name===n))?[scopeAuditTool]:[]),...(["snapshot","elements","element_info","list_categories"].every(n=>base.some(t=>t.name===n))?[legacyTool]:[]),...(["snapshot","get_doors","get_element_rooms","audit_warnings","audit_level_span"].every(n=>base.some(t=>t.name===n))?[architectureAuditTool]:[]),...(!readonly&&base.some(t=>t.name==="mep_topology")?[mepNetworkTool]:[]),...(!readonly&&base.some(t=>t.name==="python_comments")?[pythonWorkflowTool]:[]),...(!readonly&&base.some(t=>t.name==="verified_move")&&base.some(t=>t.name==="geometry_review")?[reversibleMoveTool]:[]),...(!readonly&&result.cooperativeBatchCancel===true?[cancelTool]:[]),...base.map(t=>t.name==="batch"?batchSchemaForEngine(t,result):t).map(t=>!readonly&&!t.annotations.readOnlyHint?{...t,inputSchema:{...t.inputSchema,properties:{...t.inputSchema.properties,requestId:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}$',description:'Optional unique operation ID; prevents accidental replay across restarts'}}}}:t)];
}
async function callUnjournaled(name,args){
 const t=tools.get(name);
 if(!t){
  const ext=(await resolvedExtensions()).get(name);
  if(!ext)throw Error('Unknown or unavailable reviewed tool');
  validate(ext.tool.inputSchema,args,name);
  if(name==='orthogonal_plan')reviewOrthogonalPlan(args);
  if(name==='native_shapes')reviewNativeShape(args);
  const {expectedPath,commit=false,...arguments_}=args;
  if(name==="dynamo_audit"&&args.mode==="start"&&(commit!==true||args.executeReadOnlyGraph!==true))throw Error("Dynamo start requires explicit commit and executeReadOnlyGraph=true; event cannot roll back");
  if(name==="dynamo_audit"&&args.mode==="status"&&commit)throw Error("Status must be rollback-only");
  const permit={dynamo_write_fixture:"executeFixtureGraph",family_quarantine_audit:"inspectPinnedFile",family_catalog_audit:"inspectPinnedFile",dynamo_wall_fixture:"executeFixtureGraph",family_catalog_load:"loadPinnedFile"}[name];
  if(permit&&args.mode==="start"&&(commit!==true||args[permit]!==true))throw Error("External event requires explicit commit and permit; cannot roll back");
  if(permit&&args.mode==="status"&&commit)throw Error("Status must be rollback-only");
  const {response,result}=await gateway('run_extension',{expectedPath,commit:ext.inspection?false:commit,assembly:ext.assembly,operation:name,arguments:arguments_});
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
 }
 if(readonly&&!t.annotations.readOnlyHint)throw Error('Read-only session');
 validate(t.inputSchema,args,name);
 const catalog=await gateway('catalog');
 requireCatalog(catalog);
 if(!catalog.result.implemented.includes(name))throw Error('Tool not supported by active Revit engine');
 if(name==='batch'){
  checkBatchCapabilities(args,catalog.result);
  const preflight=batchPreflight(args,registry);
  if(!preflight.ok)throw Error('Batch preflight rejected: '+JSON.stringify(preflight.issues));
  reviewWallPairs(args.steps);
  const typeIds=candidateTypeIds(args.steps),widths=new Map();
  for(let i=0;i<typeIds.length;i+=100){
   const info=await gateway('element_info',{ids:typeIds.slice(i,i+100)});
   if(!info.response.ok||!info.result.ok)throw Error('Cannot inspect wall type widths');
   for(const e of info.result.elements){const p=e.parameters.find(p=>p.id===-1001000&&p.storage==='Double');if(p&&p.value>0)widths.set(e.id,p.value*304.8);}
  }
  reviewWallPairs(args.steps,widths);
 }
 if(name==='create_family_spec')reviewFamilySpec(args);
 if(name==='edit_elements'){
  reviewEdits(args);
  if(args.commit===true&&args.steps.some(s=>s.action==='delete')){
   const preview=await gateway(name,{...args,commit:false});
   if(!preview.response.ok)throw Error('Deletion preview failed; no commit submitted');
   verifyDeletePreview(args,preview.result);
  }
 }
 const {response,result}=await gateway(name,args);
 return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
}
async function call(name,args){
 if(!args||typeof args!=='object'||Array.isArray(args))throw Error('Invalid arguments');
 if(name==='rebar_box_review'){
  if(readonly)throw Error('Read-only session');
  const result=await rebarBoxReview(args,async(n,a)=>{
   const response=await callUnjournaled(n,a);if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='scope_audit'){
  if(readonly)throw Error('Read-only session');
  const result=await scopeAudit(args,async(n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='legacy_inspect'){
  const result=await legacyInspect(args,async(n,a)=>{
   const response=await callUnjournaled(n,a);if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='legacy_read'){
  const result=await legacyRead(args,async (n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='dynamo_file_review'){
  if(!process.env.LOCALAPPDATA)throw Error('LOCALAPPDATA required');
  const result=await reviewDynamoFile(path.join(process.env.LOCALAPPDATA,'KalidisAIEngine','Sandbox'),args);
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='architecture_audit'){
  const result=await architectureAudit(args,async (n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='mep_network_review'){
  if(readonly)throw Error('Read-only session');
  const result=await mepNetworkReview(args,async (n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='python_comment_workflow'){
  if(readonly)throw Error('Read-only session');
  const result=await pythonCommentWorkflow(args,async (n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='reference_plan_review'){
  const result=referenceReview(args);
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='bim_catalog_inventory'){
  if(!process.env.LOCALAPPDATA)throw Error('LOCALAPPDATA required');
  const result=await inventory(path.join(process.env.LOCALAPPDATA,'KalidisAIEngine','Sandbox'),args);
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='reversible_pin'){
  if(readonly)throw Error('Read-only session');
  const result=await reversiblePin(args,async(n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='reversible_move'){
  if(readonly)throw Error('Read-only session');
  const result=await reversibleMove(args,async (n,a)=>{
   const response=await callUnjournaled(n,a);
   if(response.isError)throw Error(response.content[0].text);
   return JSON.parse(response.content[0].text);
  });
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 if(name==='cancel_batch'){
  if(readonly)throw Error('Read-only session');
  validate(cancelTool.inputSchema,args,name);
  // Do not queue a catalog request during a running batch. The authenticated cancel endpoint is the capability check.
  const token=(await readFile(tokenPath,'utf8')).trim();
  const response=await fetch('http://127.0.0.1:7895/cancel_batch',{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify(args),signal:AbortSignal.timeout(5000)});
  const result=await response.json();
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
 }
 if(name==='batch_preflight'){
  const result=batchPreflight(args,registry);
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:!result.ok};
 }
 if(name==='operation_status'){
  validate(statusTool.inputSchema,args,name);
  if(!process.env.LOCALAPPDATA)throw Error('LOCALAPPDATA required');
  const result=await operationStatus(path.join(process.env.LOCALAPPDATA,'KalidisAIEngine','OperationJournal'),args.requestId);
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:false};
 }
 const {requestId,...rest}=args;
 if(requestId===undefined)return callUnjournaled(name,rest);
 const t=tools.get(name)||reviewed.find(e=>e.tool.name===name)?.tool;
 if(!t||t.annotations.readOnlyHint||readonly)throw Error('requestId only available for write-enabled operations');
 validate(t.inputSchema,rest,name);
 return once(path.join(process.env.LOCALAPPDATA||'', 'KalidisAIEngine','OperationJournal'),requestId,name,rest,()=>callUnjournaled(name,rest));
}
async function dispatch(r){
 if(r?.jsonrpc!=='2.0'||typeof r.method!=='string'){error(r?.id??null,-32600,'Invalid request');return}
 if(!Object.hasOwn(r,'id'))return;
 try{
 let result;
 switch(r.method){
 case 'initialize':result={protocolVersion:['2025-06-18','2025-03-26','2024-11-05'].includes(r.params?.protocolVersion)?r.params.protocolVersion:'2025-06-18',capabilities:{tools:{}},serverInfo:{name:'Kalidis AI Engine',version:'0.4.0'},instructions:'Units mm. Query snapshot and types first. Writes restricted to local Sandbox project; transactions dry-run unless commit=true. Do not retry unknown execution state.'};break;
 case 'ping':result={};break;
 case 'tools/list':result={tools:await liveTools()};break;
 case 'tools/call':try{result=await call(r.params?.name,r.params?.arguments??{})}catch(e){result={content:[{type:'text',text:String(e.message)}],isError:true}}break;
 default:error(r.id,-32601,'Method not found');return;
 }
 send({jsonrpc:'2.0',id:r.id,result});
 }catch(e){error(r.id,-32603,String(e.message))}
}
for await(const line of readline.createInterface({input:process.stdin,crlfDelay:Infinity})){
 try{await dispatch(JSON.parse(line))}catch{error(null,-32700,'Parse error')}
}
