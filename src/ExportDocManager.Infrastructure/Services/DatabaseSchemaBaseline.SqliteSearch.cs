using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure;

internal static partial class DatabaseSchemaBaseline
{
    private static Task CreateSqliteSearchIndexesAsync(
        AppDbContext context,
        CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync(
            """
            CREATE VIEW "InvoiceSearchSource" AS
            SELECT
                invoice."Id" AS "InvoiceId",
                COALESCE(invoice."InvoiceNo", '') AS "InvoiceNo",
                COALESCE(invoice."ContractNo", '') AS "ContractNo",
                COALESCE(invoice."CustomerNameEN", '') AS "CustomerNameEN",
                COALESCE(invoice."NotifyPartyName", '') AS "NotifyPartyName",
                COALESCE(invoice."ExporterNameEN", '') AS "ExporterNameEN",
                COALESCE(invoice."ExporterNameCN", '') AS "ExporterNameCN",
                COALESCE(invoice."DestinationCountry", '') AS "DestinationCountry",
                COALESCE(invoice."PortOfLoading", '') AS "PortOfLoading",
                COALESCE(invoice."PortOfDestination", '') AS "PortOfDestination",
                COALESCE(invoice."TradeTerms", '') AS "TradeTerms",
                COALESCE(invoice."TransportMode", '') AS "TransportMode",
                COALESCE(group_concat(item."PoNumber", ' '), '') AS "ItemPoNumber",
                COALESCE(group_concat(item."StyleName", ' '), '') AS "ItemStyleName",
                COALESCE(group_concat(item."StyleNameCN", ' '), '') AS "ItemStyleNameCN",
                COALESCE(group_concat(item."StyleNo", ' '), '') AS "ItemStyleNo",
                COALESCE(group_concat(item."HSCode", ' '), '') AS "ItemHSCode",
                COALESCE(group_concat(item."Brand", ' '), '') AS "ItemBrand",
                COALESCE(group_concat(item."Origin", ' '), '') AS "ItemOrigin"
            FROM "Invoices" AS invoice
            LEFT JOIN "Items" AS item ON item."InvoiceId" = invoice."Id"
            GROUP BY invoice."Id";

            CREATE VIRTUAL TABLE "InvoiceSearch" USING fts5(
                InvoiceId UNINDEXED,
                InvoiceNo,
                ContractNo,
                CustomerNameEN,
                NotifyPartyName,
                ExporterNameEN,
                ExporterNameCN,
                DestinationCountry,
                PortOfLoading,
                PortOfDestination,
                TradeTerms,
                TransportMode,
                ItemPoNumber,
                ItemStyleName,
                ItemStyleNameCN,
                ItemStyleNo,
                ItemHSCode,
                ItemBrand,
                ItemOrigin,
                tokenize='trigram'
            );

            CREATE TRIGGER "TR_Invoices_Search_Insert" AFTER INSERT ON "Invoices" BEGIN
                INSERT INTO "InvoiceSearch" SELECT * FROM "InvoiceSearchSource" WHERE "InvoiceId" = new."Id";
            END;
            CREATE TRIGGER "TR_Invoices_Search_Update" AFTER UPDATE ON "Invoices" BEGIN
                DELETE FROM "InvoiceSearch" WHERE "InvoiceId" = old."Id";
                INSERT INTO "InvoiceSearch" SELECT * FROM "InvoiceSearchSource" WHERE "InvoiceId" = new."Id";
            END;
            CREATE TRIGGER "TR_Invoices_Search_Delete" AFTER DELETE ON "Invoices" BEGIN
                DELETE FROM "InvoiceSearch" WHERE "InvoiceId" = old."Id";
            END;
            CREATE TRIGGER "TR_Items_Search_Insert" AFTER INSERT ON "Items" BEGIN
                DELETE FROM "InvoiceSearch" WHERE "InvoiceId" = new."InvoiceId";
                INSERT INTO "InvoiceSearch" SELECT * FROM "InvoiceSearchSource" WHERE "InvoiceId" = new."InvoiceId";
            END;
            CREATE TRIGGER "TR_Items_Search_Update" AFTER UPDATE ON "Items" BEGIN
                DELETE FROM "InvoiceSearch" WHERE "InvoiceId" = old."InvoiceId";
                INSERT INTO "InvoiceSearch" SELECT * FROM "InvoiceSearchSource" WHERE "InvoiceId" = old."InvoiceId";
            END;
            CREATE TRIGGER "TR_Items_Search_Move" AFTER UPDATE OF "InvoiceId" ON "Items"
            WHEN old."InvoiceId" <> new."InvoiceId" BEGIN
                DELETE FROM "InvoiceSearch" WHERE "InvoiceId" = new."InvoiceId";
                INSERT INTO "InvoiceSearch" SELECT * FROM "InvoiceSearchSource" WHERE "InvoiceId" = new."InvoiceId";
            END;
            CREATE TRIGGER "TR_Items_Search_Delete" AFTER DELETE ON "Items" BEGIN
                DELETE FROM "InvoiceSearch" WHERE "InvoiceId" = old."InvoiceId";
                INSERT INTO "InvoiceSearch" SELECT * FROM "InvoiceSearchSource" WHERE "InvoiceId" = old."InvoiceId";
            END;

            CREATE VIEW "PaymentSearchSource" AS
            SELECT
                payment."Id" AS "PaymentId",
                COALESCE(payment."InvoiceNo", '') AS "InvoiceNo",
                COALESCE(payment."PayerName", '') AS "PayerName",
                COALESCE(payment."Project", '') AS "Project",
                COALESCE(payment."Department", '') AS "Department",
                COALESCE(payment."PayeeName", '') AS "PayeeName",
                COALESCE(payment."BankName", '') AS "BankName",
                COALESCE(payment."AccountNo", '') AS "AccountNo",
                COALESCE(payment."GoodsName", '') AS "GoodsName",
                COALESCE(payment."ShipmentCountry", '') AS "ShipmentCountry",
                COALESCE(payment."Notes", '') AS "Notes"
            FROM "Payments" AS payment;

            CREATE VIRTUAL TABLE "PaymentSearch" USING fts5(
                PaymentId UNINDEXED,
                InvoiceNo,
                PayerName,
                Project,
                Department,
                PayeeName,
                BankName,
                AccountNo,
                GoodsName,
                ShipmentCountry,
                Notes,
                tokenize='trigram'
            );

            CREATE TRIGGER "TR_Payments_Search_Insert" AFTER INSERT ON "Payments" BEGIN
                INSERT INTO "PaymentSearch" SELECT * FROM "PaymentSearchSource" WHERE "PaymentId" = new."Id";
            END;
            CREATE TRIGGER "TR_Payments_Search_Update" AFTER UPDATE ON "Payments" BEGIN
                DELETE FROM "PaymentSearch" WHERE "PaymentId" = old."Id";
                INSERT INTO "PaymentSearch" SELECT * FROM "PaymentSearchSource" WHERE "PaymentId" = new."Id";
            END;
            CREATE TRIGGER "TR_Payments_Search_Delete" AFTER DELETE ON "Payments" BEGIN
                DELETE FROM "PaymentSearch" WHERE "PaymentId" = old."Id";
            END;
            """,
            cancellationToken);
}
