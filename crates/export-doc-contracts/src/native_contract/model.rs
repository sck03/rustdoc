//! Typed declarations for the single native contract; serialization is a projection.
use serde::Serialize;
use serde_json::{Number, Value};
use std::collections::BTreeMap;

#[derive(Clone, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Primitive {
    String,
    Integer,
    Number,
    Boolean,
    Object,
    Array,
    Null,
}
#[derive(Clone, Serialize)]
#[serde(untagged)]
pub enum Types {
    One(Primitive),
    Many(Vec<Primitive>),
}
#[derive(Clone, Serialize)]
#[serde(untagged)]
pub enum Additional {
    Allowed(bool),
    Schema(Box<Schema>),
}
#[derive(Clone, Serialize)]
#[serde(untagged)]
pub enum Enumeration {
    Values(Vec<Value>),
    Null,
}

macro_rules! schema_fields {
    ($($field:ident: $kind:ty => $name:literal),* $(,)?) => {
        #[derive(Clone, Default, Serialize)]
        pub struct SchemaFields { $(#[serde(rename=$name, skip_serializing_if="Option::is_none")] pub $field: Option<$kind>,)* }
        #[derive(Clone, Serialize)]
        #[serde(untagged)]
        pub enum Schema { Definition(Box<SchemaFields>), Unspecified }
        impl Default for Schema { fn default() -> Self { Self::Definition(Box::new(SchemaFields::default())) } }
        impl Schema { $(pub fn $field(mut self, value: $kind) -> Self { if let Self::Definition(ref mut fields)=self { fields.$field=Some(value); } self })* }
    };
}
schema_fields! {
    reference: String => "$ref", kind: Types => "type",
    properties: BTreeMap<&'static str, Schema> => "properties",
    required: Vec<&'static str> => "required", items: Box<Schema> => "items",
    additional: Additional => "additionalProperties", one_of: Vec<Schema> => "oneOf",
    any_of: Vec<Schema> => "anyOf",
    format: &'static str => "format", pattern: &'static str => "pattern",
    description: &'static str => "description", title: &'static str => "title",
    default_value: Value => "default", constant: Value => "const", enumeration: Enumeration => "enum",
    minimum: Number => "minimum", maximum: Number => "maximum",
    min_length: u64 => "minLength", max_length: u64 => "maxLength",
    min_items: u64 => "minItems", max_items: u64 => "maxItems",
    max_properties: u64 => "maxProperties", unique_items: bool => "uniqueItems",
    read_only: bool => "readOnly"
}
pub fn ty(kind: Primitive) -> Schema {
    Schema::default().kind(Types::One(kind))
}
pub fn s() -> Schema {
    ty(Primitive::String)
}
pub fn i() -> Schema {
    ty(Primitive::Integer)
}
pub fn b() -> Schema {
    ty(Primitive::Boolean)
}
pub fn o(properties: Vec<(&'static str, Schema)>) -> Schema {
    ty(Primitive::Object).properties(properties.into_iter().collect())
}
/// Fields with identical validation share one definition without sharing mutable state.
pub fn object_groups(groups: Vec<(&[&'static str], Schema)>) -> Schema {
    o(groups
        .into_iter()
        .flat_map(|(names, schema)| names.iter().map(move |name| (*name, schema.clone())))
        .collect())
}
pub fn a(item: Schema) -> Schema {
    ty(Primitive::Array).items(Box::new(item))
}
pub fn r(name: &str) -> Schema {
    Schema::default().reference(format!("#/components/schemas/{name}"))
}
pub fn num(text: &str) -> Number {
    text.parse().expect("native decimal bound")
}
pub fn j_object(values: Vec<(&'static str, Value)>) -> Value {
    Value::Object(
        values
            .into_iter()
            .map(|(key, value)| (key.to_owned(), value))
            .collect(),
    )
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct RoleGrant {
    resource_key: &'static str,
    action: &'static str,
    data_scope: &'static str,
}
#[derive(Serialize)]
struct Role {
    code: &'static str,
    name: &'static str,
    description: &'static str,
    grants: Vec<RoleGrant>,
}
pub fn role(
    code: &'static str,
    name: &'static str,
    description: &'static str,
    groups: &[(&[&'static str], &[&'static str], &'static str)],
) -> Value {
    let grants = groups
        .iter()
        .flat_map(|(resources, actions, scope)| {
            resources.iter().flat_map(move |resource| {
                actions.iter().map(move |action| RoleGrant {
                    resource_key: resource,
                    action,
                    data_scope: scope,
                })
            })
        })
        .collect();
    serde_json::to_value(Role {
        code,
        name,
        description,
        grants,
    })
    .expect("native role grants")
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LegacyPermissions {
    pub read_module: Option<&'static str>,
    pub write_module: Option<&'static str>,
    pub read_access_level: Option<&'static str>,
    pub write_access_level: Option<&'static str>,
    pub selector: i64,
    pub disabled: bool,
    pub overrides_parent: bool,
}
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Requirement {
    pub resource_key: &'static str,
    pub action: &'static str,
}
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AccessResource {
    pub resource_key: &'static str,
    pub actions: Vec<&'static str>,
    pub administrator_only: bool,
}
#[derive(Clone, Serialize)]
#[serde(untagged)]
pub enum Access {
    Rule {
        r#match: &'static str,
        resources: Vec<AccessResource>,
    },
    Bypass {
        bypass: bool,
    },
}
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Policy {
    pub requires_authentication: bool,
    pub requires_desktop_access: bool,
    pub requires_license: bool,
    pub permissions: Option<LegacyPermissions>,
    pub requirements: Vec<Requirement>,
    pub permission_bypass: bool,
    pub access: Access,
}
#[derive(Clone, Serialize)]
pub struct Media {
    pub schema: Schema,
}
pub type Content = BTreeMap<&'static str, Media>;
#[derive(Clone, Serialize)]
pub struct Response {
    pub description: &'static str,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub content: Option<Content>,
}
pub type Responses = BTreeMap<&'static str, Response>;
#[derive(Clone, Serialize)]
pub struct Parameter {
    pub name: &'static str,
    #[serde(rename = "in")]
    pub location: &'static str,
    pub schema: Schema,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub required: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub description: Option<&'static str>,
}
pub fn p(
    name: &'static str,
    location: &'static str,
    required: Option<bool>,
    schema: Schema,
) -> Parameter {
    Parameter {
        name,
        location,
        required,
        schema,
        description: None,
    }
}
impl Parameter {
    pub fn description(mut self, value: &'static str) -> Self {
        self.description = Some(value);
        self
    }
}
#[derive(Clone, Serialize)]
pub struct RequestBody {
    pub required: bool,
    pub content: Content,
    #[serde(
        rename = "x-exportdoc-max-bytes",
        skip_serializing_if = "Option::is_none"
    )]
    pub max_bytes: Option<usize>,
}
#[derive(Clone, Serialize)]
pub struct Capability {
    pub kind: &'static str,
    pub action: &'static str,
    pub resource: &'static str,
    pub permission: &'static str,
}
#[derive(Clone, Serialize)]
pub struct Foundation {
    pub kind: &'static str,
    pub action: &'static str,
}
#[derive(Clone, Serialize)]
pub struct Operation {
    #[serde(rename = "x-exportdoc-timeout-seconds")]
    pub timeout_seconds: u64,
    #[serde(rename = "x-exportdoc-processing-slot")]
    pub processing_slot: bool,
    pub tags: Vec<&'static str>,
    #[serde(rename = "operationId")]
    pub id: &'static str,
    pub responses: Responses,
    #[serde(rename = "x-exportdoc-policy")]
    pub policy: Policy,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub security: Option<Vec<BTreeMap<&'static str, Vec<&'static str>>>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub parameters: Option<Vec<Parameter>>,
    #[serde(rename = "requestBody", skip_serializing_if = "Option::is_none")]
    pub body: Option<RequestBody>,
    #[serde(rename = "x-exportdoc-office", skip_serializing_if = "Option::is_none")]
    pub office: Option<Capability>,
    #[serde(
        rename = "x-exportdoc-communication",
        skip_serializing_if = "Option::is_none"
    )]
    pub communication: Option<Capability>,
    #[serde(
        rename = "x-exportdoc-foundation",
        skip_serializing_if = "Option::is_none"
    )]
    pub foundation: Option<Foundation>,
}
pub struct Endpoint {
    pub method: &'static str,
    pub path: &'static str,
    pub operation: Operation,
}
pub fn ep(
    method: &'static str,
    path: &'static str,
    id: &'static str,
    responses: Responses,
    policy: Policy,
) -> Endpoint {
    Endpoint {
        method,
        path,
        operation: Operation {
            timeout_seconds: crate::request_limits::seconds(id),
            processing_slot: matches!(
                id,
                "RecognizeOcrImage"
                    | "SaveShippingMarkImage"
                    | "SaveInvoiceSealImage"
                    | "PreviewShippingMarkImage"
            ),
            tags: vec!["ExportDocManager.Api"],
            id,
            responses,
            policy,
            security: None,
            parameters: None,
            body: None,
            office: None,
            communication: None,
            foundation: None,
        },
    }
}
impl Endpoint {
    pub fn tags(mut self, values: Vec<&'static str>) -> Self {
        self.operation.tags = values;
        self
    }
    pub fn security(mut self, value: Vec<BTreeMap<&'static str, Vec<&'static str>>>) -> Self {
        self.operation.security = Some(value);
        self
    }
    pub fn parameters(mut self, value: Vec<Parameter>) -> Self {
        self.operation.parameters = Some(value);
        self
    }
    pub fn body(mut self, required: bool, content: Content) -> Self {
        self.operation.processing_slot |= content.keys().any(|mime| !mime.contains("json"));
        self.operation.body = Some(RequestBody {
            required,
            content,
            max_bytes: None,
        });
        self
    }
    pub fn body_limit(mut self, bytes: usize) -> Self {
        self.operation
            .body
            .as_mut()
            .expect("body limit requires a body")
            .max_bytes = Some(bytes);
        self
    }
    pub fn office(
        mut self,
        kind: &'static str,
        action: &'static str,
        resource: &'static str,
        permission: &'static str,
    ) -> Self {
        self.operation.office = Some(Capability {
            kind,
            action,
            resource,
            permission,
        });
        self
    }
    pub fn communication(
        mut self,
        kind: &'static str,
        action: &'static str,
        resource: &'static str,
        permission: &'static str,
    ) -> Self {
        self.operation.communication = Some(Capability {
            kind,
            action,
            resource,
            permission,
        });
        self
    }
    pub fn foundation(mut self, kind: &'static str, action: &'static str) -> Self {
        self.operation.foundation = Some(Foundation { kind, action });
        self
    }
}

/// The common bearer/desktop JSON boundary. Exceptional media/security stays explicit in `ep`.
pub fn route(
    method: &'static str,
    path: &'static str,
    id: &'static str,
    responses: Responses,
    policy: Policy,
    parameters: Option<Vec<Parameter>>,
    body: Option<Schema>,
) -> Endpoint {
    let mut endpoint = ep(method, path, id, responses, policy).security(vec![BTreeMap::from([
        ("BearerAuth", vec![]),
        ("DesktopAccess", vec![]),
    ])]);
    endpoint.operation.parameters = parameters;
    endpoint.operation.body = body.map(|schema| RequestBody {
        required: true,
        content: BTreeMap::from([("application/json", Media { schema })]),
        max_bytes: None,
    });
    endpoint
}
