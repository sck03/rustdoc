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
    design
        .layers
        .iter()
        .filter(|layer| layer.role == "Footer" && layer.visible)
        .filter(|layer| footer_applies(layer, index, last))
        .fold(height, |top, layer| {
            let reserved = if layer.print.pin_to_page_bottom || layer.print.follow_body {
                footer_content_height(layer)
            } else {
                layer
                    .elements
                    .iter()
                    .filter(|element| element.visible && element.output_enabled)
                    .map(|element| element.y_hundredth_mm as f32 / 100.)
                    .fold(height, f32::min)
                    .min(height)
            };
            top.min((height - layer.print.min_height_hundredth_mm as f32 / 100.).max(0.))
                .min(
                    if layer.print.pin_to_page_bottom || layer.print.follow_body {
                        (height - reserved).max(0.)
                    } else {
                        reserved
                    },
                )
        })
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
