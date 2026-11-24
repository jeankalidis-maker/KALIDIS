import {open,stat,lstat,unlink,realpath} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
async function hash(f){const h=createHash('sha256');const fd=await open(f,'r');try{for await(const b of fd.createReadStream({autoClose:false}))h.update(b)}finally{await fd.close()}return h.digest('hex')}
async function normal(p){const absolute=path.resolve(p);if(process.platform==='win32'&&!/^[a-z]:\\/i.test(absolute))throw Error('Local drive only');if((await realpath(absolute)).toLowerCase()!==absolute.toLowerCase())throw Error('Path alias or reparse point');return absolute}
export async function stageCopy(source,root,name,expected){
 if(!/^[a-f0-9]{64}$/i.test(expected)||!/^ReviewCopy-[a-zA-Z0-9_-]{8,80}\.rvt$/.test(name))throw Error('Invalid SHA256 or new review name');
 const src=await normal(source),dir=await normal(root);
 if(path.basename(dir)!=='Sandbox'||path.extname(src).toLowerCase()!=='.rvt')throw Error('Sandbox and RVT required');
 const s=await lstat(src),d=await lstat(dir);if(!s.isFile()||s.isSymbolicLink()||s.nlink!==1||!d.isDirectory()||d.isSymbolicLink())throw Error('Invalid regular file or sandbox');
 if(s.size<1024||s.size>2*1024**3)throw Error('Unsupported file size');
 if(await hash(src)!==expected.toLowerCase())throw Error('Source hash mismatch');
 const target=path.join(dir,name);let dst,created=false;
 try{
  dst=await open(target,'wx');created=true;const fd=await open(src,'r');
  try{const h=createHash('sha256');for await(const b of fd.createReadStream({autoClose:false})){h.update(b);await dst.writeFile(b)}if(h.digest('hex')!==expected.toLowerCase())throw Error('Source changed during copy')}finally{await fd.close()}
  await dst.sync();await dst.close();dst=null;
  const after=await stat(src);
  if(after.size!==s.size||after.mtimeMs!==s.mtimeMs||await hash(src)!==expected.toLowerCase()||await hash(target)!==expected.toLowerCase())throw Error('Copy verification failed');
  return {ok:true,name,path:target,bytes:s.size,sha256:expected.toLowerCase(),sourceUnchanged:true,note:'Byte copy only. No detach, cloud/worksharing conversion or automatic opening; native gate still applies.'};
 }catch(e){if(dst)await dst.close();if(created)await unlink(target);throw e}
}
if(process.argv[1]&&path.basename(process.argv[1])==='stage-project-copy.mjs'){try{console.log(JSON.stringify(await stageCopy(...process.argv.slice(2))))}catch(e){console.error(e.message);process.exitCode=1}}
