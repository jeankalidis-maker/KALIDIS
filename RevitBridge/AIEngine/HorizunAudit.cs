// Adapted from Horizun Revit MCP, Copyright 2026 Horizun Group, Apache-2.0.
// Source b9032cd414810a2006e6153e138d6f43ac4da510:
// AuditModelCommand.LevelSpan.cs, AuditModelCommand.WarningDetails.cs,
// LevelSpanRules.cs and LevelSpanBounds.cs. Modified for Kalidis JSON,
// pagination and advisory-only reporting. See THIRD-PARTY-Horizun-*.
using Autodesk.Revit.DB;
using System.Text.Json;
namespace Kalidis.AIEngine;
internal static class HorizunAudit {
 internal static readonly string[] Actions={"audit_level_span","audit_warnings"};
 private static double Mm(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.Millimeters);
 private static int Int(JsonElement p,string key,int def,int lo,int hi)=>p.TryGetProperty(key,out var v)?Math.Clamp(v.GetInt32(),lo,hi):def;
 internal static object Read(Document d,JsonElement p,string action){
  int limit=Int(p,"limit",100,1,500),offset=Int(p,"offset",0,0,1000000);
  if(action=="audit_warnings"){
   var warnings=d.GetWarnings();
   var rows=warnings.Skip(offset).Take(limit).Select((w,i)=>new{index=offset+i,description=w.GetDescriptionText(),severity=w.GetSeverity().ToString(),failureId=w.GetFailureDefinitionId().Guid, elements=w.GetFailingElements().Select(id=>new{id=id.Value,uniqueId=d.GetElement(id)?.UniqueId}).ToArray()}).ToArray();
   return new{ok=true,total=warnings.Count,offset,returned=rows.Length,hasMore=offset+rows.Length<warnings.Count,warnings=rows};
  }
  if(action=="audit_level_span"){
   double tolerance=p.TryGetProperty("toleranceMm",out var t)?t.GetDouble():50;
   if(!double.IsFinite(tolerance)||tolerance<0||tolerance>1000)throw new ArgumentException("Invalid tolerance");
   var levels=new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().Select(l=>new{id=l.Id.Value,name=l.Name,z=Mm(l.ProjectElevation)}).OrderBy(l=>l.z).ToArray();
   var categories=new[]{BuiltInCategory.OST_Walls,BuiltInCategory.OST_Columns,BuiltInCategory.OST_StructuralColumns,BuiltInCategory.OST_PipeCurves,BuiltInCategory.OST_DuctCurves,BuiltInCategory.OST_CableTray,BuiltInCategory.OST_Conduit,BuiltInCategory.OST_Stairs,BuiltInCategory.OST_Ramps,BuiltInCategory.OST_GenericModel}.ToHashSet();
   int scanned=0,unreadable=0,found=0;
   var rows=new List<object>();
   foreach(var e in new FilteredElementCollector(d).WhereElementIsNotElementType()){
    if(e.Category==null||!categories.Contains(e.Category.BuiltInCategory))continue;
    scanned++;
    var b=e.get_BoundingBox(null);if(b==null){unreadable++;continue;}
    var zs=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select Mm(b.Transform.OfPoint(new XYZ(x,y,z)).Z)).ToArray();
    double lo=zs.Min(),hi=zs.Max();
    var crossed=levels.Where(l=>l.z>lo+tolerance&&l.z<hi-tolerance).ToArray();
    if(crossed.Length==0)continue;
    if(found++<offset||rows.Count>=limit)continue;
    rows.Add(new{id=e.Id.Value,uniqueId=e.UniqueId,category=e.Category.Name,bottomMm=lo,topMm=hi,crossed,review="Check intended continuity; do not split automatically."});
   }
   return new{ok=true,scanned,unreadable,total=found,offset,returned=rows.Count,hasMore=offset+rows.Count<found,toleranceMm=tolerance,findings=rows,note="Bounding-box screening, not a code violation. Multi-storey continuity can be intentional."};
  }
  throw new ArgumentException("Unknown audit action");
 }
}