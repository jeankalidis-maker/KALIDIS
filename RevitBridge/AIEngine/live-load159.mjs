import {spawn} from 'node:child_process';import {createInterface} from 'node:readline';import {writeFile} from 'node:fs/promises';import assert from 'node:assert/strict';
function client(){const p=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});const lines=createInterface({input:p.stdout});let id=0;const pending=new Map();lines.on('line',l=>{const j=JSON.parse(l),h=pending.get(j.id);if(h){pending.delete(j.id);clearTimeout(h.t);h.resolve(j)}});p.on('exit',()=>{for(const h of pending.values()){clearTimeout(h.t);h.reject(Error('Adapter exited'))}pending.clear()});return {async call(name,args={}){const n=++id,t0=performance.now();const j=await new Promise((resolve,reject)=>{const t=setTimeout(()=>{pending.delete(n);reject(Error('Request timeout'))},45000);pending.set(n,{resolve,reject,t});p.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method:'tools/call',params:{name,arguments:args}})+'\n')});let data;try{data=JSON.parse(j.result.content[0].text)}catch{data={error:j.error?.message??j.result?.content?.[0]?.text}}return {data,error:!!j.error||!!j.result?.isError,ms:Math.round(performance.now()-t0)}},close(){p.stdin.end();lines.close()}}}
const clients=Array.from({length:5},client),a=clients[0];
const stats=s=>{const t=[...s].sort((a,b)=>a-b);return {count:t.length,min:t[0],median:t[Math.floor(t.length/2)],p95:t[Math.ceil(t.length*.95)-1],max:t.at(-1)}};
const start=Date.now();
try{
 const c=(await a.call('project_context')).data;assert(c.ok&&c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt')&&!c.modified);
 const before=(await a.call('engine_health')).data;assert(before.ok&&!before.queue.running&&!before.queue.pending);
 const sequential=[],parallel=[],results={ok:0,busy:0,other:0},epochs=[];
 for(let epoch=0;epoch<4;epoch++){
  for(let i=0;i<75;i++){const r=await a.call(i%3===0?'project_context':'snapshot');assert(r.data.ok&&!r.error);sequential.push(r.ms)}
  for(let i=0;i<10;i++){const rs=await Promise.all(clients.map(x=>x.call('snapshot')));for(const r of rs){parallel.push(r.ms);if(r.data.ok===true&&!r.error)results.ok++;else if(/Revit busy|^busy$/.test(r.data.error??''))results.busy++;else{results.other++;console.log('UNEXPECTED',JSON.stringify(r))}}}
  const q=(await a.call('engine_health')).data.queue;assert(!q.running&&!q.pending&&q.started===q.completed);epochs.push({epoch,elapsedMs:Date.now()-start,queue:q});console.log('EPOCH',epoch+1,'OK',results.ok,'BUSY',results.busy);
  if(epoch===1){clients[4].close();clients[4]=client();const r=await clients[4].call('project_context');assert.equal(r.data.session,c.session);assert.equal(r.data.revision,c.revision);console.log('RECONNECT_PASS')}
 }
 const after=(await a.call('engine_health')).data,ctx=(await a.call('project_context')).data;
 assert.equal(ctx.session,c.session);assert.equal(ctx.path,c.path);assert.equal(ctx.revision,c.revision);assert.equal(ctx.modified,c.modified);assert.equal(ctx.selectionCount,c.selectionCount);assert(!after.queue.running&&!after.queue.pending&&after.queue.started===after.queue.completed);assert.equal(results.other,0);
 const report={at:new Date().toISOString(),elapsedMs:Date.now()-start,session:c.session,serial:stats(sequential),parallel:stats(parallel),parallelResults:results,queueBefore:before.queue,queueAfter:after.queue,epochs,reconnect:'passed',contextStable:true,note:'Finite 500-call smoke load on current experimental model; not a multi-hour or large-project qualification.'};
 await writeFile('LOAD-LIVE-159.json',JSON.stringify(report,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify(report));console.log('LOAD_RECONNECT_159_PASS');
}finally{for(const c of clients)c.close()}
