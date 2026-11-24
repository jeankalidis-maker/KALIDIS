// Reviewed CPython parameter workflow, with independent native readback.
import {validate} from './command-registry.mjs';
export const pythonWorkflowTool={name:'python_comment_workflow',description:'Preview, apply or restore a guarded comment change on an independent furniture instance using reviewed in-process CPython. Native readback before and after. Sandbox only.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'integer',minimum:1},uniqueId:{type:'string',minLength:1},before:{type:'string',maxLength:200},after:{type:'string',maxLength:200},direction:{type:'string',enum:['forward','undo','redo']},commit:{type:'boolean'}},required:['expectedPath','id','uniqueId','before','after','direction'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:true,idempotentHint:false,openWorldHint:false}};
export async function pythonCommentWorkflow(args,call){
 validate(pythonWorkflowTool.inputSchema,args,'python_comment_workflow');
 const {expectedPath,id,uniqueId,before,after,direction,commit=false}=args;
 if(before===after)throw Error('Change must be nontrivial');
 const from=direction==='undo'?after:before,to=direction==='undo'?before:after;
 const inspect=async expected=>{
  const s=await call('snapshot',{});
  if(s.path.toLowerCase()!==expectedPath.toLowerCase())throw Error('Active document mismatch');
  const e=(await call('element_info',{ids:[id]})).elements?.[0];
  const p=e?.parameters?.filter(p=>p.id===-1010106);
  if(e?.uniqueId!==uniqueId||p?.length!==1||p[0].readOnly||p[0].storage!=='String'||(p[0].value??'')!==expected)throw Error('Identity or comment mismatch; inspect model');
  return p[0].value??'';
 };
 await inspect(from);
 const r=await call('python_comments',{expectedPath,id,mode:'set',expected:from,value:to,commit});
 if(r.status!==(commit?'Committed':'RolledBack')||r.result?.id!==id||r.result?.value!==to||r.result?.previous!==from||!Number.isInteger(r.result?.pythonPid))throw Error('Unverified Python result; do not retry automatically');
 const final=await inspect(commit?to:from);
 return {ok:true,committed:commit,direction,final,pythonPid:r.result.pythonPid,receipt:{expectedPath,id,uniqueId,before,after},note:'Compensating comment change only; no arbitrary Python or native Undo stack.'};
}
