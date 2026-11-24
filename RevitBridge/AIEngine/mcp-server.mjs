// Native Node.js MCP stdio adapter. No external npm dependencies.
// Connects only to the authenticated loopback Revit gateway.
import readline from 'node:readline';
import {readFile,lstat} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.dirname(fileURLToPath(import.meta.url));
const manifest=JSON.parse(await readFile(path.join(root,'tools.json'),'utf8'));
const reviewed=JSON.parse(await readFile(path.join(root,'reviewed-tools.json'),'utf8'));
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
function validate(schema,value,label){
 const k=schema.type;
 if(k==='string'&&(typeof value!=='string'||value.length<(schema.minLength||0))||k==='boolean'&&typeof value!=='boolean'||k==='integer'&&!Number.isSafeInteger(value)||k==='number'&&!(typeof value==='number'&&Number.isFinite(value))||k==='array'&&!Array.isArray(value)||k==='object'&&(!value||typeof value!=='object'||Array.isArray(value)))throw Error('Invalid type: '+label);
 if(schema.enum&&!schema.enum.includes(value))throw Error('Invalid enum: '+label);
 if(k==='array'){if(value.length<(schema.minItems||0)||value.length>(schema.maxItems??Infinity))throw Error('Invalid array length: '+label);if(schema.items)for(const [i,v] of value.entries())validate(schema.items,v,label+'['+i+']');}
 if(k==='object'&&schema.properties){for(const key of schema.required||[])if(!Object.hasOwn(value,key))throw Error('Missing '+label+'.'+key);for(const [key,v] of Object.entries(value)){if(Object.hasOwn(schema.properties,key))validate(schema.properties[key],v,label+'.'+key);else if(schema.additionalProperties===false)throw Error('Unknown '+label+'.'+key);}}
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
async function liveTools(){
 const {response,result}=await gateway('catalog');
 if(!response.ok||!result.ok||!Array.isArray(result.implemented))throw Error('Live Revit catalog unavailable');
 const live=new Set(result.implemented);
 const base=manifest.filter(t=>live.has(t.name)&&(!readonly||t.annotations.readOnlyHint));
 if(live.has('run_extension')&&live.has('extensions')&&!readonly)for(const x of (await resolvedExtensions()).values())base.push(x.tool);
 return base;
}
async function call(name,args){
 const t=tools.get(name);
 if(!t){
  const ext=(await resolvedExtensions()).get(name);
  if(!ext)throw Error('Unknown or unavailable reviewed tool');
  validate(ext.tool.inputSchema,args,name);
  const {expectedPath,commit=false,...arguments_}=args;
  if(name==="dynamo_audit"&&args.mode==="start"&&(commit!==true||args.executeReadOnlyGraph!==true))throw Error("Dynamo start requires explicit commit and executeReadOnlyGraph=true; event cannot roll back");
  if(name==="dynamo_audit"&&args.mode==="status"&&commit)throw Error("Status must be rollback-only");
  const permit={dynamo_write_fixture:"executeFixtureGraph",family_quarantine_audit:"inspectPinnedFile",family_catalog_audit:"inspectPinnedFile"}[name];
  if(permit&&args.mode==="start"&&(commit!==true||args[permit]!==true))throw Error("External event requires explicit commit and permit; cannot roll back");
  if(permit&&args.mode==="status"&&commit)throw Error("Status must be rollback-only");
  const {response,result}=await gateway('run_extension',{expectedPath,commit:ext.inspection?false:commit,assembly:ext.assembly,operation:name,arguments:arguments_});
  return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
 }
 if(readonly&&!t.annotations.readOnlyHint)throw Error('Read-only session');
 if(!args||typeof args!=='object'||Array.isArray(args))throw Error('Invalid arguments');
 for(const k of Object.keys(args))if(!Object.hasOwn(t.inputSchema.properties,k))throw Error('Unknown argument '+k);
 for(const k of t.inputSchema.required||[])if(!Object.hasOwn(args,k))throw Error('Missing '+k);
 for(const [k,v] of Object.entries(args)){
  const schema=t.inputSchema.properties[k]; const kind=schema.type;
  if(kind==='string'&&typeof v!=='string'||kind==='boolean'&&typeof v!=='boolean'||kind==='integer'&&!Number.isSafeInteger(v)||kind==='number'&&!(typeof v==='number'&&Number.isFinite(v))||kind==='array'&&!Array.isArray(v))throw Error('Invalid type: '+k);
 }
 const catalog=await gateway('catalog');
 if(!catalog.result.ok||!catalog.result.implemented?.includes(name))throw Error('Tool not supported by active Revit engine');
 const {response,result}=await gateway(name,args);
 return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
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
