using Autodesk.Revit.DB;
using Kalidis.AIEngine;
using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections;
namespace Kalidis.PythonProbe;
public sealed class PythonProbe : IReviewedOperation {
 public string Name=>"python_probe";
 public object Execute(Document d,JsonElement a){
  var mode=a.GetProperty("mode").GetString();
  if(mode!="inventory"&&mode!="audit")throw new ArgumentException("Fixed read-only probe only");
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
  var context=AssemblyLoadContext.GetLoadContext(typeof(PythonProbe).Assembly)??AssemblyLoadContext.Default;
  context.Resolving+=Resolve;AssemblyLoadContext.Default.Resolving+=Resolve;
  try{
   var asm=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>x.GetName().Name=="DSCPython")??context.LoadFromAssemblyPath(file);
   var type=asm.GetType("DSCPython.CPythonEvaluator",true)!;
   var eval=type.GetProperty("Instance",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(null)??throw new InvalidOperationException("Evaluator singleton unavailable");
   var method=type.GetMethod("Evaluate",new[]{typeof(string),typeof(IList),typeof(IList)})??throw new InvalidOperationException("Evaluate API unavailable");
   const string script="import sys, json, os\nimport clr\nclr.AddReference('RevitAPI')\nfrom Autodesk.Revit.DB import FilteredElementCollector, Wall\nOUT = json.dumps({'pid':os.getpid(),'version':sys.version.split()[0],'title':doc.Title,'walls':FilteredElementCollector(doc).OfClass(Wall).WhereElementIsNotElementType().GetElementCount()})";
   var result=method.Invoke(eval,new object[]{script,new ArrayList{"doc"},new ArrayList{d}}) as string??throw new InvalidOperationException("Expected string");
   using var parsed=JsonDocument.Parse(result);var v=parsed.RootElement;
   int count=new FilteredElementCollector(d).OfClass(typeof(Wall)).WhereElementIsNotElementType().GetElementCount();
   if(v.GetProperty("pid").GetInt32()!=Environment.ProcessId||v.GetProperty("title").GetString()!=d.Title||v.GetProperty("walls").GetInt32()!=count)throw new InvalidOperationException("Python/native readback mismatch");
   return new{python=v.Clone(),nativeWallCount=count,sameProcess=true,note="Fixed audited Python read-only script; no arbitrary script execution."};
  }catch(TargetInvocationException ex){throw new InvalidOperationException(ex.InnerException?.ToString()??ex.ToString());}
  finally{context.Resolving-=Resolve;AssemblyLoadContext.Default.Resolving-=Resolve;}
 }
}