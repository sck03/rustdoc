use super::{
    auth,
    error::Result,
    store::{self, Actor, Store},
};
use crate::generated_api::*;
use serde_json::{Value, json};

pub const OPERATIONS: &[Operation] = &[SEARCH_SUPPLIER_PRODUCT_OPTIONS];
const PERMISSION: &str = "document.master-data";
const MAX_PAGE: usize = 100;

fn number(query: &[(&str, String)], name: &str, default: usize) -> usize {
    query
        .iter()
        .find(|(key, _)| *key == name)
        .and_then(|(_, value)| value.parse::<usize>().ok())
        .filter(|number| *number >= 1)
        .unwrap_or(default)
}

pub fn search(store: &Store, actor: &Actor, query: &[(&str, String)]) -> Result<Value> {
    auth::authorize(actor, PERMISSION, "view")?;
    let keyword = store::normalize(
        query
            .iter()
            .find(|(key, _)| *key == "keyword")
            .map(|(_, value)| value.as_str())
            .unwrap_or(""),
    );
    let page = number(query, "pageNumber", 1);
    let size = number(query, "pageSize", 20).min(MAX_PAGE);
    let start = i64::try_from(page.saturating_sub(1).saturating_mul(size)).unwrap_or(i64::MAX);
    let (total, items) =
        store
            .connection()?
            .query_business(&export_doc_storage::BusinessQuery {
                list: export_doc_storage::BusinessList::Products { keyword: &keyword },
                scope: auth::data_scope(actor, PERMISSION, "view"),
                fields: &[
                    ("id", "id"),
                    ("productCode", "productCode"),
                    ("nameCN", "nameCN"),
                    ("nameEN", "nameEN"),
                ],
                offset: start,
                limit: size as i64,
            })?;
    let total = total as usize;
    let pages = ((total + size - 1) / size).max(1);
    Ok(json!({
        "items": items,
        "totalCount": total,
        "pageNumber": page,
        "pageSize": size,
        "totalPages": pages,
        "hasPreviousPage": page > 1,
        "hasNextPage": page < pages,
    }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{
        contracts,
        paths::{RuntimePaths, nonce},
    };
    use std::fs;

    struct Fixture {
        store: Store,
        actor: Actor,
        _root: crate::test_support::ScratchDirectory,
    }
    impl Fixture {
        fn new() -> Self {
            let workspace = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .parent()
                .unwrap()
                .parent()
                .unwrap()
                .to_path_buf();
            let root = workspace
                .join(".codex-runtime/product-option-tests")
                .join(nonce().unwrap());
            fs::create_dir_all(&root).unwrap();
            let paths = RuntimePaths::server(&root, &root.join("Data")).unwrap();
            let store = Store::open(&paths).unwrap();
            let actor = Actor {
                edition: Default::default(),
                id: 1,
                name: "采购员".into(),
                company: "DEFAULT".into(),
                department: "GENERAL".into(),
                admin: false,
                departments: vec![],
                oa_assigned_only: false,
                grants: vec![
                    json!({"resourceKey":"document.master-data","action":"view","dataScope":"all"}),
                ],
            };
            let mut user = contracts::initial(contracts::schema("ApiUserDto"));
            user["username"] = json!("buyer");
            user["status"] = json!("Active");
            store::save(
                &*store.connection().unwrap(),
                "users",
                0,
                user,
                None,
                &actor,
                "create",
            )
            .unwrap();
            for (code, cn, en) in [
                ("S-001", "棉布", "Cotton Cloth"),
                ("S-002", "涤纶布", "Polyester Cloth"),
                ("A-010", "纽扣", "Button"),
            ] {
                let mut product = contracts::initial(contracts::schema("ApiProductDto"));
                product["productCode"] = json!(code);
                product["nameCN"] = json!(cn);
                product["nameEN"] = json!(en);
                store::save(
                    &*store.connection().unwrap(),
                    "products",
                    0,
                    product,
                    None,
                    &actor,
                    "create",
                )
                .unwrap();
            }
            Self {
                store,
                actor,
                _root: crate::test_support::ScratchDirectory(root),
            }
        }
    }

    #[test]
    fn keyword_and_paging_match_the_original_supplier_product_options() {
        let fixture = Fixture::new();
        let all = search(&fixture.store, &fixture.actor, &[]).unwrap();
        assert_eq!(all["totalCount"], 3);
        let cloth = search(
            &fixture.store,
            &fixture.actor,
            &[("keyword".into(), "布".into())],
        )
        .unwrap();
        assert_eq!(cloth["totalCount"], 2);
        assert_eq!(cloth["items"].as_array().unwrap().len(), 2);
        let english = search(
            &fixture.store,
            &fixture.actor,
            &[("keyword".into(), "button".into())],
        )
        .unwrap();
        assert_eq!(english["totalCount"], 1);
        let page = search(
            &fixture.store,
            &fixture.actor,
            &[
                ("pageNumber".into(), "1".into()),
                ("pageSize".into(), "2".into()),
            ],
        )
        .unwrap();
        assert_eq!(page["pageSize"], 2);
        assert_eq!(page["totalPages"], 2);
        assert_eq!(page["hasNextPage"], true);
        assert!(page["items"].as_array().unwrap().len() <= 2);
        let extreme = search(
            &fixture.store,
            &fixture.actor,
            &[
                ("pageNumber", usize::MAX.to_string()),
                ("pageSize", "100".into()),
            ],
        )
        .unwrap();
        assert_eq!(extreme["totalCount"], 3);
        assert!(extreme["items"].as_array().unwrap().is_empty());
    }
}
