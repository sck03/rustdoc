import path from 'node:path';

export function cargoExampleExecutable(repositoryRoot, name, environment = process.env, platform = process.platform) {
  const target = environment.CARGO_TARGET_DIR?.trim() ? environment.CARGO_TARGET_DIR : 'target';
  return path.join(path.resolve(repositoryRoot, target), environment.CARGO_BUILD_TARGET || '',
    'debug', 'examples', name + (platform === 'win32' ? '.exe' : ''));
}
