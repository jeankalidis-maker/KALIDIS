// Explicit operation IDs prevent accidental replay across adapter restarts.
import {mkdir,open,readFile,writeFile,rename} from 'node:fs/promises';
import {createHash,randomUUID} from 'node:crypto';
import path from 'node:path';
function stable(v){if(Array.isArray(v))return v.map(stable);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stable(v[k])]));return v}
export async function once(directory,id,name,args,execute){
 if(typeof id!=='string'||!/^[A-Za-z0-9_-]{8,100}$/.test(id))throw Error('Invalid requestId');
 await mkdir(directory,{recursive:true});
 const file=path.join(directory,id+'.json');
 const scope=typeof args.expectedPath==='string'&&typeof args.expectedSession==='string'?{pathDigest:createHash('sha256').update(path.resolve(args.expectedPath).toLowerCase()).digest('hex'),session:args.expectedSession,creationOnly:name==='batch'&&Array.isArray(args.steps)&&args.steps.length>0&&args.steps.every(s=>['material','wall','floor','ceiling','column','beam','foundation','pipe_sized','duct_sized','conduit_sized','cable_tray_sized','component','hosted_component'].includes(s.action))}:null;
 const digest=createHash('sha256').update(JSON.stringify(stable({name,args}))).digest('hex');
 let handle;
 try{handle=await open(file,'wx')}catch(e){
  if(e.code!=='EEXIST')throw e;
  let old;try{old=JSON.parse(await readFile(file,'utf8'))}catch{throw Error('Existing request state unreadable; do not retry')}
  if(old.digest!==digest)throw Error('requestId already used for a different operation');
  const status=await operationStatus(directory,id);
  throw Error('requestId already recorded ('+status.state+'); inspect model and do not retry automatically');
 }
 try{await handle.writeFile(JSON.stringify({digest,name,scope,state:'started',at:new Date().toISOString()}));await handle.sync()}finally{await handle.close()}
 // Any interruption leaves the started record; never retry automatically.
 let result=await execute();
 const temp=file+'.'+randomUUID()+'.tmp';
 const receipt={};let unknown=false;
 try{
  const payload=JSON.parse(result.content[0].text);
  unknown=payload.error==="execution_state_unknown_do_not_retry"||payload.status==="Pending";
  for(const k of ['ok','verified','committed','dryRun'])if(typeof payload[k]==='boolean')receipt[k]=payload[k];
  if(['Committed','RolledBack'].includes(payload.status))receipt.status=payload.status;
  if(Array.isArray(payload.elementIds)&&payload.elementIds.length<=200&&payload.elementIds.every(Number.isSafeInteger))receipt.elementIds=payload.elementIds;
 }catch{}
 const resultDigest=createHash('sha256').update(JSON.stringify(stable(result??null))).digest('hex');
 const endHandle=await open(temp,'wx');
 try{await endHandle.writeFile(JSON.stringify({digest,name,scope,state:unknown?'unknown':result?.isError?'reported_error':'completed',at:new Date().toISOString(),receipt,resultDigest}));await endHandle.sync()}finally{await endHandle.close()}
 await rename(temp,file+'.completed');
 return result;
}

export async function operationStatus(directory,id){
 if(typeof id!=='string'||!/^[A-Za-z0-9_-]{8,100}$/.test(id))throw Error('Invalid requestId');
 let raw;try{raw=JSON.parse(await readFile(path.join(directory,id+'.json'),'utf8'))}catch(e){
  if(e.code==='ENOENT')return {requestId:id,state:'not_found',safeToRetry:false};
  throw Error('Operation record unreadable; inspect model before proceeding');
 }
 if(!['started','completed','reported_error'].includes(raw.state)||typeof raw.name!=='string')throw Error('Invalid operation record');
 if(raw.state==='started'){
  let end;try{end=JSON.parse(await readFile(path.join(directory,id+'.json.completed'),'utf8'))}catch(e){if(e.code!=='ENOENT')throw Error('Completion record unreadable; inspect model before proceeding')}
  if(end){if(end.digest!==raw.digest||end.name!==raw.name||!['completed','reported_error','unknown'].includes(end.state))throw Error('Completion record mismatch');raw=end}
 }
 return {requestId:id,command:raw.name,state:raw.state,scope:raw.scope??null,at:raw.at,receipt:raw.receipt??null,resultDigest:raw.resultDigest??null,safeToRetry:false,note:['started','unknown'].includes(raw.state)?'Execution outcome unknown; inspect model.':'Adapter outcome only; verify model independently.'};
}
