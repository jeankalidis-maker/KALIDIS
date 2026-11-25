// Original bounded support screening inspired by Horizun's coherence checks (Apache-2.0).
// Bounding boxes only; no structural capacity, connection or contact verification.
import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1};
export const supportTool={name:'structural_support_screen',description:'Screen both ends of explicit horizontal axis-aligned beams against explicit column/wall candidates using model bounds. Advisory only; no structural design certification.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},beamIds:{type:'array',items:id,minItems:1,maxItems:20,uniqueItems:true},supportIds:{type:'array',items:id,minItems:1,maxItems:50,uniqueItems:true},toleranceMm:{type:'number',minimum:0,maximum:300}},required:['expectedPath','beamIds','supportIds','toleranceMm'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
const box=e=>e&&[e.minMm,e.maxMm].every(a=>Array.isArray(a)&&a.length===3&&a.every(Number.isFinite))&&e.minMm.every((x,i)=>x<=e.maxMm[i]);
export function screen(a,rows){
 const map=new Map(rows.map(e=>[e.id,e])),t=a.toleranceMm;
 if(map.size!==rows.length||a.beamIds.some(id=>a.supportIds.includes(id))||[...a.beamIds,...a.supportIds].some(id=>!map.has(id)))throw Error('Invalid explicit scope');
 const support=a.supportIds.map(id=>map.get(id));
 return a.beamIds.map(id=>{
  const e=map.get(id);
  if(!box(e)||!Number.isFinite(e.curveLengthMm)||e.curveLengthMm<=0)return {id,state:'inconclusive',reason:'missing bounds or curve length'};
  const ext=e.maxMm.map((v,i)=>v-e.minMm[i]),axis=ext[0]>=ext[1]?0:1,other=1-axis;
  if(Math.abs(ext[axis]-e.curveLengthMm)>1||ext[other]>ext[axis]/3||ext[2]>ext[axis]/3)return {id,state:'inconclusive',reason:'not a simple horizontal axis-aligned beam envelope'};
  const ends=[e.minMm[axis],e.maxMm[axis]].map((v,index)=>{
   const center=(e.minMm[other]+e.maxMm[other])/2;
   const hits=[],unknown=[];
   for(const s of support){
    if(!box(s)){unknown.push(s.id);continue}
    const gaps=[Math.max(0,s.minMm[axis]-v,v-s.maxMm[axis]),Math.max(0,s.minMm[other]-center,center-s.maxMm[other]),Math.max(0,s.minMm[2]-e.maxMm[2],e.minMm[2]-s.maxMm[2])];
    const distanceMm=Math.hypot(...gaps);
    if(distanceMm<=t)hits.push({id:s.id,distanceMm});
   }
   return {end:index===0?'min':'max',coordinateMm:v,candidates:hits,unreadableSupportIds:unknown,state:hits.length?'candidate_found':unknown.length?'inconclusive':'no_candidate_in_scope'};
  });
  return {id,axis:axis===0?'X':'Y',ends,state:ends.some(x=>x.state==='no_candidate_in_scope')?'review':ends.some(x=>x.state==='inconclusive')?'inconclusive':'candidates_both_ends'};
 });
}
export async function supportScreen(a,call){
 validate(supportTool.inputSchema,a,'structural_support_screen');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
 const ids=[...a.beamIds,...a.supportIds];if(new Set(ids).size!==ids.length)throw Error('Overlapping scope');
 const r=await call('geometry_review',{expectedPath:a.expectedPath,ids});
 if(r.status!=='RolledBack'||r.result?.elements?.length!==ids.length)throw Error('Bounds unavailable');
 const rows=screen(a,r.result.elements);
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision)throw Error('Context changed');
 return {ok:true,rows,scopeCount:ids.length,note:'Explicit caller-classified beams/supports only. AABB heuristic: not physical bearing, load path, connection or compliance. Missing candidates outside scope are not assessed.'};
}