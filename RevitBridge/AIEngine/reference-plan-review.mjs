// A reference is evidence, not a scale drawing. Gate metric reconstruction on measured closure.
import {validate} from './command-registry.mjs';
const mm={type:'number',minimum:1,maximum:1000000};
export const referenceTool={name:'reference_plan_review',description:'Review explicit measured chains and unresolved observations before reconstructing a reference plan. No inference of missing dimensions, no Revit edits.',inputSchema:{type:'object',properties:{source:{type:'string',minLength:1,maxLength:300},scaleKnown:{type:'boolean'},toleranceMm:{type:'number',minimum:0,maximum:100},chains:{type:'array',minItems:1,maxItems:100,items:{type:'object',properties:{name:{type:'string',minLength:1,maxLength:120},partsMm:{type:'array',minItems:1,maxItems:50,items:mm},totalMm:mm},required:['name','partsMm','totalMm'],additionalProperties:false}},unresolved:{type:'array',maxItems:100,items:{type:'string',minLength:1,maxLength:300}}},required:['source','scaleKnown','chains','unresolved'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function referenceReview(args){
 validate(referenceTool.inputSchema,args,'reference_plan_review');
 const tolerance=args.toleranceMm??20;
 const chains=args.chains.map(c=>{const sum=c.partsMm.reduce((a,b)=>a+b,0),delta=sum-c.totalMm;return {name:c.name,sumMm:sum,totalMm:c.totalMm,deltaMm:delta,withinTolerance:Math.abs(delta)<=tolerance}});
 const issues=[...chains.filter(c=>!c.withinTolerance).map(c=>({kind:'closure',name:c.name,deltaMm:c.deltaMm})),...args.unresolved.map(message=>({kind:'unresolved',message}))];
 if(!args.scaleKnown)issues.push({kind:'scale',message:'Graphic scale not established; do not measure image pixels'});
 return {ok:true,source:args.source,toleranceMm:tolerance,chains,issues,metricReconstructionReady:issues.length===0,executed:false,note:'Closure is a necessary check, not proof of survey accuracy, topology or structural validity. Never auto-adjust conflicting measurements.'};
}
