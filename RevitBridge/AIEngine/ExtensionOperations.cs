using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;

// Full-trust in-process extensions, NOT a security sandbox. Only locally reviewed,
// explicitly hash-approved DLLs are accepted. No downloading or remote registration.
public interface IReviewedOperation {
 string Name {get;}
 object Execute(Document document, JsonElement arguments);
}
internal static class ExtensionOperations {
 internal static readonly string[] Actions={"extensions","run_extension"};
 private static readonly Dictionary<string,(IReviewedOperation op,string hash)> Loaded=new(StringComparer.Ordinal);
 private static string Folder(string home)=>Path.Combine(home,"ReviewedExtensions");
 private static Dictionary<string,string> Approved(string home){
  string manifest=Path.Combine(Folder(home),"approved.json");
  if(!File.Exists(manifest))return new();
  return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest))??new();
 }
 internal static object List(string home){
  var approved=Approved(home);
  return new{ok=true,trust="Full-trust local code; no arbitrary remote scripts. Only locally reviewed SHA-256 approved binaries.",approved=approved.Keys,loaded=Loaded.Keys.ToArray()};
 }
 internal static object Run(Document d,JsonElement r,string home){
  string file=r.GetProperty("assembly").GetString()??"";
  string name=r.GetProperty("operation").GetString()??"";
  if(file!=Path.GetFileName(file)||!file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||file.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Simple DLL name required");
  var approved=Approved(home);
  if(!approved.TryGetValue(file,out var expected))throw new InvalidOperationException("Extension not locally approved");
  string folder=Folder(home),path=Path.Combine(folder,file);
  if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse point refused");
  byte[] bytes=File.ReadAllBytes(path);if(bytes.Length>20000000)throw new ArgumentException("Extension too large");
  string hash=Convert.ToHexString(SHA256.HashData(bytes));
  if(!hash.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Extension hash differs from local approval");
  string key=file+"::"+name;
  if(!Loaded.TryGetValue(key,out var loaded)){
   var asm=AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
   var types=asm.GetTypes().Where(t=>!t.IsAbstract&&typeof(IReviewedOperation).IsAssignableFrom(t)&&t.GetConstructor(Type.EmptyTypes)!=null);
   var candidates=types.Select(t=>(IReviewedOperation)Activator.CreateInstance(t)!).Where(t=>t.Name==name).ToArray();
   if(candidates.Length!=1)throw new InvalidOperationException("Exactly one matching reviewed operation required");
   loaded=(candidates[0],hash);Loaded.Add(key,loaded);
  }
  if(loaded.hash!=hash)throw new InvalidOperationException("Restart Revit to change extension binary");
  bool commit=r.TryGetProperty("commit",out var c)&&c.ValueKind==JsonValueKind.True;
  using var tx=new Transaction(d,"Kalidis - reviewed extension "+name);
  if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction failed");
  try{
   object result=loaded.op.Execute(d,r.GetProperty("arguments"));
   d.Regenerate();
   var state=commit?tx.Commit():tx.RollBack();
   return new{ok=state==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack),dryRun=!commit,status=state.ToString(),result};
  }catch{if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw;}
 }
}