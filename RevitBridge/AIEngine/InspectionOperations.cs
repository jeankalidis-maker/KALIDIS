using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class InspectionOperations {
 internal static readonly string[] Actions={"views","connectors","schedule_fields","clash_candidates","selection"};
 private static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
 private static double Mm(double v)=>UnitUtils.ConvertFromInternalUnits(v,UnitTypeId.Millimeters);
 private static double[] Point(XYZ p)=>new[]{Mm(p.X),Mm(p.Y),Mm(p.Z)};
 internal static object Read(UIApplication app,Document d,JsonElement r,string action){
  switch(action){
   case "views": return new{ok=true,activeViewId=d.ActiveView.Id.Value,views=new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>().Where(v=>!v.IsTemplate).Take(2000).Select(v=>new{id=v.Id.Value,name=v.Name,kind=v.ViewType.ToString(),scale=v.Scale}).ToArray()};
   case "selection":return new{ok=true,ids=app.ActiveUIDocument.Selection.GetElementIds().Select(i=>i.Value).ToArray()};
   case "connectors":{
    var e=d.GetElement(Id(r,"elementId"))??throw new ArgumentException("Element not found");
    var mgr=e is MEPCurve c?c.ConnectorManager:(e as FamilyInstance)?.MEPModel?.ConnectorManager;
    if(mgr==null)throw new ArgumentException("No connector manager");
    return new{ok=true,connectors=mgr.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End).Select(c=>new{id=c.Id,originMm=Point(c.Origin),domain=c.Domain.ToString(),shape=c.Shape.ToString(),connected=c.IsConnected,linkedOwners=c.AllRefs.Cast<Connector>().Where(k=>k.Owner.Id!=e.Id).Select(k=>k.Owner.Id.Value).Distinct().ToArray()}).ToArray()};
   }
   case "schedule_fields":{
    var s=d.GetElement(Id(r,"viewId")) as ViewSchedule??throw new ArgumentException("Not a schedule");
    var def=s.Definition;
    return new{ok=true,available=def.GetSchedulableFields().Select(f=>new{parameterId=f.ParameterId.Value,name=f.GetName(d)}).ToArray(),fields=def.GetFieldOrder().Select(i=>def.GetField(i)).Select(f=>new{parameterId=f.ParameterId.Value,name=f.GetName()}).ToArray()};
   }
   case "clash_candidates":{
    var ids=r.GetProperty("ids").EnumerateArray().Select(x=>new ElementId(x.GetInt64())).Distinct().ToArray();
    if(ids.Length<2||ids.Length>500)throw new ArgumentException("2-500 IDs required");
    double mm=r.TryGetProperty("toleranceMm",out var v)?v.GetDouble():0;
    if(!double.IsFinite(mm)||mm<0||mm>1000)throw new ArgumentException("Invalid tolerance");
    double t=UnitUtils.ConvertToInternalUnits(mm,UnitTypeId.Millimeters);
    var boxes=ids.Select(i=>(id:i,box:d.GetElement(i)?.get_BoundingBox(null))).Where(x=>x.box!=null).ToArray();
    var pairs=new List<object>();bool truncated=false;
    for(int i=0;i<boxes.Length;i++)for(int j=i+1;j<boxes.Length;j++){
      var a=boxes[i].box!;var b=boxes[j].box!;
      if(Math.Min(a.Max.X,b.Max.X)+t>Math.Max(a.Min.X,b.Min.X)&&Math.Min(a.Max.Y,b.Max.Y)+t>Math.Max(a.Min.Y,b.Min.Y)&&Math.Min(a.Max.Z,b.Max.Z)+t>Math.Max(a.Min.Z,b.Min.Z)){
        if(pairs.Count>=2000){truncated=true;continue;}pairs.Add(new{firstId=boxes[i].id.Value,secondId=boxes[j].id.Value});
      }
    }
    return new{ok=true,method="axis-aligned bounding boxes only; not confirmed physical intersections",examined=boxes.Length,omitted=ids.Length-boxes.Length,truncated,pairs};
   }
   default:throw new ArgumentException("Unknown inspection");
  }
 }
}