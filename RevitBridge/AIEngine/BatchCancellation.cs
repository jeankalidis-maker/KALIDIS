namespace Kalidis.AIEngine;
// Cooperative cancellation only; no interruption during native API calls or commit.
internal sealed class BatchCancellation {
 private readonly object gate=new();
 private bool requested,committing,finished;
 internal bool Request(){lock(gate){if(committing||finished)return false;requested=true;return true;}}
 internal void Check(){lock(gate){if(requested)throw new OperationCanceledException("Batch canceled before commit");}}
 internal void BeginCommit(){lock(gate){if(requested)throw new OperationCanceledException("Batch canceled before commit");committing=true;}}
 internal void Finish(){lock(gate){finished=true;}}
}
