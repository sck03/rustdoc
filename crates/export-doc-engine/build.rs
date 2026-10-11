use std::{env, path::Path, process::Command};

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-env-changed=EXPORTDOCMANAGER_BUILD_REVISION");
    let manifest = env::var("CARGO_MANIFEST_DIR").expect("missing Cargo manifest directory");
    let root = Path::new(&manifest).join("../..");
    let revision = env::var("EXPORTDOCMANAGER_BUILD_REVISION")
        .ok()
        .filter(|value| !value.trim().is_empty())
        .or_else(|| source_revision(&root));
    let version = env::var("CARGO_PKG_VERSION").expect("missing package version");
    let information = match revision {
        Some(revision) => {
            let revision = revision.trim();
            assert!(
                (7..=64).contains(&revision.len())
                    && revision.bytes().all(|byte| byte.is_ascii_hexdigit()),
                "EXPORTDOCMANAGER_BUILD_REVISION must be a Git commit hash"
            );
            format!(
                "{version}+git.{}",
                revision[..revision.len().min(12)].to_ascii_lowercase()
            )
        }
        None => version,
    };
    println!("cargo:rustc-env=EXPORTDOCMANAGER_INFORMATIONAL_VERSION={information}");
}

fn source_revision(root: &Path) -> Option<String> {
    let git_entry = root.join(".git");
    if !git_entry.exists() {
        return None;
    }
    if git_entry.is_file() {
        println!("cargo:rerun-if-changed={}", git_entry.display());
    }
    // Git resolves linked worktrees and packed refs; watch only revision metadata.
    let paths = git(
        root,
        &[
            "rev-parse",
            "--path-format=absolute",
            "--git-path",
            "HEAD",
            "--git-path",
            "refs",
            "--git-path",
            "packed-refs",
        ],
    )?;
    for path in paths.lines().filter(|path| Path::new(path).exists()) {
        println!("cargo:rerun-if-changed={path}");
    }
    git(root, &["rev-parse", "--verify", "HEAD"])
}

fn git(root: &Path, arguments: &[&str]) -> Option<String> {
    let mut command = Command::new("git");
    command.current_dir(root).args(arguments);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x0800_0000);
    }
    let output = command.output().ok()?;
    output
        .status
        .success()
        .then(|| String::from_utf8(output.stdout).ok())
        .flatten()
}
