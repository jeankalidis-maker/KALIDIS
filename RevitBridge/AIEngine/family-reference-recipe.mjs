import {validate} from './command-registry.mjs';
import {reviewFamilySpec} from './family-spec-review.mjs';
export const referenceFamilyTool={name:'family_reference_recipe',description:'Build a bounded native furniture family recipe from explicit confirmed dimensions. No image inference, no file creation or model edit.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},template:{type:'string',minLength:1},fileName:{type:'string',pattern:'^[A-Za-z0-9_-]{8,100}\\.rfa$'},widthMm:{type:'number',minimum:300,maximum:5000},depthMm:{type:'number',minimum:300,maximum:3000},heightMm:{type:'number',minimum:300,maximum:3000},topThicknessMm:{type:'number',minimum:10,maximum:200},legWidthMm:{type:'number',minimum:20,maximum:300},legDepthMm:{type:'number',minimum:20,maximum:300},insetMm:{type:'number',minimum:0,maximum:500},referenceId:{type:'string',minLength:1,maxLength:100},dimensionsConfirmed:{type:'boolean'}},required:['expectedPath','template','fileName','widthMm','depthMm','heightMm','topThicknessMm','legWidthMm','legDepthMm','insetMm','referenceId','dimensionsConfirmed'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export function referenceFamily(a){
 validate(referenceFamilyTool.inputSchema,a,'family_reference_recipe');
 if(!a.dimensionsConfirmed)throw Error('Explicit dimensional confirmation required');
 const {widthMm:w,depthMm:d,heightMm:h,topThicknessMm:t,legWidthMm:lw,legDepthMm:ld,insetMm:i}=a;
 if(2*(i+lw)>=w||2*(i+ld)>=d||h-t<100)throw Error('Overlapping legs or insufficient leg height');
 const rect=(x,y,w,d)=>[{x,y},{x:x+w,y},{x:x+w,y:y+d},{x,y:y+d}];
 const shapes=[{zMm:h-t,heightMm:t,points:rect(0,0,w,d)}];
 for(const x of [i,w-i-lw])for(const y of [i,d-i-ld])shapes.push({zMm:0,heightMm:h-t,points:rect(x,y,lw,ld),heightParameter:'Leg_Height'});
 const spec={expectedPath:a.expectedPath,template:a.template,fileName:a.fileName,shapes,parameters:[{name:'Reference_ID',kind:'text',value:a.referenceId,instance:false}]};
 reviewFamilySpec(spec);
 return {ok:true,spec,expected:{dimensionsMm:[w,d,h],volumeM3:(w*d*t+4*lw*ld*(h-t))/1e9,solidCount:5},referenceId:a.referenceId,note:'Explicit dimensions, four rectangular legs and rectangular top only. No hidden dimensions or automatic image recognition; not a structural capacity check.'};
}