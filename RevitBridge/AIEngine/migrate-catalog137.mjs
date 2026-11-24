// One-time migration of the four already validated Smoke catalogue loads (109,113).
import {spawnSync} from 'node:child_process';import path from 'node:path';import assert from 'node:assert/strict';
import {reserveImport} from './catalog-import-guard.mjs';
function call(n,a={}){const p=spawnSync(process.execPath,['mcp-server.mjs'],{input:JSON.stringify({jsonrpc:'2.0',id:1,method:'tools/call',params:{name:n,arguments:a}})+'\n',encoding:'utf8'});if(p.status!==0)throw Error('Adapter failed');const j=JSON.parse(p.stdout.trim());if(j.error||j.result.isError)throw Error(j.error?.message??j.result.content[0].text);return JSON.parse(j.result.content[0].text)}
const c=call('project_context');assert(c.path.endsWith('Smoke-9c3c99fd336746a0b555995ee2728a38.rvt'));
const cat=call('bim_catalog_inventory'),s=call('snapshot');assert.equal(cat.ready,4);
const historical=[
 ['Column_Sqaure.rfa',349493,'OnlineVerified-f9489d68e2604b65b95d3f7c99fb54a1',[350279,350281]],
 ['I_Single-Flush.rfa',350297,'OnlineVerified-c9d9283c24414f158f8584a765da505d',[350818,350820,350822]],
 ['Mullion_Rectangle.rfa',349121,'OnlineVerified-2722502575ec447699133d553f559ce9',[349489]],
 ['Column_Square_Tollerance.rfa',350840,'OnlineVerified-5b2955b0bc494c988ca2aeda8130aa30',[351690,351692]]
];
const dir=path.join(process.env.LOCALAPPDATA,'KalidisAIEngine','CatalogImports'),records=[];
for(const [fileName,id,name,ids] of historical){
 const p=cat.items.find(x=>x.fileName===fileName);assert(p?.ready);
 assert(s.families.some(x=>x.id===id&&x.name===name));
 const symbols=call('list_family_types',{familyName:name});
 const list=symbols.familyTypes??symbols.types;
 assert(Array.isArray(list)&&ids.every(i=>list.some(x=>x.id===i)),JSON.stringify(symbols));
 records.push({p,id,name,ids});
}
for(const {p,id,name,ids} of records){
 const key=await reserveImport(dir,c.path,p,{state:'historically_verified',familyId:id,familyName:name,symbolIds:ids,evidence:'CHECKLIST 109/113, independent snapshot and symbol inventory 137'});
 console.log(JSON.stringify({file:p.fileName,key,id,ids}));
}
console.log('HISTORICAL_CATALOG_RESERVATIONS_PASS');
