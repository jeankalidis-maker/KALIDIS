// Bind confirmed dimension-chain decisions to an explicit orthogonal plan before native preview.
// This workflow does not certify a survey or authorize commit.
import {validate} from './command-registry.mjs';
import {reviewResolution,referenceResolutionTool} from './reference-resolution-review.mjs';
import {reviewOrthogonalPlan} from './orthogonal-plan-review.mjs';
const item=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
export function referencePreviewTool(planSchema){
 const p=structuredClone(planSchema);delete p.properties.commit;
 return {name:'reference_plan_preview',description:'Gate a native orthogonal plan rollback preview on explicit dimension-chain resolutions and mapped wall, height and door dimensions. Unresolved references produce no native action.',inputSchema:item({reference:referenceResolutionTool.inputSchema,plan:p,bindings:{type:'array',minItems:1,maxItems:101,items:item({issueId:{type:'string',minLength:1,maxLength:300},target:{type:'string',enum:['wall','height','door_width']},index:{type:'integer',minimum:0,maximum:100}},['issueId','target','index'])}}),annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:false,openWorldHint:false}};
}
export function inspectReferencePreview(a,planSchema){
 validate(referencePreviewTool(planSchema).inputSchema,a,'reference_plan_preview');
 const r=reviewResolution(a.reference);
 if(!r.interpretationsComplete)return {ok:true,ready:false,executed:false,reason:'unresolved_reference',unresolved:r.unresolved};
 const topology=reviewOrthogonalPlan(a.plan);
 const p=a.plan,edges=[...p.perimeter.map((v,i)=>[v,p.perimeter[(i+1)%p.perimeter.length]]),...p.partitions.map(x=>[x.start,x.end])];
 const metrics=new Map(edges.map(([x,y],i)=>['wall:'+i,Math.hypot(y[0]-x[0],y[1]-x[1])]));
 metrics.set('height:0',p.heightMm);p.doors.forEach((d,i)=>metrics.set('door_width:'+i,d.widthMm));
 const byId=new Map(r.rows.map(row=>[row.id,row])),seen=new Set(),rows=[];
 for(const b of a.bindings){
  const k=b.target+':'+b.index,issue=byId.get(b.issueId),alt=issue?.alternatives.find(x=>x.id===issue.decision?.alternativeId);
  if(seen.has(k)||!metrics.has(k)||!alt?.chain)throw Error('Duplicate or invalid dimension binding: '+k);
  const measured=alt.totalMm,modeled=metrics.get(k),deltaMm=modeled-measured;
  rows.push({target:k,issueId:b.issueId,alternativeId:alt.id,measuredMm:measured,modeledMm:modeled,deltaMm,withinTolerance:Math.abs(deltaMm)<=r.toleranceMm});seen.add(k);
 }
 const missing=[...metrics.keys()].filter(k=>!seen.has(k));
 return {ok:true,ready:!missing.length&&rows.every(x=>x.withinTolerance),executed:false,topology,bindings:rows,missing,unresolved:[],note:'Explicit caller-supplied confirmation references; not proof of field measurements, topology outside the supported orthogonal plan, or authorization to commit.'};
}
export async function referencePlanPreview(a,planSchema,call){
 const pre=inspectReferencePreview(a,planSchema);if(!pre.ready)return pre;
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.plan.expectedPath.toLowerCase()||c.modified||c.cloud||c.workshared||c.family||c.readOnly)throw Error('Saved local fixture required');
 const r=await call('orthogonal_plan',{...a.plan,commit:false});
 if(!r.ok||r.status!=='RolledBack'||r.dryRun!==true)throw Error('Native rollback preview not verified; inspect model before retry');
 const ids=[...r.result.wallIds,r.result.floorId,...r.result.doorIds,...r.result.roomIds];
 if(ids.length!==pre.topology.walls+pre.topology.rooms+pre.topology.doors+1||new Set(ids).size!==ids.length||ids.some(i=>!Number.isSafeInteger(i)||i<1))throw Error('Native preview coverage mismatch');
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.modified!==c.modified||end.view.id!==c.view.id)throw Error('Context changed');
 return {...pre,executed:true,committed:false,status:r.status,temporaryIds:ids,roomAreasM2:r.result.roomAreasM2,note:'Native rollback preview only. No model commit; real source confirmation and authorization remain the responsibility of the user.'};
}