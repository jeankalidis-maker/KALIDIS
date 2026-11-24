using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
using System.Security.Cryptography;
using Kalidis.AIEngine;
namespace Kalidis.FamilyQuarantineAudit;
public sealed class FamilyQuarantineAudit:IReviewedOperation{
 public string Name=>"family_quarantine_audit";
 private static ExternalEvent? evt;private static Handler? handler;
 public object Execute(Document d,JsonElement a){
  var mode=a.GetProperty("mode").GetString();
  if(mode=="status")return new{status=handler?.State??"idle",result=handler?.Result};
  if(mode!="start"||!a.TryGetProperty("inspectPinnedFile",out var permit)||permit.ValueKind!=JsonValueKind.True)throw new ArgumentException("Explicit inspection required; external event");
  if(handler?.State is "queued" or "running")throw new InvalidOperationException("Already running");
  string folder=a.GetProperty("folder").GetString()??"";
  if(Path.GetFileName(folder)!=folder||!folder.StartsWith("OnlineQuarantine-",StringComparison.Ordinal)||folder.Length!=49)throw new ArgumentException("Quarantine folder");
  string root=Path.Combine(Path.GetDirectoryName(d.PathName)!,folder);
  if(!Directory.Exists(root)||File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))throw new ArgumentException("Invalid directory");
  // Explicitly pinned MIT sample only, never arbitrary downloaded documents.
  string file=Path.Combine(root,"Column_Sqaure.rfa");
  if(!File.Exists(file)||File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(file).Length!=360448||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))!="0660610EFB4B6C8DD8717BE4C4C1B6F74F044643C7D427590E6C122A02720BF9")throw new ArgumentException("Pinned file mismatch");
  if(evt==null){handler=new Handler();evt=ExternalEvent.Create(handler);}
  handler!.Project=d.PathName;handler.FileName=file;handler.State="queued";handler.Result=null;
  if(evt.Raise()!=ExternalEventRequest.Accepted){handler.State="rejected";throw new InvalidOperationException("Event refused");}
  return new{status="queued",note="Inspect pinned RFA in separate document, never load into project; no transaction rollback for file open"};
 }
 private sealed class Handler:IExternalEventHandler{
  internal string State="idle",Project="",FileName="";internal object? Result;
  public string GetName()=>"Kalidis pinned RFA geometry inspection";
  public void Execute(UIApplication app){
   State="running";Document? fd=null;
   try{
    var project=app.ActiveUIDocument?.Document??throw new InvalidOperationException("No active project");
    if(project.PathName!=Project||project.IsModifiable||project.IsFamilyDocument||project.IsWorkshared)throw new InvalidOperationException("Project changed");
    var bytes=File.ReadAllBytes(FileName);var hash=Convert.ToHexString(SHA256.HashData(bytes));
    if(hash!="0660610EFB4B6C8DD8717BE4C4C1B6F74F044643C7D427590E6C122A02720BF9")throw new InvalidOperationException("File changed");
    var folder=Path.Combine(Path.GetDirectoryName(Project)!,"FamilyReview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var copy=Path.Combine(folder,"Review.rfa");using(var f=new FileStream(copy,FileMode.CreateNew)){f.Write(bytes);}
    fd=app.Application.OpenDocumentFile(copy);
    if(!fd.IsFamilyDocument)throw new InvalidOperationException("Not a family");
    var family=fd.OwnerFamily;var types=fd.FamilyManager.Types.Cast<FamilyType>().Select(t=>t.Name).ToArray();
    var forms=new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).Cast<GenericForm>().ToArray();
    var solids=new List<Solid>();void Collect(GeometryElement? g){if(g==null)return;foreach(var o in g){if(o is Solid s&&s.Volume>1e-9)solids.Add(s);else if(o is GeometryInstance gi)Collect(gi.GetInstanceGeometry());}}
    foreach(var f in forms.Where(f=>f.IsSolid))Collect(f.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}));
    var result=new{family=family.Name,category=family.FamilyCategory?.Name,types,solidForms=forms.Count(f=>f.IsSolid),voidForms=forms.Count(f=>!f.IsSolid),solidCount=solids.Count,volumeM3=UnitUtils.ConvertFromInternalUnits(solids.Sum(s=>s.Volume),UnitTypeId.CubicMeters),sha256=hash,copy,loadedIntoProject=false,note="Geometry of current family type only, not security certification or all-type flex."};
    if(!fd.Close(false))throw new InvalidOperationException("Family close failed");fd=null;
    if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FileName)))!=hash)throw new InvalidOperationException("Source changed");
    Result=result;State="verified";
   }catch(Exception ex){State="failed";Result=new{error=ex.ToString()};}
   finally{if(fd!=null&&fd.IsValidObject&&!fd.IsModifiable)fd.Close(false);}
  }
 }
}