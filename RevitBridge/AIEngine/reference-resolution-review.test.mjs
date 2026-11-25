import assert from 'node:assert/strict';import {reviewResolution} from './reference-resolution-review.mjs';
const a={source:'fixture',scaleKnown:true,issues:[{id:'C',question:'Which chain?',observations:['3+2 vs 4'],alternatives:[{id:'A',description:'As measured',partsMm:[3000,2000],totalMm:4000},{id:'B',description:'New verified total',partsMm:[3000,2000],totalMm:5000}]}],decisions:[]};
assert(!reviewResolution(a).interpretationsComplete);
const d={issueId:'C',alternativeId:'A',confirmationReference:'synthetic fixture',reason:'test'};
assert.equal(reviewResolution({...a,decisions:[d]}).rows[0].state,'confirmed_but_conflicting');
const r=reviewResolution({...a,decisions:[{...d,alternativeId:'B'}]});assert(r.interpretationsComplete&&!r.modelingAuthorized);assert.throws(()=>reviewResolution({...a,decisions:[d,d]}),/Invalid explicit/);assert.throws(()=>reviewResolution({...a,decisions:[{...d,alternativeId:'missing'}]}),/Invalid explicit/);assert(!reviewResolution({...a,scaleKnown:false,decisions:[{...d,alternativeId:'B'}]}).interpretationsComplete);
console.log('REFERENCE_RESOLUTION_TESTS=PASS');