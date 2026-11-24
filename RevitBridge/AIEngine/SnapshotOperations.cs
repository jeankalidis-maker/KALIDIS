using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class SnapshotOperations {
 private sealed record LevelData(long id,string name,double elevationMm);
 private sealed record WallData(long id,string name,string kind);
 private sealed record FamilyData(long id,string name);
 private sealed record Data(LevelData[] levels,WallData[] wallTypes,FamilyData[] families);
 private static readonly RevisionCache<Document,Data> Cache=new(TimeSpan.FromSeconds(10));
 internal static object Read(UIApplication app,Document d){
  var timer=System.Diagnostics.Stopwatch.StartNew();var revision=Application.ModelRevision;
  var (data,hit)=Cache.Get(d,d.PathName,revision,()=>new Data(
   new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().Select(x=>new LevelData(x.Id.Value,x.Name,UnitUtils.ConvertFromInternalUnits(x.Elevation,UnitTypeId.Millimeters))).ToArray(),
   new FilteredElementCollector(d).OfClass(typeof(WallType)).Cast<WallType>().Select(x=>new WallData(x.Id.Value,x.Name,x.Kind.ToString())).ToArray(),
   new FilteredElementCollector(d).OfClass(typeof(Family)).Cast<Family>().Select(x=>new FamilyData(x.Id.Value,x.Name)).ToArray()));
  return new {ok=true,title=d.Title,path=d.PathName,version=app.Application.VersionNumber,workshared=d.IsWorkshared,family=d.IsFamilyDocument,
   data.levels,data.wallTypes,data.families,session=Application.SessionId,revision,cacheHit=hit,elapsedMs=timer.ElapsedMilliseconds};
 }
}
