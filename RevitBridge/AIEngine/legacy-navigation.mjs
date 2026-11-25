// First-party legacy compatibility. Explicit IDs only; never load or replay legacy command files.
import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1},ids={type:'array',items:id,minItems:0,maxItems:50,uniqueItems:true};
export const legacyNavigationTool={name:'legacy_navigation',description:'Adapt legacy selecionar/isolar_temporario/reset_isolamento to guarded native UI commands. Requires explicit current view, IDs and selection precondition; defaults to preview.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'string',minLength:1,maxLength:100},acao:{type:'string',enum:['selecionar','isolar_temporario','reset_isolamento']},elementIds:ids,expectedSelection:ids,viewId:id,commit:{type:'boolean'}},required:['expectedPath','id','acao','elementIds','viewId'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:false,openWorldHint:false}};
const same=(a,b)=>a.length===b.length&&a.every(x=>b.includes(x));
export async function legacyNavigation(a,call){
 validate(legacyNavigationTool.inputSchema,a,'legacy_navigation');
 const selection=a.acao==='selecionar',reset=a.acao==='reset_isolamento',commit=a.commit===true;
 if(selection?(a.expectedSelection===undefined):a.expectedSelection!==undefined)throw Error('Selection precondition required only for selecionar');
 if(!selection&&(reset?a.elementIds.length!==0:a.elementIds.length===0))throw Error('Invalid isolation scope');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase()||c.view.id!==a.viewId||c.truncated)throw Error('Document or active view mismatch');
 const old=c.elements.map(e=>e.id);
 if(selection&&!same(old,a.expectedSelection))throw Error('Selection changed');
 const r=selection?await call('select_elements',{expectedPath:a.expectedPath,viewId:a.viewId,ids:a.elementIds,expectedIds:a.expectedSelection,commit}):await call('isolate_elements_in_view',{expectedPath:a.expectedPath,viewId:a.viewId,ids:a.elementIds,reset,commit});
 if(!r.ok||r.committed!==commit||(!selection&&r.status!==(commit?'Committed':'RolledBack')))throw Error('Native operation unverified; inspect before retry');
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.view.id!==a.viewId||end.truncated||!same(end.elements.map(e=>e.id),selection&&commit?a.elementIds:old))throw Error('UI readback mismatch');
 return {id:a.id,sucesso:true,mensagem:commit?'Operacao concluida':'Previa concluida',quantidade:a.elementIds.length,elementIds:a.elementIds,erro:null,committed:commit,dados:r,compatibility:'guarded legacy navigation subset'};
}
export const legacyProximityTool={name:'legacy_proximity',description:'Adapt legacy analisar_proximidade to explicit target and candidate IDs; conservative bounding-box gap in millimeters. No whole-document search.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'string',minLength:1,maxLength:100},acao:{type:'string',enum:['analisar_proximidade']},elementIds:{...ids,minItems:1,maxItems:20},candidateIds:{...ids,minItems:1},x:{type:'number',minimum:0,maximum:100000}},required:['expectedPath','id','acao','elementIds','candidateIds'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function legacyProximity(a,call){
 validate(legacyProximityTool.inputSchema,a,'legacy_proximity');
 if(a.elementIds.some(i=>a.candidateIds.includes(i)))throw Error('Distinct target and candidate scopes required');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
 const all=[...a.elementIds,...a.candidateIds],r=await call('geometry_review',{expectedPath:a.expectedPath,ids:all});
 if(r.status!=='RolledBack'||r.result?.elements?.length!==all.length)throw Error('Incomplete geometry response');
 const map=new Map(r.result.elements.map(e=>[e.id,e]));
 if(map.size!==all.length||all.some(i=>!map.has(i)))throw Error('Scope mismatch');
 const valid=e=>[e.minMm,e.maxMm].every(p=>Array.isArray(p)&&p.length===3&&p.every(Number.isFinite))&&e.minMm.every((x,i)=>x<=e.maxMm[i]);
 const radius=a.x??1000;
 const dados=a.elementIds.map(id=>{const e=map.get(id);if(!valid(e))return {id,state:'inconclusive',vizinhos:[],unreadableIds:a.candidateIds};const unreadableIds=[],vizinhos=[];
 for(const i of a.candidateIds){const f=map.get(i);if(!valid(f)){unreadableIds.push(i);continue}const distanceMm=Math.hypot(...e.minMm.map((v,k)=>Math.max(0,v-f.maxMm[k],f.minMm[k]-e.maxMm[k])));if(distanceMm<=radius)vizinhos.push({id:i,uniqueId:f.uniqueId,distanciaMm:distanceMm,categoria:f.category})}
 return {id,state:unreadableIds.length?'partial':'screened',vizinhos:vizinhos.sort((x,y)=>x.distanciaMm-y.distanciaMm||x.id-y.id),unreadableIds};});
 const end=await call('project_context',{});if(end.path!==c.path||end.session!==c.session||end.revision!==c.revision)throw Error('Context changed');
 return {id:a.id,sucesso:true,quantidade:dados.length,elementIds:a.elementIds,erro:null,raioMm:radius,dados,compatibility:'explicit scope only',note:'AABB gap, not solid clearance; no candidates outside supplied IDs assessed.'};
}