// Conservative screening of newly requested straight walls on the same level.
// Widths come from native WallType parameters, not from their display names.
export function reviewWallPairs(steps,widths=new Map()){
 const walls=steps.filter(s=>s.action==='wall');
 for(let i=0;i<walls.length;i++)for(let j=i+1;j<walls.length;j++){
  const a=walls[i],b=walls[j];if(a.levelId!==b.levelId)continue;
  const dx=a.end.x-a.start.x,dy=a.end.y-a.start.y,l=Math.hypot(dx,dy);
  const ex=b.end.x-b.start.x,ey=b.end.y-b.start.y,m=Math.hypot(ex,ey);
  if(l<.001||m<.001||Math.abs(dx*ey-dy*ex)>1e-7*l*m)continue;
  const ux=dx/l,uy=dy/l;
  const project=p=>(p.x-a.start.x)*ux+(p.y-a.start.y)*uy;
  const sep=Math.abs((b.start.x-a.start.x)*uy-(b.start.y-a.start.y)*ux);
  const x=project(b.start),y=project(b.end),overlap=Math.min(l,Math.max(x,y))-Math.max(0,Math.min(x,y));
  if(overlap<=.1)continue;
  const wa=widths.get(a.typeId),wb=widths.get(b.typeId),limit=Number.isFinite(wa)&&Number.isFinite(wb)?(wa+wb)/2:.1;
  if(sep<limit-.01)throw Error('Overlapping parallel wall requests on same level: separation '+sep.toFixed(3)+' mm, overlap length '+overlap.toFixed(3)+' mm');
 }
}
export function candidateTypeIds(steps){
 const w=steps.filter(s=>s.action==='wall');return [...new Set(w.filter(a=>Number.isSafeInteger(a.typeId)&&w.some(b=>a!==b&&a.levelId===b.levelId)).map(a=>a.typeId))];
}
