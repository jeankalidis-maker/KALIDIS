import {validate} from './command-registry.mjs';
export const pinTool={name:'reversible_pin',description:'Guarded compensating pin/unpin of an explicit element, with preview and independent state readback. Not global Undo.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'integer',minimum:1},uniqueId:{type:'string',minLength:1},before:{type:'boolean'},after:{type:'boolean'},direction:{type:'string',enum:['forward','undo','redo']},commit:{type:'boolean'}},required:['expectedPath','id','uniqueId','before','after','direction'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:true,idempotentHint:false,openWorldHint:false}};
export async function reversiblePin(a,call){
 validate(pinTool.inputSchema,a,'reversible_pin');
 if(a.before===a.after)throw Error('Before and after must differ');
 const from=a.direction==='undo'?a.after:a.before,to=!from,commit=a.commit===true;
 const inspect=async expected=>{
  const s=await call('snapshot',{});if(s.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
  const r=await call('geometry_review',{expectedPath:a.expectedPath,ids:[a.id]}),e=r.result?.elements?.[0];
  if(r.status!=='RolledBack'||e?.id!==a.id||e.uniqueId!==a.uniqueId||e.pinned!==expected)throw Error('Identity or pin state mismatch; inspect model');
  return e;
 };
 await inspect(from);
 const r=await call('edit_elements',{expectedPath:a.expectedPath,steps:[{action:'pin',elementId:a.id,value:to}],commit});
 if(r.status!==(commit?'Committed':'RolledBack')||r.results?.length!==1||r.results[0].elementId!==a.id)throw Error('Edit outcome not verified; do not retry automatically');
 const final=await inspect(commit?to:from);
 return {ok:true,committed:commit,direction:a.direction,pinned:final.pinned,receipt:{expectedPath:a.expectedPath,id:a.id,uniqueId:a.uniqueId,before:a.before,after:a.after},note:'Compensating pin state only. Preview verifies rollback, not transient in-transaction pin value. No atomic compare-and-set; avoid concurrent editors.'};
}
