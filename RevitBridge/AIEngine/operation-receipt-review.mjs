import {createHash} from 'node:crypto';import path from 'node:path';import {validate} from './command-registry.mjs';
export const receiptTool={name:'operation_receipt_review',description:'Recover a scoped batch receipt and compare created element IDs with the current project. Never replays, cannot certify causality or recover an unknown commit.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},requestId:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}$'}},required:['expectedPath','requestId'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export const pathDigest=p=>createHash('sha256').update(path.resolve(p).toLowerCase()).digest('hex');
export async function reviewReceipt(a,readStatus,call){
 validate(receiptTool.inputSchema,a,'operation_receipt_review');
 const status=await readStatus(a.requestId);
 if(status.state!=='completed'||!status.scope?.creationOnly||status.command!=='batch'||!status.receipt?.verified||!Array.isArray(status.receipt.elementIds)||!status.receipt.elementIds.length)return {ok:true,status,consistent:null,requiresManualInspection:true,note:'No verified creation receipt; do not retry.'};
 const before=await call('project_context',{});
 if(!before.ok||before.path.toLowerCase()!==path.resolve(a.expectedPath).toLowerCase()||status.scope.pathDigest!==pathDigest(before.path)||status.scope.session!==before.session)throw Error('Receipt path/session mismatch');
 const rows=[];
 for(const id of status.receipt.elementIds){
  try{const r=await call('element_info',{ids:[id]});const e=r.elements?.[0];if(e?.id!==id||typeof e.uniqueId!=='string')throw Error('Unexpected element response');rows.push({id,present:true,uniqueId:e.uniqueId,typeId:e.typeId})}
  catch(e){if(String(e.message).includes('Missing element '+id))rows.push({id,present:false});else throw e}
 }
 const after=await call('project_context',{});if(after.session!==before.session||after.revision!==before.revision||after.path!==before.path)throw Error('Model changed during receipt review');
 const expected=status.receipt.committed===true;
 const consistent=rows.every(r=>r.present===expected);
 return {ok:true,status,consistent,expectedPresent:expected,observedRevision:after.revision,rows,requiresManualInspection:!consistent,note:'Current ID presence only; later edits or ID reuse may alter results. Does not prove identity at commit or certify unknown outcomes.'};
}