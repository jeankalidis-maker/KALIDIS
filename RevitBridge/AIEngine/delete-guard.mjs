export function verifyDeletePreview(args,preview){
 const requested=new Set(args.steps.filter(s=>s.action==='delete').map(s=>s.elementId));
 if(requested.size===0)return;
 if(preview.ok!==true||preview.status!=='RolledBack'||!Array.isArray(preview.results))throw Error('Deletion preview unavailable; no commit submitted');
 const reported=new Set();
 for(const r of preview.results)if(Array.isArray(r.deletedIds)){
  if(!requested.has(r.sourceId)||!r.deletedIds.includes(r.sourceId)||r.deletedIds.some(i=>!Number.isSafeInteger(i)))throw Error('Unexpected deletion preview');
  for(const id of r.deletedIds)reported.add(id);
 }
 if([...requested].some(i=>!reported.has(i)))throw Error('Deletion preview incomplete; no commit submitted');
 const extras=[...reported].filter(i=>!requested.has(i));
 if(extras.length)throw Error('Dependent deletion refused; unrequested IDs: '+extras.join(','));
}
