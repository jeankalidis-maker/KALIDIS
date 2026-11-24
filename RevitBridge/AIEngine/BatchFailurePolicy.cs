using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;
// Batch safety: unexpected Revit failures abort rather than silently approving a warning.
internal sealed class BatchFailurePolicy : IFailuresPreprocessor {
 internal List<string> Messages { get; }=new();
 public FailureProcessingResult PreprocessFailures(FailuresAccessor a){
  var messages=a.GetFailureMessages();
  if(messages.Count==0)return FailureProcessingResult.Continue;
  foreach(var m in messages.Take(30))Messages.Add(m.GetSeverity()+": "+m.GetDescriptionText());
  return FailureProcessingResult.ProceedWithRollBack;
 }
}