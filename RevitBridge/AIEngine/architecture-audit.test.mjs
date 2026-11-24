import assert from 'node:assert/strict';
import {architectureAudit} from './architecture-audit.mjs';
const args={expectedPath:'C:/Sandbox/Test.rvt',minimumDoorWidthMm:800,limit:3};
const call=async(n)=>{
 if(n==='snapshot')return {path:args.expectedPath,title:'Test'};
 if(n==='get_doors')return {doors:[{id:1,widthMm:762},{id:2,widthMm:914},{id:3,widthMm:null}]};
 if(n==='get_element_rooms')return {elements:[{id:1,fromRoom:{id:10},toRoom:{id:11}}]};
 if(n==='audit_warnings')return {total:4,warnings:[],hasMore:true};
 if(n==='audit_level_span')return {total:0,findings:[],hasMore:false};
 throw Error(n);
};
const r=await architectureAudit(args,call);
assert.equal(r.belowThreshold,1);assert.equal(r.unknownWidth,1);assert.equal(r.doors[0].fromRoom.id,10);assert.equal(r.doorsPossiblyTruncated,true);assert.equal(r.warningsTruncated,true);
await assert.rejects(architectureAudit({...args,expectedPath:'Other.rvt'},call),/mismatch/);
console.log('ARCHITECTURE_AUDIT_TESTS=PASS');
