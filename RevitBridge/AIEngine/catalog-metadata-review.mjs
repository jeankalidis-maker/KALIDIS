// Read saved metadata of the four pinned catalogue files, no file open/upgrade or model write.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';import {inventory} from './bim-catalog-inventory.mjs';
const exec=promisify(execFile);
const cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a',scriptHash='a82512e399eaeb9067d76cffbbaa2bb84339e927ccf6a7272486e9f446d4083f';
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','catalog_metadata159.py')});
async function pin(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export const catalogMetadataTool={name:'catalog_metadata_review',description:'Review saved Revit version and external reference metadata of four pinned RFA assets, with provenance and hash checks. No family open, upgrade, load or model edit.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1}},required:['expectedPath'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function catalogMetadataAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pin(f.cli,cliHash,100000000);await pin(f.script,scriptHash,12000);return true}catch{return false}}
export function verifyMetadata(r,c,inv){
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.blocked?.length||r.failures?.length||r.unclosed_documents?.length||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.result?.path!==c.path||r.result.revitVersion!=='2025'||r.result.openedDocuments!==0||r.result.rows?.length!==4||inv.items?.length!==4||!inv.items.every(x=>x.ready))throw Error('Pinned catalogue metadata incomplete');
 const rows=inv.items.map(p=>{
  const matched=r.result.rows.filter(x=>x.fileName===p.fileName&&x.sha256===p.sha256);
  if(matched.length!==1)throw Error('Pinned metadata identity mismatch');
  const x=matched[0],blockers=[];
  if(x.metadataError||!/^20[0-9]{2}$/.test(x.format??'')||Number(x.format)>2025||x.later!==false||x.workshared!==false||x.current!==(x.format==='2025'))blockers.push('version_or_worksharing_unverified');
  if(x.transmissionState!=='read'||!Array.isArray(x.externalReferences))blockers.push('external_reference_metadata_unavailable');
  else if(x.externalReferences.length)blockers.push('external_references_require_resolution');
  return {...x,license:p.license,source:p.source,revision:p.revision,upgradeRequired:x.current===false,blockers,metadataReady:blockers.length===0};
 });
 return {ok:true,runId:r.run_id,rows,note:'Saved TransmissionData only; absence of reported references does not prove nested-family or render-asset closure. Isolated geometry audit and explicit authorization remain necessary.'};
}
export function requireMetadata(report,pin){
 const matches=report.rows.filter(x=>x.fileName===pin.fileName&&x.sha256===pin.sha256);
 if(matches.length!==1||!matches[0].metadataReady)throw Error('Catalogue metadata gate blocked import: '+(matches[0]?.blockers.join(', ')||'unverified file'));
}
export async function catalogMetadata(a,call,root){
 validate(catalogMetadataTool.inputSchema,a,'catalog_metadata_review');
 if(!await catalogMetadataAvailable(root))throw Error('Reviewed catalogue metadata query unavailable');
 const sandbox=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox'),c=await call('project_context',{});
 if(!c.ok||c.path!==path.resolve(a.expectedPath)||path.dirname(c.path)!==sandbox||c.family||c.workshared||c.cloud||c.readOnly||c.modified)throw Error('Saved local Sandbox required');
 const inv=await inventory(sandbox,{});
 if(inv.ready!==4)throw Error('All four pinned assets required');
 const f=files(root);let r;
 try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=query','--engine=ironpython','--title=Kalidis catalogue metadata','--revit=2025','--timeout=30'],{timeout:55000,maxBuffer:1048576,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Metadata query outcome unavailable; inspect run history before retry')}
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed');
 return verifyMetadata(r,c,inv);
}