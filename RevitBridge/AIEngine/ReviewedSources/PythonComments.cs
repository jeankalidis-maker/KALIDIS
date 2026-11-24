using Autodesk.Revit.DB;
using Kalidis.AIEngine;
using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections;
namespace Kalidis.PythonComments;
public sealed class PythonComments : IReviewedOperation {
 public string Name=>"python_comments";
 public object Execute(Document d,JsonElement a){
  var mode=a.GetProperty("mode").GetString();
  if(mode!="set")throw new ArgumentException("Fixed comment update only");
  var id=a.GetProperty("id").GetInt64();var target=d.GetElement(new ElementId(id)) as FamilyInstance??throw new ArgumentException("Family instance required");
  if(target.Category?.Id.Value!=(long)BuiltInCategory.OST_Furniture||target.Pinned||target.GroupId!=ElementId.InvalidElementId||target.Host!=null)throw new ArgumentException("Independent furniture only");
  var p=target.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)??throw new ArgumentException("No comments parameter");
  var previous=p.AsString()??"";var expected=a.GetProperty("expected").GetString()??"";var value=a.GetProperty("value").GetString()??"";
  if(p.IsReadOnly||previous!=expected||value.Length>200)throw new InvalidOperationException("Stale value, read-only or overlong comments");
  string root=Path.Combine(Path.GetDirectoryName(typeof(Document).Assembly.Location)!,"AddIns","DynamoForRevit");
  string file=Path.Combine(root,"DSCPython.dll");
  if(!File.Exists(file))throw new InvalidOperationException("Autodesk CPython evaluator unavailable");
  if(mode=="inventory")return new{installed=File.Exists(file),loaded=AppDomain.CurrentDomain.GetAssemblies().Where(x=>x.GetName().Name is "DSCPython" or "Python.Runtime" or "DynamoCore").Select(x=>new{name=x.GetName().Name,version=x.GetName().Version?.ToString()}).ToArray()};
  Assembly? Resolve(AssemblyLoadContext c,AssemblyName n){
   if(n.Name==null||n.Name.Contains('/')||n.Name.Contains('\\'))return null;
   foreach(var dir in new[]{root,Path.Combine(root,"Revit")}){
    string p=Path.Combine(dir,n.Name+".dll");
    if(File.Exists(p))return c.LoadFromAssemblyPath(p);
   }
   return null;
  }
  var context=AssemblyLoadContext.GetLoadContext(typeof(PythonComments).Assembly)??AssemblyLoadContext.Default;
  context.Resolving+=Resolve;AssemblyLoadContext.Default.Resolving+=Resolve;
  try{
   var asm=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>x.GetName().Name=="DSCPython")??context.LoadFromAssemblyPath(file);
   var type=asm.GetType("DSCPython.CPythonEvaluator",true)!;
   var eval=type.GetProperty("Instance",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(null)??throw new InvalidOperationException("Evaluator singleton unavailable");
   var method=type.GetMethod("Evaluate",new[]{typeof(string),typeof(IList),typeof(IList)})??throw new InvalidOperationException("Evaluate API unavailable");
   const string script="import json, os\nimport clr\nclr.AddReference('RevitAPI')\nfrom Autodesk.Revit.DB import ElementId, BuiltInParameter\nfrom System import Int64\np = doc.GetElement(ElementId(Int64(eid))).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)\nif not p.Set(value): raise Exception('Set rejected')\nOUT = json.dumps({'pid':os.getpid(),'value':p.AsString()})";
   var result=method.Invoke(eval,new object[]{script,new ArrayList{"doc","eid","value"},new ArrayList{d,id,value}}) as string??throw new InvalidOperationException("Expected string");
   d.Regenerate();
   using var parsed=JsonDocument.Parse(result);var v=parsed.RootElement;
   if(v.GetProperty("pid").GetInt32()!=Environment.ProcessId||v.GetProperty("value").GetString()!=value||p.AsString()!=value)throw new InvalidOperationException("Python/native readback mismatch");
   return new{id,previous,value,pythonPid=Environment.ProcessId,note="Fixed audited parameter update in outer native transaction"};
  }catch(TargetInvocationException ex){throw new InvalidOperationException(ex.InnerException?.ToString()??ex.ToString());}
  finally{context.Resolving-=Resolve;AssemblyLoadContext.Default.Resolving-=Resolve;}
 }
}