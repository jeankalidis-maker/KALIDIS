// Original Kalidis command registry. No external dependencies.
import {readFile} from 'node:fs/promises';
import path from 'node:path';
function canonical(value){
 if(Array.isArray(value))return '['+value.map(canonical).join(',')+']';
 if(value!==null&&typeof value==='object')return '{'+Object.keys(value).sort().map(k=>JSON.stringify(k)+':'+canonical(value[k])).join(',')+'}';
 return JSON.stringify(value);
}
export function validate(schema,value,label='arguments'){
 const fail=(reason)=>{throw new Error(label+': '+reason)};
 if(!schema||typeof schema!=='object')fail('invalid schema');
 if(schema.anyOf){if(!schema.anyOf.some(s=>{try{validate(s,value,label);return true}catch{return false}}))fail('no schema variant matches')}
 if(schema.oneOf){if(schema.oneOf.filter(s=>{try{validate(s,value,label);return true}catch{return false}}).length!==1)fail('expected exactly one schema variant')}
 if(schema.allOf)for(const sub of schema.allOf)validate(sub,value,label);
 const kind=schema.type;
 const valid=k=>({string:()=>typeof value==='string',boolean:()=>typeof value==='boolean',integer:()=>Number.isSafeInteger(value),number:()=>typeof value==='number'&&Number.isFinite(value),array:()=>Array.isArray(value),object:()=>value!==null&&typeof value==='object'&&!Array.isArray(value),null:()=>value===null}[k]?.()??false);
 if(kind&&!(Array.isArray(kind)?kind.some(valid):valid(kind)))fail('invalid type');
 if(schema.enum&&!schema.enum.some(v=>canonical(v)===canonical(value)))fail('invalid enum');
 if(Object.hasOwn(schema,'const')&&canonical(value)!==canonical(schema.const))fail('invalid constant');
 if(typeof value==='string'){
  if(value.length<(schema.minLength??0)||value.length>(schema.maxLength??Infinity))fail('invalid length');
  if(schema.pattern&&!new RegExp(schema.pattern).test(value))fail('invalid pattern');
 }
 if(typeof value==='number'){
  if(value<(schema.minimum??-Infinity)||value>(schema.maximum??Infinity)||value<=(schema.exclusiveMinimum??-Infinity)||value>=(schema.exclusiveMaximum??Infinity))fail('outside numeric bounds');
 }
 if(Array.isArray(value)){
  if(value.length<(schema.minItems??0)||value.length>(schema.maxItems??Infinity))fail('invalid array length');
  if(schema.uniqueItems&&new Set(value.map(canonical)).size!==value.length)fail('duplicate items');
  if(schema.items)value.forEach((v,i)=>validate(schema.items,v,label+'['+i+']'));
 }else if(value!==null&&typeof value==='object'){
  for(const k of schema.required??[])if(!Object.hasOwn(value,k))fail('missing '+k);
  for(const [k,v] of Object.entries(value)){
   if(Object.hasOwn(schema.properties??{},k))validate(schema.properties[k],v,label+'.'+k);
   else if(schema.additionalProperties===false)fail('unknown '+k);
   else if(typeof schema.additionalProperties==='object')validate(schema.additionalProperties,v,label+'.'+k);
  }
 }
}
export async function loadRegistry(root){
 const read=async file=>JSON.parse((await readFile(path.join(root,file),'utf8')).replace(/^\uFEFF/,''));
 const base=await read('tools.json'), reviewed=await read('reviewed-tools.json');
 const all=new Map();
 for(const [tool,kind,entry] of [...base.map(t=>[t,'native',null]),...reviewed.map(e=>[e.tool,'reviewed',e])]){
  if(!tool?.name||all.has(tool.name)||tool.inputSchema?.type!=='object'||!tool.annotations)throw Error('Invalid or duplicate registry entry: '+tool?.name);
  if(kind==='reviewed'&&!/^[A-F0-9]{12}$/.test(entry.sourcePrefix))throw Error('Invalid source prefix: '+tool.name);
  all.set(tool.name,{tool,kind,entry});
 }
 return {base,reviewed,all};
}
export function describe(registry){
 return [...registry.all.values()].map(({tool,kind})=>({name:tool.name,kind,readOnly:tool.annotations.readOnlyHint===true,destructive:tool.annotations.destructiveHint===true,required:tool.inputSchema.required??[]}));
}
