// Pure batch preflight: native Revit dry-run remains the geometric authority.
import {validate} from './command-registry.mjs';
import {validateClosedProfile} from './profile-topology.mjs';
const num={type:'number',minimum:-10000000,maximum:10000000}, positive={type:'number',exclusiveMinimum:0,maximum:30000};
const str={type:'string',minLength:1}, id={anyOf:[{type:'integer'},{type:'string',pattern:'^[$].{1,64}$'}]};
const obj=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
const pt2=obj({x:num,y:num,z:num},['x','y']),pt3=obj({x:num,y:num,z:num});
const ids={type:'array',items:{type:'integer'},minItems:1,maxItems:500};
const pts=(p,min,max)=>({type:'array',items:p,minItems:min,maxItems:max});
const levelType={levelId:id,typeId:id},line2={start:pt2,end:pt2},line3={start:pt3,end:pt3};
const profile={...levelType,points:pts(pt2,3,200)}, component={...levelType,point:pt3};
const mep={...levelType,...line3}, system={...mep,systemTypeId:id};
const fit={firstId:id,firstPoint:pt3,secondId:id,secondPoint:pt3};
const view={viewId:id}, pair={firstId:id,secondId:id};
const schemas={
 level:obj({name:str,elevationMm:num}),wall:obj({...levelType,...line2,heightMm:positive}),
 floor:obj(profile),ceiling:obj(profile),room:obj({levelId:id,x:num,y:num,name:str}),grid:obj({...line2,name:str}),material:obj({name:str}),
 view3d:obj({typeId:id,name:str}),plan:obj({...levelType,name:str}),beam:obj({...levelType,...line3}),
 column:obj(component),component:obj(component),hosted_component:obj({...component,hostId:id}),
 pipe:obj(system),duct:obj(system),conduit:obj(mep),cable_tray:obj(mep),opening:obj({hostId:id,...line3}),
 text:obj({...view,point:pt3,text:{type:'string'},typeId:id}),detail_line:obj({...view,...line3}),viewport:obj({...view,sheetId:id,point:pt3}),
 foundation:obj(component),terrain:obj({...levelType,points:pts(pt3,3,5000)}),
 section:obj({typeId:id,...line3,heightMm:positive,depthMm:positive,name:str}),
 roof:obj({...profile,slopeDegrees:{type:'number',minimum:0,exclusiveMaximum:80}}),
 model_line:obj(line3),tag:obj({...view,hostId:id,typeId:id,point:pt3}),
 dimension:obj({...view,...line3,gridIds:{...ids,minItems:2,maxItems:50}}),
 schedule_field:obj({...view,parameterId:{type:'integer'}}),duplicate_type:obj({typeId:id,name:str}),elbow:obj(fit),union:obj(fit),
 wall_top_constraint:obj({hostId:id,levelId:id,offsetMm:num},['hostId','levelId']),
 join_geometry:obj(pair),unjoin_geometry:obj(pair),room_tag:obj({...view,hostId:id,point:pt2}),
 schedule_on_sheet:obj({...view,sheetId:id,point:pt3}),area_plan:obj({...levelType,name:str}),
 area_boundary:obj({...view,...line3}),area:obj({...view,point:pt2,name:str}),
 apply_view_template:obj({...view,templateId:id}),set_view_detail_level:obj({...view,detailLevel:{type:'string',pattern:'^(Coarse|Medium|Fine|coarse|medium|fine)$'}}),
 set_section_box:obj({...view,enable:{type:'boolean'},min:pt3,max:pt3},['viewId']),
 duplicate_view:obj({...view,name:str,duplicateOption:{type:'string'}},['viewId','name']),
 hide_elements:obj({...view,ids}),unhide_elements:obj({...view,ids}),group_elements:obj({ids,name:str}),
 perspective:obj({typeId:id,eye:pt3,target:pt3,name:str}),mirror_copy:obj({hostId:id,origin:pt3,normal:pt3}),
 pipe_sized:obj({...system,diameterMm:positive}),duct_sized:obj({...system,diameterMm:positive,widthMm:positive,heightMm:positive},Object.keys(system)),
 transition:obj(fit),tee:obj({...fit,thirdId:id,thirdPoint:pt3}),railing:obj({...levelType,points:pts(pt2,2,200),closed:{type:'boolean'}},['levelId','typeId','points']),
 shaft_opening:obj({levelId:id,topLevelId:id,points:pts(pt2,3,200)}),wall_flip:obj({hostId:id}),
 conduit_sized:obj({...mep,diameterMm:positive}),cable_tray_sized:obj({...mep,widthMm:positive,heightMm:positive})
};
const aliasFields=new Set(['levelId','typeId','hostId','systemTypeId','viewId','sheetId','firstId','secondId','thirdId','templateId','topLevelId']);
const edits=new Set(['schedule_field','wall_top_constraint','join_geometry','unjoin_geometry','apply_view_template','set_view_detail_level','set_section_box','hide_elements','unhide_elements','wall_flip']);
const dist=(a,b,xy=false)=>Math.hypot(a.x-b.x,a.y-b.y,xy?0:(a.z??0)-(b.z??0));
export function batchPreflight(args,registry){
 validate(registry.all.get('batch').tool.inputSchema,args,'batch');
 if((args.expectedSession===undefined)!==(args.expectedRevision===undefined))throw Error('Both expectedSession and expectedRevision required together');
 if(!args.expectedPath.trim()||!args.expectedPath.toLowerCase().endsWith('.rvt'))throw Error('Expected RVT path');
 const aliases=new Set(),counts={},issues=[];
 let created=0,modified=0;
 for(const [i,step] of args.steps.entries()){
  const {action,alias,...values}=step;
  try{
   if(!Object.hasOwn(schemas,action))throw Error('No preflight schema for '+action);
   validate(schemas[action],values,action);
   for(const [k,v] of Object.entries(values))if(aliasFields.has(k)&&typeof v==='string'&&!aliases.has(v.slice(1)))throw Error('Unknown prior alias '+v);
   if(alias!==undefined){if(typeof alias!=='string'||!alias.length||alias.length>64||aliases.has(alias))throw Error('Invalid or duplicate alias');aliases.add(alias)}
   if(values.start&&values.end&&dist(values.start,values.end,['wall','grid'].includes(action))<0.001)throw Error('Coincident endpoints');
   if(['floor','ceiling','roof','shaft_opening','railing'].includes(action)){
    const p=values.points;const closed=action!=='railing'||values.closed===true;
    for(let j=1;j<p.length;j++)if(dist(p[j],p[j-1],true)<0.001)throw Error('Repeated consecutive profile point');
    if(closed&&dist(p[0],p.at(-1),true)<0.001)throw Error('Do not repeat closing point');
    if(closed)validateClosedProfile(p);
   }
   if(action==='duct_sized'){const round=values.diameterMm!==undefined,rect=values.widthMm!==undefined&&values.heightMm!==undefined;if(round?(values.widthMm!==undefined||values.heightMm!==undefined):!rect)throw Error('Choose round diameter or both rectangular dimensions')}
   if(action==='set_section_box'&&values.enable!==false&&(!values.min||!values.max||['x','y','z'].some(k=>Math.abs(values.min[k]-values.max[k])<1)))throw Error('Positive section box extents required');
   if(action==='section'&&dist(values.start,values.end,true)<100)throw Error('Section too short');
   if(action==='perspective'&&dist(values.eye,values.target)<1)throw Error('Eye and target must differ');
   if(action==='mirror_copy'&&Math.hypot(values.normal.x,values.normal.y,values.normal.z)<1e-9)throw Error('Zero mirror normal');
   if(action==='area_boundary'&&Math.abs(values.start.z-values.end.z)>0.00001)throw Error('Horizontal area boundary required');
   counts[action]=(counts[action]??0)+1;if(edits.has(action))modified++;else created++;
  }catch(e){issues.push({step:i+1,action,error:e.message});break}
 }
 return {ok:issues.length===0,executed:false,geometricallyVerified:false,commitRequested:args.commit===true,steps:args.steps.length,estimatedCreates:created,estimatedEdits:modified,counts,issues,limitations:'Static argument and dependency checks only. Native Revit dry-run is required for types, topology, geometry, permissions and transactions.'};
}
export function coveredActions(){return Object.keys(schemas).sort()}

export function batchActionSchemas(){return structuredClone(schemas)}
