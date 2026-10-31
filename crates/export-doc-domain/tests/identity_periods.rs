use export_doc_domain::identity::{Assignment, EffectivePeriod, StableId, assignments};
#[test]
fn stable_ids_preserve_integer_precision_and_reject_ambiguous_encodings() {
    let id: StableId = serde_json::from_str("\"9007199254740993\"").unwrap();
    assert_eq!(id.number(), 9007199254740993);
    assert_eq!(serde_json::to_string(&id).unwrap(), "\"9007199254740993\"");
    for value in ["0", "01", "-1", "1e3", "9223372036854775808", ""] {
        assert!(StableId::parse(value).is_err());
    }
    assert!(serde_json::from_str::<StableId>("9007199254740993").is_err());
}
#[test]
fn adjacent_employment_periods_do_not_overlap_and_end_is_exclusive() {
    let first = EffectivePeriod {
        starts_on: "2026-01-01".into(),
        ends_on: Some("2026-10-31".into()),
    };
    let next = EffectivePeriod {
        starts_on: "2026-10-31".into(),
        ends_on: None,
    };
    let date = export_doc_domain::oa::date("2026-10-31").unwrap();
    assert!(!first.contains(date).unwrap());
    assert!(next.contains(date).unwrap());
    assert!(!first.overlaps(&next).unwrap());
    for end in ["2026-01-01", "2025-12-31", "2026-02-30", "+2026-10-31"] {
        assert!(
            EffectivePeriod {
                starts_on: "2026-01-01".into(),
                ends_on: Some(end.into())
            }
            .bounds()
            .is_err()
        );
    }
}
#[test]
fn duty_membership_changes_cannot_duplicate_effective_responsibility() {
    let first = Assignment {
        id: None,
        appointment_id: "appointment-a".into(),
        project_id: None,
        role: String::new(),
        period: EffectivePeriod {
            starts_on: "2026-01-01".into(),
            ends_on: Some("2026-06-01".into()),
        },
    };
    let mut second = first.clone();
    second.period = EffectivePeriod {
        starts_on: "2026-06-01".into(),
        ends_on: None,
    };
    assignments(&[first.clone(), second.clone()]).unwrap();
    second.period.starts_on = "2026-05-31".into();
    assert!(assignments(&[first, second]).is_err());
}
