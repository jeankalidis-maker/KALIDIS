import assert from 'node:assert/strict';import {reviewFormulaDependencies} from './formula-dependencies.mjs';
const p=new Map(['r','h','driver','ratio','text label'].map(n=>[n,{}]));
assert.deepEqual(reviewFormulaDependencies(p,{H:'R * Ratio',R:'Driver'}).order,['r','h']);
assert.throws(()=>reviewFormulaDependencies(p,{R:'R * 2'}),/Circular/);
assert.throws(()=>reviewFormulaDependencies(p,{R:'H',H:'R'}),/Circular/);
assert.throws(()=>reviewFormulaDependencies(p,{R:'[Text Label]','Text Label':'R'}),/Circular/);
assert.doesNotThrow(()=>reviewFormulaDependencies(p,{'Text Label':'"Text Label"'}));
assert.throws(()=>reviewFormulaDependencies(p,{R:'Driver',r:'Driver'}),/Duplicate/);
console.log('FORMULA_DEPENDENCIES_TESTS=PASS');