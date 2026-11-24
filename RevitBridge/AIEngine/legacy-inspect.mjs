import {validate} from './command-registry.mjs';
export const legacyInspectTool={name:'legacy_inspect',description:'Compatibility for legacy selecionar, snapshot_elemento and listar_comandos_revit, read only; no native command execution.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'string',minLength:1,maxLength:100},acao:{type:'string',enum:['selecionar','snapshot_elemento','listar_comandos_revit']},elementIds:{type:'array',items:{type:'integer',minimum:1},minItems:1,maxItems:100,uniqueItems:true},busca:{type:'string',maxLength:200}},required:['expectedPath','id','acao'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function legacyInspect(a,call){
 validate(legacyInspectTool.inputSchema,a,'legacy_inspect');
 const s=await call('snapshot',{});if(s.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
 let dados;
 if(a.acao==='snapshot_elemento'){
  if(!a.elementIds||a.busca!==undefined)throw Error('Explicit element IDs required');
  const r=await call('element_info',{ids:a.elementIds});
  if(r.elements.length!==a.elementIds.length||a.elementIds.some(i=>!r.elements.some(e=>e.id===i)))throw Error('Element missing');
  dados={elementos:r.elements,nota:'Native parameters; raw doubles in internal units; no full solid snapshot'};
 }else if(a.acao==='selecionar'){
  if(a.elementIds||a.busca!==undefined)throw Error('Only current selection read; no UI selection changes');
  dados=await call('selection',{});
 }else{
  if(a.elementIds)throw Error('No element IDs for command inventory');
  dados=await call('native_status',a.busca?{query:a.busca}:{});
 }
 const e=await call('snapshot',{});if(e.path!==s.path||e.title!==s.title)throw Error('Document changed');
 return {id:a.id,sucesso:true,dados,compatibility:'read-only subset',note:'selecionar reads selection; does not change it'};
}