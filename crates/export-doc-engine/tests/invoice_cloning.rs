#[path = "support/invoice_clone_contract.rs"]
mod invoice_clone_contract;
#[path = "support/native_fixture.rs"]
mod native_fixture;

#[test]
fn invoice_copies_follow_the_contract_and_fail_atomically() {
    let fixture = native_fixture::Fixture::new();
    invoice_clone_contract::exercise(&|op, path, query, body| {
        fixture.client().json(op, path, query, body)
    });
}
