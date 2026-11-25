// Local adaptations of KenLP MIT command patterns. Fixed explicit local test scope.
// All writes dispatched only after the Application sandbox path guard.
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
namespace Kalidis.AIEngine;
internal static class PluginGapPorts {
 internal static readonly string[] Actions={"create_spot_elevation","tag_all_in_view","set_level_elevation","import_parameters","close_document","link_file","wall_joint_finish"};
 static ElementId Id(JsonElement r,string n)=>new(r.GetProperty(n).GetInt64());
 static double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
 static double ToMm(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.Millimeters);
 static string S(JsonElement r,string n)=>r.GetProperty(n).GetString()??throw new ArgumentException(n);
 static Element E(Document d,ElementId id)=>d.GetElement(id)??throw new ArgumentException("Missing element "+id.Value);
 static T Get<T>(Document d,JsonElement r,string n) where T:Element=>E(d,Id(r,n)) as T??throw new ArgumentException("Expected "+typeof(T).Name);
 static bool Commit(JsonElement r)=>r.TryGetProperty("commit",out var p)&&p.ValueKind==JsonValueKind.True;
 static string Sibling(Document d,string name){
  if(Path.GetFileName(name)!=name||!name.EndsWith(".rvt",StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(name,@"^(LinkFixture|CloseFixture)-[a-f0-9]{32}\.rvt$"))throw new ArgumentException("Dedicated fixture filename required");
  var p=Path.Combine(Path.GetDirectoryName(d.PathName)!,name);
  if(!File.Exists(p)||File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)||string.Equals(p,d.PathName,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid sibling fixture");
  return p;
 }
 internal static object Run(UIApplication app,Document d,JsonElement r,string action){
  var commit=Commit(r);
  if(action=="close_document"){
   var p=Sibling(d,S(r,"fileName"));
   var target=app.Application.Documents.Cast<Document>().SingleOrDefault(x=>!x.IsLinked&&string.Equals(x.PathName,p,StringComparison.OrdinalIgnoreCase));
   if(target==null)return new{ok=true,wasOpen=false,closed=false,path=p};
   if(target.Equals(d)||target.IsModified||target.IsWorkshared||target.IsModelInCloud||target.IsFamilyDocument||target.IsReadOnly)throw new InvalidOperationException("Only inactive saved local fixture can be closed");
   bool closed=commit&&target.Close(false);
   if(commit&&(!closed||app.Application.Documents.Cast<Document>().Any(x=>!x.IsLinked&&string.Equals(x.PathName,p,StringComparison.OrdinalIgnoreCase))))throw new InvalidOperationException("Close not verified");
   return new{ok=true,wasOpen=true,closed,path=p,committed=commit};
  }
  using var group=new TransactionGroup(d,"Kalidis "+action);
  if(group.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction group unavailable");
  using var tx=new Transaction(d,action);
  if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction unavailable");
  var policy=new BatchFailurePolicy();var opts=tx.GetFailureHandlingOptions();opts.SetFailuresPreprocessor(policy);opts.SetClearAfterRollback(true);tx.SetFailureHandlingOptions(opts);
  object result;Action verify=()=>{};
  try{
   switch(action){
    case "wall_joint_finish":{(result,verify)=WallJointOperations.Run(d,r);break;}
    case "create_spot_elevation":{
     var floor=Get<Floor>(d,r,"elementId");var view=Get<ViewPlan>(d,r,"viewId");
     if(view.IsTemplate||view.ViewType!=ViewType.FloorPlan)throw new ArgumentException("Floor plan required");
     if(!new FilteredElementCollector(d,view.Id).OfClass(typeof(Floor)).ToElementIds().Contains(floor.Id))throw new ArgumentException("Floor absent from view collector");
     var point=r.GetProperty("point");double x=Mm(point.GetProperty("x").GetDouble()),y=Mm(point.GetProperty("y").GetDouble());
     var hits=new List<(Reference reference,XYZ point)>();
     foreach(var rf in HostObjectUtils.GetTopFaces(floor)){
      if(floor.GetGeometryObjectFromReference(rf) is not PlanarFace face||face.FaceNormal.Z<0.999999)continue;
      var p=new XYZ(x,y,face.Origin.Z);var hit=face.Project(p);
      if(hit!=null&&hit.Distance<Mm(.01)&&face.IsInside(hit.UVPoint))hits.Add((rf,hit.XYZPoint));
     }
     if(hits.Count!=1)throw new InvalidOperationException("Exactly one horizontal referenced top face required");
     var h=hits[0];var spot=d.Create.NewSpotElevation(view,h.reference,h.point,h.point,h.point,h.point,false)??throw new InvalidOperationException("Spot creation refused");
     var id=spot.Id;verify=()=>{if(d.GetElement(id) is not SpotDimension s||s.OwnerViewId!=view.Id)throw new InvalidOperationException("Spot readback mismatch");};
     result=new{id=id.Value,viewId=view.Id.Value,elementId=floor.Id.Value,elevationMm=ToMm(h.point.Z)};break;
    }
    case "tag_all_in_view":{
     var view=Get<ViewPlan>(d,r,"viewId");var type=Get<FamilySymbol>(d,r,"typeId");var cat=Id(r,"categoryId");
     if(view.IsTemplate||view.ViewType!=ViewType.FloorPlan||type.Category==null||!type.Category.IsTagCategory)throw new ArgumentException("Floor plan and tag type required");
     var scope=r.GetProperty("ids").EnumerateArray().Select(v=>new ElementId(v.GetInt64())).ToArray();
     if(scope.Length<1||scope.Length>50||scope.Distinct().Count()!=scope.Length)throw new ArgumentException("Explicit unique scope required");
     var visible=new FilteredElementCollector(d,view.Id).OfCategoryId(cat).WhereElementIsNotElementType().ToElementIds().ToHashSet();
     if(scope.Any(id=>!visible.Contains(id)||E(d,id) is RevitLinkInstance))throw new ArgumentException("Scope/category/view mismatch");
     var tagged=new FilteredElementCollector(d,view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().SelectMany(t=>t.GetTaggedLocalElementIds()).ToHashSet();
     var created=new List<(ElementId tag,ElementId host)>();
     foreach(var id in scope.Where(id=>!tagged.Contains(id))){
      var e=E(d,id);var b=e.get_BoundingBox(view)??throw new InvalidOperationException("No view bounds");
      var p=b.Transform.OfPoint((b.Min+b.Max)/2);var tag=IndependentTag.Create(d,type.Id,view.Id,new Reference(e),false,TagOrientation.Horizontal,p);
      created.Add((tag.Id,id));
     }
     verify=()=>{foreach(var row in created){if(d.GetElement(row.tag) is not IndependentTag t||t.OwnerViewId!=view.Id||t.GetTypeId()!=type.Id||!t.GetTaggedLocalElementIds().Contains(row.host))throw new InvalidOperationException("Tag readback mismatch");}};
     result=new{viewId=view.Id.Value,created=created.Select(x=>new{tagId=x.tag.Value,hostId=x.host.Value}).ToArray(),skipped=scope.Where(tagged.Contains).Select(x=>x.Value).ToArray(),note="Explicit visible scope; no tag collision layout check."};break;
    }
    case "set_level_elevation":{
     var level=Get<Level>(d,r,"id");double before=r.GetProperty("expectedMm").GetDouble(),after=r.GetProperty("elevationMm").GetDouble();
     if(!level.Name.StartsWith("KalidisLevel-",StringComparison.Ordinal)||level.Pinned||level.UniqueId!=S(r,"uniqueId")||Math.Abs(ToMm(level.Elevation)-before)>.001||Math.Abs(before-after)<.01)throw new InvalidOperationException("Stale or non-fixture level");
     if(level.GetDependentElements(null).Any(id=>id!=level.Id)||new FilteredElementCollector(d).WhereElementIsNotElementType().Any(e=>e.Id!=level.Id&&e.LevelId==level.Id))throw new InvalidOperationException("Level has dependent elements");
     if(new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().Any(l=>l.Id!=level.Id&&Math.Abs(ToMm(l.Elevation)-after)<1))throw new InvalidOperationException("Conflicting level elevation");
     level.Elevation=Mm(after);verify=()=>{if(Math.Abs(ToMm(level.Elevation)-after)>.001)throw new InvalidOperationException("Elevation readback mismatch");};
     result=new{id=level.Id.Value,beforeMm=before,afterMm=after,note="Only empty dedicated fixture levels."};break;
    }
    case "import_parameters":{
     var items=r.GetProperty("items").EnumerateArray().ToArray();
     if(items.Length<1||items.Length>30)throw new ArgumentException("1-30 rows required");
     var seen=new HashSet<(long,long)>();var verifyRows=new List<Action>();var receipts=new List<object>();
     foreach(var row in items){
      var e=E(d,Id(row,"elementId"));var pid=row.GetProperty("parameterId").GetInt64();
      if(e is ElementType||e.UniqueId!=S(row,"uniqueId")||!seen.Add((e.Id.Value,pid)))throw new ArgumentException("Identity or duplicate row");
      var p=e.Parameters.Cast<Parameter>().SingleOrDefault(v=>v.Id.Value==pid)??throw new ArgumentException("Parameter missing");
      if(p.IsReadOnly||!p.HasValue)throw new InvalidOperationException("Read-only or unset parameter");
      string kind=S(row,"kind");var old=row.GetProperty("before");var next=row.GetProperty("after");
      if(kind=="text"&&pid is -1010106 or -1001203&&p.StorageType==StorageType.String){
       var from=old.GetString()??throw new ArgumentException("Expected string");var to=next.GetString()??throw new ArgumentException("Expected string");
       if(from==to||from.Length>200||to.Length>200||p.AsString()!=from||!p.Set(to))throw new InvalidOperationException("Stale or invalid text");
       verifyRows.Add(()=>{if(p.AsString()!=to)throw new InvalidOperationException("Text readback mismatch");});
      }else if((kind=="lengthMm"||kind=="number")&&pid>0&&e is FamilyInstance f&&f.Category?.Id.Value==(long)BuiltInCategory.OST_Furniture&&f.Host==null&&f.GroupId==ElementId.InvalidElementId&&!f.Pinned&&(f.MEPModel?.ConnectorManager?.Connectors.Size??0)==0&&p.StorageType==StorageType.Double&&p.Definition.GetDataType()==(kind=="lengthMm"?SpecTypeId.Length:SpecTypeId.Number)){
       double from=old.GetDouble(),to=next.GetDouble();if(!double.IsFinite(from)||!double.IsFinite(to)||Math.Abs(from)>30000||Math.Abs(to)>30000||Math.Abs(from-to)<.000001)throw new ArgumentException("Invalid numeric value");
       double cv(double n)=>kind=="lengthMm"?Mm(n):n;
       if(Math.Abs(p.AsDouble()-cv(from))>1e-8||!p.Set(cv(to)))throw new InvalidOperationException("Stale or invalid numeric value");
       verifyRows.Add(()=>{if(Math.Abs(p.AsDouble()-cv(to))>1e-8)throw new InvalidOperationException("Numeric readback mismatch");});
      }else throw new ArgumentException("Unsupported parameter storage/spec/scope");
      receipts.Add(new{elementId=e.Id.Value,parameterId=pid,kind,before=old,after=next});
     }
     verify=()=>{foreach(var f in verifyRows)f();};
     result=new{applied=items.Length,rows=receipts,note="Atomic explicit CAS rows, limited storage and category; no spreadsheet I/O."};break;
    }
    case "link_file":{
     var p=Sibling(d,S(r,"fileName"));
     if(!Path.GetFileName(p).StartsWith("LinkFixture-",StringComparison.Ordinal))throw new ArgumentException("Link fixture required");
     string expectedHash=S(r,"sha256");using(var source=File.OpenRead(p)){if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Link fixture hash mismatch");}
     var transmission=TransmissionData.ReadTransmissionData(ModelPathUtils.ConvertUserVisiblePathToModelPath(p));
     if(transmission!=null&&transmission.GetAllExternalFileReferenceIds().Count!=0)throw new InvalidOperationException("Fixture has external dependencies");
     using(var info=BasicFileInfo.Extract(p)){if(info.IsWorkshared||!info.IsSavedInCurrentVersion)throw new InvalidOperationException("Local current-version RVT required");}
     if(app.Application.Documents.Cast<Document>().Any(x=>string.Equals(x.PathName,p,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Fixture already open or linked");
     foreach(var link in new FilteredElementCollector(d).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>()){
      var ext=link.GetExternalFileReference();if(ext!=null&&string.Equals(ModelPathUtils.ConvertModelPathToUserVisiblePath(ext.GetAbsolutePath()),p,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Duplicate link path");
     }
     var res=RevitLinkType.Create(d,ModelPathUtils.ConvertUserVisiblePathToModelPath(p),new RevitLinkOptions(false));
     var inst=RevitLinkInstance.Create(d,res.ElementId);var id=inst.Id;var typeId=res.ElementId;
     verify=()=>{if(d.GetElement(id) is not RevitLinkInstance l||l.GetTypeId()!=typeId||l.GetLinkDocument()==null)throw new InvalidOperationException("Link readback mismatch");};
     result=new{linkId=id.Value,typeId=typeId.Value,path=p,note="Fixture link only; loading can have external cache effects."};break;
    }
    default:throw new ArgumentException("Unknown operation");
   }
   d.Regenerate();verify();
   var inner=tx.Commit();if(inner!=TransactionStatus.Committed){group.RollBack();return new{ok=false,status=inner.ToString(),committed=false,failures=policy.Messages};}
   verify();var status=commit?group.Assimilate():group.RollBack();
   return new{ok=status==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack),status=status.ToString(),committed=status==TransactionStatus.Committed,result,failures=policy.Messages};
  }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
 }
}