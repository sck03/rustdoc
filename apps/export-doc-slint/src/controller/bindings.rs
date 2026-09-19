use super::*;
pub fn bind(ui: &AppWindow, state: Rc<RefCell<Desktop>>) {
    macro_rules! bind {
        ($name:ident, |$s:ident $(,$arg:ident)*| $body:expr) => { bind!(App, $name, |$s $(,$arg)*| $body); };
        ($global:ty, $name:ident, |$s:ident $(,$arg:ident)*| $body:expr) => {{
            let state=state.clone();
            ui.global::<$global>().$name(move |$($arg),*| {
                match state.try_borrow_mut(){
                    Ok(mut $s)=>{ $body }
                    Err(_)=>{let state=state.clone();slint::Timer::single_shot(std::time::Duration::ZERO,move||{let mut $s=state.borrow_mut();$body});}
                }
            });
        }};
    }
    bind!(crate::Access, on_access_action, |s, action, value| s
        .access_action(&action, &value));
    bind!(crate::Access, on_access_field, |s, key, value| s
        .access_field(&key, &value));
    bind!(crate::Access, on_access_grant, |s, key, index| s
        .access_grant(&key, index));
    bind!(on_login, |s, username, password| s.start(Work::Login(
        username.to_string(),
        password.to_string()
    )));
    bind!(crate::Audit, on_action, |s, action| s.audit_action(&action));
    bind!(crate::Query, on_action, |s, action| s.query_action(&action));
    bind!(crate::InvoiceFiles, on_action, |s, action| s
        .invoice_file_action(&action));
    bind!(crate::PdfMerge, on_action, |s, action, index| s
        .pdf_merge_action(&action, index));
    bind!(crate::DocumentPackage, on_action, |s, action| s
        .document_package_action(&action));
    bind!(crate::Recovery, on_action, |s, action| s
        .recovery_action(&action));
    bind!(
        crate::DocumentPackage,
        on_template_toggled,
        |s, path, selected| s.document_package_toggle(&path, selected)
    );
    bind!(crate::Sales, on_action, |s, action, value| s
        .sales_action(&action, &value));
    bind!(crate::SingleWindow, on_action, |s, action, index| s
        .single_window_action(&action, index));
    bind!(crate::SingleWindow, on_edited, |s, key, value| s
        .single_window_edit(&key, &value, None));
    bind!(crate::SingleWindow, on_chosen, |s, key, index| s
        .single_window_edit(&key, "", Some(index)));
    bind!(crate::SingleWindow, on_toggled, |s, key| s
        .single_window_toggle(&key));
    bind!(crate::Ocr, on_action, |s, action, index| s
        .ocr_action(&action, index));
    bind!(crate::Mail, on_action, |s, action, index| s
        .mail_action(&action, index));
    bind!(crate::Mail, on_edited, |s, key, value| s
        .mail_edit(&key, &value));
    bind!(crate::Hs, on_action, |s, action, index| s
        .hs_action(&action, index));
    bind!(crate::Hs, on_edited, |s, key, value| s
        .hs_edit(&key, &value, None));
    bind!(crate::Hs, on_chosen, |s, key, index| s.hs_edit(
        &key,
        "",
        Some(index)
    ));
    bind!(crate::Packing, on_action, |s, action, index| s
        .packing_action(&action, index));
    bind!(crate::Packing, on_edited, |s, key, value| s
        .packing_edit(&key, &value, None));
    bind!(crate::Packing, on_selected, |s, key, index| s.packing_edit(
        &key,
        "",
        Some(index)
    ));
    bind!(crate::Sales, on_edited, |s, key, value| s
        .sales_edit(&key, &value, None));
    bind!(crate::Sales, on_selected, |s, key, index| s.sales_edit(
        &key,
        "",
        Some(index)
    ));
    bind!(crate::PartyFiles, on_action, |s, action, index| s
        .party_file_action(&action, index));
    bind!(crate::Crm, on_action, |s, action, index| s
        .crm_action(&action, index));
    bind!(on_logout, |s| s.logout());
    bind!(crate::Exchange, on_action, |s, action| s
        .exchange_action(&action));
    bind!(crate::License, on_action, |s, action| s
        .license_action(&action));
    bind!(crate::TemplateFiles, on_action, |s, action| s
        .template_file_action(&action));
    bind!(on_navigate, |s, key| s.navigate(&key));
    bind!(on_open_tab, |s, key| s.open_tab(&key));
    bind!(on_toggle_form_section, |s, key| s.toggle_form_section(&key));
    bind!(on_organization_action, |s, action, code| s
        .organization_action(&action, &code));
    bind!(on_personnel_action, |s, action, value| s
        .personnel_action(&action, &value));
    bind!(on_business_action, |s, action, value| s
        .business_action(&action, &value));
    bind!(on_worklist_open, |s, index| s.worklist_open(index));
    bind!(on_dashboard_action, |s, key| s.dashboard_action(&key));
    bind!(on_attachment_action, |s, action, value| s
        .attachment_action(&action, &value));
    bind!(on_organization_search, |s, keyword| {
        s.organization.keyword = keyword.to_string();
        s.sync_organization();
    });
    bind!(on_settings_category_selected, |s, key| s
        .settings_category(&key));
    bind!(on_refresh, |s| {
        s.workspace.page = 1;
        s.refresh();
    });
    bind!(on_change_page, |s, direction| s.change_page(direction));
    bind!(on_new_record, |s| s.new_record());
    bind!(on_select_record, |s, id| s.select_record(id as i64));
    bind!(on_open_record, |s, id| s.open_record(id as i64));
    bind!(on_delete_record, |s| s.delete_record());
    bind!(on_record_action, |s, key| s.record_action(&key));
    bind!(on_field_edited, |s, key, value| s.field_edit(&key, &value));
    bind!(on_field_selected, |s, key, index| s
        .field_select(&key, index.max(0) as usize));
    bind!(on_array_action, |s, key, action| s
        .array_action(&key, &action));
    bind!(on_save_form, |s| s.save_form());
    bind!(on_cancel_form, |s| s.cancel_form());
    bind!(on_confirm, |s, yes| s.confirmation(yes));
    bind!(on_invoice_action, |s, action| s.invoice_action(&action));
    bind!(on_payment_action, |s, action| s.payment_action(&action));
    bind!(on_payment_field_edited, |s, key, value| s
        .payment_field_edit(&key, &value));
    bind!(on_payment_field_selected, |s, key, index| s
        .payment_field_select(&key, index.max(0) as usize));
    bind!(on_payment_tab_changed, |s, index| s.payment_tab(index));
    bind!(on_office_action, |s, action, id| s
        .office_action(&action, id));
    bind!(on_excel_action, |s, action| s.excel_action(&action));
    bind!(on_maintenance_action, |s, action| s
        .maintenance_action(&action));
    bind!(on_import_confirmed, |s, accepted| s.finish_import(accepted));
    bind!(on_file_action, |s, action| s.file_action(&action));
    bind!(on_cancel_operation, |s| s.cancel_operation());
    bind!(on_invoice_field_edited, |s, key, value| s
        .invoice_field_edit(&key, &value));
    bind!(on_invoice_field_selected, |s, key, index| s
        .invoice_field_select(&key, index.max(0) as usize));
    bind!(on_invoice_tab_changed, |s, index| s.invoice_tab(index));
    bind!(on_grid_scroll, |s, first, count| s
        .grid_scroll(first.max(0) as usize, count.max(1) as usize));
    bind!(on_grid_select, |s, row, column, extend| s.grid_select(
        row.max(0) as usize,
        column.max(0) as usize,
        extend
    ));
    bind!(on_grid_commit, |s, value| {
        if let Some(ui) = s.ui.upgrade() {
            ui.global::<App>().set_cell_value(value);
        }
        if let Err(error) = s.commit_cell() {
            s.error(error);
        }
    });
    bind!(on_grid_command, |s, command, shift| s
        .grid_command(&command, shift));
    bind!(on_grid_row_action, |s, action, row| s
        .grid_row_action(&action, row.max(0) as usize));
    bind!(on_column_visible, |s, column, visible| s
        .column_visible(column.max(0) as usize, visible));
    bind!(on_product_action, |s, action| s.product_action(&action));
    bind!(on_pdf_action, |s, action| s.pdf_action(&action));
    bind!(on_designer_action, |s, action| s.designer_action(&action));
    bind!(on_designer_select, |s, id| s.designer_select(&id));
    bind!(on_designer_move, |s, id, dx, dy, finished| if finished {
        s.designer_move(&id, dx, dy);
    });
    bind!(on_designer_field, |s, key, value| s
        .designer_field(&key, &value));
    let state = state.clone();
    ui.window().on_close_requested(move || {
        state.borrow_mut().close();
        slint::CloseRequestResponse::KeepWindowShown
    });
}
