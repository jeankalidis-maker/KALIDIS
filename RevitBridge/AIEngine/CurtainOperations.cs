using System.Text.Json;
using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;
internal static class CurtainOperations {
 internal static object Run(Document d,JsonElement a){
  double Mm(double v)=>UnitUtils.ConvertToInternalUnits(v,UnitTypeId.Millimeters);
  double N(JsonElement e,string k,double lo,double hi){var v=e.GetProperty(k).GetDouble();if(!double.IsFinite(v)||v<lo||v>hi)throw new ArgumentException(k);return v;}
  T E<T>(string k) where T:Element=>d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as T??throw new ArgumentException(k);
  object Inspect(Wall w){
   var grid=w.CurtainGrid??throw new ArgumentException("Curtain wall grid required");
   var u=grid.GetUGridLineIds().Select(i=>i.Value).ToArray();
   var v=grid.GetVGridLineIds().Select(i=>i.Value).ToArray();
   var panels=grid.GetPanelIds().Select(i=>i.Value).ToArray();
   var mullions=grid.GetMullionIds().Select(i=>i.Value).ToArray();
   return new {id=w.Id.Value,uniqueId=w.UniqueId,typeId=w.GetTypeId().Value,u,v,panels,mullions,panelCount=panels.Length};
  }
  var mode=a.GetProperty("mode").GetString();
  if(mode=="audit")return new{ok=true,result=Inspect(E<Wall>("id")),note="Native grid inventory; not panel performance certification."};
  if(mode!="create")throw new ArgumentException("Unknown mode");
  var type=E<WallType>("typeId");if(type.Kind!=WallKind.Curtain)throw new ArgumentException("Curtain wall type required");
  var level=E<Level>("levelId");
  XYZ P(JsonElement p)=>new(Mm(N(p,"x",-1000000,1000000)),Mm(N(p,"y",-1000000,1000000)),level.Elevation);
  var start=P(a.GetProperty("start"));var end=P(a.GetProperty("end"));
  var len=UnitUtils.ConvertFromInternalUnits(start.DistanceTo(end),UnitTypeId.Millimeters);
  if(len<1000||len>30000)throw new ArgumentException("Curtain length 1000-30000 mm");
  var height=N(a,"heightMm",1000,12000);
  bool commit=a.TryGetProperty("commit",out var c)&&c.ValueKind==JsonValueKind.True;
  using var tx=new Transaction(d,"Kalidis - curtain wall");if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction not started");
  var policy=new BatchFailurePolicy();
  tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(policy).SetClearAfterRollback(true));
  try{
   var w=Wall.Create(d,Line.CreateBound(start,end),type.Id,level.Id,Mm(height),0,false,false);d.Regenerate();
   var grid=w.CurtainGrid??throw new InvalidOperationException("No curtain grid");
   var added=new List<long>();
   if(a.TryGetProperty("gridLines",out var lines)){
    if(lines.GetArrayLength()>30)throw new ArgumentException("Max 30 grid lines");
    foreach(var g in lines.EnumerateArray()){
     var along=N(g,"alongMm",100,len-100);var z=N(g,"heightMm",100,height-100);
     var isU=g.GetProperty("isUGridLine").GetBoolean();
     var point=start+(end-start).Normalize()*Mm(along)+XYZ.BasisZ*Mm(z);
     var line=grid.AddGridLine(isU,point,false)??throw new InvalidOperationException("Grid line not created");
     added.Add(line.Id.Value);d.Regenerate();
    }
   }
   var preview=Inspect(w);var id=w.Id;
   if(grid.GetPanelIds().Count==0)throw new InvalidOperationException("No panels generated");
   var all=grid.GetUGridLineIds().Concat(grid.GetVGridLineIds()).Select(x=>x.Value).ToHashSet();
   if(added.Any(i=>!all.Contains(i)))throw new InvalidOperationException("Grid line verification failed");
   var state=commit?tx.Commit():tx.RollBack();
   bool ok=state==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack)&&policy.Messages.Count==0&&(d.GetElement(id)!=null)==commit;
   return new{ok,status=state.ToString(),committed=ok&&commit,preview,addedGridIds=added,failures=policy.Messages,note="Straight curtain wall and explicit grids only; mullion and panel edits not implemented."};
  }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw;}
 }
}