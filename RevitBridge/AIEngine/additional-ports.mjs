// Adapted concepts from KenLP (MIT) GetParameter, CheckClearance, CopyParameters,
// and Mvclr (MIT) tools/parameter_tools.py; local implementation, no external runtime.
// See retained THIRD-PARTY notices. Legacy snapshot uses first-party bridge semantics.
import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1},ids={type:'array',minItems:1,maxItems:30,uniqueItems:true,items:id},p={type:'string',minLength:1};
const obj=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
const tool=(name,description,inputSchema,readOnlyHint=true)=>({name,description,inputSchema,annotations:{readOnlyHint,destructiveHint:!readOnlyHint,idempotentHint:readOnlyHint,openWorldHint:false}});
export const portTools=[
 tool('get_parameter','Exact native parameter ID or unambiguous name, preserving storage and units.',obj({expectedPath:p,id,parameterId:{type:'integer'},parameterName:p},['expectedPath','id'])),
 tool('get_element_properties','Native instance and type properties with provenance; never conflate instance and type values.',obj({expectedPath:p,id,includeTypeParams:{type:'boolean'}},['expectedPath','id'])),
 tool('check_clearance','Conservative Euclidean gap between two model AABBs; overlapping boxes do not establish a solid clash.',obj({expectedPath:p,firstId:id,secondId:id,minimumMm:{type:'number',minimum:0,maximum:100000}}),false),
 tool('copy_text_parameter','Copy the instance Comments value between explicit same-category elements, with expected previous values and rollback preview.',obj({expectedPath:p,sourceId:id,sourceUniqueId:p,expectedSource:{type:'string',maxLength:1000},targets:{type:'array',minItems:1,maxItems:20,items:obj({id,uniqueId:p,expectedBefore:{type:'string',maxLength:1000}})},commit:{type:'boolean'}},['expectedPath','sourceId','sourceUniqueId','expectedSource','targets']),false),
 tool('legacy_geometry_snapshot','Legacy snapshot_elemento expanded to include bounds, location and solid volume for explicit IDs.',obj({expectedPath:p,requestId:p,ids}),false)
];
export const portNeeds={
 get_parameter:['project_context','element_info'],get_element_properties:['project_context','element_info'],
 check_clearance:['project_context','geometry_review'],copy_text_parameter:['project_context','element_info','edit_elements'],
 legacy_geometry_snapshot:['project_context','element_info','geometry_review','solid_audit']
};
function find(r,id){const e=r.elements?.find(e=>e.id===id);if(!e)throw Error('Missing element '+id);return e}
function param(e,id,name){
 const matches=e.parameters.filter(p=>id!==undefined?p.id===id:p.name===name);
 if(matches.length!==1)throw Error('Missing or ambiguous parameter');
 return matches[0];
}
function bounds(e){if(!e||![e.minMm,e.maxMm].every(a=>Array.isArray(a)&&a.length===3&&a.every(Number.isFinite))||e.minMm.some((x,i)=>x>e.maxMm[i]))throw Error('Invalid bounds');return e}
export async function runPort(name,a,call){
 const t=portTools.find(t=>t.name===name);if(!t)throw Error('Unknown port');validate(t.inputSchema,a,name);
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
 async function finish(){const e=await call('project_context',{});if(e.path!==c.path||e.session!==c.session||e.revision!==c.revision)throw Error('Context changed during inspection')}
 async function info(ids){return call('element_info',{ids})}
 async function geometry(ids){const r=await call('geometry_review',{expectedPath:a.expectedPath,ids});if(r.status!=='RolledBack')throw Error('Geometry readback not verified');return r.result}
 let result;
 if(name==='get_parameter'){
  if((a.parameterId===undefined)===(a.parameterName===undefined))throw Error('Exactly one parameter selector required');
  const e=find(await info([a.id]),a.id);result={id:e.id,uniqueId:e.uniqueId,parameter:param(e,a.parameterId,a.parameterName),note:'Double values use internal Revit units; display is localized.'};
 }else if(name==='get_element_properties'){
  const instance=find(await info([a.id]),a.id);let type=null;
  if(a.includeTypeParams!==false&&instance.typeId>0)type=find(await info([instance.typeId]),instance.typeId);
  result={instance,type};
 }else if(name==='check_clearance'){
  if(a.firstId===a.secondId)throw Error('Distinct IDs required');
  const g=await geometry([a.firstId,a.secondId]),x=bounds(find(g,a.firstId)),y=bounds(find(g,a.secondId));
  const gap=x.minMm.map((v,i)=>Math.max(0,v-y.maxMm[i],y.minMm[i]-x.maxMm[i]));
  const distanceMm=Math.hypot(...gap);
  result={ids:[x.id,y.id],axisGapsMm:gap,distanceMm,minimumMm:a.minimumMm,screen:distanceMm>=a.minimumMm?'box_gap_pass_only':'review_required',physicalClearanceVerified:false,note:'Conservative bounding-box distance; zero is not proof of collision.'};
 }else if(name==='legacy_geometry_snapshot'){
  const i=await info(a.ids),g=await geometry(a.ids),s=await call('solid_audit',{expectedPath:a.expectedPath,ids:a.ids});if(s.status!=='RolledBack')throw Error('Solid audit unavailable');
  result={id:a.requestId,sucesso:true,elementos:a.ids.map(id=>({elemento:find(i,id),geometria:find(g,id),solidos:find(s.result,id)})),note:'No face loops or room boundaries; legacy subset.'};
 }else if(name==='copy_text_parameter'){
  const targetIds=a.targets.map(t=>t.id);if(new Set(targetIds).size!==targetIds.length||targetIds.includes(a.sourceId))throw Error('Distinct target IDs required');
  const read=async()=>info([a.sourceId,...targetIds]),r=await read(),s=find(r,a.sourceId);
  const text=e=>{const p=param(e,-1010106);if(p.storage!=='String'||typeof p.value!=='string')throw Error('Explicit non-null string Comments required');return p};
  if(s.uniqueId!==a.sourceUniqueId||text(s).value!==a.expectedSource)throw Error('Source changed');
  for(const t of a.targets){const e=find(r,t.id),p=text(e);if(e.uniqueId!==t.uniqueId||e.category!==s.category||e.typeId<=0||p.readOnly||p.value!==t.expectedBefore)throw Error('Target identity, category or old value mismatch')}
  await finish();
  const steps=a.targets.map(t=>({action:'set_parameter',elementId:t.id,parameterId:-1010106,value:a.expectedSource}));
  const d=await call('edit_elements',{expectedPath:a.expectedPath,steps,commit:false});if(d.status!=='RolledBack')throw Error('Preview failed');
  const pre=await read();for(const t of a.targets)if(text(find(pre,t.id)).value!==t.expectedBefore||find(pre,t.id).uniqueId!==t.uniqueId)throw Error('Target changed after preview');
  if(text(find(pre,a.sourceId)).value!==a.expectedSource)throw Error('Source changed after preview');
  if(a.commit===true){const x=await call('edit_elements',{expectedPath:a.expectedPath,steps,commit:true});if(x.status!=='Committed')throw Error('Commit outcome uncertain; inspect model')}
  const post=await read();
  for(const t of a.targets){const e=find(post,t.id);if(e.uniqueId!==t.uniqueId||text(e).value!==(a.commit===true?a.expectedSource:t.expectedBefore))throw Error('Readback mismatch; do not retry')}
  return {ok:true,committed:a.commit===true,targets:a.targets.map(t=>({id:t.id,before:t.expectedBefore,after:text(find(post,t.id)).value})),note:'Comments only; no atomic compare-and-set across clients. Avoid concurrent editors.'};
 }
 await finish();return {ok:true,...result};
}