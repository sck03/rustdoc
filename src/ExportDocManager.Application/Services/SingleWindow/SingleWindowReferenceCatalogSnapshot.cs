using System.Threading;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowReferenceCatalogSnapshot
    {
        public SingleWindowReferenceCatalogSnapshot(
            SingleWindowReferenceCatalogModel? catalog = null,
            IEnumerable<CustomsCooIssuingAuthorityEntry>? issuingAuthorities = null)
        {
            catalog ??= new SingleWindowReferenceCatalogModel();
            FieldMapper = new SingleWindowFieldMapperHelpers(catalog);
            EditorOptions = new SingleWindowReferenceCatalogs(catalog);
            IssuingAuthorities = new CustomsCooIssuingAuthorityCatalog(issuingAuthorities);
        }

        public SingleWindowFieldMapperHelpers FieldMapper { get; }

        public SingleWindowReferenceCatalogs EditorOptions { get; }

        public CustomsCooIssuingAuthorityCatalog IssuingAuthorities { get; }
    }

    public interface ISingleWindowReferenceCatalogSnapshotProvider
    {
        SingleWindowReferenceCatalogSnapshot Current { get; }
    }

    public sealed class SingleWindowReferenceCatalogSnapshotStore : ISingleWindowReferenceCatalogSnapshotProvider
    {
        private SingleWindowReferenceCatalogSnapshot _current;

        public SingleWindowReferenceCatalogSnapshotStore(
            SingleWindowReferenceCatalogSnapshot? initialSnapshot = null)
        {
            _current = initialSnapshot ?? new SingleWindowReferenceCatalogSnapshot();
        }

        public SingleWindowReferenceCatalogSnapshot Current => Volatile.Read(ref _current);

        public void Replace(SingleWindowReferenceCatalogSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            Volatile.Write(ref _current, snapshot);
        }
    }
}
