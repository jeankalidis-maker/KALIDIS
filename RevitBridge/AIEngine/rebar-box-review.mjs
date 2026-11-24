import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1};
export const rebarBoxTool={name:'rebar_box_review',description:'Screen nominal rebar bounds against the actual host bounding box and inventory. Not physical cover or structural design certification.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},hostId:id,ids:{type:'array',items:id,minItems:1,maxItems:29,uniqueItems:true},minimumMm:{type:'number',minimum:0,maximum:1000}},required:['expectedPath','hostId','ids','minimumMm'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
function bounds(e){if(!e||![e.minMm,e.maxMm].every(p=>Array.isArray(p)&&p.length===3&&p.every(Number.isFinite))||e.minMm.some((x,i)=>x>e.maxMm[i]))throw Error('Invalid element bounds');return e}
export async function rebarBoxReview(a,call){
 validate(rebarBoxTool.inputSchema,a,'rebar_box_review');if(a.ids.includes(a.hostId))throw Error('Host cannot be reinforcement');
 const before=await call('project_context',{});if(before.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Wrong project');
 const geom=await call('geometry_review',{expectedPath:a.expectedPath,ids:[a.hostId,...a.ids]});
 const inv=await call('rebar_inventory',{expectedPath:a.expectedPath,ids:a.ids});
 const solid=await call('solid_audit',{expectedPath:a.expectedPath,ids:[a.hostId]});
 if([geom,inv,solid].some(r=>r.status!=='RolledBack'))throw Error('Unverified readback');
 const host=bounds(geom.result.elements.find(e=>e.id===a.hostId)),h=solid.result.elements[0];
 if(h.id!==a.hostId||!h.rebarHost||h.solidCount<1||h.volumeM3<=0)throw Error('Invalid host geometry');
 const boxM3=host.maxMm.reduce((v,x,i)=>v*(x-host.minMm[i]),1)/1e9;
 const rows=a.ids.map(i=>{const g=bounds(geom.result.elements.find(e=>e.id===i)),r=inv.result.elements.find(e=>e.id===i);if(!r||r.hostId!==a.hostId)throw Error('Rebar host mismatch '+i);
 const gaps=[...g.minMm.map((x,j)=>x-host.minMm[j]),...g.maxMm.map((x,j)=>host.maxMm[j]-x)],min=Math.min(...gaps);
 return {id:i,minimumBoxGapMm:min,gapsMm:gaps,quantity:r.quantity,totalLengthM:r.totalLengthM,estimatedMassKg:r.estimatedMassKg,screen:min<0?'outside_host_box':min+0.01<a.minimumMm?'below_requested_box_gap':'box_gap_pass_only'};
 });
 const after=await call('project_context',{});if(after.path!==before.path||after.session!==before.session||after.revision!==before.revision)throw Error('Model changed during review');
 return {ok:true,hostId:a.hostId,hostSolidVolumeM3:h.volumeM3,hostBoxVolumeM3:boxM3,boxFillRatio:boxM3>0?h.volumeM3/boxM3:null,minimumRequestedMm:a.minimumMm,rows,physicalCoverVerified:false,note:'AABB screening only. Bounding boxes may include nonphysical extents, and host voids/irregular surfaces are not measured. Does not certify cover, anchorage or design.'};
}