import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1};
export const familyReviewTool={name:'family_instance_review',description:'Review explicitly instantiated family types against expected dimensions and volumes; report uncovered types. Geometry and parameters only, not certification of arbitrary families.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},familyId:id,instances:{type:'array',minItems:1,maxItems:29,items:{type:'object',properties:{id,typeId:id,dimensionsMm:{type:'array',minItems:3,maxItems:3,items:{type:'number',minimum:0}},volumeM3:{type:'number',minimum:0},solidCount:{type:'integer',minimum:0},typeParameters:{type:'array',maxItems:30,items:{type:'object',properties:{name:{type:'string',minLength:1,maxLength:80},kind:{type:'string',enum:['lengthMm','number']},value:{type:'number'}},required:['name','kind','value'],additionalProperties:false}}},required:['id','typeId'],additionalProperties:false}},lengthToleranceMm:{type:'number',minimum:.01,maximum:10},volumeRelativeTolerance:{type:'number',minimum:.000001,maximum:.05}},required:['expectedPath','familyId','instances'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function reviewFamilyInstances(a,call){
 validate(familyReviewTool.inputSchema,a,'family_instance_review');
 const ids=a.instances.map(x=>x.id);if(new Set(ids).size!==ids.length)throw Error('Duplicate instance');
 const c=await call('project_context',{});if(!c.ok||c.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Project mismatch');
 const family=await call('family_info',{familyId:a.familyId});
 const types=await call('list_family_types',{familyName:family.name});
 const listed=types.familyTypes?.filter(t=>t.familyId===a.familyId);
 if(!listed?.length)throw Error('Family type inventory unavailable');
 const actual=await call('element_info',{ids});
 const typeIds=[...new Set(a.instances.filter(e=>e.typeParameters?.length).map(e=>e.typeId))];
 const typeData=typeIds.length?(await call('element_info',{ids:typeIds})).elements:[];
 const g=await call('geometry_review',{expectedPath:a.expectedPath,ids});
 const s=await call('solid_audit',{expectedPath:a.expectedPath,ids});
 if(g.status!=='RolledBack'||s.status!=='RolledBack')throw Error('Geometry verification unavailable');
 const rows=a.instances.map(e=>{
  const i=actual.elements.find(x=>x.id===e.id),b=g.result.elements.find(x=>x.id===e.id),v=s.result.elements.find(x=>x.id===e.id);
  if(!i||!b||!v||i.typeId!==e.typeId||!listed.some(t=>t.id===e.typeId))throw Error('Instance/type mismatch '+e.id);
  if(!Array.isArray(b.minMm)||!Array.isArray(b.maxMm)||b.minMm.length!==3||b.maxMm.length!==3)throw Error('Missing instance bounds');
  const size=b.maxMm.map((x,j)=>x-b.minMm[j]);
  if(!size.every(n=>Number.isFinite(n)&&n>=0)||!Number.isFinite(v.volumeM3))throw Error('Invalid geometry data');
  const checks={};
  if(e.dimensionsMm)checks.dimensions=size.every((x,j)=>Math.abs(x-e.dimensionsMm[j])<=(a.lengthToleranceMm??.1));
  if(e.volumeM3!==undefined)checks.volume=Math.abs(v.volumeM3-e.volumeM3)<=Math.max(1e-9,e.volumeM3*(a.volumeRelativeTolerance??.001));
  if(e.solidCount!==undefined)checks.solids=v.solidCount===e.solidCount;
  const parameterChecks=(e.typeParameters??[]).map(expected=>{
   const found=typeData.find(t=>t.id===e.typeId)?.parameters.filter(p=>p.name===expected.name)??[];
   if(found.length!==1||found[0].storage!=='Double'||!Number.isFinite(found[0].value))throw Error('Ambiguous or missing type parameter '+expected.name);
   const p=found[0],length=expected.kind==='lengthMm';
   if(length?!p.unit?.startsWith('autodesk.unit.unit:millimeters-'):!p.unit?.startsWith('autodesk.unit.unit:general-'))throw Error('Unexpected native unit for '+expected.name);
   const actual=length?p.value*304.8:p.value;
   return {name:expected.name,kind:expected.kind,expected:expected.value,actual,pass:Math.abs(actual-expected.value)<=(length?(a.lengthToleranceMm??.1):1e-8)};
  });
  if(parameterChecks.length)checks.typeParameters=parameterChecks.every(x=>x.pass);
  return {id:e.id,typeId:e.typeId,name:listed.find(t=>t.id===e.typeId).name,dimensionsMm:size,volumeM3:v.volumeM3,solidCount:v.solidCount,parameterCount:i.parameters.length,parameterChecks,checks,passes:Object.values(checks).every(Boolean),measuredOnly:Object.keys(checks).length===0};
 });
 const after=await call('project_context',{});if(after.path!==c.path||after.session!==c.session||after.revision!==c.revision)throw Error('Model changed during review');
 const covered=new Set(rows.map(r=>r.typeId)),missing=listed.filter(t=>!covered.has(t.id)).map(t=>({id:t.id,name:t.name}));
 return {ok:true,familyId:a.familyId,familyName:family.name,types:listed.length,covered:covered.size,missing,allTypesCovered:missing.length===0,allExpectedChecksPassed:rows.every(r=>r.passes&&!r.measuredOnly),rows,note:'Axis-aligned project bounds and measured solids; no flex beyond provided instances, connectors or hidden geometry certification.'};
}