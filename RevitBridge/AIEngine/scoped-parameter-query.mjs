// Adapted from KenLP QueryWhereCommand (MIT), pinned fdf6efa23e57fe59effb8191666d04b4f73142a0.
// Deterministic query over an explicit scope, not a whole-document search. No arbitrary regex or code.
import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1};
const selector={type:'object',properties:{parameterId:{type:'integer'},parameterName:{type:'string',minLength:1,maxLength:150},scope:{type:'string',enum:['instance','type','auto']}},required:['scope'],additionalProperties:false};
const condition={...selector,properties:{...selector.properties,operator:{type:'string',enum:['eq','neq','gt','gte','lt','lte','contains','starts_with','ends_with','is_empty','not_empty']},value:{anyOf:[{type:'string',maxLength:1000},{type:'number'},{type:'boolean'}]}},required:['scope','operator']};
export const scopedQueryTool={name:'query_scoped_parameters',description:'Deterministic parameter filtering of explicit element IDs, distinguishing instance and type scope and internal units. Adapted KenLP QueryWhere subset.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},ids:{type:'array',minItems:1,maxItems:30,uniqueItems:true,items:id},where:{type:'array',maxItems:20,items:condition},select:{type:'array',maxItems:20,items:selector},limit:{type:'integer',minimum:1,maximum:30}},required:['expectedPath','ids','where'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
function matchParam(e,s){const p=e?.parameters?.filter(p=>s.parameterId!==undefined?p.id===s.parameterId:p.name===s.parameterName)??[];if(p.length>1)throw Error('Ambiguous parameter selector');return p[0]}
function resolve(e,t,s){if((s.parameterId===undefined)===(s.parameterName===undefined))throw Error('Exactly one parameter selector required');if(s.scope!=='type'){const p=matchParam(e,s);if(p)return {scope:'instance',...p}}if(s.scope!=='instance'){const p=matchParam(t,s);if(p)return {scope:'type',...p}}return null}
function matches(p,c){
 if(!p)return false;
 const v=p.value,op=c.operator,x=c.value;
 if(op==='is_empty'||op==='not_empty'){if(x!==undefined)throw Error('Empty operator takes no value');const empty=v===null||v===undefined||v==='';return op==='is_empty'?empty:!empty}
 if(x===undefined||v===null||v===undefined||typeof v!==typeof x)throw Error('Condition requires value matching native storage');
 if(op==='eq')return v===x;if(op==='neq')return v!==x;
 if(['gt','gte','lt','lte'].includes(op)){if(typeof v!=='number')throw Error('Numeric comparison requires number in native internal units');return op==='gt'?v>x:op==='gte'?v>=x:op==='lt'?v<x:v<=x}
 if(typeof v!=='string')throw Error('String comparison requires string');
 return op==='contains'?v.includes(x):op==='starts_with'?v.startsWith(x):v.endsWith(x);
}
export async function scopedQuery(a,call){
 validate(scopedQueryTool.inputSchema,a,'query_scoped_parameters');
 for(const s of [...a.where,...(a.select??[])])if((s.parameterId===undefined)===(s.parameterName===undefined))throw Error('Exactly one parameter selector required');
 const before=await call('project_context',{});if(!before.ok||before.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
 const r=await call('element_info',{ids:a.ids}),els=r.elements;
 if(!Array.isArray(els)||els.length!==a.ids.length||a.ids.some(id=>!els.some(e=>e.id===id)))throw Error('Incomplete explicit scope');
 const needType=[...a.where,...(a.select??[])].some(s=>s.scope!=='instance');
 const types=[...new Set(els.map(e=>e.typeId).filter(id=>Number.isSafeInteger(id)&&id>0))];
 const ts=needType&&types.length?(await call('element_info',{ids:types})).elements:[];
 if(needType&&types.some(id=>!ts.some(t=>t.id===id)))throw Error('Incomplete type scope');
 const rows=[];let missingConditions=0;
 for(const e of els){
  const t=ts.find(t=>t.id===e.typeId);
  const tests=a.where.map(c=>{const p=resolve(e,t,c);if(!p)missingConditions++;return matches(p,c)});
  if(tests.every(Boolean))rows.push({id:e.id,uniqueId:e.uniqueId,name:e.name,typeId:e.typeId,values:(a.select??[]).map(s=>({selector:s,parameter:resolve(e,t,s)}))});
 }
 const after=await call('project_context',{});if(after.path!==before.path||after.session!==before.session||after.revision!==before.revision)throw Error('Context changed during query');
 return {ok:true,scopeCount:els.length,matchedCount:rows.length,returned:Math.min(rows.length,a.limit??30),rows:rows.slice(0,a.limit??30),missingConditions,note:'Explicit ID scope only. Doubles are native internal units, not display values. Missing parameters never match; ambiguous selectors fail closed.'};
}