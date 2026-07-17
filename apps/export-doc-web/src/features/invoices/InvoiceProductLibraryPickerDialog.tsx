import { type FormEvent, useEffect, useRef, useState } from "react";
import { PackageCheck, PackagePlus, PackageSearch, RefreshCw, Search, X } from "lucide-react";
import type { ApiProductDto } from "../../api/index.ts";
import { formatPlainNumber, normalizeText, numberValue } from "../../ui/formUtils.ts";

export function ProductLibraryPickerDialog({
  focusedRowIndex,
  initialKeyword,
  isBusy,
  itemsCount,
  products,
  readOnly,
  onApply,
  onClose,
  onRefresh,
  onSearch,
}: {
  focusedRowIndex: number | null;
  initialKeyword: string;
  isBusy: boolean;
  itemsCount: number;
  products: ApiProductDto[];
  readOnly: boolean;
  onApply: (product: ApiProductDto) => void;
  onClose: () => void;
  onRefresh: () => void;
  onSearch: (keyword: string) => void;
}) {
  const searchInputRef = useRef<HTMLInputElement | null>(null);
  const [keyword, setKeyword] = useState(initialKeyword);
  const [selectedProductId, setSelectedProductId] = useState(() => products[0]?.id ?? 0);
  const [productSnapshot, setProductSnapshot] = useState<ApiProductDto[]>(() => products);

  const displayedProducts = filterProductLibraryProducts(
    products.length > 0 ? products : productSnapshot,
    keyword,
  );

  useEffect(() => {
    searchInputRef.current?.focus();
    searchInputRef.current?.select();
  }, []);

  useEffect(() => {
    if (products.length > 0) {
      setProductSnapshot(products);
    }
  }, [products]);

  useEffect(() => {
    if (displayedProducts.some((product) => product.id === selectedProductId)) {
      return;
    }

    setSelectedProductId(displayedProducts[0]?.id ?? 0);
  }, [displayedProducts, selectedProductId]);

  const selectedProduct = displayedProducts.find((product) => product.id === selectedProductId) ?? null;
  const targetRowText =
    focusedRowIndex == null || focusedRowIndex < 0 || focusedRowIndex >= itemsCount
      ? "末尾"
      : `第 ${focusedRowIndex + 2} 行`;

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const field = event.currentTarget.elements.namedItem("productLibraryKeyword");
    const nextKeyword = field instanceof HTMLInputElement ? field.value : keyword;
    setKeyword(nextKeyword);
    onSearch(nextKeyword);
  }

  function applyProduct(product: ApiProductDto | null) {
    if (readOnly || !product) {
      return;
    }

    onApply(product);
  }

  return (
    <div className="single-window-lock-backdrop" onKeyDown={(event) => {
      if (event.key === "Escape") {
        event.stopPropagation();
        onClose();
      }
    }}>
      <div className="single-window-lock-dialog product-library-dialog" role="dialog" aria-modal="true" aria-labelledby="product-library-picker-title">
        <div className="single-window-lock-header">
          <div className="single-window-lock-title">
            <PackageSearch size={18} aria-hidden="true" />
            <h2 id="product-library-picker-title">商品库选择</h2>
            <span>{displayedProducts.length}</span>
          </div>
          <button className="icon-button compact-icon-button" type="button" title="关闭" aria-label="关闭商品库选择" onClick={onClose}>
            <X size={16} aria-hidden="true" />
          </button>
        </div>

        <form className="product-library-toolbar" aria-label="商品库选择搜索栏" onSubmit={submitSearch}>
          <label className="product-library-search">
            <span>搜索</span>
            <input
              ref={searchInputRef}
              aria-label="商品库选择搜索"
              name="productLibraryKeyword"
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
            />
          </label>
          <button className="command-button secondary" type="submit" disabled={isBusy}>
            <Search size={16} aria-hidden="true" />
            <span>搜索</span>
          </button>
          <button className="command-button secondary" type="button" disabled={isBusy} onClick={onRefresh}>
            <RefreshCw size={16} aria-hidden="true" />
            <span>刷新</span>
          </button>
          <div className="product-library-target">
            <span>插入</span>
            <strong>{targetRowText}</strong>
          </div>
        </form>

        <div className="table-frame product-library-table-frame" aria-busy={isBusy}>
          <table className="product-library-table">
            <thead>
              <tr>
                <th>编码</th>
                <th>英文品名</th>
                <th>中文品名</th>
                <th>HS 编码</th>
                <th>材质</th>
                <th>品牌</th>
                <th>原产地</th>
                <th>单位</th>
                <th>默认价</th>
              </tr>
            </thead>
            <tbody>
              {displayedProducts.length === 0 ? (
                <tr>
                  <td colSpan={9} className="empty-cell small-empty">
                    暂无商品
                  </td>
                </tr>
              ) : (
                displayedProducts.map((product) => {
                  const selected = product.id === selectedProductId;
                  return (
                    <tr
                      key={product.id}
                      className={selected ? "product-library-row-selected" : undefined}
                      tabIndex={0}
                      aria-selected={selected}
                      onClick={() => setSelectedProductId(product.id)}
                      onDoubleClick={() => applyProduct(product)}
                      onKeyDown={(event) => {
                        if (event.key === "Enter") {
                          event.preventDefault();
                          applyProduct(product);
                        }
                      }}
                    >
                      <td title={normalizeText(product.productCode)}>{normalizeText(product.productCode)}</td>
                      <td title={normalizeText(product.nameEN)}>{normalizeText(product.nameEN)}</td>
                      <td title={normalizeText(product.nameCN)}>{normalizeText(product.nameCN)}</td>
                      <td title={normalizeText(product.hsCode)}>{normalizeText(product.hsCode)}</td>
                      <td title={normalizeText(product.material)}>{normalizeText(product.material)}</td>
                      <td title={normalizeText(product.brand)}>{normalizeText(product.brand)}</td>
                      <td title={normalizeText(product.origin)}>{normalizeText(product.origin)}</td>
                      <td title={formatProductUnits(product)}>{formatProductUnits(product)}</td>
                      <td>{formatPlainNumber(numberValue(product.defaultPrice))}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        <div className="single-window-lock-footer">
          <span className="product-library-selection">{selectedProduct ? formatProductOptionLabel(selectedProduct) : "未选择商品"}</span>
          <button className="command-button secondary" type="button" onClick={onClose}>
            取消
          </button>
          <button className="solid action-button" type="button" disabled={readOnly || !selectedProduct} onClick={() => applyProduct(selectedProduct)}>
            <PackagePlus size={16} aria-hidden="true" />
            <span>套用</span>
          </button>
        </div>
      </div>
    </div>
  );
}

export function formatProductOptionLabel(product: ApiProductDto) {
  const code = normalizeText(product.productCode);
  const name = normalizeText(product.nameEN || product.nameCN);
  const hsCode = normalizeText(product.hsCode);
  return [code, name, hsCode].filter(Boolean).join(" / ") || `#${product.id}`;
}

function formatProductUnits(product: ApiProductDto) {
  const unit = [normalizeText(product.unitEN), normalizeText(product.unitCN)].filter(Boolean).join("/");
  const packageUnit = [normalizeText(product.packageUnitEN), normalizeText(product.packageUnitCN)].filter(Boolean).join("/");
  return [unit, packageUnit].filter(Boolean).join(" | ");
}

function filterProductLibraryProducts(products: ApiProductDto[], keyword: string) {
  const keywordValue = normalizeText(keyword).toLowerCase();
  if (!keywordValue) {
    return products;
  }

  return products.filter((product) =>
    [
      product.productCode,
      product.nameEN,
      product.nameCN,
      product.hsCode,
      product.material,
      product.brand,
      product.origin,
    ].some((value) => normalizeText(value).toLowerCase().includes(keywordValue)),
  );
}
