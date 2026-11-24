using System.Text.Json;
using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;
internal static class FamilyEditOperations {
 internal static readonly string[] Actions={"edit_family_copy"};
 private static string S(JsonElement r,string k)=>r.GetProperty(k).GetString()??throw new ArgumentException(k);
 private static double Number(JsonElement r,string k){double v=r.GetProperty(k).GetDouble();if(!double.IsFinite(v)||Math.Abs(v)>10000000)throw new ArgumentException("Invalid number");return v;}
 internal static object Run(Document d,JsonElement r,string root){
  var family=d.GetElement(new ElementId(r.GetProperty("familyId").GetInt64())) as Family??throw new ArgumentException("Invalid family");
  if(!family.IsEditable||family.IsInPlace)throw new InvalidOperationException("Family is not editable");
  string file=S(r,"fileName");
  if(file!=Path.GetFileName(file)||!file.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||file.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Simple RFA filename required");
  string target=Path.Combine(root,file);if(File.Exists(target))throw new IOException("No overwrite");
  var changes=r.GetProperty("changes").EnumerateArray().ToArray();if(changes.Length<1||changes.Length>100)throw new ArgumentException("1-100 changes");
  bool commit=r.TryGetProperty("commit",out var c)&&c.ValueKind==JsonValueKind.True;
  var fd=d.EditFamily(family);
  try{
   using var tx=new Transaction(fd,"Kalidis - family copy");if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction failed");
   var fm=fd.FamilyManager;
   foreach(var op in changes){
    string action=S(op,"action");
    if(action=="new_type"){fm.NewType(S(op,"name"));continue;}
    if(action=="select_type"){fm.CurrentType=fm.Types.Cast<FamilyType>().Single(t=>t.Name==S(op,"name"));continue;}
    string name=S(op,"name");
    FamilyParameter fp;
    if(action=="add_parameter"){
     var spec=S(op,"kind") switch{"length"=>SpecTypeId.Length,"number"=>SpecTypeId.Number,"text"=>SpecTypeId.String.Text,_=>throw new ArgumentException("Unsupported parameter kind")};
     fp=fm.AddParameter(name,GroupTypeId.Data,spec,op.TryGetProperty("instance",out var inst)&&inst.ValueKind==JsonValueKind.True);
    }else fp=fm.Parameters.Cast<FamilyParameter>().Single(p=>p.Definition.Name==name);
    if(action=="set_formula"){fm.SetFormula(fp,S(op,"formula"));continue;}
    if(action!="set_value"&&action!="add_parameter")throw new ArgumentException("Unsupported family edit");
    if(fm.CurrentType==null)fm.NewType("Default");
    if(!op.TryGetProperty("value",out var value))continue;
    if(fp.StorageType==StorageType.String)fm.Set(fp,value.GetString());
    else if(fp.StorageType==StorageType.Double&&fp.Definition.GetDataType()==SpecTypeId.Length)fm.Set(fp,UnitUtils.ConvertToInternalUnits(Number(op,"value"),UnitTypeId.Millimeters));
    else if(fp.StorageType==StorageType.Double&&fp.Definition.GetDataType()==SpecTypeId.Number)fm.Set(fp,Number(op,"value"));
    else if(fp.StorageType==StorageType.Integer)fm.Set(fp,value.GetInt32());
    else throw new ArgumentException("Unsupported parameter value");
   }
   fd.Regenerate();
   var types=fm.Types.Cast<FamilyType>().Select(t=>t.Name).ToArray();
   var parameters=fm.Parameters.Cast<FamilyParameter>().Select(p=>new{name=p.Definition.Name,formula=p.Formula,instance=p.IsInstance}).ToArray();
   var state=commit?tx.Commit():tx.RollBack();
   if(state!=(commit?TransactionStatus.Committed:TransactionStatus.RolledBack))throw new InvalidOperationException("Unexpected family transaction status");
   if(commit)fd.SaveAs(target,new SaveAsOptions{OverwriteExistingFile=false});
   return new{ok=true,dryRun=!commit,path=commit?target:null,types,parameters,note="Original project family was not reloaded"};
  }finally{fd.Close(false);}
 }
}