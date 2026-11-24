import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
const child=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});
const lines=createInterface({input:child.stdout});
const pending=new Map();let id=0;
lines.on('line',l=>{const j=JSON.parse(l);const p=pending.get(j.id);if(p){pending.delete(j.id);clearTimeout(p.timer);if(j.error)p.reject(Error(j.error.message));else p.resolve(j.result);}});
child.on('exit',()=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Adapter exited'))}pending.clear()});
function rpc(method,params){return new Promise((resolve,reject)=>{const n=++id;const timer=setTimeout(()=>{pending.delete(n);reject(Error('Read-only test timeout'))},15000);pending.set(n,{resolve,reject,timer});child.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method,params})+'\n')})}
async function call(name,args){const start=performance.now();const r=await rpc('tools/call',{name,arguments:args});if(r.isError)throw Error(r.content[0].text);return {data:JSON.parse(r.content[0].text),ms:Math.round(performance.now()-start)}}
try{
 const init=await rpc('initialize',{protocolVersion:'2025-06-18',capabilities:{},clientInfo:{name:'KalidisMatrix',version:'1.0'}});
 const list=await rpc('tools/list',{}),matrix=list.tools.map(t=>({name:t.name,readOnly:t.annotations?.readOnlyHint===true,status:'not_retested_this_run'}));
 assert.equal(new Set(matrix.map(t=>t.name)).size,matrix.length);
 const probes=[['snapshot',{}],['types',{}],['views',{}],['selection',{}],['list_categories',{}],['get_doors',{limit:10}],['list_phases',{}],['list_view_templates',{}],['get_worksets',{}],['get_linked_files',{}],['audit_warnings',{limit:5}],['audit_level_span',{limit:5}],['model_health',{}],['element_info',{ids:[354362]}]];
 for(const [n,args] of probes){const t=matrix.find(t=>t.name===n);if(!t||!t.readOnly)continue;try{const r=await call(n,args);assert(r.data.ok===true);t.status='passed_read_only';t.elapsedMs=r.ms}catch(e){t.status='failed';t.error=String(e.message).slice(0,200)}}
 const samples=[];for(let i=0;i<20;i++){const r=await call('snapshot',{});assert(r.data.ok===true);samples.push(r.ms)}
 const sorted=[...samples].sort((a,b)=>a-b);const summary={count:20,minMs:sorted[0],medianMs:(sorted[9]+sorted[10])/2,p95Ms:sorted[18],maxMs:sorted[19]};
 const report={engine:init.serverInfo,at:new Date().toISOString(),catalogCount:matrix.length,passed:matrix.filter(t=>t.status==='passed_read_only').length,failed:matrix.filter(t=>t.status==='failed').length,notRetested:matrix.filter(t=>t.status==='not_retested_this_run').length,snapshotBenchmark:summary,tools:matrix,note:'Read-only probe matrix; not universal individual coverage or large-model stress.'};
 await writeFile(process.argv[2]??'MATRIX-LIVE-127.json',JSON.stringify(report,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify({...report,tools:undefined}));if(report.failed)process.exitCode=2;
}finally{child.stdin.end();lines.close();}
