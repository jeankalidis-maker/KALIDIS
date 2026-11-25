// Confirmation ledger for measured references. Preserves source observations; never invents corrections.
import {validate} from './command-registry.mjs';
import {referenceReview} from './reference-plan-review.mjs';
const str={type:'string',minLength:1,maxLength:300};
const mm={type:'number',minimum:1,maximum:1000000};
const item=(p,r=Object.keys(p))=>({type:'object',properties:p,required:r,additionalProperties:false});
export const referenceResolutionTool={name:'reference_resolution_review',description:'Evaluate measured reference alternatives without changing original evidence. Explicitly document confirmed interpretations and revisions; no model edits or automatic approval.',inputSchema:item({source:str,scaleKnown:{type:'boolean'},toleranceMm:{type:'number',minimum:0,maximum:100},issues:{type:'array',minItems:1,maxItems:100,items:item({id:str,question:str,observations:{type:'array',minItems:1,maxItems:20,items:str},alternatives:{type:'array',minItems:1,maxItems:10,items:item({id:str,description:str,partsMm:{type:'array',minItems:1,maxItems:50,items:mm},totalMm:mm},['id','description'])}},['id','question','observations','alternatives'])},decisions:{type:'array',maxItems:100,items:item({issueId:str,alternativeId:str,confirmationReference:str,reason:str})}},['source','scaleKnown','issues','decisions']),annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function reviewResolution(a){
 validate(referenceResolutionTool.inputSchema,a,'reference_resolution_review');
 const tolerance=a.toleranceMm??20,byId=new Map(),decisions=new Map();
 for(const i of a.issues){if(byId.has(i.id))throw Error('Duplicate issue');if(new Set(i.alternatives.map(v=>v.id)).size!==i.alternatives.length)throw Error('Duplicate alternative');byId.set(i.id,i);}
 for(const d of a.decisions){if(decisions.has(d.issueId)||!byId.get(d.issueId)?.alternatives.some(x=>x.id===d.alternativeId)||!d.confirmationReference.trim()||!d.reason.trim())throw Error('Invalid explicit decision');decisions.set(d.issueId,d);}
 const rows=a.issues.map(i=>{
  const alternatives=i.alternatives.map(v=>{
   if((v.partsMm===undefined)!==(v.totalMm===undefined))throw Error('Incomplete dimension chain');
   const chain=v.partsMm===undefined?null:referenceReview({source:a.source,scaleKnown:true,toleranceMm:tolerance,chains:[{name:v.id,partsMm:v.partsMm,totalMm:v.totalMm}],unresolved:[]}).chains[0];
   return {...v,chain};
  });
  const decision=decisions.get(i.id)??null,selected=alternatives.find(v=>v.id===decision?.alternativeId);
  const state=!selected?'confirmation_required':selected.chain&&!selected.chain.withinTolerance?'confirmed_but_conflicting':'interpretation_recorded';
  return {...i,alternatives,decision,state};
 });
 const unresolved=rows.filter(r=>r.state!=='interpretation_recorded').map(r=>({id:r.id,state:r.state}));
 if(!a.scaleKnown)unresolved.push({id:'scale',state:'unknown'});
 return {ok:true,source:a.source,toleranceMm:tolerance,rows,unresolved,interpretationsComplete:unresolved.length===0,modelingAuthorized:false,note:'A reference to confirmation is supplied by caller, not verified user identity or permission. Even a closed set of interpretations requires independent survey/topology validation and explicit model authorization.'};
}