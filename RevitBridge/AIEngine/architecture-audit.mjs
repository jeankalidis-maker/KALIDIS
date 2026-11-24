import {validate} from './command-registry.mjs';
export const architectureAuditTool={name:'architecture_audit',description:'Combine native KenLP door/room queries and Horizun warning and level-span audits. Explicit nominal door width threshold, advisory only; no automatic edits or compliance certification.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},minimumDoorWidthMm:{type:'number',minimum:300,maximum:2000},limit:{type:'integer',minimum:1,maximum:200}},required:['expectedPath','minimumDoorWidthMm'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function architectureAudit(args,call){
 validate(architectureAuditTool.inputSchema,args,'architecture_audit');
 const limit=args.limit??100;
 const initial=await call('snapshot',{});
 if(initial.path.toLowerCase()!==args.expectedPath.toLowerCase())throw Error('Active document mismatch');
 const doors=await call('get_doors',{limit});
 const relations=doors.doors.length?await call('get_element_rooms',{ids:doors.doors.map(x=>x.id)}):{elements:[]};
 const rel=new Map(relations.elements.map(x=>[x.id,x]));
 const findings=doors.doors.map(d=>({id:d.id,name:d.name,widthMm:d.widthMm,belowThreshold:d.widthMm==null?null:d.widthMm<args.minimumDoorWidthMm,fromRoom:rel.get(d.id)?.fromRoom??null,toRoom:rel.get(d.id)?.toRoom??null,relationshipUnavailable:rel.get(d.id)?.unavailable??[],levelName:d.levelName}));
 const warnings=await call('audit_warnings',{limit,offset:0});
 const spans=await call('audit_level_span',{limit,offset:0});
 const last=await call('snapshot',{});
 if(last.path!==initial.path||last.title!==initial.title)throw Error('Document changed during audit');
 return {ok:true,doorCount:findings.length,doorsPossiblyTruncated:doors.doors.length===limit,belowThreshold:findings.filter(d=>d.belowThreshold===true).length,unknownWidth:findings.filter(d=>d.belowThreshold===null).length,doors:findings,warningTotal:warnings.total,warnings:warnings.warnings,warningsTruncated:warnings.hasMore,levelSpanTotal:spans.total,levelSpans:spans.findings,spansTruncated:spans.hasMore,note:'Nominal door widths, not measured clear opening. Room relationships depend on phase. Level-span findings can be intentional. Read-only, not an atomic snapshot or code compliance assessment.'};
}
