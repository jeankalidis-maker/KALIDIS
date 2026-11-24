using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class InspectionOperations {
 internal static readonly string[] Actions={"views","connectors","schedule_fields","clash_candidates","selection","model_health","clash_confirm","schedule_data","routing_preferences"};
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
   case "model_health":{
    var warnings=d.GetWarnings().Take(500).Select(w=>new{description=w.GetDescriptionText(),ids=w.GetFailingElements().Select(i=>i.Value).ToArray()}).ToArray();
    var rooms=new FilteredElementCollector(d).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Autodesk.Revit.DB.Architecture.Room>().Select(room=>new{id=room.Id.Value,name=room.Name,number=room.Number,placed=room.Location!=null,areaM2=UnitUtils.ConvertFromInternalUnits(room.Area,UnitTypeId.SquareMeters)}).ToArray();
    return new{ok=true,warningCount=d.GetWarnings().Count,warnings,rooms,unplaced=rooms.Count(x=>!x.placed),zeroArea=rooms.Count(x=>x.areaM2==0)};
   }
   case "clash_confirm":{
    var pairs=r.GetProperty("pairs").EnumerateArray().ToArray();
    if(pairs.Length<1||pairs.Length>100)throw new ArgumentException("1-100 pairs required");
    var results=new List<object>();
    foreach(var pair in pairs){
     var a=Id(pair,"firstId");var b=Id(pair,"secondId");
     if(a==b||d.GetElement(a)==null||d.GetElement(b)==null){results.Add(new{firstId=a.Value,secondId=b.Value,status="invalid"});continue;}
     try{
      var filter=new ElementIntersectsElementFilter(d.GetElement(a));
      bool intersects=filter.PassesFilter(d,b);
      results.Add(new{firstId=a.Value,secondId=b.Value,status=intersects?"intersection_reported":"no_intersection_reported"});
     }catch(Autodesk.Revit.Exceptions.ArgumentException){results.Add(new{firstId=a.Value,secondId=b.Value,status="unsupported_geometry"});}
    }
    return new{ok=true,method="Revit ElementIntersectsElementFilter; unsupported or joined geometry can be missed",results};
   }
   case "schedule_data":{
    var schedule=d.GetElement(Id(r,"viewId")) as ViewSchedule??throw new ArgumentException("Not a schedule");
    var body=schedule.GetTableData().GetSectionData(SectionType.Body);
    int limit=r.TryGetProperty("limit",out var lim)?Math.Clamp(lim.GetInt32(),1,500):100;
    var rows=new List<string[]>();
    for(int i=body.FirstRowNumber;i<=body.LastRowNumber&&rows.Count<limit;i++){
     var cells=new List<string>();
     for(int j=body.FirstColumnNumber;j<=body.LastColumnNumber&&cells.Count<100;j++)cells.Add(schedule.GetCellText(SectionType.Body,i,j));
     rows.Add(cells.ToArray());
    }
    return new{ok=true,totalRows=body.NumberOfRows,totalColumns=body.NumberOfColumns,rows};
   }
   case "routing_preferences":{
    var type=d.GetElement(Id(r,"typeId")) as MEPCurveType??throw new ArgumentException("Expected MEP curve type");
    var manager=type.RoutingPreferenceManager;
    var groups=new[]{RoutingPreferenceRuleGroupType.Segments,RoutingPreferenceRuleGroupType.Elbows,RoutingPreferenceRuleGroupType.Junctions,RoutingPreferenceRuleGroupType.Transitions,RoutingPreferenceRuleGroupType.Unions};
    return new{ok=true,typeId=type.Id.Value,name=type.Name,groups=groups.Select(g=>new{group=g.ToString(),rules=Enumerable.Range(0,manager.GetNumberOfRules(g)).Select(i=>{
     var rule=manager.GetRule(g,i);var part=d.GetElement(rule.MEPPartId);
     return new{index=i,partId=rule.MEPPartId.Value,partName=part?.Name,description=rule.Description,criteria=rule.NumberOfCriteria};
    }).ToArray()}).ToArray()};
   }
   default:throw new ArgumentException("Unknown inspection");
  }
 }
}