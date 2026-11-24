import {createHash} from 'node:crypto';import {mkdir,open,lstat} from 'node:fs/promises';import path from 'node:path';
export function importKey(project,sha256){if(!/^[a-f0-9]{64}$/i.test(sha256))throw Error('Invalid pinned hash');return createHash('sha256').update(path.resolve(project).toLowerCase()+'\n'+sha256.toLowerCase()).digest('hex')}
export function verifyPin(inventory,args){const p=inventory.items?.find(x=>x.fileName===args.fileName);if(!p?.ready||p.license!=='MIT'||!p.candidates?.some(c=>c.folder===args.folder&&c.state==='integrity_verified'))throw Error('Pinned catalogue provenance or integrity not verified');return p}
export function verifyAudit(pin,audit){const r=audit.result?.result;if(audit.status!=='RolledBack'||audit.result.status!=='verified'||r?.file!==pin.fileName||r.sha256?.toLowerCase()!==pin.sha256||r.loadedIntoProject!==false||!Array.isArray(r.types)||!r.types.length||(!(r.solidCount>0)&&!(r.curveElements>0)))throw Error('Matching independent RFA audit required before load');}
export async function reserveImport(root,project,pin,evidence={}){
 await mkdir(root,{recursive:true});const st=await lstat(root);if(!st.isDirectory()||st.isSymbolicLink())throw Error('Invalid catalogue import directory');
 const key=importKey(project,pin.sha256),file=path.join(root,key+'.json');
 let fd;try{fd=await open(file,'wx')}catch(e){if(e.code==='EEXIST')throw Error('Catalogue import already recorded or reserved; inspect existing family and do not load again');throw e}
 try{await fd.writeFile(JSON.stringify({key,source:pin.source,revision:pin.revision,sha256:pin.sha256,fileName:pin.fileName,at:new Date().toISOString(),...evidence}));await fd.sync()}finally{await fd.close()}
 return key;
}
export async function assertNotRecorded(root,project,pin){const file=path.join(root,importKey(project,pin.sha256)+'.json');try{await lstat(file);throw Error('Catalogue import already recorded or reserved; inspect existing family and do not load again')}catch(e){if(e.code!=='ENOENT')throw e}}
