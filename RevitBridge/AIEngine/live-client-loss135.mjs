import {spawn} from 'node:child_process';import {createInterface} from 'node:readline';import {randomUUID} from 'node:crypto';import assert from 'node:assert/strict';
function client(){const p=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});const lines=createInterface({input:p.stdout});let id=0;const pending=new Map();lines.on('line',l=>{const j=JSON.parse(l),h=pending.get(j.id);if(h){pending.delete(j.id);clearTimeout(h.t);h.resolve(j)}});p.on('exit',()=>{for(const h of pending.values()){clearTimeout(h.t);h.reject(Error('Client disconnected'))}pending.clear()});return {async call(name,args={},allow=false){const n=++id;const j=await new Promise((resolve,reject)=>{const t=setTimeout(()=>{pending.delete(n);reject(Error('Timeout'))},45000);pending.set(n,{t,resolve,reject});p.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method:'tools/call',params:{name,arguments:args}})+'\n')});if(j.error)throw Error(j.error.message);const raw=j.result.content[0].text;if(j.result.isError&&!allow)throw Error(raw);try{return JSON.parse(raw)}catch{return {error:raw}}},close(){p.stdin.end();lines.close()},disconnect(){p.kill()}}}
const a=client(),b=client();const pause=ms=>new Promise(r=>setTimeout(r,ms));
try{
 const c=await a.call('project_context');assert(c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt'));
 const requestId='drop135-'+randomUUID(),prefix='Drop135-'+randomUUID().slice(0,8);
 const steps=Array.from({length:180},(_,i)=>({action:'material',name:prefix+'-'+i}));
 const args={expectedPath:c.path,steps,commit:false,requestId};
 const work=a.call('batch',args,true).then(r=>({returned:r}),e=>({disconnected:e.message}));
 let journal,observed=false;
 for(let i=0;i<20;i++){journal=await b.call('operation_status',{requestId});if(journal.state==='started'){observed=true;break}if(journal.state==='completed')break;await pause(20)}
 assert(observed,'No in-flight journal observed');
 const busy=await b.call('snapshot',{},true);
 console.log('CONCURRENT_READ '+JSON.stringify({ok:busy.ok,error:busy.error??null}));assert(/Revit busy/.test(busy.error??''),'No busy response observed');
 a.disconnect();const lost=await work;console.log('CLIENT_OUTCOME '+JSON.stringify(lost));assert(lost.disconnected,'Client received outcome before disconnect');
 let after;
 for(let i=0;i<30;i++){after=await b.call('elements',{query:prefix,limit:500},true);if(after.ok===true)break;await pause(150)}
 assert(after.ok&&after.elements.length===0,'Preview residue or unavailable');
 journal=await b.call('operation_status',{requestId});assert(journal.safeToRetry===false&&['started','completed','unknown','reported_error'].includes(journal.state));
 const duplicate=await b.call('batch',args,true);assert(/already recorded/.test(duplicate.error??''));
 const restored=await b.call('project_context');assert(restored.path===c.path&&restored.session===c.session);
 console.log('RECOVERY '+JSON.stringify({state:journal.state,receipt:journal.receipt,matchingMaterials:after.elements.length,duplicate:duplicate.error,reconnected:true}));console.log('CLIENT_LOSS_LIVE=PASS');
}finally{a.close();b.close()}
