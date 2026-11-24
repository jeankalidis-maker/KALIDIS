// Local inventory for the reviewed external RFA catalogue. No downloads or model edits.
import {readdir,lstat,readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {validate} from './command-registry.mjs';
const source='magnetar-io/revit-test-datasets',revision='fd755e18cec40ed016d06b082f420d796571fef1';
const pins=[
 ['Column_Sqaure.rfa','Column Square','coluna quadrada pilar',360448,'0660610efb4b6c8dd8717be4c4c1b6f74f044643c7d427590e6c122a02720bf9'],
 ['I_Single-Flush.rfa','Single Flush Door','porta folha simples',307200,'66ae731f85416ba0544038d9e442aaf399ff99ae8e1f4388be980b6636549d1f'],
 ['Mullion_Rectangle.rfa','Rectangular Mullion Profile','perfil retangular montante',512000,'745e6c9bc4e39eba1a109f50641d46dd78bfde165684af464b1e24e6b34338ef'],
 ['Column_Square_Tollerance.rfa','Structural Square Column Tolerance','pilar estrutural quadrado coluna',372736,'7fc2c31851d25c6611418cbb50c3e6a0cfaecdf345f012ecde7058fe596d512f']
].map(([fileName,name,keywords,bytes,sha256])=>({fileName,name,keywords,bytes,sha256,license:'MIT',source,revision}));
export const inventoryTool={name:'bim_catalog_inventory',description:'Search the four pinned external RFA catalogue entries in English or Portuguese; verify local quarantine file integrity and provenance. No network or project modifications.',inputSchema:{type:'object',properties:{query:{type:'string',maxLength:200}},additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const norm=s=>s.normalize('NFD').replace(/\p{Diacritic}/gu,'').toLowerCase();
async function bounded(file,limit){
 const st=await lstat(file);if(!st.isFile()||st.isSymbolicLink()||st.size>limit)throw Error('not a bounded regular file');
 return readFile(file);
}
export async function inventory(root,args={}){
 validate(inventoryTool.inputSchema,args,'bim_catalog_inventory');
 if(path.basename(root)!=='Sandbox')throw Error('Sandbox expected');
 const st=await lstat(root);if(!st.isDirectory()||st.isSymbolicLink())throw Error('Invalid Sandbox');
 const folders=(await readdir(root,{withFileTypes:true})).filter(x=>x.isDirectory()&&!x.isSymbolicLink()&&/^OnlineQuarantine-[a-f0-9]{32}$/.test(x.name));
 if(folders.length>200)throw Error('Too many quarantine directories; inspect manually');
 const words=norm(args.query??'').trim().split(/\s+/).filter(Boolean);
 const results=[];
 for(const pin of pins.filter(p=>words.every(w=>norm([p.fileName,p.name,p.keywords].join(' ')).includes(w)))){
  const candidates=[];
  for(const folder of folders){
   const dir=path.join(root,folder.name);
   const fp=path.join(dir,pin.fileName);
   try{await lstat(fp)}catch(e){if(e.code==='ENOENT')continue;throw e}
   let state='invalid',reason;
   try{
    const f=await bounded(fp,pin.bytes);
    if(f.length!==pin.bytes||createHash('sha256').update(f).digest('hex')!==pin.sha256||f.subarray(0,8).toString('hex')!=='d0cf11e0a1b11ae1')throw Error('Size, hash or header mismatch');
    const m=JSON.parse((await bounded(path.join(dir,'manifest.json'),8192)).toString('utf8').replace(/^\uFEFF/,''));
    if(m.sha256!==pin.sha256||m.source!==source||m.revision!==revision||m.license!=='MIT'||m.bytes!==pin.bytes)throw Error('Provenance manifest mismatch');
    const lic=(await bounded(path.join(dir,'LICENSE.txt'),8192)).toString('utf8');
    if(!lic.includes('MIT License')||!lic.includes('magnetar-io'))throw Error('License notice mismatch');
    state='integrity_verified';
   }catch(e){reason=e.message}
   candidates.push({folder:folder.name,state,...(reason?{reason}:{})});
  }
  results.push({...pin,candidates,ready:candidates.filter(x=>x.state==='integrity_verified').length===1});
 }
 return {ok:true,count:results.length,ready:results.filter(x=>x.ready).length,items:results,note:'File integrity/provenance only. Does not prove model compatibility, loaded state, security or all-type geometry.'};
}
