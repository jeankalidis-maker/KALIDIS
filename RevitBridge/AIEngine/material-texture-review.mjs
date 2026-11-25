// Explicit material appearance reference review, with checks on known staged PNG files only.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';import {textureVerify} from './local-texture-catalog.mjs';
const exec=promisify(execFile);
const cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a',scriptHash='e12043b99570ec1a647bfb4ae8c93c14502b12c26d1c33ffe669bd91f2ba200f';
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','material_texture_review161.py')});
const obj=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
export const materialTextureTool={name:'material_texture_review',description:'Read native appearance graph bitmap references for explicit material identities. Verify staged PNG paths and expected hashes; mark other paths and unpinned files for review. No model writes.',inputSchema:obj({expectedPath:{type:'string',minLength:1},items:{type:'array',minItems:1,maxItems:100,items:obj({id:{type:'integer',minimum:1},uniqueId:{type:'string',minLength:1,maxLength:128}})},expectedFiles:{type:'array',maxItems:100,items:obj({fileName:{type:'string',pattern:'^TextureFixture-[a-f0-9]{32}\\.png$'},sha256:{type:'string',pattern:'^[a-f0-9]{64}$'}})}},['expectedPath','items','expectedFiles']),annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
async function pinned(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export async function materialTextureAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pinned(f.cli,cliHash,100000000);await pinned(f.script,scriptHash,12000);return true}catch{return false}}
export async function verifyMaterialTextures(r,a,verify){
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.failures?.length||r.blocked?.length||r.unclosed_documents?.length||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.result?.path!==a.expectedPath||r.result.rows?.length!==a.items.length)throw Error('Material texture query incomplete');
 const hashes=new Map(a.expectedFiles.map(x=>[x.fileName,x.sha256])),seen=new Set(),used=new Set(),checks=new Map(),rows=[];
 if(hashes.size!==a.expectedFiles.length||new Set(a.items.map(e=>e.id)).size!==a.items.length)throw Error('Duplicate scope or expected file');
 for(const e of a.items){
  const m=r.result.rows.filter(x=>x.id===e.id&&x.uniqueId===e.uniqueId);
  if(m.length!==1||seen.has(e.id)||!Array.isArray(m[0].maps)||m[0].maps.length>30)throw Error('Material identity or map coverage mismatch');
  seen.add(e.id);const maps=[];
  for(const map of m[0].maps){
   if(!['staged','empty','unresolved_path'].includes(map.state))throw Error('Unknown map state');
   if(map.state!=='staged'){maps.push({state:map.state,ready:false});continue;}
   if(!/^TextureFixture-[a-f0-9]{32}\.png$/.test(map.fileName??''))throw Error('Unexpected staged filename');
   const h=hashes.get(map.fileName);
   if(!h){maps.push({fileName:map.fileName,state:'hash_unpinned',ready:false});continue;}
   used.add(map.fileName);
   if(!checks.has(map.fileName))checks.set(map.fileName,await verify({fileName:map.fileName,sha256:h}));
   const q=checks.get(map.fileName);maps.push({fileName:map.fileName,state:q.state,ready:q.ready===true});
  }
  rows.push({id:e.id,uniqueId:e.uniqueId,assetId:m[0].assetId,maps,allMapsVerified:maps.every(x=>x.ready)});
 }
 return {ok:true,runId:r.run_id,rows,verifiedMaps:rows.flatMap(x=>x.maps).filter(x=>x.ready).length,unverifiedMaps:rows.flatMap(x=>x.maps).filter(x=>!x.ready).length,unusedExpectedFiles:[...hashes.keys()].filter(x=>!used.has(x)),complete:rows.every(x=>x.allMapsVerified)&&[...hashes.keys()].every(x=>used.has(x)),note:'Explicit material scope. Only staged local PNG maps with supplied expected hashes verified. Other installed/relative/absolute references are unresolved; no arbitrary path access or proof of rendering.'};
}
export async function materialTextureReview(a,call,root){
 validate(materialTextureTool.inputSchema,a,'material_texture_review');
 if(!await materialTextureAvailable(root))throw Error('Reviewed appearance query unavailable');
 if(new Set(a.items.map(x=>x.id)).size!==a.items.length||new Set(a.expectedFiles.map(x=>x.fileName)).size!==a.expectedFiles.length)throw Error('Duplicate scope or expected file');
 const sandbox=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox'),c=await call('project_context',{});
 if(!c.ok||c.path!==a.expectedPath||path.dirname(c.path)!==sandbox||c.modified||c.cloud||c.workshared||c.family||c.readOnly)throw Error('Saved local Sandbox required');
 const f=files(root);let r;
 try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=query','--engine=ironpython','--title=Kalidis appearance review','--revit=2025','--timeout=30','--inputs='+JSON.stringify({items:a.items})],{timeout:55000,maxBuffer:2097152,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Appearance query outcome unavailable; inspect history before retry')}
 const report=await verifyMaterialTextures(r,a,x=>textureVerify(x,sandbox));
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed');
 return report;
}