#[path = "support/organization_fixture.rs"]
mod organization_fixture;
#[path = "support/organization_query.rs"]
mod organization_query;
#[path = "support/workspace.rs"]
mod test_workspace;

use export_doc_storage::Connection;

#[test]
fn sqlite_organization_references_follow_actual_ownership() {
    let root = test_workspace::create("organization-reference-tests");
    {
        let connection = Connection::sqlite(&root.join("data.db")).unwrap();
        organization_query::exercise(&connection);
    }
    test_workspace::remove(&root).unwrap();
}

#[cfg(feature = "postgres")]
#[test]
#[ignore = "requires isolated PostgreSQL 18 maintenance/app roles"]
fn postgres_organization_references_follow_actual_ownership() {
    let maintenance = std::env::var("EXPORTDOC_TEST_POSTGRES_MAINTENANCE").unwrap();
    let app = std::env::var("EXPORTDOC_TEST_POSTGRES_APP").unwrap();
    Connection::initialize_postgres(&maintenance, "native_owner").unwrap();
    let connection = Connection::postgres(&app).unwrap();
    organization_query::exercise(&connection);
}
