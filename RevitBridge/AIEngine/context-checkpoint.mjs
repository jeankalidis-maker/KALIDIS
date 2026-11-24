import {mkdir,open,readFile,lstat} from 'node:fs/promises';
import path from 'node:path';import {validate} from './command-registry.mjs';
export const checkpointTool={name:'context_checkpoint',description:'Save or verify a local sandbox project context checkpoint; verify path, session, revision, view, selection and tracked identities. Never authorizes writes.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},checkpointId:{type:'string',pattern:'^[A-Za-z0-9_-]{8,80}$'},mode:{type:'string',enum:['save','verify']},ids:{type:'array',items:{type:'integer',minimum:1},maxItems:100,uniqueItems:true}},required:['expectedPath','checkpointId','mode'],additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,idempotentHint:false,openWorldHint:false}};
function identity(c){return {path:c.path,title:c.title,session:c.session,revision:c.revision,units:c.units,viewId:c.view.id,selection:c.elements.map(e=>({id:e.id,uniqueId:e.uniqueId})),selectionCount:c.selectionCount}}
export async function checkpoint(a,call,root){
 validate(checkpointTool.inputSchema,a,'context_checkpoint');
 const base=path.dirname(root),file=path.resolve(a.expectedPath);
 if(path.dirname(file).toLowerCase()!==path.join(base,'Sandbox').toLowerCase()||path.extname(file).toLowerCase()!=='.rvt')throw Error('Only local Sandbox RVT');
 const c=await call('project_context',{selectionLimit:200});if(!c.ok||c.path.toLowerCase()!==file.toLowerCase()||c.family||c.workshared||c.cloud||c.readOnly||c.truncated||c.units!=='mm')throw Error('Unsupported or mismatched project context');
 await mkdir(root,{recursive:true});const st=await lstat(root);if(!st.isDirectory()||st.isSymbolicLink())throw Error('Invalid checkpoint directory');
 const f=path.join(root,a.checkpointId+'.json');
 let old;
 if(a.mode==='verify'){if(a.ids!==undefined)throw Error('Verify uses stored IDs');const st=await lstat(f);if(!st.isFile()||st.isSymbolicLink()||st.size>100000)throw Error('Invalid checkpoint');old=JSON.parse(await readFile(f,'utf8'));if(old.context.path!==c.path)throw Error('Checkpoint project mismatch')}
 const ids=a.mode==='save'?(a.ids??[]):old.tracked.map(e=>e.id);
 const tracked=ids.length?(await call('element_info',{ids})).elements.map(e=>({id:e.id,uniqueId:e.uniqueId,typeId:e.typeId})).sort((a,b)=>a.id-b.id):[];
 const end=await call('project_context',{selectionLimit:200});
 const now=identity(end);if(JSON.stringify(now)!==JSON.stringify(identity(c)))throw Error('Context changed during capture');
 if(tracked.length!==ids.length||tracked.some(e=>typeof e.uniqueId!=='string'))throw Error('Tracked identity missing');
 if(a.mode==='save'){const record={context:now,tracked};const fd=await open(f,'wx');try{await fd.writeFile(JSON.stringify(record));await fd.sync()}finally{await fd.close()}return {ok:true,saved:true,context:now,tracked,note:'Checkpoint is evidence, not write permission.'}}
 const mismatches=[];for(const k of ['session','revision','units','viewId','selectionCount','selection'])if(JSON.stringify(old.context[k])!==JSON.stringify(now[k]))mismatches.push(k);
 if(JSON.stringify(old.tracked)!==JSON.stringify(tracked))mismatches.push('tracked');
 return {ok:true,match:mismatches.length===0,mismatches,context:now,tracked,note:'A new session or changed revision requires explicit new checkpoint. Not a model lock.'};
}