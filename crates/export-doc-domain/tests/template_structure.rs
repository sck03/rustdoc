use export_doc_domain::{
    designer::{Design, ImageResource},
    template,
};

fn blank() -> Design {
    let mut d = Design::invoice();
    for layer in &mut d.layers {
        layer.elements.clear();
    }
    d
}

#[test]
fn structural_limits_and_resource_identity_are_enforced_without_a_browser() {
    let base = blank();
    assert!(template::validate(&base, &[]).is_ok());
    let mut d = base.clone();
    d.layers.clear();
    assert!(template::validate(&d, &[]).is_err());
    let mut d = base.clone();
    d.layers.retain(|layer| layer.role != "Body");
    assert!(template::validate(&d, &[]).is_err());
    let mut d = base.clone();
    d.layers[1].id = d.layers[0].id.clone();
    assert!(template::validate(&d, &[]).is_err());
    let mut d = base.clone();
    d.layers = (0..17)
        .map(|n| {
            let mut layer = base.layers[1].clone();
            layer.id = format!("layer-{n}");
            layer
        })
        .collect();
    assert!(template::validate(&d, &[]).is_err());
    let mut d = base.clone();
    let element = Design::invoice().layers[0].elements[0].clone();
    d.layers[1].elements = (0..1001)
        .map(|n| {
            let mut e = element.clone();
            e.id = format!("element-{n}");
            e
        })
        .collect();
    assert!(template::validate(&d, &[]).is_err());
    let mut d = base.clone();
    d.layers = (0..5)
        .map(|n| {
            let mut layer = base.layers[1].clone();
            layer.id = format!("layer-{n}");
            layer.elements = (0..801)
                .map(|m| {
                    let mut e = element.clone();
                    e.id = format!("element-{n}-{m}");
                    e
                })
                .collect();
            layer
        })
        .collect();
    assert!(template::validate(&d, &[]).is_err());
    let sha = "a".repeat(64);
    let resource = ImageResource {
        id: format!("img-{sha}.png"),
        sha256: sha,
        media_type: "image/png".into(),
        byte_length: 8,
        alt_text: "样例".into(),
    };
    let mut d = base.clone();
    d.resources.push(resource.clone());
    assert!(template::validate(&d, &[]).is_ok());
    d.resources.push(resource.clone());
    assert!(template::validate(&d, &[]).is_err());
    for (media, extension, bytes) in [
        ("image/gif", "gif", 8),
        ("image/webp", "webp", 8),
        ("image/png", "jpg", 8),
        ("image/png", "png", 0),
        ("image/png", "png", 33 * 1024 * 1024),
    ] {
        let mut d = base.clone();
        let mut r = resource.clone();
        r.media_type = media.into();
        r.id = format!("img-{}.{}", r.sha256, extension);
        r.byte_length = bytes;
        d.resources.push(r);
        assert!(
            template::validate(&d, &[]).is_err(),
            "{media}/{extension}/{bytes}"
        );
    }
    let mut d = base;
    let mut e = element;
    e.id = d.layers[0].id.clone();
    d.layers[0].elements.push(e);
    assert!(template::validate(&d, &[]).is_err());
}
