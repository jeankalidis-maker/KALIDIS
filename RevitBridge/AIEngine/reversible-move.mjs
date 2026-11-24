// Compensating translation, not Revit's global Undo stack.
import {validate} from './command-registry.mjs';
const point={type:'array',minItems:3,maxItems:3,items:{type:'number',minimum:-1000000,maximum:1000000}};
export const reversibleMoveTool={name:'reversible_move',description:'Preview or apply a guarded forward, undo or redo translation of one unhosted point family. Supply original and target positions and UniqueId. Independent readback; no global Undo stack.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'integer',minimum:1},uniqueId:{type:'string',minLength:1},beforeMm:point,afterMm:point,direction:{type:'string',enum:['forward','undo','redo']},commit:{type:'boolean'}},required:['expectedPath','id','uniqueId','beforeMm','afterMm','direction'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:true,idempotentHint:false,openWorldHint:false}};
const distance=(a,b)=>Math.hypot(...a.map((v,i)=>v-b[i]));
export async function reversibleMove(args,call){
 validate(reversibleMoveTool.inputSchema,args,'reversible_move');
 const {id,uniqueId,expectedPath,beforeMm,afterMm,direction,commit=false}=args;
 if(distance(beforeMm,afterMm)<0.2||distance(beforeMm,afterMm)>10000)throw Error('Translation must be 0.2–10000 mm');
 const from=direction==='undo'?afterMm:beforeMm,to=direction==='undo'?beforeMm:afterMm;
 const inspect=async expected=>{
  const report=await call('geometry_review',{expectedPath,ids:[id]});
  const row=report.result?.elements?.[0];
  if(report.status!=='RolledBack'||!row||row.id!==id||row.uniqueId!==uniqueId||row.pinned||!Array.isArray(row.locationMm)||distance(row.locationMm,expected)>0.1)throw Error('Identity or position mismatch; inspect model, do not replay');
  return row.locationMm;
 };
 const initial=await inspect(from);
 const r=await call('verified_move',{expectedPath,id,expectedMm:from,targetMm:to,commit});
 if(r.status!==(commit?'Committed':'RolledBack')||!r.result?.verifiedInsideTransaction||distance(r.result.afterMm,to)>0.1)throw Error('Move outcome not verified; do not retry automatically');
 const final=await inspect(commit?to:from);
 return {ok:true,direction,committed:commit,initialMm:initial,finalMm:final,receipt:{expectedPath,id,uniqueId,beforeMm,afterMm},verifiedPosition:true,note:'Compensating translation only. Does not undo other changes or guarantee dependent geometry unchanged.'};
}
