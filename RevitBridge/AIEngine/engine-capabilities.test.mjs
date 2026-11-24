import assert from 'node:assert/strict';
import {checkBatchCapabilities,batchSchemaForEngine} from './engine-capabilities.mjs';
import {requireCatalog} from './engine-capabilities.mjs';
const old={implemented:['batch']},latest={implemented:['batch','project_context'],cooperativeBatchCancel:true};
const guarded={expectedSession:'abc',expectedRevision:1};
assert.throws(()=>checkBatchCapabilities(guarded,old),/revision guards/);
assert.throws(()=>checkBatchCapabilities({expectedSession:'abc'},latest),/together/);
assert.throws(()=>checkBatchCapabilities({jobId:'a'},old),/cancellation/);
assert.doesNotThrow(()=>checkBatchCapabilities(guarded,latest));
assert.doesNotThrow(()=>checkBatchCapabilities({},old));
const schema={inputSchema:{properties:{expectedPath:{},expectedSession:{},expectedRevision:{},jobId:{}}}};
assert.deepEqual(Object.keys(batchSchemaForEngine(schema,old).inputSchema.properties),['expectedPath']);
assert.equal(Object.keys(batchSchemaForEngine(schema,latest).inputSchema.properties).length,4);
console.log('ENGINE_CAPABILITIES_TESTS=PASS');

assert.throws(()=>requireCatalog({response:{ok:false,status:409},result:{ok:false,error:'busy'}}),/Revit busy/);
assert.throws(()=>requireCatalog({response:{ok:false,status:504},result:{ok:false,error:'execution_state_unknown_do_not_retry'}}),/catalog unavailable/);
assert.deepEqual(requireCatalog({response:{ok:true,status:200},result:{ok:true,implemented:['snapshot']}}).implemented,['snapshot']);