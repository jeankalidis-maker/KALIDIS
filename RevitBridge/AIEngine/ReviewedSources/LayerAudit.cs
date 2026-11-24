using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.LayerAudit;
public sealed class LayerAudit : IReviewedOperation {
 public string Name=>"layer_audit";
 public object Execute(Document d,JsonElement a){
  var ids=a.GetProperty("ids").EnumerateArray().Select(x=>x.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>100)throw new ArgumentException("1-100 types or host elements");
  double Mm(double v)=>UnitUtils.ConvertFromInternalUnits(v,UnitTypeId.Millimeters);
  var rows=ids.Select(id=>{
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing "+id);
   var t=e as HostObjAttributes??d.GetElement(e.GetTypeId()) as HostObjAttributes??throw new ArgumentException("Host type required");
   var c=t.GetCompoundStructure()??throw new ArgumentException("Compound structure missing");
   var layers=c.GetLayers().Select((l,i)=>new{index=i,function=l.Function.ToString(),widthMm=Mm(l.Width),materialId=l.MaterialId.Value,material=(d.GetElement(l.MaterialId) as Material)?.Name}).ToArray();
   return new{id,typeId=t.Id.Value,type=t.Name,widthMm=Mm(c.GetWidth()),layerSumMm=layers.Sum(l=>l.widthMm),layers};
  }).ToArray();
  return new{elements=rows,note="Nominal compound structure layers; variable layers and actual installed thickness require separate review."};
 }
}