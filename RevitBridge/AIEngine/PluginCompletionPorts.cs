// Adapted from KenLP/RevitMCPServer MIT, pinned fdf6efa23e57fe59effb8191666d04b4f73142a0.
// Explicit local test scope enforced by Application. No external command server.
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
namespace Kalidis.AIEngine;
internal static class PluginCompletionPorts {
 internal static readonly string[] Actions={"select_elements","isolate_elements_in_view","array_linear","ungroup_elements","create_filled_region","override_element_graphics","list_category_parameters","reversible_text_parameters","clear_colors","zoom_to_elements"};
 static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
 static double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
 static XYZ Vector(JsonElement x)=>new(Mm(x.GetProperty("x").GetDouble()),Mm(x.GetProperty("y").GetDouble()),Mm(x.GetProperty("z").GetDouble()));
 static Element E(Document d,ElementId id)=>d.GetElement(id)??throw new ArgumentException("Missing element "+id.Value);
 static View V(Document d,JsonElement r)=>E(d,Id(r,"viewId")) as View??throw new ArgumentException("View required");
 static ElementId[] Ids(Document d,JsonElement r,string k,int max=50,bool empty=false){
  var ids=r.GetProperty(k).EnumerateArray().Select(x=>new ElementId(x.GetInt64())).ToArray();
  if(ids.Length>max||(!empty&&ids.Length==0)||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Invalid distinct ID scope");
  foreach(var id in ids)if(E(d,id) is ElementType)throw new ArgumentException("Instances only");
  return ids;
 }
 static bool Flag(JsonElement r,string k)=>r.TryGetProperty(k,out var p)&&p.ValueKind==JsonValueKind.True;
 internal static object Run(UIApplication app,Document d,JsonElement r,string action){
  bool commit=Flag(r,"commit");
  if(action=="list_category_parameters"){
   var ids=Ids(d,r,"ids",30);
   var cat=Id(r,"categoryId");
   var els=ids.Select(id=>E(d,id)).ToArray();
   if(els.Any(e=>e.Category?.Id!=cat))throw new ArgumentException("Category mismatch");
   var rows=els.SelectMany(e=>e.Parameters.Cast<Parameter>().Select(p=>new{id=p.Id.Value,name=p.Definition.Name,storage=p.StorageType.ToString(),spec=p.Definition.GetDataType().TypeId,readOnly=p.IsReadOnly,elementId=e.Id.Value})).GroupBy(p=>new{p.id,p.name,p.storage,p.spec}).Select(g=>new{g.Key.id,g.Key.name,g.Key.storage,g.Key.spec,coverage=g.Select(p=>p.elementId).Distinct().Count(),writableCount=g.Count(p=>!p.readOnly)}).OrderBy(p=>p.id).ToArray();
   return new{ok=true,categoryId=cat.Value,sampleCount=els.Length,parameters=rows,note="Explicit sample of instance parameters; not full category or type schema."};
  }
  if(action=="select_elements"){
   var ids=Ids(d,r,"ids",200,true);
   var old=Ids(d,r,"expectedIds",200,true);
   var actual=app.ActiveUIDocument.Selection.GetElementIds().ToHashSet();
   if(!actual.SetEquals(old)||d.ActiveView.Id!=Id(r,"viewId"))throw new InvalidOperationException("Selection or view changed");
   if(commit){app.ActiveUIDocument.Selection.SetElementIds(ids);if(!app.ActiveUIDocument.Selection.GetElementIds().ToHashSet().SetEquals(ids))throw new InvalidOperationException("Selection readback mismatch");}
   return new{ok=true,committed=commit,viewId=d.ActiveView.Id.Value,selectedIds=(commit?ids:old).Select(i=>i.Value),requestedIds=ids.Select(i=>i.Value)};
  }
  if(action=="zoom_to_elements"){
   var view=V(d,r);if(view.Id!=d.ActiveView.Id||view.IsTemplate||view.ViewType!=ViewType.FloorPlan)throw new ArgumentException("Active floor plan required");
   var ids=Ids(d,r,"ids");
   var visible=new FilteredElementCollector(d,view.Id).WhereElementIsNotElementType().ToElementIds().ToHashSet();
   if(ids.Any(id=>!visible.Contains(id)))throw new ArgumentException("Elements must belong to current view collector");
   var boxes=ids.Select(id=>E(d,id).get_BoundingBox(view)??throw new InvalidOperationException("Missing view bounds")).ToArray();
   var ui=app.ActiveUIDocument.GetOpenUIViews().SingleOrDefault(v=>v.ViewId==view.Id)??throw new InvalidOperationException("Active UIView unavailable");
   var a=new XYZ(boxes.Min(b=>b.Min.X)-Mm(200),boxes.Min(b=>b.Min.Y)-Mm(200),view.GenLevel!.Elevation);
   var b=new XYZ(boxes.Max(b=>b.Max.X)+Mm(200),boxes.Max(b=>b.Max.Y)+Mm(200),view.GenLevel!.Elevation);
   if(commit)ui.ZoomAndCenterRectangle(a,b);
   var corners=ui.GetZoomCorners();
   return new{ok=true,committed=commit,viewId=view.Id.Value,ids=ids.Select(i=>i.Value),requestedMm=new[]{new[]{a.X*304.8,a.Y*304.8},new[]{b.X*304.8,b.Y*304.8}},actualCornersMm=corners.Select(p=>new[]{p.X*304.8,p.Y*304.8,p.Z*304.8}).ToArray(),note="Active plan only; UI zoom, not model edit."};
  }
  using var group=action=="ungroup_elements"?new TransactionGroup(d,"Kalidis - guarded ungroup"):null;
  if(group!=null&&group.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Group transaction failed");
  using var tx=new Transaction(d,"Kalidis - "+action);
  if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction failed");
  var policy=new BatchFailurePolicy();var options=tx.GetFailureHandlingOptions();options.SetFailuresPreprocessor(policy);options.SetClearAfterRollback(true);tx.SetFailureHandlingOptions(options);
  object result;
  try{
   switch(action){
    case "isolate_elements_in_view":{
     var view=V(d,r);if(view.Id!=d.ActiveView.Id||view.IsTemplate||!view.CanUseTemporaryVisibilityModes())throw new ArgumentException("Active isolatable view required");
     bool reset=Flag(r,"reset"),before=view.IsTemporaryHideIsolateActive();
     if(before!=reset)throw new InvalidOperationException("Existing temporary visibility state must be preserved; reset only active mode");
     var ids=Ids(d,r,"ids",50,reset);
     if(reset){if(ids.Length!=0)throw new ArgumentException("Reset requires empty IDs");view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);}
     else{foreach(var id in ids)if(E(d,id) is RevitLinkInstance)throw new ArgumentException("Host elements only");view.IsolateElementsTemporary(ids);}
     d.Regenerate();if(view.IsTemporaryHideIsolateActive()==reset)throw new InvalidOperationException("Temporary mode readback mismatch");
     result=new{viewId=view.Id.Value,before,activeInside=!reset,ids=ids.Select(i=>i.Value).ToArray()};break;
    }
    case "array_linear":{
     var e=E(d,Id(r,"id"));if(e.UniqueId!=r.GetProperty("uniqueId").GetString()||e.Pinned||e.GroupId!=ElementId.InvalidElementId||e is not FamilyInstance f||f.Host!=null||f.Location is not LocationPoint lp||f.MEPModel!=null)throw new ArgumentException("Unhosted ungrouped point family required");
     var count=r.GetProperty("count").GetInt32();var offset=Vector(r.GetProperty("spacing"));if(count<1||count>30||offset.GetLength()<Mm(10)||offset.GetLength()*count>Mm(30000))throw new ArgumentException("Invalid array extent");
     var origin=lp.Point;var copies=new List<long>();
     for(int i=1;i<=count;i++){var ids=ElementTransformUtils.CopyElement(d,e.Id,offset*i);if(ids.Count!=1)throw new InvalidOperationException("Unexpected dependent copies");d.Regenerate();var copy=E(d,ids.Single());if(copy.GetTypeId()!=e.GetTypeId()||copy.Location is not LocationPoint p||p.Point.DistanceTo(origin+offset*i)>Mm(.1))throw new InvalidOperationException("Copy geometry mismatch");copies.Add(copy.Id.Value);}
     result=new{sourceId=e.Id.Value,createdIds=copies,count};break;
    }
    case "ungroup_elements":{
     var g=E(d,Id(r,"groupId")) as Group??throw new ArgumentException("Group required");
     var before=Ids(d,r,"expectedMemberIds",50).ToHashSet();
     if(g.UniqueId!=r.GetProperty("uniqueId").GetString()||g.Pinned||!g.GetMemberIds().ToHashSet().SetEquals(before)||g.GetAvailableAttachedDetailGroupTypeIds().Count!=0)throw new InvalidOperationException("Group identity or members mismatch");
     var gid=g.Id;var members=g.UngroupMembers().ToHashSet();d.Regenerate();
     if(!members.SetEquals(before)||members.Any(id=>E(d,id).GroupId!=ElementId.InvalidElementId))throw new InvalidOperationException("Ungroup readback mismatch");
     result=new{groupId=gid.Value,memberIds=members.Select(i=>i.Value).ToArray()};break;
    }
    case "create_filled_region":{
     var v=V(d,r);if(v.IsTemplate||v.ViewType!=ViewType.FloorPlan)throw new ArgumentException("Floor plan required");
     var type=E(d,Id(r,"typeId")) as FilledRegionType??throw new ArgumentException("Filled region type required");
     var points=r.GetProperty("points").EnumerateArray().Select(p=>new XYZ(Mm(p.GetProperty("x").GetDouble()),Mm(p.GetProperty("y").GetDouble()),v.GenLevel?.Elevation??throw new ArgumentException("Plan level required"))).ToArray();
     if(points.Length<3||points.Length>100)throw new ArgumentException("3-100 points");
     var loop=new CurveLoop();for(int i=0;i<points.Length;i++)loop.Append(Line.CreateBound(points[i],points[(i+1)%points.Length]));
     var region=FilledRegion.Create(d,type.Id,v.Id,new List<CurveLoop>{loop});d.Regenerate();if(region.OwnerViewId!=v.Id||region.GetBoundaries().Count!=1)throw new InvalidOperationException("Region mismatch");
     result=new{id=region.Id.Value,viewId=v.Id.Value,typeId=type.Id.Value,pointCount=points.Length};break;
    }
    case "clear_colors":
    case "override_element_graphics":{
     var v=V(d,r);if(v.IsTemplate||!v.AreGraphicsOverridesAllowed())throw new ArgumentException("View cannot override graphics");
     var ids=Ids(d,r,"ids");bool clear=action=="clear_colors";var color=clear?default:r.GetProperty("color");var col=clear?Color.InvalidColorValue:new Color(color.GetProperty("r").GetByte(),color.GetProperty("g").GetByte(),color.GetProperty("b").GetByte());
     var expected=r.GetProperty("expectedColor");var trans=clear?0:r.GetProperty("transparency").GetInt32();if(trans<0||trans>100)throw new ArgumentException("Transparency 0-100");
     int oldTrans=r.GetProperty("expectedTransparency").GetInt32();
     foreach(var id in ids){if(E(d,id) is RevitLinkInstance)throw new ArgumentException("Host only");var old=v.GetElementOverrides(id);var c=old.ProjectionLineColor;if(old.Transparency!=oldTrans||(expected.ValueKind==JsonValueKind.Null?c.IsValid:!c.IsValid||c.Red!=expected.GetProperty("r").GetByte()||c.Green!=expected.GetProperty("g").GetByte()||c.Blue!=expected.GetProperty("b").GetByte()))throw new InvalidOperationException("Existing graphics changed");}
     foreach(var id in ids){var ogs=v.GetElementOverrides(id);ogs.SetProjectionLineColor(col);ogs.SetSurfaceTransparency(trans);v.SetElementOverrides(id,ogs);}
     d.Regenerate();foreach(var id in ids){var o=v.GetElementOverrides(id);if(o.Transparency!=trans||(clear?o.ProjectionLineColor.IsValid:!o.ProjectionLineColor.IsValid||o.ProjectionLineColor.Red!=col.Red||o.ProjectionLineColor.Green!=col.Green||o.ProjectionLineColor.Blue!=col.Blue))throw new InvalidOperationException("Graphics readback mismatch");}
     result=new{viewId=v.Id.Value,ids=ids.Select(i=>i.Value),transparency=trans,note="Only projection line color and surface transparency changed; existing other overrides retained."};break;
    }
    case "reversible_text_parameters":{
     var direction=r.GetProperty("direction").GetString();if(direction is not ("forward" or "undo" or "redo"))throw new ArgumentException("Direction");
     var changes=r.GetProperty("changes").EnumerateArray().ToArray();if(changes.Length<1||changes.Length>30)throw new ArgumentException("1-30 edits");
     var seen=new HashSet<(long,long)>();var pending=new List<(Element e,Parameter p,string from,string to)>();
     foreach(var c in changes){
      var e=E(d,Id(c,"id"));var pid=c.GetProperty("parameterId").GetInt64();
      if(e is ElementType||e.UniqueId!=c.GetProperty("uniqueId").GetString()||!seen.Add((e.Id.Value,pid))||pid is not (-1010106 or -1001203))throw new ArgumentException("Invalid instance or parameter scope");
      var p=e.Parameters.Cast<Parameter>().SingleOrDefault(p=>p.Id.Value==pid);
      string from=c.GetProperty(direction=="undo"?"after":"before").GetString()??throw new ArgumentException("Non-null before required");
      string to=c.GetProperty(direction=="undo"?"before":"after").GetString()??throw new ArgumentException("Non-null after required");
      if(p==null||p.IsReadOnly||p.StorageType!=StorageType.String||p.AsString()!=from||from==to||from.Length>200||to.Length>200)throw new InvalidOperationException("Stale, null or unsupported parameter");
      pending.Add((e,p,from,to));
     }
     foreach(var item in pending)if(!item.p.Set(item.to))throw new InvalidOperationException("Parameter update refused");
     d.Regenerate();if(pending.Any(x=>x.p.AsString()!=x.to))throw new InvalidOperationException("Parameter readback mismatch");
     result=new{direction,changes=pending.Select(x=>new{id=x.e.Id.Value,uniqueId=x.e.UniqueId,parameterId=x.p.Id.Value,before=x.from,inside=x.p.AsString()}).ToArray(),note="Atomic explicit text edits only. Not global Undo."};break;
    }
    default:throw new ArgumentException("Unsupported plugin operation");
   }
   TransactionStatus status;
   if(group!=null){
    var inner=tx.Commit();
    if(inner!=TransactionStatus.Committed){group.RollBack();return new{ok=false,status=inner.ToString(),committed=false,failures=policy.Messages,error="Ungroup inner transaction failed"};}
    if(d.GetElement(Id(r,"groupId"))!=null||Ids(d,r,"expectedMemberIds",50).Any(id=>E(d,id).GroupId!=ElementId.InvalidElementId))throw new InvalidOperationException("Post-commit group readback mismatch");
    status=commit?group.Assimilate():group.RollBack();
   }else status=commit?tx.Commit():tx.RollBack();
   return new{ok=status==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack),status=status.ToString(),committed=status==TransactionStatus.Committed,failures=policy.Messages,result};
  }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();if(group!=null&&group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
 }
}