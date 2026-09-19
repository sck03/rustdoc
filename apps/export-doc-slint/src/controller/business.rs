use super::*;
use crate::{PageTab, business_model::BusinessModel, form_sections};
use export_doc_engine::{contracts, engine::catalog, workspace};
use serde_json::Value;

impl Desktop {
    pub fn open_business(&mut self, value: Value) {
        let resource = if self.workspace.root == "crm-customers" {
            "crm-customers"
        } else {
            "suppliers"
        };
        if value["id"].as_i64().is_none_or(|id| id <= 0) {
            self.error("业务资料没有有效编号。");
            return;
        }
        let changed = self
            .business
            .as_ref()
            .is_none_or(|current| current.id() != value["id"].as_i64().unwrap());
        self.business = Some(BusinessModel {
            resource,
            record: value,
        });
        self.cancel_form();
        if changed {
            self.workspace.resource = resource;
        }
        if let Some(ui) = self.ui.upgrade() {
            let app = ui.global::<App>();
            app.set_page("business".into());
            app.set_tabs(model(vec![]));
        }
        self.sync_business();
        if self.workspace.resource != resource {
            self.refresh();
        }
    }
    pub fn sync_business(&mut self) {
        let (Some(business), Some(ui)) = (&self.business, self.ui.upgrade()) else {
            return;
        };
        let app = ui.global::<App>();
        app.set_business_title(workspace::display(&business.record["name"]).into());
        app.set_business_summary(
            format!(
                "{} · {}",
                workspace::display(&business.record["countryRegion"]),
                workspace::display(&business.record["status"])
            )
            .into(),
        );
        app.set_business_resource(business.resource.into());
        app.set_resource(self.workspace.resource.into());
        app.set_business_tabs(model(
            business
                .tabs()
                .into_iter()
                .filter(|(key, _)| {
                    workspace::read_operation(key).is_some_and(|operation| self.allowed(operation))
                })
                .map(|(key, label)| PageTab {
                    key: key.into(),
                    label: label.into(),
                })
                .collect(),
        ));
        let resource = catalog::resource(business.resource).unwrap();
        let form = FormModel::new(
            contracts::schema(resource.schema),
            business.record.clone(),
            self.lookups.clone(),
        );
        app.set_business_sections(model(form_sections::complete(
            &form,
            business.resource,
            &mut self.disclosure_state,
        )));
        app.set_business_can_edit(self.can(resource.update));
        app.set_business_can_delete(resource.delete.is_some_and(|operation| self.can(operation)));
        app.set_business_actions(model(
            workspace::actions(business.resource, &business.record)
                .into_iter()
                .filter(|(operation, _)| self.can(*operation))
                .map(|(operation, label)| PageTab {
                    key: operation.id.into(),
                    label: label.into(),
                })
                .collect(),
        ));
        if self.workspace.resource == business.resource {
            self.workspace.selected = Some(business.record.clone());
        }
    }
    pub fn business_action(&mut self, action: &str, value: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(business) = &self.business else {
            return;
        };
        match action {
            "back" => {
                let resource = business.resource;
                self.navigate_now(resource);
            }
            "tab" => {
                let Some((key, _)) = business.tabs().into_iter().find(|(key, _)| *key == value)
                else {
                    return;
                };
                if !workspace::read_operation(key).is_some_and(|operation| self.allowed(operation))
                {
                    self.error("当前账号没有此业务资料的权限。");
                    return;
                }
                self.workspace.resource = key;
                self.workspace.selected = None;
                self.workspace.payload = None;
                self.workspace.page = 1;
                if let Some(ui) = self.ui.upgrade() {
                    let app = ui.global::<App>();
                    app.set_search("".into());
                    app.set_records(model(vec![]));
                    app.set_actions(model(vec![]));
                    app.set_resource(key.into());
                }
                self.sync_business();
                if key != self.business.as_ref().unwrap().resource {
                    self.refresh();
                }
            }
            "edit" => {
                let record = business.record.clone();
                self.edit_record(Some(record));
            }
            "delete" => {
                self.workspace.selected = Some(business.record.clone());
                self.delete_record();
            }
            _ => {
                self.workspace.selected = Some(business.record.clone());
                self.record_action(action);
            }
        }
    }
    pub fn refresh_business_profile(&mut self) -> bool {
        if let Some(business) = &self.business {
            if self.workspace.resource == business.resource {
                let operation = catalog::resource(business.resource).unwrap().get.unwrap();
                self.request(operation, business.id(), vec![], None, "business-detail");
                return true;
            }
        }
        false
    }
}
