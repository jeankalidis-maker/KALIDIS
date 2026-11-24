import assert from 'node:assert/strict';import {reviewNativeShape} from './native-shape-review.mjs';
const pts=(...a)=>a.map(([x,y])=>({x,y}));
const a={mode:'floor_hole',outer:pts([0,0],[8000,0],[8000,6000],[0,6000]),inner:pts([1000,1000],[3000,1000],[3000,3000],[1000,3000])};
assert.equal(reviewNativeShape(a).netAreaMm2,44000000);
assert.throws(()=>reviewNativeShape({...a,inner:pts([0,0],[3000,1000],[3000,3000])}),/strictly inside/);
assert.throws(()=>reviewNativeShape({...a,outer:pts([0,0],[8000,0],[8000,6000],[6000,6000],[6000,2000],[2000,2000],[2000,6000],[0,6000]),inner:pts([1000,3000],[7000,3000],[7000,4000],[1000,4000])}),/strictly inside/);
assert.throws(()=>reviewNativeShape({mode:'arc_wall',start:{x:0,y:0},mid:{x:1,y:1},end:{x:2,y:2},heightMm:3000}),/Degenerate/);
assert.equal(reviewNativeShape({mode:'room_boundary',points:a.outer}).areaMm2,48000000);
console.log('NATIVE_SHAPE_REVIEW_TESTS=PASS');