// Capabilities must be proved by the active engine, not by the local schema.
export function checkBatchCapabilities(args,catalog){
 const hasSession=Object.hasOwn(args,'expectedSession'),hasRevision=Object.hasOwn(args,'expectedRevision');
 if(hasSession!==hasRevision)throw Error('Both expectedSession and expectedRevision required together');
 if(hasSession&&!catalog.implemented?.includes('project_context'))throw Error('Active engine does not support revision guards; install and verify the updated engine first');
 if(Object.hasOwn(args,'jobId')&&catalog.cooperativeBatchCancel!==true)throw Error('Active engine does not support cooperative batch cancellation');
}
export function batchSchemaForEngine(tool,catalog){
 const properties={...tool.inputSchema.properties};
 if(!catalog.implemented?.includes('project_context')){delete properties.expectedSession;delete properties.expectedRevision}
 if(catalog.cooperativeBatchCancel!==true)delete properties.jobId;
 return {...tool,inputSchema:{...tool.inputSchema,properties}};
}
