/**
 * Compatibility entry point for the historical print regression name.
 *
 * Printing is owned by the V3 schema/exporter.  The old fixture depended on
 * the removed V2 report shell, so route this stable npm command to the current
 * V3 print regression rather than keeping a parallel legacy runtime alive.
 */
import "./test_report_designer_v3_print_regression.mjs";
