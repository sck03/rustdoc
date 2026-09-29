import assert from 'node:assert/strict';
import path from 'node:path';
import { cargoExampleExecutable } from './lib/cargo-paths.mjs';
const root = path.resolve(import.meta.dirname, '..');
assert.equal(cargoExampleExecutable(root, 'office_review', {}, 'win32'), path.join(root, 'target/debug/examples/office_review.exe'));
assert.equal(cargoExampleExecutable(root, 'office_review', { CARGO_TARGET_DIR: 'custom target' }, 'linux'), path.join(root, 'custom target/debug/examples/office_review'));
assert.equal(cargoExampleExecutable(root, 'office_review', { CARGO_TARGET_DIR: path.join(root, 'custom'), CARGO_BUILD_TARGET: 'aarch64-unknown-linux-gnu' }, 'linux'), path.join(root, 'custom/aarch64-unknown-linux-gnu/debug/examples/office_review'));
console.log('Cargo example path resolution passed.');
