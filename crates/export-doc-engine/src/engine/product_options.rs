use super::{
    auth,
    error::Result,
    records::text,
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
    let mut rows = vec![];
    for record in store.all("products")? {
        if !auth::visible(actor, PERMISSION, "view", &record) {
            continue;
        }
        if !keyword.is_empty()
            && ![
                text(&record, "productCode"),
                text(&record, "nameCN"),
                text(&record, "nameEN"),
            ]
            .iter()
            .any(|value| store::normalize(value).contains(&keyword))
        {
            continue;
        }
        rows.push(json!({
            "id": record["id"],
            "productCode": text(&record, "productCode"),
            "nameCN": text(&record, "nameCN"),
            "nameEN": text(&record, "nameEN"),
        }));
    }
    rows.sort_by(|left, right| {
        text(left, "productCode")
            .cmp(&text(right, "productCode"))
            .then_with(|| text(left, "nameCN").cmp(&text(right, "nameCN")))
            .then_with(|| left["id"].as_i64().cmp(&right["id"].as_i64()))
    });
    let total = rows.len();
    let pages = ((total + size - 1) / size).max(1);
    let start = (page - 1) * size;
    let items = rows
        .into_iter()
        .skip(start)
        .take(size)
        .collect::<Vec<Value>>();
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
        root: std::path::PathBuf,
        store: Store,
        actor: Actor,
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
                id: 1,
                name: "采购员".into(),
                company: "DEFAULT".into(),
                department: "GENERAL".into(),
                admin: false,
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
            Self { root, store, actor }
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
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
    }
}
