// Build a manual scalar addition graph using the previously validated Dynamo 3.3 node shapes.
// No scripts, packages, bindings, external execution or arbitrary expressions.
import {randomUUID,createHash} from 'node:crypto';import {mkdir,lstat,writeFile} from 'node:fs/promises';import path from 'node:path';import {validate} from './command-registry.mjs';import {inspectGraph} from './dynamo-file-review.mjs';
export const dynamoSumTool={name:'dynamo_sum_graph',description:'Generate a manual .dyn graph of 2-20 scalar inputs, addition nodes and Watch. Static arithmetic cross-check only; does not execute Dynamo.',inputSchema:{type:'object',properties:{values:{type:'array',minItems:2,maxItems:20,items:{type:'integer',minimum:-1000000,maximum:1000000}},save:{type:'boolean'}},required:['values'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:false,openWorldHint:false}};
const uid=()=>randomUUID().replace(/-/g,'');
export function makeSumGraph(values){
 validate(dynamoSumTool.inputSchema,{values},'dynamo_sum_graph');
 const nodes=[],links=[],views=[],inputs=[];
 const port=(name,description)=>({Id:uid(),Name:name,Description:description,UsingDefaultValue:false,Level:2,UseLevels:false,KeepListStructure:false});
 function node(type,nt,name,ins,outs,props,x,y,asInput=false){
  const n={ConcreteType:type,Id:uid(),NodeType:nt,Inputs:ins,Outputs:outs,Replication:nt==='FunctionNode'?'Auto':'Disabled',Description:name,...props};
  nodes.push(n);views.push({Id:n.Id,Name:name,IsSetAsInput:asInput,IsSetAsOutput:false,Excluded:false,ShowGeometry:true,X:x,Y:y});return n;
 }
 const connect=(a,b)=>links.push({Start:a.Id,End:b.Id,Id:uid(),IsHidden:'False'});
 const sliders=values.map((v,i)=>{
  const props={NumberType:'Double',MinimumValue:Math.min(0,v),MaximumValue:Math.max(1,v),StepValue:1,InputValue:v};
  const n=node('CoreNodeModels.Input.DoubleSlider, CoreNodeModels','NumberInputNode','Number Slider',[],[port('','Double')],props,0,i*125,true);
  inputs.push({Id:n.Id,Name:'Number Slider',Type:'number',Type2:'number',Value:String(v),MaximumValue:props.MaximumValue,MinimumValue:props.MinimumValue,StepValue:1,NumberType:'Double',Description:'Produces numeric values'});return n;
 });
 let prev=sliders[0].Outputs[0];
 for(let i=1;i<sliders.length;i++){const n=node('Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore','FunctionNode','+',[port('x','Numeric input'),port('y','Numeric input')],[port('var','Sum')],{FunctionSignature:'+@var[]..[],var[]..[]'},i*250,i*90);connect(prev,n.Inputs[0]);connect(sliders[i].Outputs[0],n.Inputs[1]);prev=n.Outputs[0];}
 const watch=node('CoreNodeModels.Watch, CoreNodeModels','ExtensionNode','Watch',[port('','Node to show output from')],[port('','Node output')],{WatchWidth:100,WatchHeight:60},sliders.length*250,sliders.length*90);connect(prev,watch.Inputs[0]);
 return {Uuid:randomUUID(),IsCustomNode:false,Description:'Kalidis generated scalar sum; manual execution',Name:'Kalidis Sum',ElementResolver:{ResolutionMap:{}},Inputs:inputs,Outputs:[],Nodes:nodes,Connectors:links,Dependencies:[],NodeLibraryDependencies:[],EnableLegacyPolyCurveBehavior:true,Thumbnail:'',GraphDocumentationURL:null,ExtensionWorkspaceData:[],Author:'Kalidis',Bindings:[],View:{Dynamo:{ScaleFactor:1,HasRunWithoutCrash:false,IsVisibleInDynamoLibrary:false,Version:'3.3.0.5104',RunType:'Manual',RunPeriod:'1000'},ConnectorPins:[],NodeViews:views,Annotations:[],X:0,Y:0,Zoom:1}};
}
export function evaluateSumGraph(g){
 const review=inspectGraph(g);if(review.requiresManualReview||review.issues.length||g.Outputs.length||g.IsCustomNode||g.ExtensionWorkspaceData.length||Object.keys(g.ElementResolver.ResolutionMap).length)throw Error('Graph outside pure scalar subset');
 const values=new Map(),links=new Map(),outputs=new Map(),pending=new Map();
 for(const n of g.Nodes){
  if(n.Outputs.length!==1||n.Inputs.some(p=>p.UsingDefaultValue||p.UseLevels)||n.Outputs.some(p=>p.UseLevels))throw Error('Unsupported ports');
  if(n.ConcreteType==='CoreNodeModels.Input.DoubleSlider, CoreNodeModels'&&n.Inputs.length===0&&Number.isSafeInteger(n.InputValue)&&Math.abs(n.InputValue)<=1000000){values.set(n.Outputs[0].Id,n.InputValue);}
  else if(n.ConcreteType==='Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore'&&n.FunctionSignature==='+@var[]..[],var[]..[]'&&n.Inputs.length===2)pending.set(n.Id,n);
  else if(n.ConcreteType==='CoreNodeModels.Watch, CoreNodeModels'&&n.Inputs.length===1)pending.set(n.Id,n);
  else throw Error('Unreviewed node');
  outputs.set(n.Outputs[0].Id,n);
 }
 for(const c of g.Connectors){if(links.has(c.End)||!outputs.has(c.Start))throw Error('Invalid or multiple input links');links.set(c.End,c.Start)}
 const watches=[];
 while(pending.size){let progressed=false;for(const [id,n] of pending){const sources=n.Inputs.map(p=>links.get(p.Id));if(sources.some(p=>!p))throw Error('Unwired input');if(!sources.every(p=>values.has(p)))continue;const nums=sources.map(p=>values.get(p));const v=nums.reduce((a,b)=>a+b,0);if(!Number.isSafeInteger(v))throw Error('Integer overflow');values.set(n.Outputs[0].Id,v);if(n.ConcreteType.startsWith('CoreNodeModels.Watch,'))watches.push(v);pending.delete(id);progressed=true}if(!progressed)throw Error('Unresolved dependency')}
 if(watches.length!==1)throw Error('Exactly one Watch required');
 return {expectedWatch:watches[0],...review};
}
export async function generateSum(a,root){
 validate(dynamoSumTool.inputSchema,a,'dynamo_sum_graph');const graph=makeSumGraph(a.values),audit=evaluateSumGraph(graph);
 if(audit.expectedWatch!==a.values.reduce((s,v)=>s+v,0))throw Error('Static evaluator mismatch');
 const bytes=JSON.stringify(graph,null,2),sha256=createHash('sha256').update(bytes).digest('hex');let file=null;
 if(a.save){const s=await lstat(root);if(path.basename(root)!=='Sandbox'||!s.isDirectory()||s.isSymbolicLink())throw Error('Invalid sandbox');const folder=path.join(root,'DynamoGenerated-'+uid());await mkdir(folder);file=path.join(folder,'sum.dyn');await writeFile(file,bytes,{flag:'wx'});}
 return {ok:true,file,sha256,audit,graph,note:'Generated manual graph; expected value from independent static evaluator, not actual Dynamo runtime. No execution authorization.'};
}