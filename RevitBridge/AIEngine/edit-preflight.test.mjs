import assert from 'node:assert/strict';import {reviewEdits} from './edit-preflight.mjs';
const p={x:1,y:2,z:3};
for(const s of [{action:'move',elementId:1,offset:p},{action:'copy',elementId:1,offset:p},{action:'rotate',elementId:1,origin:p,degrees:90},{action:'set_parameter',elementId:1,parameterId:-1010106,value:'x'},{action:'pin',elementId:1,value:true},{action:'rename',elementId:1,value:'x'},{action:'change_type',elementId:1,typeId:2},{action:'delete',elementId:1}])assert(reviewEdits({steps:[s]}).ok);
for(const s of [{action:'move',id:1,offset:p},{action:'delete',elementId:0},{action:'set_parameter',elementId:1,value:'x'},{action:'pin',elementId:1,value:1}])assert.throws(()=>reviewEdits({steps:[s]}));
console.log('EDIT_PREFLIGHT_TESTS=PASS');