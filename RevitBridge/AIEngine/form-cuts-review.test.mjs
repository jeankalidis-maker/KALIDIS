import assert from 'node:assert/strict';import {reviewFormCuts} from './form-cuts-review.mjs';
const a={shapes:[{}, {void:true},{void:true}],blends:[{}, {void:true}],sweeps:[{}],formCuts:[{solidIndex:0,voidIndices:[1,2]},{solidIndex:3,voidIndices:[4]}]};
assert.equal(reviewFormCuts(a),2);
assert.throws(()=>reviewFormCuts({...a,formCuts:[{solidIndex:0,voidIndices:[1,1]}]}),/reused/);
assert.throws(()=>reviewFormCuts({...a,formCuts:[{solidIndex:1,voidIndices:[2]}]}),/target/);
assert.throws(()=>reviewFormCuts({...a,formCuts:[{solidIndex:0,voidIndices:[5]}]}),/cutter/);
assert.throws(()=>reviewFormCuts({...a,voidCuts:[{solidIndex:0,voidIndex:1}]}),/Choose/);
console.log('FORM_CUTS_TESTS=PASS');