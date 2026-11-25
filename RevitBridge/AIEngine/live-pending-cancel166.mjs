import {spawn} from 'node:child_process';import {createInterface} from 'node:readline';import {randomUUID} from 'node:crypto';import assert from 'node:assert/strict';
function client(){const p=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});const rl=createInterface({input:p.stdout});let id=0;const pending=new Map();rl.on('line',l=>{const j=JSON.parse(l),x=pending.get(j.id);if(x){pending.delete(j.id);clearTimeout(x.t);x.resolve(j)}});p.on('exit',()=>{for(const x of pending.values()){clearTimeout(x.t);x.reject(Error('Adapter exited'))}pending.clear()});return {async call(name,args={},allow=false){const n=++id;const j=await new Promise((resolve,reject)=>{const t=setTimeout(()=>{pending.delete(n);reject(Error('Timeout; inspect model'))},45000);pending.set(n,{resolve,reject,t});p.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method:'tools/call',params:{name,arguments:args}})+'\n')});if(j.error||j.result.isError&&!allow)throw Error(j.error?.message??j.result.content[0].text);const raw=j.result.content[0].text;try{return JSON.parse(raw)}catch{return {error:raw}}},close(){p.stdin.end();rl.close()}}}
const a=client(),b=client();
try{
 const c=await a.call('project_context');assert(c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt')&&!c.modified);
 const q0=(await b.call('engine_health')).queue;assert(!q0.running&&!q0.pending);
 const seen=new Set();
 for(let attempt=0;attempt<40&&!seen.has('pending');attempt++){
  const jobId=randomUUID().replaceAll('-',''),prefix='Pending166-'+jobId.slice(0,12);
  const steps=Array.from({length:35},(_,i)=>({action:'material',name:prefix+'-'+i}));
  let done=false;const work=a.call('batch',{expectedPath:c.path,jobId,steps,commit:false},true).then(r=>{done=true;return r});
  let phase=null;
  for(let i=0;i<100&&!done&&!phase;i++){
   const r=await b.call('cancel_batch',{jobId},true);if(r.ok===true)phase=r.phase;
  }
  const r=await work;
  if(phase){assert(['pending','running'].includes(phase));assert(phase==='pending'?/canceled before execution/i.test(r.error??''):/canceled before commit/i.test(r.error??''));seen.add(phase)}
  else assert(r.verified&&!r.committed,JSON.stringify(r));
  const q=(await b.call('engine_health')).queue;assert(!q.running&&!q.pending&&q.started===q.completed);
  assert.equal((await b.call('elements',{query:prefix,limit:500})).elements.length,0);
  console.log(JSON.stringify({attempt,phase,result:r.error??r.status,canceled:q.canceled}));
 }
 const q1=(await b.call('engine_health')).queue;
 if(seen.has('pending'))assert(q1.canceled>q0.canceled);
 console.log('ACTUAL_PHASES '+JSON.stringify([...seen]));
 console.log('CANCELED_COUNTER_DELTA '+(q1.canceled-q0.canceled));
 if(!seen.has('pending'))process.exitCode=2;
}finally{a.close();b.close()}