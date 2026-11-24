import assert from 'node:assert/strict';import {verifyDeletePreview} from './delete-guard.mjs';
const a={steps:[{action:'delete',elementId:7}]};
const p={ok:true,status:'RolledBack',results:[{sourceId:7,deletedIds:[7]}]};
assert.doesNotThrow(()=>verifyDeletePreview(a,p));
assert.throws(()=>verifyDeletePreview(a,{...p,results:[{sourceId:7,deletedIds:[7,9]}]}),/Dependent deletion refused/);
assert.throws(()=>verifyDeletePreview(a,{...p,status:'Committed'}),/preview unavailable/);
assert.throws(()=>verifyDeletePreview(a,{...p,results:[]}),/incomplete/);
console.log('DELETE_GUARD_TESTS=PASS');
