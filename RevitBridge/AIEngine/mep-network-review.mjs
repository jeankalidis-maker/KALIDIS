import {validate} from './command-registry.mjs';
export const mepNetworkTool={name:'mep_network_review',description:'Audit connected components, open ends, reciprocal physical connections and external references of explicit MEP elements. Uses native C# topology; not hydraulic design.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},ids:{type:'array',items:{type:'integer',minimum:1},minItems:1,maxItems:100}},required:['expectedPath','ids'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function analyzeTopology(ids,report){
 const nodes=new Map(ids.map(id=>[id,new Set()])), connectors=new Map(),issues=[],external=[];
 if(nodes.size!==ids.length)throw Error('Duplicate IDs');
 for(const c of report.connectors??[]){
  const key=c.ownerId+':'+c.connectorId;
  if(!nodes.has(c.ownerId)||connectors.has(key))throw Error('Unexpected or duplicate connector');
  connectors.set(key,c);
 }
 for(const id of ids)if(![...connectors.values()].some(c=>c.ownerId===id))issues.push({kind:'no_end_connectors',id});
 for(const [key,c] of connectors){
  if(c.isConnected&&!(c.physicalEndRefs??[]).length)issues.push({kind:'unresolved_connected_end',key});
  if(!c.isConnected&&(c.physicalEndRefs??[]).length)issues.push({kind:'inconsistent_connection_state',key});
  for(const ref of c.physicalEndRefs??[]){
   if(!nodes.has(ref.ownerId)){external.push({from:key,to:ref.ownerId+':'+ref.connectorId});continue}
   const peer=connectors.get(ref.ownerId+':'+ref.connectorId);
   if(!peer||!peer.isConnected||!(peer.physicalEndRefs??[]).some(r=>r.ownerId===c.ownerId&&r.connectorId===c.connectorId)){issues.push({kind:'nonreciprocal_connection',key,peer:ref});continue}
   nodes.get(c.ownerId).add(ref.ownerId);nodes.get(ref.ownerId).add(c.ownerId);
  }
 }
 const visited=new Set(),components=[];
 for(const id of ids){if(visited.has(id))continue;const todo=[id],members=[];visited.add(id);
  while(todo.length){const n=todo.pop();members.push(n);for(const next of nodes.get(n))if(!visited.has(next)){visited.add(next);todo.push(next)}}
  const rows=[...connectors.values()].filter(c=>members.includes(c.ownerId));
  components.push({ids:members.sort((a,b)=>a-b),openEnds:rows.filter(c=>!c.isConnected).length,connectedEnds:rows.filter(c=>c.isConnected).length});
 }
 return {ok:true,componentCount:components.length,components,external,issues,internallyConsistent:issues.length===0,closedScope:external.length===0,note:'Selected physical end connectors only; open ends may be intentional. No flow, sizing or pressure validation.'};
}
export async function mepNetworkReview(args,call){
 validate(mepNetworkTool.inputSchema,args,'mep_network_review');
 if(new Set(args.ids).size!==args.ids.length)throw Error('Duplicate IDs');
 const r=await call('mep_topology',args);
 if(r.status!=='RolledBack'||!r.result||r.result.elements!==args.ids.length)throw Error('Native topology not verified');
 return analyzeTopology(args.ids,r.result);
}
