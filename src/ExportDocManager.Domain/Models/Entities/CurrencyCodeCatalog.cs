namespace ExportDocManager.Models.Entities;

/// <summary>
/// ISO 4217 alphabetic currency codes accepted by commercial records. The
/// catalog is deliberately deterministic and does not depend on the host OS
/// culture database, which can differ between Windows, Linux and macOS.
/// </summary>
public static class CurrencyCodeCatalog
{
    private const string Codes =
        "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD " +
        "CAD CDF CHE CHF CHW CLF CLP CNY COP COU CRC CUC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP " +
        "GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR " +
        "KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN " +
        "MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR " +
        "SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD USN " +
        "UYI UYU UYW UZS VED VES VND VUV WST XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR XOF XPD XPF XPT XSU " +
        "XTS XUA XXX YER ZAR ZMW ZWG";

    private static readonly IReadOnlySet<string> KnownCodes = Codes
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string? value) =>
        KnownCodes.Contains((value ?? string.Empty).Trim().ToUpperInvariant());

    public static string Normalize(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return IsKnown(normalized)
            ? normalized
            : throw new ArgumentException("币种必须使用有效的 ISO 4217 三位代码。");
    }
}
