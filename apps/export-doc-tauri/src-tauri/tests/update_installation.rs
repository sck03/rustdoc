#[path = "../src/update_installation.rs"]
mod update_installation;
use std::cell::RefCell;

#[test]
fn shutdown_failure_prevents_installer_and_preserves_reason() {
    let calls = RefCell::new(Vec::new());
    let result = update_installation::run(
        || {
            calls.borrow_mut().push("stop");
            Err("storage failure".into())
        },
        || {
            calls.borrow_mut().push("install");
            Ok(())
        },
    );
    assert_eq!(*calls.borrow(), ["stop"]);
    assert!(result.unwrap_err().contains("未安装更新：storage failure"));
}
#[test]
fn installation_runs_after_stop_and_failure_requires_restart() {
    for fails in [false, true] {
        let calls = RefCell::new(Vec::new());
        let result = update_installation::run(
            || {
                calls.borrow_mut().push("stop");
                Ok(())
            },
            || {
                calls.borrow_mut().push("install");
                if fails {
                    Err("installer rejected".into())
                } else {
                    Ok(())
                }
            },
        );
        assert_eq!(*calls.borrow(), ["stop", "install"]);
        if fails {
            assert!(result.unwrap_err().contains("重启原程序"));
        } else {
            assert!(result.is_ok());
        }
    }
}
