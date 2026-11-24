import assert from 'node:assert/strict';
import {mkdtemp,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {validate,loadRegistry,describe} from './command-registry.mjs';
import {once} from './operation-journal.mjs';
const root=path.dirname(fileURLToPath(import.meta.url)),r=await loadRegistry(root);
assert.equal(r.all.size,r.base.length+r.reviewed.length);
assert.ok(r.all.has('orthogonal_plan'));assert.ok(r.all.has('family_catalog_load'));
const schema={type:'object',properties:{items:{type:'array',minItems:1,items:{type:'integer',minimum:1}}},required:['items'],additionalProperties:false};
validate(schema,{items:[1,2]});for(const v of [{items:[]},{items:[0]},{items:['2']},{items:[1],other:1},{}])assert.throws(()=>validate(schema,v));
const dir=await mkdtemp(path.join(tmpdir(),'kalidis-journal-'));let count=0;
try{
 const work=async()=>{count++;return {isError:false}};
 await once(dir,'test-id-001','batch',{a:1,b:2},work);
 await assert.rejects(once(dir,'test-id-001','batch',{b:2,a:1},work),/already recorded/);
 await assert.rejects(once(dir,'test-id-001','batch',{a:2},work),/different operation/);
 await assert.rejects(once(dir,'test-id-002','batch',{},async()=>{throw Error('simulated interruption')}),/simulated interruption/);
 await assert.rejects(once(dir,'test-id-002','batch',{},work),/started/);
 assert.equal(count,1);
}finally{await rm(dir,{recursive:true,force:true})}
console.log('REGISTRY_JOURNAL_TESTS=PASS; registered='+describe(r).length+' (offline)');
