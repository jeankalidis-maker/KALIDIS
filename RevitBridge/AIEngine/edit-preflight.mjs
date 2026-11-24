import {validate} from './command-registry.mjs';
const id={type:'integer',minimum:1},num={type:'number',minimum:-10000000,maximum:10000000},str={type:'string',minLength:1,maxLength:1000},pt={type:'object',properties:{x:num,y:num,z:num},required:['x','y','z'],additionalProperties:false};
const obj=(properties,required=Object.keys(properties))=>({type:'object',properties,required,additionalProperties:false});
const base={elementId:id};
const schemas={move:obj({...base,offset:pt}),copy:obj({...base,offset:pt}),rotate:obj({...base,origin:pt,degrees:{type:'number',minimum:-360,maximum:360}}),set_parameter:obj({...base,parameterId:{type:'integer'},name:str,value:{type:['string','number']}},['elementId','value']),pin:obj({...base,value:{type:'boolean'}}),rename:obj({...base,value:str}),change_type:obj({...base,typeId:id}),delete:obj(base)};
export function reviewEdits(args){
 if(!Array.isArray(args.steps)||args.steps.length<1||args.steps.length>200)throw Error('1-200 edits required');
 for(const [i,s] of args.steps.entries()){
  const {action,...fields}=s;
  if(!Object.hasOwn(schemas,action))throw Error('Unsupported edit '+action);
  validate(schemas[action],fields,'edit['+i+']');
  if(action==='set_parameter'&&(Object.hasOwn(fields,'parameterId')===Object.hasOwn(fields,'name')))throw Error('Exactly one parameter selector required');
 }
 return {ok:true,count:args.steps.length};
}
