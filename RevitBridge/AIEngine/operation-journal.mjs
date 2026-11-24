// Explicit operation IDs prevent accidental replay across adapter restarts.
import {mkdir,open,readFile,writeFile,rename} from 'node:fs/promises';
import {createHash,randomUUID} from 'node:crypto';
import path from 'node:path';
function stable(v){if(Array.isArray(v))return v.map(stable);if(v&&typeof v==='object')return Object.fromEntries(Object.keys(v).sort().map(k=>[k,stable(v[k])]));return v}
export async function once(directory,id,name,args,execute){
 if(typeof id!=='string'||!/^[A-Za-z0-9_-]{8,100}$/.test(id))throw Error('Invalid requestId');
 await mkdir(directory,{recursive:true});
 const file=path.join(directory,id+'.json');
 const digest=createHash('sha256').update(JSON.stringify(stable({name,args}))).digest('hex');
 let handle;
 try{handle=await open(file,'wx')}catch(e){
  if(e.code!=='EEXIST')throw e;
  let old;try{old=JSON.parse(await readFile(file,'utf8'))}catch{throw Error('Existing request state unreadable; do not retry')}
  if(old.digest!==digest)throw Error('requestId already used for a different operation');
  throw Error('requestId already recorded ('+old.state+'); inspect model and do not retry automatically');
 }
 try{await handle.writeFile(JSON.stringify({digest,name,state:'started',at:new Date().toISOString()}));await handle.sync()}finally{await handle.close()}
 // Any interruption leaves the started record; never retry automatically.
 let result=await execute();
 const temp=file+'.'+randomUUID()+'.tmp';
 await writeFile(temp,JSON.stringify({digest,name,state:result?.isError?'reported_error':'completed',at:new Date().toISOString()}),{flag:'wx'});
 await rename(temp,file);
 return result;
}
