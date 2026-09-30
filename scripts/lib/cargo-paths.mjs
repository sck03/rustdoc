import path from 'node:path';

export function cargoArtifactDirectory(repositoryRoot, environment = process.env, profile = 'debug') {
  const target = environment.CARGO_TARGET_DIR?.trim() ? environment.CARGO_TARGET_DIR : 'target';
  return path.join(path.resolve(repositoryRoot, target), environment.CARGO_BUILD_TARGET || '',
    profile);
}

export function cargoExampleExecutable(repositoryRoot, name, environment = process.env, platform = process.platform) {
  return path.join(cargoArtifactDirectory(repositoryRoot, environment), 'examples', name + (platform === 'win32' ? '.exe' : ''));
}
