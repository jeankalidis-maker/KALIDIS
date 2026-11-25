// Keep advertised and callable family features aligned with the actual DLL.
const forms=a=>[...(a.shapes??[]),...(a.blends??[]),...(a.sweeps??[])];
export function requiresMultiAxis(a){
 return a.expectedCategory!==undefined||a.subcategories!==undefined||forms(a).some(f=>f.axis!==undefined||f.centerMm!==undefined||f.subcategory!==undefined)||(a.referencePlanes??[]).some(p=>p.axis==='z')||(a.parameters??[]).some(p=>!['length','number','text'].includes(p.kind));
}
export function checkFamilyCapabilities(a,c){
 if(a.formCuts!==undefined&&c.groupedFamilyCuts!==true)throw Error('Grouped form cuts require a newer loaded Revit engine');
 if(requiresMultiAxis(a)&&c.familyMultiAxis!==true)throw Error('Multi-axis and subcategory family specifications require a newer loaded engine');
}
export function familySchemaForEngine(tool,c){
 const t=structuredClone(tool),p=t.inputSchema.properties;
 if(c.groupedFamilyCuts!==true)delete p.formCuts;
 if(c.familyMultiAxis!==true){
  delete p.subcategories;delete p.expectedCategory;
  for(const key of ['shapes','blends','sweeps']){const q=p[key].items.properties;delete q.axis;delete q.centerMm;delete q.subcategory;}
  p.referencePlanes.items.properties.axis.enum=['x','y'];
  p.parameters.items.properties.kind.enum=['length','number','text'];
  p.parameters.items.properties.value.anyOf=[{type:'number'},{type:'string'}];
 }
 return t;
}