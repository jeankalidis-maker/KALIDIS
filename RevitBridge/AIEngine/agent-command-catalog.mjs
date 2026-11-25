// Inspired by BIMCraft list_commands: expose exact local schemas, not upstream commands pretending to exist.
import {validate} from './command-registry.mjs';
import {batchActionSchemas} from './batch-preflight.mjs';
import {allowedPlanCommands} from './agent-plan.mjs';
export const agentCatalogTool={name:'agent_command_catalog',description:'Discover active Kalidis tools and precise batch action schemas, including the subset supported by agent_plan.',inputSchema:{type:'object',properties:{query:{type:'string',maxLength:100},includeSchemas:{type:'boolean'}},additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function agentCatalog(a,live){
 validate(agentCatalogTool.inputSchema,a,'agent_command_catalog');
 const q=(a.query??'').toLowerCase(),matches=(n,d)=>!q||n.toLowerCase().includes(q)||d.toLowerCase().includes(q);
 const tools=live.filter(t=>matches(t.name,t.description??'')).map(t=>({name:t.name,description:t.description,readOnly:t.annotations?.readOnlyHint===true,...(a.includeSchemas?{inputSchema:t.inputSchema}:{})}));
 const batch=live.find(t=>t.name==='batch'),activePlan=live.some(t=>t.name==='agent_plan');
 const active=new Set(batch?.inputSchema?.properties?.steps?.items?.properties?.action?.enum??[]);
 const actions=Object.entries(batchActionSchemas()).filter(([n])=>active.has(n)&&matches(n,'')).map(([name,schema])=>({name,agentPlan:activePlan&&allowedPlanCommands.has(name),...(a.includeSchemas?{inputSchema:schema}:{})}));
 return {ok:true,totalLiveTools:live.length,matchedTools:tools.length,matchedBatchActions:actions.length,tools,actions,note:'Active local capabilities only; schema validation and preview do not establish model correctness.'};
}