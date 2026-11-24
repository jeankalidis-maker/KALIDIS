import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {inventory} from './bim-catalog-inventory.mjs';
const parent=await mkdtemp(path.join(tmpdir(),'kalidis-catalog-'));
const root=path.join(parent,'Sandbox');
try{
 await mkdir(root);
 const all=await inventory(root);assert.equal(all.count,4);assert.equal(all.ready,0);
 assert.equal((await inventory(root,{query:'pilar estrutural'})).count,1);
 assert.equal((await inventory(root,{query:'PORTA'})).count,1);
 assert.equal((await inventory(root,{query:'inexistente'})).count,0);
 const dir=path.join(root,'OnlineQuarantine-'+ 'a'.repeat(32));await mkdir(dir);
 await writeFile(path.join(dir,'Column_Sqaure.rfa'),'invalid');
 const bad=await inventory(root,{query:'column square'});assert.ok(bad.items.some(x=>x.candidates.some(c=>c.state==='invalid')));assert.equal(bad.ready,0);
}finally{await rm(parent,{recursive:true,force:true})}
console.log('BIM_CATALOG_INVENTORY_TESTS=PASS');
