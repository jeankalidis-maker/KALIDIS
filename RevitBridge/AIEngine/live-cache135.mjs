import {spawn} from 'node:child_process';import {createInterface} from 'node:readline';import {randomUUID} from 'node:crypto';import assert from 'node:assert/strict';
const p=spawn(process.execPath,['mcp-server.mjs'],{stdio:['pipe','pipe','inherit']});const lines=createInterface({input:p.stdout});let id=0;const pending=new Map();
lines.on('line',l=>{const j=JSON.parse(l),h=pending.get(j.id);if(h){pending.delete(j.id);clearTimeout(h.t);h.resolve(j)}});
p.on('exit',()=>{for(const h of pending.values()){clearTimeout(h.t);h.reject(Error('Adapter exited'))}pending.clear()});
async function call(name,args={}){const n=++id;const j=await new Promise((resolve,reject)=>{const t=setTimeout(()=>{pending.delete(n);reject(Error('Timeout; inspect model before retry'))},45000);pending.set(n,{t,resolve,reject});p.stdin.write(JSON.stringify({jsonrpc:'2.0',id:n,method:'tools/call',params:{name,arguments:args}})+'\n')});if(j.error||j.result.isError)throw Error(j.error?.message??j.result.content[0].text);return JSON.parse(j.result.content[0].text)}
const brief=s=>({hit:s.cacheHit,revision:s.revision,session:s.session,ms:s.elapsedMs});
try{
 const c=await call('project_context');assert(c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt')&&!c.workshared&&!c.cloud&&!c.readOnly&&!c.family);
 const a=await call('snapshot'),b=await call('snapshot');console.log('CACHE_HIT '+JSON.stringify([brief(a),brief(b)]));assert(b.cacheHit&&a.revision===b.revision&&a.session===b.session);
 await new Promise(r=>setTimeout(r,10500));const ttl=await call('snapshot');console.log('CACHE_TTL '+JSON.stringify(brief(ttl)));assert(!ttl.cacheHit);
 const q=await call('project_context'),name='Cache135-'+randomUUID();const args={expectedPath:c.path,expectedSession:q.session,expectedRevision:q.revision,steps:[{action:'material',name}],commit:false};const d=await call('batch',args);assert(d.verified&&!d.committed);
 const current=await call('project_context');const r=await call('batch',{...args,expectedRevision:current.revision,commit:true});assert(r.verified&&r.committed&&r.elementIds.length===1);console.log('FIXTURE '+r.elementIds[0]);
 const n=await call('snapshot'),h=await call('snapshot');console.log('CACHE_CHANGE '+JSON.stringify([brief(n),brief(h)]));assert(n.revision>current.revision&&!n.cacheHit&&h.cacheHit);
 const del=await call('edit_elements',{expectedPath:c.path,steps:[{action:'delete',elementId:r.elementIds[0]}],commit:true});assert(del.status==='Committed'&&del.results[0].deletedIds.length===1);
 const out=await call('snapshot');assert(!out.cacheHit&&out.revision>n.revision);
 const copy=await call('open_test_project',{fileName:'ReviewCopy-9edd1758940540df97a8e8cf8b099d3f.rvt'});const s=await call('snapshot'),s2=await call('snapshot');assert(s.path===copy.path&&!s.cacheHit&&s2.cacheHit);
 await call('open_test_project',{fileName:'Smoke-9c3c99fd336746a0b555995ee2728a38.rvt'});const back=await call('snapshot'),back2=await call('snapshot');assert(back.path===c.path&&!back.cacheHit&&back2.cacheHit);console.log('CACHE_POST_DEPLOY_PASS');
}finally{p.stdin.end();lines.close()}
