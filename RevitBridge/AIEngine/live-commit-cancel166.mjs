import {spawn} from 'node:child_process';import {createInterface} from 'node:readline';import {randomUUID} from 'node:crypto';import assert from 'node:assert/strict';
function client(){const p=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});const rl=createInterface({input:p.stdout});let id=0;const pending=new Map();rl.on('line',l=>{const j=JSON.parse(l),x=pending.get(j.id);if(x){pending.delete(j.id);clearTimeout(x.t);x.resolve(j)}});p.on('exit',()=>{for(const x of pending.values()){clearTimeout(x.t);x.reject(Error('Adapter exited'))}pending.clear()});return {async call(name,args={},allow=false){const n=++id;const j=await new Promise((resolve,reject)=>{const t=setTimeout(()=>{pending.delete(n);reject(Error('Timeout; inspect model'))},45000);pending.set(n,{resolve,reject,t});p.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method:'tools/call',params:{name,arguments:args}})+'\n')});if(j.error||j.result.isError&&!allow)throw Error(j.error?.message??j.result.content[0].text);const raw=j.result.content[0].text;try{return JSON.parse(raw)}catch{return {error:raw}}},close(){p.stdin.end();rl.close()}}}
const a=client(),b=client();
try{
 const c=await a.call('project_context');assert(c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt')&&!c.modified);
 let canceled=0,committed=0;
 for(let attempt=0;attempt<12&&(canceled===0||committed===0);attempt++){
  const jobId=randomUUID().replaceAll('-',''),prefix='CommitRace166-'+jobId.slice(0,12);
  const steps=Array.from({length:attempt%2===0?30:1},(_,i)=>({action:'material',name:prefix+'-'+i}));
  let done=false;const work=a.call('batch',{expectedPath:c.path,jobId,steps,commit:true},true).then(r=>{done=true;return r});
  let phase=null;let late=null;
  if(attempt%2===0){
   for(let i=0;i<150&&!done&&!phase;i++){
    const q=(await b.call('engine_health')).queue;
    if(q.running&&q.activeAction==='batch'){const r=await b.call('cancel_batch',{jobId},true);if(r.ok)phase=r.phase;else late=r.phase}
   }
  }else{
   // Intentionally let this small batch cross the commit gate before the final request.
   await work;
   const r=await b.call('cancel_batch',{jobId},true);
   assert(!r.ok&&r.phase==='not_active_or_commit_started');late=r.phase;
  }
  const r=await work;
  const elements=(await b.call('elements',{query:prefix,limit:500})).elements;
  if(phase){
   assert(['pending','running'].includes(phase));assert(/canceled before (execution|commit)/i.test(r.error??''));assert(!r.committed);assert.equal(elements.length,0);
   if(phase==='running')canceled++;
  }else{
   assert(r.committed&&r.verified);assert.equal(r.elementIds.length,steps.length);assert.equal(elements.length,steps.length);
   const found=new Set(elements.map(e=>e.id));assert(r.elementIds.every(id=>found.has(id)));
   const ids=r.elementIds;const preview=await a.call('edit_elements',{expectedPath:c.path,steps:ids.map(id=>({action:'delete',elementId:id})),commit:false});
   assert.equal(preview.status,'RolledBack');assert(preview.results.every(v=>v.deletedIds.length===1));
   const del=await a.call('edit_elements',{expectedPath:c.path,steps:ids.map(id=>({action:'delete',elementId:id})),commit:true});
   assert.equal(del.status,'Committed');assert.equal((await b.call('elements',{query:prefix,limit:500})).elements.length,0);committed++;
  }
  const q=(await b.call('engine_health')).queue;assert(!q.running&&!q.pending&&q.started===q.completed);
  console.log(JSON.stringify({attempt,phase,late,committed:r.committed===true,count:steps.length}));
 }
 assert(canceled>0&&committed>0);
 const final=await a.call('save_test',{expectedPath:c.path});assert(final.ok);
 console.log('COMMIT_GATE_OUTCOMES_PASS '+JSON.stringify({canceled,committed}));
}finally{a.close();b.close()}