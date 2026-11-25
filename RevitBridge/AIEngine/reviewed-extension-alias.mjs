// Generic invocation is only an alias of a registered, uniquely resolved reviewed tool.
// Never run an unregistered local DLL from an MCP request.
import {validate} from './command-registry.mjs';
export function resolveReviewedAlias(args,entries){
 if(typeof args.assembly!=='string'||!/^Kalidis\.Reviewed\.[A-Fa-f0-9]{12}\.[A-Fa-f0-9]{12}\.dll$/.test(args.assembly))throw Error('Simple reviewed DLL identity required');
 const e=entries.get(args.operation);
 if(!e||e.assembly!==args.assembly)throw Error('Extension not uniquely registered and locally approved; use a named reviewed tool');
 const fields=args.arguments;
 if(!fields||typeof fields!=='object'||Array.isArray(fields)||['expectedPath','commit','requestId','action','__proto__','constructor','prototype'].some(k=>Object.hasOwn(fields,k)))throw Error('Reserved argument or invalid extension arguments');
 if(e.inspection&&args.commit===true)throw Error('Inspection extension cannot commit');
 const mapped={...fields,expectedPath:args.expectedPath};
 if(Object.hasOwn(e.tool.inputSchema.properties??{},'commit'))mapped.commit=args.commit===true;
 else if(args.commit===true)throw Error('Commit is not supported by registered extension');
 validate(e.tool.inputSchema,mapped,e.tool.name);
 return {name:e.tool.name,args:mapped};
}