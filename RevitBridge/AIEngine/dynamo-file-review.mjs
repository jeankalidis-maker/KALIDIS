import {readFile,lstat} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {validate} from './command-registry.mjs';
export const dynamoReviewTool={name:'dynamo_file_review',description:'Inspect a local Dynamo fixture file without executing it. Identify scripts, dependencies, run mode and broken or cyclic links. Never grants execution permission.',inputSchema:{type:'object',properties:{folder:{type:'string',pattern:'^DynamoFixture-[a-f0-9]{32}$'},fileName:{type:'string',enum:['addition.dyn','write.dyn']}},required:['folder','fileName'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function inspectGraph(g){
 if(!g||!Array.isArray(g.Nodes)||!Array.isArray(g.Connectors)||g.Nodes.length>500||g.Connectors.length>2000)throw Error('Unsupported or oversized graph');
 const ids=new Set(),ports=new Map(),edges=new Map(),issues=[],scriptNodes=[];
 for(const n of g.Nodes){
  if(typeof n.Id!=='string'||!n.Id||ids.has(n.Id))throw Error('Invalid or duplicate node ID');
  ids.add(n.Id);edges.set(n.Id,new Set());
  for(const [dir,list] of [['input',n.Inputs??[]],['output',n.Outputs??[]]]){
   if(!Array.isArray(list))throw Error('Invalid ports');
   for(const p of list){if(typeof p.Id!=='string'||!p.Id||ports.has(p.Id))throw Error('Invalid or duplicate port ID');ports.set(p.Id,{node:n.Id,dir});}
  }
  if(typeof n.Code==='string'||/python|codeblock/i.test([n.ConcreteType,n.NodeType].join(' ')))scriptNodes.push({id:n.Id,type:n.NodeType,engine:n.Engine??null});
 }
 for(const c of g.Connectors){
  const a=ports.get(c.Start),b=ports.get(c.End);
  if(!a||!b||a.dir!=='output'||b.dir!=='input'){issues.push({kind:'invalid_link',id:c.Id??null});continue}
  edges.get(a.node).add(b.node);
 }
 const visiting=new Set(),done=new Set();
 const visit=id=>{if(visiting.has(id))return true;if(done.has(id))return false;visiting.add(id);for(const peer of edges.get(id))if(visit(peer))return true;visiting.delete(id);done.add(id);return false};
 if([...ids].some(visit))issues.push({kind:'cyclic_graph'});
 const runMode=g.View?.Dynamo?.RunType??null;
 const dependencies=[...(Array.isArray(g.Dependencies)?g.Dependencies:[]),...(Array.isArray(g.NodeLibraryDependencies)?g.NodeLibraryDependencies:[])];
 const bindings=Array.isArray(g.Bindings)?g.Bindings.length:null;
 return {nodeCount:ids.size,connectorCount:g.Connectors.length,runMode,scriptNodes,dependencyCount:dependencies.length,bindings,issues,requiresManualReview:scriptNodes.length>0||dependencies.length>0||runMode!=='Manual'||bindings!==0||issues.length>0,executionApproved:false,note:'Static inventory only; node types and graph links do not establish safety or correct output.'};
}
export async function reviewDynamoFile(root,args){
 validate(dynamoReviewTool.inputSchema,args,'dynamo_file_review');
 if(path.basename(root)!=='Sandbox')throw Error('Sandbox expected');
 for(const p of [root,path.join(root,args.folder)]){const s=await lstat(p);if(!s.isDirectory()||s.isSymbolicLink())throw Error('Invalid directory')}
 const f=path.join(root,args.folder,args.fileName),s=await lstat(f);
 if(!s.isFile()||s.isSymbolicLink()||s.size>2000000)throw Error('Invalid graph file');
 const bytes=await readFile(f);
 return {ok:true,folder:args.folder,fileName:args.fileName,sha256:createHash('sha256').update(bytes).digest('hex'),...inspectGraph(JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/,'')))};
}
