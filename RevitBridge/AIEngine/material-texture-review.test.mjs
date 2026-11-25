import assert from 'node:assert/strict';import {verifyMaterialTextures} from './material-texture-review.mjs';
const fileName='TextureFixture-'+'a'.repeat(32)+'.png',a={expectedPath:'test.rvt',items:[{id:1,uniqueId:'u'},{id:2,uniqueId:'v'}],expectedFiles:[{fileName,sha256:'a'.repeat(64)}]};
const r={status:'ok',decision:'rolled_back',changes:{added_count:0,modified_count:0,deleted_count:0},result:{path:'test.rvt',rows:a.items.map(x=>({...x,assetId:3,maps:[{state:'staged',fileName}]}))}};
let calls=0;const good=await verifyMaterialTextures(r,a,async()=>{calls++;return {ready:true,state:'verified'}});assert(good.complete&&good.verifiedMaps===2&&calls===1);
const bad=await verifyMaterialTextures(r,a,async()=>({ready:false,state:'missing'}));assert(!bad.complete&&bad.unverifiedMaps===2);
assert(!(await verifyMaterialTextures(r,{...a,expectedFiles:[]},async()=>{throw Error('Must not read')})).complete);
await assert.rejects(verifyMaterialTextures({...r,changes:{...r.changes,modified_count:1}},a,async()=>{}),/incomplete/);
console.log('MATERIAL_TEXTURE_REVIEW_TESTS=PASS');