// Review ordered planar 3-D line/arc loops with the native transient geometry kernel.
import path from 'node:path';import {execFile} from 'node:child_process';import {promisify} from 'node:util';import {lstat,readFile} from 'node:fs/promises';import {createHash} from 'node:crypto';import {validate} from './command-registry.mjs';
const exec=promisify(execFile),cliHash='c8f2be08de3661872334c33a9bf7ae1f879a93c4b2a38786eb150c37b380e58a',scriptHash='b7205f70aa275cb91ab48022396a90ecec558e94339eda8a9ec2998e34c720ee';
const obj=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
const pt={type:'array',minItems:3,maxItems:3,items:{type:'number',minimum:-1000000,maximum:1000000}};
const line=obj({kind:{const:'line'},start:pt,end:pt}),arc=obj({kind:{const:'arc'},start:pt,mid:pt,end:pt});
export const profileGeometryTool={name:'profile_geometry_review',description:'Validate explicit coplanar 3D closed line/arc loops with holes using transient Revit solid geometry. Optionally propose exact duplicate zero-line removal and uniquely determined segment reversal. No model writes, no invented dimensions.',inputSchema:obj({expectedPath:{type:'string',minLength:1},loops:{type:'array',minItems:1,maxItems:8,items:{type:'array',minItems:2,maxItems:100,items:{oneOf:[line,arc]}}},repairExact:{type:'boolean'}},['expectedPath','loops']),annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const eq=(a,b)=>a.every((v,i)=>v===b[i]),dist=(a,b)=>Math.hypot(...a.map((v,i)=>v-b[i]));
export function prepareProfiles(a){
 validate(profileGeometryTool.inputSchema,a,'profile_geometry_review');
 if(a.loops.flat().length>100)throw Error('At most 100 segments');
 const corrections=[],loops=a.loops.map((raw,l)=>{
  const s=[];raw.forEach((v,i)=>{if(v.kind==='line'&&eq(v.start,v.end)&&a.repairExact===true){corrections.push({loop:l,segment:i,kind:'remove_exact_zero_line'});return}if(dist(v.start,v.end)<1)throw Error('Short or closed single segment');s.push({...v,originalIndex:i})});
  if(s.length<2)throw Error('Too few segments');
  // Preserve the given order and first direction; only reverse a uniquely matching subsequent curve.
  for(let i=1;i<s.length;i++){
   if(eq(s[i-1].end,s[i].start))continue;
   if(a.repairExact===true&&eq(s[i-1].end,s[i].end)){const q=s[i];[q.start,q.end]=[q.end,q.start];corrections.push({loop:l,segment:q.originalIndex,kind:'reverse_exact_endpoint'});}
   else throw Error('Disconnected ordered segments');
  }
  if(!eq(s.at(-1).end,s[0].start))throw Error('Open loop; no coordinate snapping');
  return s.map(({originalIndex,...v})=>v);
 });
 return {loops,corrections};
}
const files=root=>({cli:path.join(process.env.APPDATA??'','pyRevit-Master','bin','pyrevit.exe'),script:path.join(root,'Integrations','pyRevit','ReviewedScripts','profile_geometry162.py')});
async function pin(f,h,max){const s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>max||createHash('sha256').update(await readFile(f)).digest('hex')!==h)throw Error('Reviewed file mismatch')}
export async function profileGeometryAvailable(root){try{if(!process.env.APPDATA)return false;const f=files(root);await pin(f.cli,cliHash,100000000);await pin(f.script,scriptHash,12000);return true}catch{return false}}
export function verifyProfileResult(r,a,pre){
 if(r.status!=='ok'||r.decision!=='rolled_back'||r.error||r.result_truncated||r.blocked?.length||r.failures?.length||r.unclosed_documents?.length||!r.changes||['added_count','modified_count','deleted_count'].some(k=>r.changes[k]!==0)||r.result?.path!==a.expectedPath)throw Error('Native geometry query incomplete');
 const x=r.result;
 if(x.valid!==true){if(x.valid!==false||typeof x.reason!=='string')throw Error('Invalid native result');return {ok:true,valid:false,reason:x.reason,corrections:pre.corrections,runId:r.run_id,modelChanges:0}}
 const n=pre.loops.length,positive=v=>Number.isFinite(v)&&v>0;
 if(!Array.isArray(x.normal)||x.normal.length!==3||x.normal.some(v=>!Number.isFinite(v))||Math.abs(Math.hypot(...x.normal)-1)>1e-7||x.curveCounts?.length!==n||x.curveCounts.some((v,i)=>v!==pre.loops[i].length)||x.loopAreasMm2?.length!==n||!x.loopAreasMm2.every(positive)||x.perimetersMm?.length!==n||!x.perimetersMm.every(positive)||!positive(x.netAreaMm2)||Math.abs(x.netAreaMm2-(x.loopAreasMm2[0]-x.loopAreasMm2.slice(1).reduce((a,b)=>a+b,0)))>Math.max(.01,x.netAreaMm2*1e-5))throw Error('Native geometry result mismatch');
 return {ok:true,valid:true,runId:r.run_id,normal:x.normal,loopAreasMm2:x.loopAreasMm2,netAreaMm2:x.netAreaMm2,perimetersMm:x.perimetersMm,curveCounts:x.curveCounts,corrections:pre.corrections,normalizedLoops:pre.loops,modelChanges:0,note:'Transient coplanar line/arc profiles; volume tolerance 1e-5 relative for curved faces, not exact intersection certification; no spline, nonplanar surface or automatic edits to existing elements.'};
}
export async function profileGeometry(a,call,root){
 const pre=prepareProfiles(a);
 if(!await profileGeometryAvailable(root))throw Error('Reviewed geometry query unavailable');
 const sandbox=path.join(process.env.LOCALAPPDATA??'','KalidisAIEngine','Sandbox'),c=await call('project_context',{});
 if(!c.ok||c.path!==a.expectedPath||path.dirname(c.path)!==sandbox||c.modified||c.cloud||c.workshared||c.family||c.readOnly)throw Error('Saved local Sandbox required');
 const f=files(root);let r;try{const {stdout}=await exec(f.cli,['agent','run',f.script,'--mode=query','--engine=ironpython','--title=Kalidis transient profile review','--revit=2025','--timeout=30','--inputs='+JSON.stringify({loops:pre.loops})],{timeout:55000,maxBuffer:2097152,windowsHide:true});r=JSON.parse(stdout)}catch{throw Error('Profile query outcome unavailable; inspect history before retry')}
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision||end.modified!==c.modified)throw Error('Context changed');
 return verifyProfileResult(r,a,pre);
}