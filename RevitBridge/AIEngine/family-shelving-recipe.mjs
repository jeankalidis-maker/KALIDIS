// Local dimensional recipe inspired by FamFab's family-generation workflow (MIT).
// Open-front rectangular shelving unit, no image-derived measurements or structural claim.
import {validate} from './command-registry.mjs';import {reviewFamilySpec} from './family-spec-review.mjs';
const n=(minimum,maximum)=>({type:'number',minimum,maximum});
export const shelvingTool={name:'family_shelving_recipe',description:'Generate native RFA recipe for rectangular open shelving with explicit confirmed external dimensions and board thickness. No file or model writes.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},template:{type:'string',minLength:1},fileName:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}\\.rfa$'},widthMm:n(300,3000),depthMm:n(150,1500),heightMm:n(300,3000),thicknessMm:n(10,100),shelfCount:{type:'integer',minimum:0,maximum:8},referenceId:{type:'string',minLength:1,maxLength:100},dimensionsConfirmed:{type:'boolean'}},required:['expectedPath','template','fileName','widthMm','depthMm','heightMm','thicknessMm','shelfCount','referenceId','dimensionsConfirmed'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function shelvingRecipe(a){
 validate(shelvingTool.inputSchema,a,'family_shelving_recipe');
 if(!a.dimensionsConfirmed)throw Error('Explicit confirmation required');
 const {widthMm:w,depthMm:d,heightMm:h,thicknessMm:t,shelfCount:k}=a;
 const clear=(h-(k+2)*t)/(k+1);
 if(w-2*t<100||d-t<100||clear<100)throw Error('Insufficient clear opening');
 const rect=(x,y,w,d)=>[{x,y},{x:x+w,y},{x:x+w,y:y+d},{x,y:y+d}];
 const board=(x,y,z,w,d,h)=>({zMm:z,heightMm:h,points:rect(x,y,w,d)});
 const shapes=[board(0,0,0,t,d,h),board(w-t,0,0,t,d,h),board(t,d-t,0,w-2*t,t,h)];
 for(let i=0;i<k+2;i++)shapes.push(board(t,0,i===k+1?h-t:i*(clear+t),w-2*t,d-t,t));
 const volume=2*t*d*h+(w-2*t)*t*h+(k+2)*(w-2*t)*(d-t)*t;
 const spec={expectedPath:a.expectedPath,template:a.template,fileName:a.fileName,shapes,parameters:[{name:'Reference_ID',kind:'text',value:a.referenceId,instance:false}]};
 reviewFamilySpec(spec);
 return {ok:true,spec,expected:{dimensionsMm:[w,d,h],volumeM3:volume/1e9,solidCount:k+5,clearOpeningMm:clear},boards:{side:2,back:1,horizontal:k+2},note:'Fixed-size dimensional recipe; boards meet without volumetric overlap. No flex claim, hardware, finish, load capacity or image inference.'};
}