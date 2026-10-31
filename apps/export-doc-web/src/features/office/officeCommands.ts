import type { ExportDocManagerApiClient, OaAction, OaApprovalReassign } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";

export type OfficeActionInput = Omit<OaAction,"commandKey"> & {commandKey?:string};
export type ApprovalReassignInput = Omit<OaApprovalReassign,"commandKey"> & {commandKey?:string};
const commands = new WeakMap<ExportDocManagerApiClient,Map<string,string>>();

/** A retry in the same identity keeps its acknowledgement key. The bounded
 * session map disappears with the API client on logout or identity switch. */
export function officeCommand<T extends {commandKey?:string}>(client:ExportDocManagerApiClient,target:string,body:T):T & {commandKey:string} {
  if(body.commandKey)return {...body,commandKey:body.commandKey};
  let keys=commands.get(client);
  if(!keys){keys=new Map();commands.set(client,keys);}
  const request=JSON.stringify([target,body]);
  let key=keys.get(request);
  if(!key){key=createRequestKey();keys.set(request,key);if(keys.size>500)keys.delete(keys.keys().next().value!);}
  return {...body,commandKey:key};
}
