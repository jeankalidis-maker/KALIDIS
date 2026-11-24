using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
using System.Security.Cryptography;
using Kalidis.AIEngine;
namespace Kalidis.FamilyCatalogAudit;
public sealed class FamilyCatalogAudit:IReviewedOperation{
 public string Name=>"family_catalog_audit";
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
  var allowed=new Dictionary<string,(int size,string hash)>{
   {"Column_Sqaure.rfa",(360448,"0660610EFB4B6C8DD8717BE4C4C1B6F74F044643C7D427590E6C122A02720BF9")},
   {"I_Single-Flush.rfa",(307200,"66AE731F85416BA0544038D9E442AAF399FF99AE8E1F4388BE980B6636549D1F")},
   {"Mullion_Rectangle.rfa",(512000,"745E6C9BC4E39EBA1A109F50641D46DD78BFDE165684AF464B1E24E6B34338EF")},
   {"Column_Square_Tollerance.rfa",(372736,"7FC2C31851D25C6611418CBB50C3E6A0CFAECDF345F012ECDE7058FE596D512F")}};
  var name=a.GetProperty("fileName").GetString()??"";
  if(!allowed.TryGetValue(name,out var pinned))throw new ArgumentException("Unreviewed catalogue entry");
  string file=Path.Combine(root,name);
  if(!File.Exists(file)||File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(file).Length!=pinned.size||Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))!=pinned.hash)throw new ArgumentException("Pinned file mismatch");
  if(evt==null){handler=new Handler();evt=ExternalEvent.Create(handler);}
  handler!.Hash=pinned.hash;handler.Project=d.PathName;handler.FileName=file;handler.State="queued";handler.Result=null;
  if(evt.Raise()!=ExternalEventRequest.Accepted){handler.State="rejected";throw new InvalidOperationException("Event refused");}
  return new{status="queued",note="Inspect pinned RFA in separate document, never load into project; no transaction rollback for file open"};
 }
 private sealed class Handler:IExternalEventHandler{
  internal string State="idle",Project="",FileName="",Hash="";internal object? Result;
  public string GetName()=>"Kalidis pinned RFA geometry inspection";
  public void Execute(UIApplication app){
   State="running";Document? fd=null;
   try{
    var project=app.ActiveUIDocument?.Document??throw new InvalidOperationException("No active project");
    if(project.PathName!=Project||project.IsModifiable||project.IsFamilyDocument||project.IsWorkshared)throw new InvalidOperationException("Project changed");
    var bytes=File.ReadAllBytes(FileName);var hash=Convert.ToHexString(SHA256.HashData(bytes));
    if(hash!=Hash)throw new InvalidOperationException("File changed");
    var folder=Path.Combine(Path.GetDirectoryName(Project)!,"FamilyReview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var copy=Path.Combine(folder,"Review.rfa");using(var f=new FileStream(copy,FileMode.CreateNew)){f.Write(bytes);}
    fd=app.Application.OpenDocumentFile(copy);
    if(!fd.IsFamilyDocument)throw new InvalidOperationException("Not a family");
    var family=fd.OwnerFamily;var types=fd.FamilyManager.Types.Cast<FamilyType>().Select(t=>t.Name).ToArray();
    var forms=new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).Cast<GenericForm>().ToArray();
    var solids=new List<Solid>();void Collect(GeometryElement? g){if(g==null)return;foreach(var o in g){if(o is Solid s&&s.Volume>1e-9)solids.Add(s);else if(o is GeometryInstance gi)Collect(gi.GetInstanceGeometry());}}
    foreach(var f in forms.Where(f=>f.IsSolid))Collect(f.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}));
    var curves=new FilteredElementCollector(fd).OfClass(typeof(CurveElement)).Cast<CurveElement>().Count();
    var result=new{file=Path.GetFileName(FileName),curveElements=curves,family=family.Name,category=family.FamilyCategory?.Name,types,solidForms=forms.Count(f=>f.IsSolid),voidForms=forms.Count(f=>!f.IsSolid),solidCount=solids.Count,volumeM3=UnitUtils.ConvertFromInternalUnits(solids.Sum(s=>s.Volume),UnitTypeId.CubicMeters),sha256=hash,copy,loadedIntoProject=false,note="Geometry of current family type only, not security certification or all-type flex."};
    if(!fd.Close(false))throw new InvalidOperationException("Family close failed");fd=null;
    if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FileName)))!=hash)throw new InvalidOperationException("Source changed");
    Result=result;State="verified";
   }catch(Exception ex){State="failed";Result=new{error=ex.ToString()};}
   finally{if(fd!=null&&fd.IsValidObject&&!fd.IsModifiable)fd.Close(false);}
  }
 }
}