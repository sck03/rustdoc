use export_doc_domain::designer::Design;

pub(super) fn layer_header_bottom(design: &Design, height: f32) -> f32 {
    design
        .layers
        .iter()
        .filter(|layer| layer.role == "Header" && layer.visible)
        .fold(0., |bottom, layer| {
            let content = layer
                .elements
                .iter()
                .filter(|element| element.visible && element.output_enabled)
                .map(|element| (element.y_hundredth_mm + element.height_hundredth_mm) as f32 / 100.)
                .fold(0., f32::max);
            bottom
                .max(content)
                .max(layer.print.min_height_hundredth_mm as f32 / 100.)
                .min(height)
        })
}

pub(super) fn layer_footer_top(design: &Design, height: f32) -> f32 {
    footer_top_for(design, height, 0, true)
}

pub(super) fn footer_applies(
    layer: &export_doc_domain::designer::Layer,
    index: usize,
    last: bool,
) -> bool {
    if layer.print.first_page_only {
        index == 0
    } else {
        layer.print.repeat_on_every_page || last
    }
}

pub(super) fn footer_top_for(design: &Design, height: f32, index: usize, last: bool) -> f32 {
    let mut fixed_top = height;
    let mut following_height = 0_f32;
    for layer in design
        .layers
        .iter()
        .filter(|layer| layer.role == "Footer" && layer.visible)
        .filter(|layer| footer_applies(layer, index, last))
    {
        let minimum = layer.print.min_height_hundredth_mm as f32 / 100.;
        if layer.print.follow_body {
            following_height = following_height.max(footer_content_height(layer).max(minimum));
            continue;
        }
        let top = if layer.print.pin_to_page_bottom {
            height - footer_content_height(layer)
        } else {
            layer
                .elements
                .iter()
                .filter(|element| element.visible && element.output_enabled)
                .map(|element| element.y_hundredth_mm as f32 / 100.)
                .fold(height, f32::min)
        };
        fixed_top = fixed_top.min(top).min(height - minimum);
    }
    // A following band must fit before fixed page footers, not occupy their
    // reserved space. Multiple following layers share the same body anchor.
    (fixed_top - following_height).max(0.)
}

pub(super) fn footer_content_bottom(layer: &export_doc_domain::designer::Layer) -> f32 {
    let bottom = layer
        .elements
        .iter()
        .filter(|element| element.visible && element.output_enabled)
        .map(|element| (element.y_hundredth_mm + element.height_hundredth_mm) as f32 / 100.)
        .fold(0., f32::max);
    if bottom > 0. {
        bottom
    } else {
        layer.print.min_height_hundredth_mm as f32 / 100.
    }
}

pub(super) fn footer_content_height(layer: &export_doc_domain::designer::Layer) -> f32 {
    let top = layer
        .elements
        .iter()
        .filter(|element| element.visible && element.output_enabled)
        .map(|element| element.y_hundredth_mm as f32 / 100.)
        .fold(f32::INFINITY, f32::min);
    if top.is_finite() {
        footer_content_bottom(layer) - top
    } else {
        layer.print.min_height_hundredth_mm as f32 / 100.
    }
}
