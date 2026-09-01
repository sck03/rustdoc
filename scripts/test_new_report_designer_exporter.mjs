/**
 * Compatibility entry point for the historical exporter test name.
 *
 * The report designer runtime is V3 only.  The former test fixture built a
 * version-2 block schema and therefore asserted behaviour that is deliberately
 * no longer available at runtime.  Keep the npm script stable for local and
 * CI callers, but execute the current V3 contract suite instead of reviving a
 * second exporter or a V2 fallback path.
 */
import "./test_report_designer_v3_contract.mjs";
