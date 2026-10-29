// Explicit versioned definitions for end-to-end workflow scenarios.
export function approvalStage(key, approverUserIds = [], dependsOn = [], source = 'Named') {
  return { key, name: key, dependsOn, source, approverUserIds, decision: 'All', selfApproval: 'Skip', repeatedApprover: 'Allow', conditions: [], dueHours: 48 };
}

export async function publishDefinitions(invoke, token, settings, keys) {
  let saved = await invoke('SaveOaApprovalSettings', { ...settings, expectedVersion: settings.versionNumber }, {}, token);
  for (const definitionKey of keys) saved = await invoke('PublishOaDefinition', { expectedVersion: saved.versionNumber, definitionKey }, {}, token);
  return saved;
}

export async function approveTask(invoke, token, kind, id, note = '审批验收') {
  const row = await invoke(`Get${kind}Request`, undefined, { id }, token);
  if (!row.reviewTaskKeys?.length) throw new Error(`No assigned task for ${kind} #${id}`);
  return invoke(`Approve${kind}Request`, { expectedVersion: row.versionNumber, taskKey: row.reviewTaskKeys[0], note }, { id }, token);
}


export async function approveResourceTask(invoke, token, kind, title) {
  const room = kind === 'room';
  const page = await invoke(room ? 'ListMeetingBookings' : 'ListOfficeSupplyRequests', undefined, { mineOnly: false, status: 'Pending' }, token);
  const row = page.items.find(row => (room ? row.title : row.supplyName) === title);
  if (!row?.reviewTaskKeys?.length) throw new Error('Missing resource approval task: ' + title);
  return invoke(room ? 'ApproveMeetingBooking' : 'ApproveOfficeSupplyRequest', { expectedVersion: row.versionNumber, taskKey: row.reviewTaskKeys[0], note: '资源独立审批' }, { id: row.id }, token);
}
