// Native Node.js MCP stdio adapter. No external npm dependencies.
// Connects only to the authenticated loopback Revit gateway.
import readline from 'node:readline';
import {validate,loadRegistry} from './command-registry.mjs';
import {batchPreflight} from './batch-preflight.mjs';
import {mepNetworkReview,mepNetworkTool} from './mep-network-review.mjs';
import {pythonCommentWorkflow,pythonWorkflowTool} from './python-comment-workflow.mjs';
import {referenceReview,referenceTool} from './reference-plan-review.mjs';
import {inventory,inventoryTool} from './bim-catalog-inventory.mjs';
import {reversibleMove,reversibleMoveTool} from './reversible-move.mjs';
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
 if(!response.ok||!result.ok||!Array.isArray(result.approved))return new Map();
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
 if(!response.ok||!result.ok||!Array.isArray(result.implemented))throw Error('Live Revit catalog unavailable');
 const live=new Set(result.implemented);
 const base=manifest.filter(t=>live.has(t.name)&&(!readonly||t.annotations.readOnlyHint));
 if(live.has('run_extension')&&live.has('extensions')&&!readonly)for(const x of (await resolvedExtensions()).values())base.push(x.tool);
 return [statusTool,preflightTool,inventoryTool,referenceTool,...(!readonly&&base.some(t=>t.name==="mep_topology")?[mepNetworkTool]:[]),...(!readonly&&base.some(t=>t.name==="python_comments")?[pythonWorkflowTool]:[]),...(!readonly&&base.some(t=>t.name==="verified_move")&&base.some(t=>t.name==="geometry_review")?[reversibleMoveTool]:[]),...(!readonly&&result.cooperativeBatchCancel===true?[cancelTool]:[]),...base.map(t=>!readonly&&!t.annotations.readOnlyHint?{...t,inputSchema:{...t.inputSchema,properties:{...t.inputSchema.properties,requestId:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}$',description:'Optional unique operation ID; prevents accidental replay across restarts'}}}}:t)];
}
async function callUnjournaled(name,args){
 const t=tools.get(name);
 if(!t){
  const ext=(await resolvedExtensions()).get(name);
  if(!ext)throw Error('Unknown or unavailable reviewed tool');
  validate(ext.tool.inputSchema,args,name);
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
 if(!catalog.result.ok||!catalog.result.implemented?.includes(name))throw Error('Tool not supported by active Revit engine');
 const {response,result}=await gateway(name,args);
 return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
}
async function call(name,args){
 if(!args||typeof args!=='object'||Array.isArray(args))throw Error('Invalid arguments');
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
