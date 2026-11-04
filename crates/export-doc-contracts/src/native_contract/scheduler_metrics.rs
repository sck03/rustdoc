//! Both schedule owners publish the same health snapshot.
use super::model::*;

fn fields() -> Vec<(&'static str, Schema)> {
    vec![
        ("ticks", i().minimum(num("0")).format("int64")),
        (
            "lastErrorStatus",
            Schema::default()
                .kind(Types::Many(vec![Primitive::Integer, Primitive::Null]))
                .minimum(num("100"))
                .maximum(num("599")),
        ),
        (
            "lastSuccessAt",
            Schema::default()
                .kind(Types::Many(vec![Primitive::String, Primitive::Null]))
                .format("date-time"),
        ),
        ("panics", i().minimum(num("0")).format("int64")),
        ("consecutivePanics", i().minimum(num("0")).format("int64")),
        ("blocked", b()),
        ("threadAlive", b()),
    ]
}
fn health(properties: Vec<(&'static str, Schema)>) -> Schema {
    o(properties)
        .required(vec!["ticks", "lastErrorStatus"])
        .additional(Additional::Allowed(false))
}
pub(super) fn workflow() -> Schema {
    let mut properties = fields();
    properties.push(("exchange", health(fields())));
    health(properties)
}
