import assert from 'node:assert/strict';import {legacyInspect} from './legacy-inspect.mjs';
const a={expectedPath:'C:/test.rvt',id:'a',acao:'snapshot_elemento',elementIds:[1]};
const call=async n=>({snapshot:{path:a.expectedPath,title:'test'},element_info:{elements:[{id:1}]},selection:{ids:[1]},native_status:{commands:[]}}[n]);
assert.equal((await legacyInspect(a,call)).dados.elementos[0].id,1);
await assert.rejects(legacyInspect({...a,elementIds:[2]},call));
await assert.rejects(legacyInspect({...a,acao:'selecionar'},call));
assert.deepEqual((await legacyInspect({expectedPath:a.expectedPath,id:'a',acao:'selecionar'},call)).dados.ids,[1]);
assert.deepEqual((await legacyInspect({expectedPath:a.expectedPath,id:'a',acao:'listar_comandos_revit'},call)).dados.commands,[]);
console.log('LEGACY_INSPECT_TESTS=PASS');