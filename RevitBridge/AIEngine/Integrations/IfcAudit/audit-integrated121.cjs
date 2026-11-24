const fs=require('node:fs'),crypto=require('node:crypto'),assert=require('node:assert/strict'),W=require('web-ifc');
async function main(){
 const file=process.argv[2];assert(file&&file.toLowerCase().endsWith('.ifc'));
 const bytes=fs.readFileSync(file),api=new W.IfcAPI();await api.Init();
 const model=api.OpenModel(new Uint8Array(bytes),{COORDINATE_TO_ORIGIN:false});
 assert(model>=0&&api.IsModelOpen(model),'IFC not opened');
 try{
  const types=['IFCPROJECT','IFCBUILDING','IFCBUILDINGSTOREY','IFCWALL','IFCWALLSTANDARDCASE','IFCSLAB','IFCDOOR','IFCSPACE','IFCPIPESEGMENT','IFCRELCONTAINEDINSPATIALSTRUCTURE','IFCRELAGGREGATES','IFCRELVOIDSELEMENT','IFCRELFILLSELEMENT','IFCRELDEFINESBYPROPERTIES'];
  const ids=t=>{const a=api.GetLineIDsWithType(model,W[t]);return Array.from({length:a.size()},(_,i)=>a.get(i))};
  const counts=Object.fromEntries(types.map(t=>[t,ids(t).length]));
  assert(counts.IFCPROJECT===1&&counts.IFCBUILDING>=1&&counts.IFCBUILDINGSTOREY>=2);
  assert(counts.IFCWALL+counts.IFCWALLSTANDARDCASE>=12&&counts.IFCSLAB>=2&&counts.IFCDOOR>=4&&counts.IFCSPACE>=6);
  const line=i=>api.GetLine(model,i);
  const project=line(ids('IFCPROJECT')[0]),assignment=line(project.UnitsInContext.value);
  const units=assignment.Units.map(h=>line(h.value)).filter(u=>u.UnitType).map(u=>({kind:u.UnitType.value,name:u.Name?.value,prefix:u.Prefix?.value??null}));
  assert(units.some(u=>u.kind==='LENGTHUNIT'&&u.name==='METRE'&&u.prefix==='MILLI'),'Unexpected project length unit');
  const storeys=ids('IFCBUILDINGSTOREY').map(i=>({id:i,name:line(i).Name?.value,elevation:line(i).Elevation?.value}));
  assert(storeys.some(s=>Math.abs(s.elevation)<.01)&&storeys.some(s=>Math.abs(s.elevation-4000)<.01));
  let checkedRefs=0;for(const t of ['IFCRELCONTAINEDINSPATIALSTRUCTURE','IFCRELAGGREGATES','IFCRELVOIDSELEMENT','IFCRELFILLSELEMENT','IFCRELDEFINESBYPROPERTIES']){
   for(const i of ids(t)){const r=line(i);for(const k of Object.keys(r).filter(k=>/^Relating|^Related/.test(k))){for(const h of [].concat(r[k]??[])){assert(Number.isInteger(h.value)&&line(h.value),'Unresolved relationship '+i);checkedRefs++;}}}
  }
  assert(checkedRefs>0&&counts.IFCRELFILLSELEMENT>=4);
  let meshCount=0,parts=0,triangles=0;const geometryTypes={};const tested=new Set(),empty=[],invalid=[],physical=new Set();
  api.StreamAllMeshes(model,mesh=>{
   if(mesh.geometries.size()===0)return;
   meshCount++;const t=api.GetNameFromTypeCode(api.GetLineType(model,mesh.expressID));geometryTypes[t]=(geometryTypes[t]??0)+1;
   for(let i=0;i<mesh.geometries.size();i++){const p=mesh.geometries.get(i),g=api.GetGeometry(model,p.geometryExpressID);
    try{const n=g.GetIndexDataSize(),v=g.GetVertexDataSize();if(n===0||v===0){empty.push({element:mesh.expressID,type:t,geometry:p.geometryExpressID,n,v});continue}if(n%3!==0||v%6!==0){invalid.push({element:mesh.expressID,type:t,geometry:p.geometryExpressID,n,v});continue}parts++;triangles+=n/3;physical.add(mesh.expressID);
     if(!tested.has(p.geometryExpressID)){const verts=api.GetVertexArray(g.GetVertexData(),v),idx=api.GetIndexArray(g.GetIndexData(),n);assert(verts.every(Number.isFinite));assert(idx.every(k=>k<v/6));tested.add(p.geometryExpressID);}
    }finally{g.delete();}
   }
  });
    assert(meshCount>0&&triangles>0);
  const axes=[],unresolved=[];
  for(const e of empty){
   const entity=line(e.element),shape=line(entity.Representation.value);
   const representations=shape.Representations.map(h=>line(h.value));
   const axis=representations.some(r=>r.RepresentationIdentifier?.value==='Axis'&&r.Items.some(h=>h.value===e.geometry));
   if(axis&&physical.has(e.element))axes.push(e);else unresolved.push(e);
  }
  const requiredBodies=['IFCWALL','IFCWALLSTANDARDCASE','IFCSLAB','IFCDOOR','IFCFLOWSEGMENT','IFCFLOWFITTING'];
  const missingBodies=requiredBodies.flatMap(t=>ids(t).filter(id=>!physical.has(id)).map(id=>({id,type:t})));
    const geometryPass=unresolved.length===0&&invalid.length===0&&missingBodies.length===0;
  // Specific Integrated121 regression: exported Revit element IDs are IFC Tags.
  const expectedWalls=[352602,352603,352604,352605,352606,352607,352649,352650,352651,352652,352653,352654];
  const expectedFloors=[352610,352657],expectedDoors=[352615,352616,352662,352663];
  const tagged=new Map();
  for(const t of requiredBodies)for(const i of ids(t)){const e=line(i);if(e.Tag?.value)tagged.set(String(e.Tag.value),{id:i,type:t,entity:e})}
  const scopes=[['IFCWALLSTANDARDCASE',expectedWalls],['IFCSLAB',expectedFloors],['IFCDOOR',expectedDoors]];
  let matched=0;
  for(const [type,expected] of scopes)for(const tag of expected){
   const e=tagged.get(String(tag));assert(e&&e.type===type&&physical.has(e.id),'Missing expected IFC body '+tag);
   if(type==='IFCDOOR')assert(Math.abs(e.entity.OverallWidth.value-914.4)<.01&&Math.abs(e.entity.OverallHeight.value-2438.4)<.01);
   matched++;
  }
  const doorFills=new Set(ids('IFCRELFILLSELEMENT').map(i=>line(i).RelatedBuildingElement.value));
  for(const tag of expectedDoors)assert(doorFills.has(tagged.get(String(tag)).id),'Missing door fill '+tag);
  console.log(JSON.stringify({ok:true,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),schema:api.GetModelSchema(model),bytes:bytes.length,counts,units,storeys,checkedRefs,meshCount,parts,triangles,geometryTypes,uniqueGeometryBuffers:tested.size,axisOnlyParts:axes.length,unresolvedGeometryParts:unresolved,invalidGeometryParts:invalid,missingBodies,geometryPass,matchedIntegrated121Tags:matched,validatedDoorFills:expectedDoors.length},null,2));
  if(!geometryPass)process.exitCode=2;
 }finally{api.CloseModel(model);}
}
main().catch(e=>{console.error(e);process.exitCode=1});
