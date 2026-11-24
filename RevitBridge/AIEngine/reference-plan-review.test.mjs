import assert from 'node:assert/strict';
import {referenceReview} from './reference-plan-review.mjs';
const a={source:'Synthetic calibrated survey',scaleKnown:true,toleranceMm:20,chains:[{name:'A',partsMm:[2000,3000],totalMm:5000}],unresolved:[]};
assert.equal(referenceReview(a).metricReconstructionReady,true);
assert.equal(referenceReview({...a,scaleKnown:false}).metricReconstructionReady,false);
assert.equal(referenceReview({...a,unresolved:['Unknown door width']}).metricReconstructionReady,false);
assert.equal(referenceReview({...a,chains:[{name:'A',partsMm:[2000,3000],totalMm:4900}]}).chains[0].deltaMm,100);
assert.throws(()=>referenceReview({...a,chains:[{name:'A',partsMm:[-1],totalMm:1}]}));
console.log('REFERENCE_PLAN_REVIEW_TESTS=PASS');
