use anyhow::{anyhow, bail, Context, Result};
use std::{fs, path::Path};

pub(crate) fn decode_ctc(
    data: Vec<f32>,
    shape: &[usize],
    labels: &[String],
) -> Result<(String, f32)> {
    if shape.len() != 2 && shape.len() != 3 {
        bail!("invalid recognition output shape")
    }
    if shape.len() == 3 && shape[0] != 1 {
        bail!("recognition output batch size must be one")
    }
    let sequence_length = if shape.len() == 3 { shape[1] } else { shape[0] };
    let class_count = shape[shape.len() - 1];
    if sequence_length == 0 || class_count == 0 {
        bail!("recognition output dimensions must be non-zero")
    }
    let required_length = sequence_length
        .checked_mul(class_count)
        .context("recognition output dimensions overflow")?;
    if data.len() != required_length {
        bail!(
            "recognition output data length mismatch: expected {required_length}, got {}",
            data.len()
        )
    }
    if class_count != labels.len()
        && class_count != labels.len() + 1
        && class_count != labels.len() + 2
    {
        bail!(
            "unsupported recognition output class count: expected {}, {} or {}, got {class_count}",
            labels.len(),
            labels.len() + 1,
            labels.len() + 2
        )
    }
    if data.iter().any(|value| !value.is_finite()) {
        bail!("recognition output contains a non-finite score")
    }

    let has_blank_class = class_count == labels.len() + 1 || class_count == labels.len() + 2;
    let (mut previous, mut text, mut confidence_sum, mut accepted_count) =
        (usize::MAX, String::new(), 0f32, 0usize);
    for time_step in 0..sequence_length {
        let row = &data[time_step * class_count..(time_step + 1) * class_count];
        let (index, score) = row
            .iter()
            .copied()
            .enumerate()
            .max_by(|left, right| left.1.total_cmp(&right.1))
            .ok_or_else(|| anyhow!("recognition output row is empty"))?;
        if index == previous {
            continue;
        }
        previous = index;
        if has_blank_class && index == 0 {
            continue;
        }
        let label_index = if has_blank_class { index - 1 } else { index };
        if label_index == labels.len() && class_count == labels.len() + 2 {
            text.push(' ');
            confidence_sum += score;
            accepted_count += 1;
        } else if let Some(label) = labels.get(label_index) {
            text.push_str(label);
            confidence_sum += score;
            accepted_count += 1;
        }
    }
    let confidence = if accepted_count == 0 {
        0.
    } else {
        confidence_sum / accepted_count as f32
    };
    Ok((text, confidence))
}

pub(crate) fn load_labels(path: &Path) -> Result<Vec<String>> {
    let text = fs::read_to_string(path)?;
    let mut in_dictionary = false;
    let mut labels = vec![];
    for raw in text.lines() {
        let trimmed = raw.trim();
        if !in_dictionary {
            in_dictionary = trimmed == "character_dict:";
            continue;
        }
        let leading_trimmed = raw.trim_start();
        if !leading_trimmed.starts_with('-') {
            if !leading_trimmed.is_empty() && !leading_trimmed.starts_with('#') {
                break;
            }
            continue;
        }
        let mut label = leading_trimmed[1..].trim().to_string();
        if label.len() >= 2
            && ((label.starts_with('\'') && label.ends_with('\''))
                || (label.starts_with('"') && label.ends_with('"')))
        {
            label = label[1..label.len() - 1].to_string();
        }
        labels.push(label);
    }
    if labels.is_empty() {
        bail!("character_dict is missing in {}", path.display())
    }
    Ok(labels)
}

pub(crate) fn text_quality(value: &str) -> usize {
    value
        .chars()
        .filter(|character| {
            character.is_alphanumeric() || ('\u{4e00}'..='\u{9fff}').contains(character)
        })
        .count()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decode_ctc_collapses_duplicates_and_maps_extra_class_to_space() {
        let labels = vec!["A".to_string(), "B".to_string()];
        let data = vec![
            0.01, 0.95, 0.02, 0.02, 0.01, 0.96, 0.02, 0.01, 0.98, 0.01, 0.01, 0.00, 0.01, 0.01,
            0.02, 0.96, 0.01, 0.02, 0.95, 0.02,
        ];
        let (text, confidence) = decode_ctc(data, &[1, 5, 4], &labels).unwrap();
        assert_eq!(text, "A B");
        assert!(confidence > 0.9);
    }

    #[test]
    fn decode_ctc_rejects_invalid_shapes_and_batch_sizes() {
        let labels = vec!["A".to_string()];
        assert!(decode_ctc(vec![0.1], &[1], &labels).is_err());
        assert!(decode_ctc(vec![0.1, 0.9, 0.2, 0.8], &[2, 1, 2], &labels).is_err());
        assert!(decode_ctc(Vec::new(), &[1, 0, 2], &labels).is_err());
    }

    #[test]
    fn decode_ctc_rejects_mismatched_or_overflowing_data_lengths() {
        let labels = vec!["A".to_string()];
        assert!(decode_ctc(vec![0.1], &[1, 2], &labels).is_err());
        assert!(decode_ctc(Vec::new(), &[usize::MAX, 2], &labels).is_err());
    }

    #[test]
    fn decode_ctc_rejects_unsupported_class_counts_and_non_finite_scores() {
        let labels = vec!["A".to_string()];
        assert!(decode_ctc(vec![0.1, 0.2, 0.3, 0.4], &[1, 4], &labels).is_err());
        assert!(decode_ctc(vec![f32::NAN, 0.2], &[1, 2], &labels).is_err());
        assert!(decode_ctc(vec![0.1, f32::INFINITY], &[1, 2], &labels).is_err());
    }
}
