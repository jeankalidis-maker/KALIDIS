import {spawn} from 'node:child_process';import {createInterface} from 'node:readline';import {randomUUID} from 'node:crypto';import assert from 'node:assert/strict';
function client(){const p=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});const rl=createInterface({input:p.stdout});let id=0;const pending=new Map();rl.on('line',l=>{const j=JSON.parse(l),x=pending.get(j.id);if(x){pending.delete(j.id);clearTimeout(x.t);x.resolve(j)}});p.on('exit',()=>{for(const x of pending.values()){clearTimeout(x.t);x.reject(Error('Adapter exited'))}pending.clear()});return {async call(name,args={},allow=false){const n=++id;const j=await new Promise((resolve,reject)=>{const t=setTimeout(()=>{pending.delete(n);reject(Error('Timeout; inspect model'))},45000);pending.set(n,{resolve,reject,t});p.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method:'tools/call',params:{name,arguments:args}})+'\n')});if(j.error||j.result.isError&&!allow)throw Error(j.error?.message??j.result.content[0].text);const raw=j.result.content[0].text;try{return JSON.parse(raw)}catch{return {error:raw}}},close(){p.stdin.end();rl.close()}}}
const a=client(),b=client();
try{
 const c=await a.call('project_context');assert(c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt'));
 const health=await b.call('engine_health');assert(health.ok&&!health.queue.running&&!health.queue.pending);
 const seen=new Set();
 for(const target of ['pending','running']){
  for(let attempt=0;attempt<5&&!seen.has(target);attempt++){
   const jobId=randomUUID().replaceAll('-',''),prefix='Cancel137-'+jobId.slice(0,12);
   const steps=Array.from({length:190},(_,i)=>({action:'material',name:prefix+'-'+i}));
   let done=false;const work=a.call('batch',{expectedPath:c.path,jobId,steps,commit:false},true).then(r=>{done=true;return r});
   let accepted=false,phase;
   for(let i=0;i<100&&!done&&!accepted;i++){
    const q=(await b.call('engine_health')).queue;
    if(target==='pending'&&q.pending||target==='running'&&q.running&&q.activeAction==='batch'){
     phase=target;
     const r=await b.call('cancel_batch',{jobId},true);accepted=r.ok===true;
    }
   }
   const r=await work;
   if(accepted){assert(/canceled before commit/i.test(r.error??''));seen.add(phase)}
   else assert(r.verified&&!r.committed||/canceled before commit/i.test(r.error??''),JSON.stringify(r));
   assert.equal((await b.call('elements',{query:prefix,limit:500})).elements.length,0);
   console.log(JSON.stringify({target,attempt,accepted,phase,result:r.error??r.status??'rollback'}));
  }
 }
 const lateId=randomUUID().replaceAll('-',''),name='CancelLate137-'+lateId;
 const commit=await a.call('batch',{expectedPath:c.path,jobId:lateId,steps:[{action:'material',name}],commit:true});assert(commit.committed&&commit.verified);
 const late=await b.call('cancel_batch',{jobId:lateId},true);assert(late.ok===false);
 const del=await a.call('edit_elements',{expectedPath:c.path,steps:[{action:'delete',elementId:commit.elementIds[0]}],commit:true});assert(del.status==='Committed');
 const final=await b.call('engine_health');assert(!final.queue.running&&!final.queue.pending&&final.queue.started===final.queue.completed);
 console.log('PHASES '+JSON.stringify([...seen]));console.log('LATE_CANCEL_REFUSED_AND_FIXTURE_REMOVED');console.log('HEALTH '+JSON.stringify(final.queue));if(!seen.has('running')||!seen.has('pending'))process.exitCode=2;
}finally{a.close();b.close()}
