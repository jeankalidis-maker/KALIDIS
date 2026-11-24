import assert from 'node:assert/strict';
import {pythonCommentWorkflow} from './python-comment-workflow.mjs';
let value='old',uid='u',calls=0;
const a={expectedPath:'C:/Sandbox/Test.rvt',id:1,uniqueId:'u',before:'old',after:'new',direction:'forward'};
const call=async (n,x)=>{
 if(n==='snapshot')return {path:a.expectedPath};
 if(n==='element_info')return {elements:[{uniqueId:uid,parameters:[{id:-1010106,storage:'String',readOnly:false,value}]}]};
 assert.equal(x.expected,value);calls++;if(x.commit)value=x.value;
 return {status:x.commit?'Committed':'RolledBack',result:{id:1,value:x.value,previous:x.expected,pythonPid:123}};
};
assert.equal((await pythonCommentWorkflow(a,call)).final,'old');
assert.equal((await pythonCommentWorkflow({...a,commit:true},call)).final,'new');
assert.equal((await pythonCommentWorkflow({...a,direction:'undo',commit:true},call)).final,'old');
assert.equal((await pythonCommentWorkflow({...a,direction:'redo',commit:true},call)).final,'new');
await assert.rejects(pythonCommentWorkflow({...a,commit:true},call),/mismatch/);
uid='other';await assert.rejects(pythonCommentWorkflow({...a,direction:'undo',commit:true},call),/mismatch/);
assert.equal(calls,4);
console.log('PYTHON_COMMENT_WORKFLOW_TESTS=PASS');
