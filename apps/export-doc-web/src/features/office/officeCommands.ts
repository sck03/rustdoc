import { ApiError, type ApiRequestInit, type ExportDocManagerApiClient, type OaAction } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";

export type OfficeActionInput = Omit<OaAction,"commandKey"> & {commandKey?:string};
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

/** Resolve an ambiguous write via its actor-scoped receipt, without resending it. */
export function acknowledgedCommands<N extends ReceiptTarget>(client: ExportDocManagerApiClient, name: N) {
  type Row = Awaited<ReturnType<ExportDocManagerApiClient[`get${N}`]>>;
  return async <T extends {commandKey: string}>(id: number, body: T, init: ApiRequestInit | undefined,
    send: (request: {id:number; body:T}, init?: ApiRequestInit) => Promise<Row>): Promise<Row> => {
    try { return await send({id,body},init); }
    catch (cause) {
      init?.signal?.throwIfAborted();
      if (!(cause instanceof TypeError || (cause instanceof Error && cause.name === "TimeoutError") || (cause instanceof ApiError && [502,503,504].includes(cause.status)))) throw cause;
      try { await client[`get${name}CommandResult`]({id, commandKey:body.commandKey},init); }
      catch (receiptError) { init?.signal?.throwIfAborted(); throw new Error("操作结果待确认，请刷新后核对。", {cause:receiptError}); }
      init?.signal?.throwIfAborted();
      try { return await client[`get${name}`]({id},init) as Row; }
      catch (refreshError) { init?.signal?.throwIfAborted(); throw new Error("操作已提交，刷新详情失败，请重新打开记录。", {cause:refreshError}); }
    }
  };
}
type ReceiptTarget = { [K in keyof ExportDocManagerApiClient]: K extends `get${infer N}CommandResult` ? N : never }[keyof ExportDocManagerApiClient];
