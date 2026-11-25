// All-form cut groups: ordered extrusions, then blends, then sweeps.
// Exactly one target per group, each void used at most once.
export function reviewFormCuts(a){
 const groups=a.formCuts??[];
 if(!Array.isArray(groups)||groups.length>50)throw Error('Maximum 50 form cut groups');
 if(groups.length&&(a.voidCuts??[]).length)throw Error('Choose legacy voidCuts or formCuts');
 const forms=[...a.shapes,...(a.blends??[]),...(a.sweeps??[])],used=new Set();
 for(const g of groups){
  const i=g.solidIndex;
  if(!Number.isInteger(i)||!forms[i]||forms[i].void||used.has(i))throw Error('Invalid or reused target form');
  if(!Array.isArray(g.voidIndices)||g.voidIndices.length<1||g.voidIndices.length>10)throw Error('1-10 cutters required');
  used.add(i);
  for(const j of g.voidIndices){if(!Number.isInteger(j)||!forms[j]||!forms[j].void||used.has(j))throw Error('Invalid or reused cutter form');used.add(j)}
 }
 return groups.length;
}