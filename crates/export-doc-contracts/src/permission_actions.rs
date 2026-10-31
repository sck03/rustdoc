//! CRUD intent aliases belong to the API permission contract, not to a host.
pub fn service_action<'a>(
    key: &str,
    requested: &'a str,
    has_action: impl Fn(&str) -> bool,
) -> &'a str {
    if key == "organization.directory" && matches!(requested, "create" | "edit" | "delete") {
        return "manage";
    }
    if has_action(requested) {
        return requested;
    }
    if has_action("operate") && has_action("manage") {
        match requested {
            "create" | "edit" => "operate",
            "delete" => "manage",
            _ => requested,
        }
    } else {
        requested
    }
}
