// Local Autodesk texture catalogue. No network, redistribution or installed-file modification.
import path from 'node:path';
import {lstat,readdir,readFile,writeFile,realpath} from 'node:fs/promises';
import {createHash,randomUUID} from 'node:crypto';
import {validate} from './command-registry.mjs';
const maxBytes=10000000,allowed=new Set(['.png','.jpg','.jpeg']);
const normalize=s=>s.normalize('NFD').replace(/\p{Diacritic}/gu,'').toLowerCase();
const hash=b=>createHash('sha256').update(b).digest('hex');
export const textureSearchTool={name:'local_texture_search',description:'Search installed Autodesk image textures by name, classify likely material and map role, and recommend a local PNG candidate. No license redistribution claim or physical scale inference.',inputSchema:{type:'object',properties:{query:{type:'string',maxLength:150},limit:{type:'integer',minimum:1,maximum:100},offset:{type:'integer',minimum:0,maximum:20000}},additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export const textureStageTool={name:'local_texture_stage',description:'Verify an explicit installed PNG texture ID and expected SHA256, then copy into a unique Sandbox fixture. No Revit edit. Only local installed assets, not redistribution.',inputSchema:{type:'object',properties:{assetId:{type:'string',pattern:'^[a-f0-9]{64}$'},sha256:{type:'string',pattern:'^[a-f0-9]{64}$'}},required:['assetId','sha256'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:false,openWorldHint:false}};
function meta(relative){
 const n=normalize(path.basename(relative));
 const role=/normal|nrm|bump|height|displace/.test(n)?'relief':/rough|gloss|specular|reflect/.test(n)?'surface':/opacity|alpha|transparen|cutout/.test(n)?'opacity':'color_candidate';
 const category=/wood|beech|oak|walnut|timber|maple|pine/.test(n)?'wood':/concrete|cement/.test(n)?'concrete':/ceramic|tile|porcelain/.test(n)?'ceramic':/steel|metal|alumin|copper/.test(n)?'metal':/stone|marble|granite/.test(n)?'stone':'other';
 return {assetId:hash(Buffer.from(relative.replace(/\\/g,'/').toLowerCase())),relative,name:path.basename(relative),category,role};
}
export async function textureIndex(root){
 const canonical=await realpath(root),s=await lstat(root);if(!s.isDirectory()||s.isSymbolicLink()||canonical.toLowerCase()!==path.resolve(root).toLowerCase())throw Error('Invalid texture root');
 const results=[],seen=new Set();let visited=0;
 async function walk(dir,depth){
  if(depth>12||++visited>2000)throw Error('Texture inventory bound exceeded');
  for(const d of await readdir(dir,{withFileTypes:true})){
   if(d.isSymbolicLink())continue;
   const p=path.join(dir,d.name);
   if(d.isDirectory()){if((await realpath(p)).toLowerCase()===p.toLowerCase())await walk(p,depth+1);continue;}
   if(!d.isFile()||!allowed.has(path.extname(p).toLowerCase()))continue;
   const st=await lstat(p);if(!st.isFile()||st.isSymbolicLink()||st.size<16||st.size>maxBytes)continue;
   const m=meta(path.relative(root,p));if(seen.has(m.assetId))throw Error('Duplicate asset identity');seen.add(m.assetId);results.push({...m,bytes:st.size,format:path.extname(p).slice(1).toLowerCase()});
   if(results.length>20000)throw Error('Inventory too large');
  }
 }
 await walk(root,0);return results.sort((a,b)=>a.relative.localeCompare(b.relative));
}
const terms={madeira:'wood',concreto:'concrete',ceramica:'ceramic',metal:'metal',pedra:'stone',carvalho:'oak',nogueira:'walnut'};
export function matchTextures(rows,query=''){
 const words=normalize(query).trim().split(/\s+/).filter(Boolean).map(x=>terms[x]??x);
 return rows.filter(r=>words.every(w=>normalize([r.relative,r.category,r.role].join(' ')).includes(w))).sort((a,b)=>Number(b.role==='color_candidate')-Number(a.role==='color_candidate')||Number(b.format==='png')-Number(a.format==='png')||b.bytes-a.bytes||a.relative.localeCompare(b.relative));
}
function pngDimensions(b){if(b.length<33||b.subarray(0,8).toString('hex')!=='89504e470d0a1a0a'||b.readUInt32BE(8)!==13||b.toString('ascii',12,16)!=='IHDR')throw Error('PNG header invalid');const w=b.readUInt32BE(16),h=b.readUInt32BE(20);if(Math.min(w,h)<16||Math.max(w,h)>4096)throw Error('Unsupported PNG dimensions');return [w,h]}
async function inspect(root,row){
 const p=path.join(root,row.relative),r=await realpath(p),s=await lstat(p);if(r.toLowerCase()!==p.toLowerCase()||!s.isFile()||s.isSymbolicLink()||s.size!==row.bytes||s.size>maxBytes)throw Error('Asset changed');
 const b=await readFile(p);if(b.length!==row.bytes)throw Error('Asset size changed');
 return {b,sha256:hash(b),sizePx:row.format==='png'?pngDimensions(b):null};
}
const rights='Installed Autodesk asset; local use subject to installed license. Redistribution permission not verified; do not upload or distribute.';
export async function textureSearch(a,root){
 validate(textureSearchTool.inputSchema,a,'local_texture_search');
 const all=await textureIndex(root),matches=matchTextures(all,a.query),offset=a.offset??0,rows=matches.slice(offset,offset+(a.limit??30)),items=[];
 for(const row of rows){try{const q=await inspect(root,row);items.push({...row,sha256:q.sha256,sizePx:q.sizePx,ready:true,license: rights})}catch(e){items.push({...row,ready:false,reason:e.message})}}
 const recommendation=items.find(x=>x.ready&&x.role==='color_candidate'&&x.format==='png')??null;
 return {ok:true,indexed:all.length,total:matches.length,offset,items,recommendation,note:'Name-based category/map heuristics, not verified PBR semantics. Recommendation limited to returned page; review appearance and choose scale explicitly. JPEG stage not supported by this tool.'};
}
export async function textureStage(a,root,sandbox){
 validate(textureStageTool.inputSchema,a,'local_texture_stage');
 const rows=await textureIndex(root),row=rows.find(x=>x.assetId===a.assetId);
 if(!row||row.format!=='png')throw Error('Explicit installed PNG required');
 const q=await inspect(root,row);if(q.sha256!==a.sha256)throw Error('Pinned source changed');
 const s=await lstat(sandbox),real=await realpath(sandbox);if(path.basename(sandbox)!=='Sandbox'||!s.isDirectory()||s.isSymbolicLink()||real.toLowerCase()!==path.resolve(sandbox).toLowerCase())throw Error('Invalid Sandbox');
 const fileName='TextureFixture-'+randomUUID().replace(/-/g,'')+'.png',dst=path.join(sandbox,fileName);
 await writeFile(dst,q.b,{flag:'wx'});const verified=await readFile(dst);if(hash(verified)!==q.sha256)throw Error('Staged hash mismatch');
 return {ok:true,fileName,sha256:q.sha256,sizePx:q.sizePx,source:row.relative,license:rights,note:'Staged exact bytes. Scale must be chosen before applying material. Do not delete staged file while any material references it.'};
}
export const textureVerifyTool={name:'local_texture_verify',description:'Verify a staged TextureFixture PNG exists, is local, has expected hash and valid bounded header. Report missing or changed assets before reusing material.',inputSchema:{type:'object',properties:{fileName:{type:'string',pattern:'^TextureFixture-[a-f0-9]{32}\\.png$'},sha256:{type:'string',pattern:'^[a-f0-9]{64}$'}},required:['fileName','sha256'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function textureVerify(a,sandbox){
 validate(textureVerifyTool.inputSchema,a,'local_texture_verify');
 const s=await lstat(sandbox),real=await realpath(sandbox);if(path.basename(sandbox)!=='Sandbox'||!s.isDirectory()||s.isSymbolicLink()||real.toLowerCase()!==path.resolve(sandbox).toLowerCase())throw Error('Invalid Sandbox');
 const p=path.join(sandbox,a.fileName);
 try{
  const st=await lstat(p);if(!st.isFile()||st.isSymbolicLink()||st.size<33||st.size>maxBytes||(await realpath(p)).toLowerCase()!==p.toLowerCase())return {ok:true,ready:false,state:'invalid_file'};
  const b=await readFile(p);if(b.length!==st.size||hash(b)!==a.sha256)return {ok:true,ready:false,state:'hash_mismatch'};
  return {ok:true,ready:true,state:'verified',fileName:a.fileName,sizePx:pngDimensions(b),sha256:a.sha256,note:'Exact staged bytes and PNG header verified; not a full image decoder or model reference audit.'};
 }catch(e){return {ok:true,ready:false,state:e.code==='ENOENT'?'missing':'invalid_file'}}
}
