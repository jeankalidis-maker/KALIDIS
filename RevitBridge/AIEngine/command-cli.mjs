#!/usr/bin/env node
// Offline registry CLI: never calls Revit and never reads credentials.
import {readFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {operationStatus} from './operation-journal.mjs';
import {batchPreflight} from './batch-preflight.mjs';
import {loadRegistry,describe,validate} from './command-registry.mjs';
const r=await loadRegistry(path.dirname(fileURLToPath(import.meta.url)));
const [cmd,name,file]=process.argv.slice(2);
try{
 if(cmd==='commands'){const list=describe(r).filter(x=>!name||x.name.includes(name));console.log(JSON.stringify({count:list.length,live:false,commands:list},null,2))}
 else if(cmd==='status'){if(!process.env.LOCALAPPDATA)throw Error('LOCALAPPDATA required');console.log(JSON.stringify(await operationStatus(path.join(process.env.LOCALAPPDATA,'KalidisAIEngine','OperationJournal'),name),null,2))}
 else if(cmd==='preflight'){if(!name)throw Error('Expected batch JSON file');const result=batchPreflight(JSON.parse(await readFile(name,'utf8')),r);console.log(JSON.stringify(result,null,2));if(!result.ok)process.exitCode=1}
 else if(cmd==='schema'){const e=r.all.get(name);if(!e)throw Error('Unknown command');console.log(JSON.stringify(e.tool,null,2))}
 else if(cmd==='validate'){const e=r.all.get(name);if(!e||!file)throw Error('Expected command and JSON file');const args=JSON.parse(await readFile(file,'utf8'));validate(e.tool.inputSchema,args,name);console.log(JSON.stringify({ok:true,command:name,executed:false}))}
 else throw Error('Usage: node command-cli.mjs commands [filter] | schema NAME | validate NAME JSON_FILE | status REQUEST_ID | preflight JSON_FILE');
}catch(e){console.error(e.message);process.exitCode=1}
