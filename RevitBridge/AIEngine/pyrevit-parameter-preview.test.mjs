import assert from 'node:assert/strict';import {inspectChanges,verifyPythonPreview,pythonBatchTool} from './pyrevit-parameter-preview.mjs';import {validate} from './command-registry.mjs';
const a={expectedPath:'test.rvt',changes:[{id:1,uniqueId:'u1',parameter:'comments',before:null,after:'test'},{id:2,uniqueId:'u2',parameter:'mark',before:'',after:'test2'}]};
const rows=[{id:1,uniqueId:'u1',typeId:3,parameters:[{id:-1010106,storage:'String',value:null}]},{id:2,uniqueId:'u2',typeId:4,parameters:[{id:-1001203,storage:'String',value:''}]}];
validate(pythonBatchTool.inputSchema,a);assert.equal(inspectChanges(a.changes,rows).length,2);
assert.throws(()=>inspectChanges([...a.changes,a.changes[0]],rows),/Duplicate/);
assert.throws(()=>inspectChanges([{...a.changes[0],before:''}],rows),/mismatch/);
assert.throws(()=>validate(pythonBatchTool.inputSchema,{...a,commit:true}),/unknown/);
const r={status:'ok',decision:'rolled_back',run_id:'test',changes:{added_count:0,deleted_count:0,modified_count:2,modified:[{id:1},{id:2}]},result:{path:a.expectedPath,rows:a.changes.map(c=>({id:c.id,parameter:c.parameter,before:c.before,inside:c.after}))}};
assert(verifyPythonPreview(r,a).rollbackVerified);assert.throws(()=>verifyPythonPreview({...r,changes:{...r.changes,added_count:1}},a),/Unexpected/);assert.throws(()=>verifyPythonPreview({...r,decision:'committed'},a),/Unexpected/);console.log('PYREVIT_PARAMETER_PREVIEW_TESTS=PASS');