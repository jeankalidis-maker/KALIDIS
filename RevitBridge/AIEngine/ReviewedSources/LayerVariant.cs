using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.LayerVariant;
public sealed class LayerVariant : IReviewedOperation {
 public string Name=>"layer_variant";
 public object Execute(Document d,JsonElement a){
  var type=d.GetElement(new ElementId(a.GetProperty("typeId").GetInt64())) as HostObjAttributes??throw new ArgumentException("Host type required");
  var old=type.GetCompoundStructure()??throw new ArgumentException("Compound structure required");
  if(old.IsVerticallyCompound)throw new ArgumentException("Only simple layered types");
  var edits=a.GetProperty("layers").EnumerateArray().ToArray();
  if(edits.Length<1||edits.Length>20)throw new ArgumentException("1-20 layers");
  var name=a.GetProperty("name").GetString()??throw new ArgumentException("Name required");
  double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
  double ToMm(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.Millimeters);
  var prior=old.GetLayers().Select(l=>new{width=l.Width,material=l.MaterialId}).ToArray();
  var seen=new HashSet<int>();
  foreach(var e in edits){
   int i=e.GetProperty("index").GetInt32();if(i<0||i>=prior.Length||!seen.Add(i))throw new ArgumentException("Invalid layer index");
   if(e.TryGetProperty("widthMm",out var w)){
    double n=w.GetDouble();if(!double.IsFinite(n)||n<1||n>2000)throw new ArgumentException("Invalid width");
    old.SetLayerWidth(i,Mm(n));
   }
   if(e.TryGetProperty("materialId",out var m)){
    var id=new ElementId(m.GetInt64());if(d.GetElement(id) is not Material)throw new ArgumentException("Invalid material");old.SetMaterialId(i,id);
   }
  }
  var copy=(HostObjAttributes)type.Duplicate(name);copy.SetCompoundStructure(old);d.Regenerate();
  var after=copy.GetCompoundStructure()??throw new InvalidOperationException("Missing variant structure");
  var rows=after.GetLayers().ToArray();var unchanged=type.GetCompoundStructure()!.GetLayers().ToArray();
  if(rows.Length!=prior.Length||unchanged.Length!=prior.Length)throw new InvalidOperationException("Layer count changed");
  for(int i=0;i<rows.Length;i++){
   if(Math.Abs(unchanged[i].Width-prior[i].width)>Mm(0.01)||unchanged[i].MaterialId!=prior[i].material)throw new InvalidOperationException("Original modified");
   if(Math.Abs(rows[i].Width-old.GetLayerWidth(i))>Mm(0.01)||rows[i].MaterialId!=old.GetMaterialId(i))throw new InvalidOperationException("Layer readback mismatch");
  }
  return new{id=copy.Id.Value,sourceId=type.Id.Value,name=copy.Name,widthMm=ToMm(after.GetWidth()),layers=rows.Select((l,i)=>new{index=i,widthMm=ToMm(l.Width),materialId=l.MaterialId.Value}),originalUnchanged=true};
 }
}