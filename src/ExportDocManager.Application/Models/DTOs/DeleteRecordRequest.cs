namespace ExportDocManager.Models;

public sealed record DeleteRecordRequest(int ExpectedVersion, string Reason);
