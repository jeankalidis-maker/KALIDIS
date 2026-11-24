using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
namespace Kalidis.AIEngine;
internal static class StairOperations {
 internal static readonly string[] Actions={"straight_stair"};
 private static double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
 private static double ToMm(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.Millimeters);
 private static double N(JsonElement r,string key){var n=r.GetProperty(key).GetDouble();if(!double.IsFinite(n)||Math.Abs(n)>1000000)throw new ArgumentException("Invalid "+key);return n;}
 private static XYZ Point(JsonElement r)=>new(Mm(N(r,"x")),Mm(N(r,"y")),0);
 private sealed class RejectFailures : IFailuresPreprocessor {
  internal List<string> Messages=new();
  public FailureProcessingResult PreprocessFailures(FailuresAccessor a){
   var messages=a.GetFailureMessages();foreach(var m in messages)Messages.Add(m.GetDescriptionText());
   return messages.Count==0?FailureProcessingResult.Continue:FailureProcessingResult.ProceedWithRollBack;
  }
 }
 internal static object Run(Document d,JsonElement r){
  var lower=d.GetElement(new ElementId(r.GetProperty("baseLevelId").GetInt64())) as Level??throw new ArgumentException("Invalid base level");
  var upper=d.GetElement(new ElementId(r.GetProperty("topLevelId").GetInt64())) as Level??throw new ArgumentException("Invalid top level");
  double rise=upper.ProjectElevation-lower.ProjectElevation;
  if(rise<=Mm(100)||rise>Mm(12000))throw new ArgumentException("Invalid level height");
  var a=Point(r.GetProperty("start"));var b=Point(r.GetProperty("end"));
  double width=N(r,"widthMm");if(width<300||width>5000||a.DistanceTo(b)<Mm(500))throw new ArgumentException("Invalid stair dimensions");
  bool commit=r.TryGetProperty("commit",out var flag)&&flag.ValueKind==JsonValueKind.True;
  using var scope=new StairsEditScope(d,"Kalidis - straight stair");
  var guard=new RejectFailures();
  ElementId stairId=scope.Start(lower.Id,upper.Id);
  try{
   int desired,actual;double tread,riser,actualWidth,runTop;
   using(var tx=new Transaction(d,"Kalidis - stair run")){
    if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Cannot start stair transaction");
    try{
     var opts=tx.GetFailureHandlingOptions();opts.SetFailuresPreprocessor(guard);opts.SetClearAfterRollback(true);tx.SetFailureHandlingOptions(opts);
     var stair=(Stairs)d.GetElement(stairId);
     if(r.TryGetProperty("typeId",out var tid)){var id=new ElementId(tid.GetInt64());if(d.GetElement(id) is not StairsType)throw new ArgumentException("Invalid stair type");stair.ChangeTypeId(id);d.Regenerate();}
     var run=StairsRun.CreateStraightRun(d,stairId,Line.CreateBound(a,b),StairsRunJustification.Center);
     run.ActualRunWidth=Mm(width);d.Regenerate();
     desired=stair.DesiredRisersNumber;actual=stair.ActualRisersNumber;tread=ToMm(stair.ActualTreadDepth);riser=ToMm(stair.ActualRiserHeight);actualWidth=ToMm(run.ActualRunWidth);runTop=run.TopElevation;
     if(desired!=actual||Math.Abs(runTop-rise)>Mm(2))throw new InvalidOperationException($"Run does not reach top level: {actual}/{desired} risers; adjust path length.");
     if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Stair transaction rolled back: "+string.Join("; ",guard.Messages));
    }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw;}
   }
   if(commit)scope.Commit(guard);else scope.Cancel();
   bool exists=d.GetElement(stairId)!=null;
   return new{ok=exists==commit,dryRun=!commit,committed=commit&&exists,verified=exists==commit,id=stairId.Value,desiredRisers=desired,actualRisers=actual,treadMm=tread,riserMm=riser,widthMm=actualWidth,riseMm=ToMm(rise),failures=guard.Messages,note="Geometry only; code compliance not certified."};
  }catch{if(scope.IsActive)scope.Cancel();throw;}
 }
}