using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class ProjectOperations {
 internal static readonly string[] Actions={"new_test_project","open_test_project","elements","element_info","edit_elements","export_project"};
 private static string S(JsonElement r,string k)=>r.GetProperty(k).GetString()??throw new ArgumentException(k);
 private static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
 private static double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
 private static XYZ Vector(JsonElement r)=>new(Mm(r.GetProperty("x").GetDouble()),Mm(r.GetProperty("y").GetDouble()),Mm(r.GetProperty("z").GetDouble()));
 private static string FileIn(string root,string name,string ext){
  if(name!=Path.GetFileName(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!name.EndsWith(ext,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid file name");
  if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Sandbox cannot be a junction");
  return Path.Combine(root,name);
 }
 internal static object NewOrOpen(UIApplication app,JsonElement r,string action,string root){
  Directory.CreateDirectory(root);
  string p=FileIn(root,S(r,"fileName"),".rvt");
  if(action=="open_test_project"){
   if(!File.Exists(p)||(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Invalid sandbox file");
   var ui=app.OpenAndActivateDocument(p);return new{ok=true,path=ui.Document.PathName};
  }
  if(File.Exists(p))throw new IOException("No overwrite");
  string templates=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Autodesk","RVT 2025","Templates");
  string tpl=Path.GetFullPath(S(r,"template"));
  if(!tpl.StartsWith(templates+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(tpl)||!tpl.EndsWith(".rte",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use installed project template");
  var doc=app.Application.NewProjectDocument(tpl);
  try{doc.SaveAs(p,new SaveAsOptions{OverwriteExistingFile=false});}
  finally{doc.Close(false);}
  return new{ok=true,path=p};
 }
 internal static object Elements(Document d,JsonElement r){
  int limit=r.TryGetProperty("limit",out var n)?Math.Clamp(n.GetInt32(),1,1000):100;
  string q=r.TryGetProperty("query",out var v)?v.GetString()??"":"";
  var elements=new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e=>q.Length==0||(e.Category?.Name??"").Contains(q,StringComparison.OrdinalIgnoreCase)||e.Name.Contains(q,StringComparison.OrdinalIgnoreCase)).Take(limit).Select(e=>{
   var b=e.get_BoundingBox(null);return new{id=e.Id.Value,uniqueId=e.UniqueId,name=e.Name,category=e.Category?.Name,typeId=e.GetTypeId().Value,box=b==null?null:new{min=new[]{b.Min.X,b.Min.Y,b.Min.Z}.Select(x=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters)).ToArray(),max=new[]{b.Max.X,b.Max.Y,b.Max.Z}.Select(x=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters)).ToArray()}};
  }).ToArray();return new{ok=true,elements};
 }
 internal static object Info(Document d,JsonElement r){
  var ids=r.GetProperty("ids").EnumerateArray().Select(x=>new ElementId(x.GetInt64())).ToArray();
  if(ids.Length<1||ids.Length>100)throw new ArgumentException("1-100 elements");
  var elements=ids.Select(id=>{
   var e=d.GetElement(id)??throw new ArgumentException("Missing element "+id.Value);
   var parameters=e.Parameters.Cast<Parameter>().Select(p=>new{name=p.Definition.Name,id=p.Id.Value,storage=p.StorageType.ToString(),readOnly=p.IsReadOnly,unit=p.StorageType==StorageType.Double?p.GetUnitTypeId().TypeId:null,display=p.AsValueString(),value=p.StorageType switch{StorageType.String=>(object?)p.AsString(),StorageType.Integer=>p.AsInteger(),StorageType.Double=>p.AsDouble(),StorageType.ElementId=>p.AsElementId().Value,_=>null}}).ToArray();
   return new{id=id.Value,name=e.Name,uniqueId=e.UniqueId,category=e.Category?.Name,typeId=e.GetTypeId().Value,parameters};
  }).ToArray();return new{ok=true,elements};
 }
 internal static object Edit(Document d,JsonElement r){
  var steps=r.GetProperty("steps").EnumerateArray().ToArray();if(steps.Length<1||steps.Length>200)throw new ArgumentException("1-200 edits");
  bool commit=r.TryGetProperty("commit",out var c)&&c.ValueKind==JsonValueKind.True;
  using var tx=new Transaction(d,"Kalidis - edit batch");if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction failed");
  var results=new List<object>();
  try{
   foreach(var s in steps){
    var id=Id(s,"elementId");var e=d.GetElement(id)??throw new ArgumentException("Element not found");
    switch(S(s,"action")){
     case "move": ElementTransformUtils.MoveElement(d,id,Vector(s.GetProperty("offset")));break;
     case "copy": var copied=ElementTransformUtils.CopyElement(d,id,Vector(s.GetProperty("offset")));results.Add(new{sourceId=id.Value,copiedIds=copied.Select(x=>x.Value).ToArray()});continue;
     case "rotate": var p=Vector(s.GetProperty("origin"));ElementTransformUtils.RotateElement(d,id,Line.CreateBound(p,p+XYZ.BasisZ),s.GetProperty("degrees").GetDouble()*Math.PI/180);break;
     case "set_parameter": {
      Parameter parameter;
      if(s.TryGetProperty("parameterId",out var pid))parameter=e.Parameters.Cast<Parameter>().Single(p=>p.Id.Value==pid.GetInt64());
      else parameter=e.GetParameters(S(s,"name")).Single();
      if(parameter.IsReadOnly)throw new InvalidOperationException("Read-only parameter");
      var value=s.GetProperty("value");
      bool success=parameter.StorageType switch{
       StorageType.String=>parameter.Set(value.GetString()),
       StorageType.Integer=>parameter.Set(value.GetInt32()),
       StorageType.Double when parameter.Definition.GetDataType()==SpecTypeId.Length=>parameter.Set(Mm(value.GetDouble())),
       StorageType.Double when parameter.Definition.GetDataType()==SpecTypeId.Number=>parameter.Set(value.GetDouble()),
       StorageType.ElementId=>parameter.Set(new ElementId(value.GetInt64())),
       _=>throw new ArgumentException("Unsupported parameter unit")
      };
      if(!success)throw new InvalidOperationException("Parameter update failed");break;
     }
     case "pin":e.Pinned=s.GetProperty("value").GetBoolean();break;
     case "rename":e.Name=S(s,"value");break;
     case "change_type": e.ChangeTypeId(Id(s,"typeId"));break;
     case "delete": var removed=d.Delete(id);results.Add(new{sourceId=id.Value,deletedIds=removed.Select(x=>x.Value).ToArray()});continue;
     default:throw new ArgumentException("Unsupported edit");
    }
    results.Add(new{elementId=id.Value});
   }
   d.Regenerate();var status=commit?tx.Commit():tx.RollBack();return new{ok=status==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack),status=status.ToString(),dryRun=!commit,results};
  }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw;}
 }
 internal static object Export(Document d,JsonElement r,string root){
  string format=S(r,"format");string ext=format=="ifc"?".ifc":format=="dwg"?".dwg":format=="pdf"?".pdf":throw new ArgumentException("Unsupported format");
  string p=FileIn(root,S(r,"fileName"),ext);
  string folder=Path.Combine(root,"export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  string name=Path.GetFileNameWithoutExtension(p);bool ok;
  if(format=="ifc"){using var opt=new IFCExportOptions();using var tx=new Transaction(d,"Kalidis - IFC export");if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("IFC export transaction failed");try{ok=d.Export(folder,name,opt);}finally{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();}}
  else {
   var ids=r.GetProperty("viewIds").EnumerateArray().Select(x=>new ElementId(x.GetInt64())).ToList();if(ids.Count==0||ids.Count>100)throw new ArgumentException("1-100 views required");
   if(format=="dwg"){using var opt=new DWGExportOptions();ok=d.Export(folder,name,ids,opt);}
   else {using var opt=new PDFExportOptions{Combine=true,FileName=name};ok=d.Export(folder,ids,opt);}
  }
  return new{ok,folder,files=Directory.GetFiles(folder)};
 }
}