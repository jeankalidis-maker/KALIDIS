// Native Node.js MCP stdio adapter. No external npm dependencies.
// Connects only to the authenticated loopback Revit gateway.
import readline from 'node:readline';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.dirname(fileURLToPath(import.meta.url));
const manifest=JSON.parse(await readFile(path.join(root,'tools.json'),'utf8'));
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
 return manifest.filter(t=>live.has(t.name)&&(!readonly||t.annotations.readOnlyHint));
}
async function call(name,args){
 const t=tools.get(name);
 if(!t)throw Error('Unknown tool');
 if(readonly&&!t.annotations.readOnlyHint)throw Error('Read-only session');
 if(!args||typeof args!=='object'||Array.isArray(args))throw Error('Invalid arguments');
 for(const k of Object.keys(args))if(!Object.hasOwn(t.inputSchema.properties,k))throw Error('Unknown argument '+k);
 for(const k of t.inputSchema.required||[])if(!Object.hasOwn(args,k))throw Error('Missing '+k);
 for(const [k,v] of Object.entries(args)){
  const schema=t.inputSchema.properties[k]; const kind=schema.type;
  if(kind==='string'&&typeof v!=='string'||kind==='boolean'&&typeof v!=='boolean'||kind==='integer'&&!Number.isSafeInteger(v)||kind==='number'&&!(typeof v==='number'&&Number.isFinite(v))||kind==='array'&&!Array.isArray(v))throw Error('Invalid type: '+k);
 }
 const {response,result}=await gateway(name,args);
 return {content:[{type:'text',text:JSON.stringify(result)}],isError:!response.ok||result.ok!==true};
}
async function dispatch(r){
 if(r?.jsonrpc!=='2.0'||typeof r.method!=='string'){error(r?.id??null,-32600,'Invalid request');return}
 if(!Object.hasOwn(r,'id'))return;
 try{
 let result;
 switch(r.method){
 case 'initialize':result={protocolVersion:['2025-06-18','2025-03-26','2024-11-05'].includes(r.params?.protocolVersion)?r.params.protocolVersion:'2025-06-18',capabilities:{tools:{}},serverInfo:{name:'Kalidis AI Engine',version:'0.3.1'},instructions:'Units mm. Query snapshot and types first. Writes restricted to local Sandbox project; transactions dry-run unless commit=true. Do not retry unknown execution state.'};break;
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
